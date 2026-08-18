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

            // Point sources pass through untouched: the host has already resolved distance and
            // occlusion, and nothing here would improve on that. The switch is honoured here so
            // the solver never has to test it per source per node.
            if (settings.EnableHeatSources && sample.HeatSources != null)
            {
                state.HeatSources = sample.HeatSources;
                state.HeatSourceCount = Math.Min(sample.HeatSourceCount, sample.HeatSources.Length);
            }

            // ---- solar ---------------------------------------------------------------------
            // The flag and the fraction have to agree in both directions: a caller that only set
            // the flag — every scenario written before the fraction existed — still means "no sun".
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
            // Resolved to what it is worth right now, once, so nothing below has to know about
            // intensity. Clear air softens to Calm, whose every term is the identity.
            WeatherResponse.Weather weather = WeatherResponse.Soften(sample.Weather, Clamp01(sample.WeatherIntensity));

            state.WeatherIntensity = Clamp01(sample.WeatherIntensity);
            state.WeatherTemperatureOffset = weather.TemperatureOffset;

            // ---- ambient -------------------------------------------------------------------
            //
            // Everything that decides what the air should be doing goes into a target, and the
            // lag is applied to that target exactly once at the end. Scaling the running ambient
            // instead compounds the scale against the lag every step: 0.977 four times a second
            // against a 45 second lag settles at 14% of the intended figure, which is how a
            // snowfield at 5.6 km came to sit at 36 K.

            // Sine of the sun's height above the horizon: negative at night, 1 overhead.
            float elevation = Vector3.Dot(SafeNormalize(sample.UpDirection), SafeNormalize(sample.SunDirection));

            // A sample from before the ground had a say sends 0, which would flatten the day
            // to nothing; that reads as "no opinion" and leaves the planet's own swing.
            float groundSwing = sample.GroundSwing > 0f ? sample.GroundSwing : 1f;

            // Ground and weather both shift the air and both change the size of its day, so they
            // arrive at the model as one offset and one swing. Overcast is the same fact twice —
            // the cloud that keeps the sun off by day keeps the heat in at night.
            float offset = sample.GroundOffset + weather.TemperatureOffset;
            float swing = groundSwing * WeatherResponse.SwingMultiplier(weather);

            float target = ClimateModel.Target(planet, sample.LatitudeSine, elevation, offset, swing);

            // Colder the higher it is, then faded toward vacuum only where the air actually runs
            // out. Two separate facts that the single density multiply used to conflate.
            target = ClimateModel.Lapse(target, sample.Altitude, planet.AmbientLapseRate);
            target = ClimateModel.Thin(target, density, settings.VacuumTemperature);

            // Underground there is no day, no weather and no sky, only rock and what is under it.
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

            // Air chases that rather than being it, so the day's peak lands after noon. A grid
            // with no history to chase from takes the target and starts there.
            float ambient = sample.HasPreviousAmbient
                ? ClimateModel.Follow(
                    sample.PreviousAmbient, target, sample.SecondsSincePrevious, planet.AmbientLagSeconds)
                : target;

            state.SetAmbient(Math.Max(settings.VacuumTemperature, ambient));

            // ---- convection ----------------------------------------------------------------
            // Wet air strips heat off a hull far faster than dry air of the same speed, and the
            // wind term alone cannot say so: fog barely moves and still carries heat away.
            float windBonus = 1f + (WindConvectionScale * (float)Math.Sqrt(state.WindSpeed));
            state.ConvectionCoefficient =
                planet.ConvectionCoefficient * windBonus * Math.Max(0f, weather.ConvectionMultiplier);

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
        /// Maps raw air density onto how fluid-like the environment is. 1 - (1 - d)^4: at 25%
        /// density the atmosphere already behaves 68% like sea level, which matches how quickly
        /// convection dominates radiation in a real atmosphere.
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
