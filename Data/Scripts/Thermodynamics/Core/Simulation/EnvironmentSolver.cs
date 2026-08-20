using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Turns a raw <see cref="EnvironmentSample"/> into the <see cref="EnvironmentState"/> the
    /// solver consumes. Pure function of its inputs — no game state, no time dependence.
    /// </summary>
    public static class EnvironmentSolver
    {
        /// <summary>
        /// Wind speed bonus on the convection coefficient: h = h0 * (1 + 0.1 * sqrt(v)).
        /// </summary>
        public const float WindConvectionScale = 0.1f;

        public static EnvironmentState Solve(ThermalSettings settings, PlanetThermalProperties planet, EnvironmentSample sample)
        {
            if (settings == null) throw new ArgumentNullException("settings");

            EnvironmentState state = new EnvironmentState();
            state.SunDirectionLocal = sample.SunDirectionLocal;
            state.WindDirectionLocal = sample.RelativeWindDirectionLocal;
            state.WindSpeed = Math.Max(0f, sample.RelativeWindSpeed);

            // Point sources pass through unchanged: the host has already resolved distance and
            // occlusion. The enable switch is applied here so the solver never tests it per source
            // per node.
            if (settings.EnableHeatSources && sample.HeatSources != null)
            {
                state.HeatSources = sample.HeatSources;
                state.HeatSourceCount = Math.Min(sample.HeatSourceCount, sample.HeatSources.Length);
            }

            // ---- solar ---------------------------------------------------------------------
            // The occlusion flag and the fraction must agree in both directions, since a caller may
            // set only the flag.
            state.SolarOcclusion = Clamp01(sample.SolarOcclusion);
            if (sample.IsSolarOccluded || !settings.EnableSolarHeat) state.SolarOcclusion = 1f;

            state.IsSolarOccluded = state.SolarOcclusion >= 1f;

            // ---- no planet, or planets disabled --------------------------------------------
            bool usePlanet = settings.EnablePlanets && sample.HasPlanet && planet != null;
            if (!usePlanet)
            {
                state.SetAmbient(settings.VacuumTemperature);
                state.AirDensity = 0f;
                state.AtmosphereFactor = 0f;
                state.ConvectionCoefficient = 0f;
                state.SolarEnergy = settings.SolarEnergy * (1f - state.SolarOcclusion);
                state.FrictionActive = false;
                return state;
            }

            float density = Clamp01(sample.AirDensity);
            state.AirDensity = density;
            state.AtmosphereFactor = AtmosphereFactor(density);

            // ---- weather -------------------------------------------------------------------
            // Resolved once against its intensity, so nothing below handles intensity. Clear air
            // softens to Calm, whose terms are all the identity.
            WeatherResponse.Weather weather = WeatherResponse.Soften(sample.Weather, Clamp01(sample.WeatherIntensity));

            state.WeatherIntensity = Clamp01(sample.WeatherIntensity);
            state.WeatherTemperatureOffset = weather.TemperatureOffset;

            // ---- ambient -------------------------------------------------------------------
            //
            // Every term contributing to the air temperature goes into a target, and the lag is
            // applied to that target exactly once at the end. Scaling the running ambient instead
            // compounds the scale against the lag every step: 0.977 four times a second against a
            // 45 second lag settles at 14 % of the intended figure.

            // Sine of the sun's height above the horizon: negative at night, 1 overhead.
            float elevation = Vector3.Dot(SafeNormalize(sample.UpDirection), SafeNormalize(sample.SunDirection));

            // A sample that supplies no ground swing sends 0, which would flatten the day entirely;
            // it is treated as unspecified and leaves the planet's own swing.
            float groundSwing = sample.GroundSwing > 0f ? sample.GroundSwing : 1f;

            // Ground and weather both shift the air and both change the size of its day, so they
            // reach the model as one offset and one swing. Overcast weather narrows the swing in
            // both directions: cloud that blocks the sun by day also retains heat at night.
            float offset = sample.GroundOffset + weather.TemperatureOffset;
            float swing = groundSwing * WeatherResponse.SwingMultiplier(weather);

            float target = ClimateModel.Target(planet, sample.LatitudeSine, elevation, offset, swing);

            // Cooled by altitude first, then faded towards vacuum only where the air runs out.
            // These are separate effects and a single density multiply would conflate them.
            target = ClimateModel.Lapse(target, sample.Altitude, planet.AmbientLapseRate);
            target = ClimateModel.Thin(target, density, settings.VacuumTemperature);

            // Underground there is no day, no weather and no sky, only the rock and the planet's
            // interior.
            if (sample.Depth > 0f)
            {
                target = ClimateModel.Underground(
                    planet, target, sample.Depth, sample.Radius, sample.MeanRadius);
            }

            if (sample.IsUnderground || sample.Depth > 0f)
            {
                state.IsSolarOccluded = true;
                state.SolarOcclusion = 1f;
            }

            // The lag is applied to the target, which places the day's peak after noon. A grid with
            // no previous ambient starts at the target.
            float ambient = sample.HasPreviousAmbient
                ? ClimateModel.Follow(
                    sample.PreviousAmbient, target, sample.SecondsSincePrevious, planet.AmbientLagSeconds)
                : target;

            state.SetAmbient(Math.Max(settings.VacuumTemperature, ambient));

            // ---- convection ----------------------------------------------------------------
            // Humid air removes heat faster than dry air at the same speed, which the wind term
            // alone cannot express: fog barely moves and still carries heat away.
            float windBonus = 1f + (WindConvectionScale * (float)Math.Sqrt(state.WindSpeed));

            // Scaled by how fluid-like the air is, which is the whole reason AtmosphereFactor
            // exists. Without it convection was all or nothing: any air at all, however thin,
            // convected at the planet's full sea-level coefficient. A field dump reported
            // 50 W/(m2 K) at 44 km with the air density reading 0.0000, which is what that looks
            // like from outside. The stability estimator had always applied the factor, so the
            // two halves of the model disagreed about the same mechanism.
            state.ConvectionCoefficient = planet.ConvectionCoefficient
                * state.AtmosphereFactor
                * windBonus
                * Math.Max(0f, weather.ConvectionMultiplier);

            // ---- solar through atmosphere --------------------------------------------------
            state.SolarEnergy = settings.SolarEnergy
                * (1f - state.SolarOcclusion)
                * (1f - (planet.SolarDecay * state.AtmosphereFactor))
                * Math.Max(0f, weather.SolarMultiplier);
            if (state.SolarEnergy < 0f) state.SolarEnergy = 0f;

            // ---- friction ------------------------------------------------------------------
            state.FrictionActive = settings.EnableFriction
                && density > 0.01f
                && state.WindSpeed > settings.FrictionAtSpeedsAbove;

            return state;
        }

        /// <summary>
        /// Maps raw air density onto how fluid-like the environment is, as 1 - (1 - d)^4. At 25 %
        /// density the atmosphere behaves 68 % like sea level, matching how quickly convection comes
        /// to dominate radiation in a real atmosphere.
        /// </summary>
        public static float AtmosphereFactor(float airDensity)
        {
            float inverse = 1f - Clamp01(airDensity);
            float squared = inverse * inverse;
            return 1f - (squared * squared);
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static Vector3 SafeNormalize(Vector3 v)
        {
            float lengthSquared = v.LengthSquared();
            if (lengthSquared <= 1e-12f) return Vector3.Zero;
            return v / (float)Math.Sqrt(lengthSquared);
        }
    }
}
