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

            // ---- ambient -------------------------------------------------------------------
            float ambient;
            if (sample.IsUnderground)
            {
                ambient = planet.UndergroundTemperature;
                state.IsSolarOccluded = true;
                state.SolarOcclusion = 1f;
            }
            else
            {
                // -1 at the antisolar point, +1 with the sun overhead
                float dot = Vector3.Dot(SafeNormalize(sample.UpDirection), SafeNormalize(sample.SunDirection));
                float t = (dot + 1f) * 0.5f;
                ambient = planet.NightTemperature + (t * (planet.DayTemperature - planet.NightTemperature));
            }

            // Thin air holds little heat, so ambient tends toward vacuum as density falls.
            ambient *= state.AtmosphereFactor;
            state.SetAmbient(Math.Max(settings.VacuumTemperature, ambient));

            // ---- convection ----------------------------------------------------------------
            float windBonus = 1f + (WindConvectionScale * (float)Math.Sqrt(state.WindSpeed));
            state.ConvectionCoefficient = planet.ConvectionCoefficient * windBonus;

            // ---- solar through atmosphere --------------------------------------------------
            state.SolarEnergy = settings.SolarEnergy
                * (1f - state.SolarOcclusion)
                * (1f - (planet.SolarDecay * state.AtmosphereFactor));
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
