using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Models planetary climate and environmental temperature calculations.
    /// Handles latitude effects, day/night cycles, altitude effects, and underground thermal profiles.
    /// </summary>
    public static class ClimateModel
    {
        /// <summary>
        /// Calculates the target equilibrium temperature for a surface location.
        /// Considers planet properties, latitude, sun elevation, and ground conditions.
        /// </summary>
        /// <param name="planet">Planet thermal properties.</param>
        /// <param name="latitudeSine">Sine of latitude (0 at equator, 1 at pole).</param>
        /// <param name="sunElevationSine">Sine of sun elevation angle (0 at horizon, 1 at zenith).</param>
        /// <param name="groundOffset">Temperature offset from ground surface.</param>
        /// <returns>Target equilibrium temperature in Kelvin.</returns>
        public static float Target(
            PlanetThermalProperties planet, float latitudeSine, float sunElevationSine, float groundOffset)
        {
            return Target(planet, latitudeSine, sunElevationSine, groundOffset, 1f);
        }


        /// <summary>
        /// Calculates the target equilibrium temperature for a surface location.
        /// Combines diurnal temperature swing with latitude-based pole drop and ground conditions.
        /// </summary>
        /// <param name="planet">Planet thermal properties.</param>
        /// <param name="latitudeSine">Sine of latitude (0 at equator, 1 at pole).</param>
        /// <param name="sunElevationSine">Sine of sun elevation angle (0 at horizon, 1 at zenith).</param>
        /// <param name="groundOffset">Temperature offset from ground surface.</param>
        /// <param name="groundSwing">Day/night temperature swing multiplier (0-1).</param>
        /// <returns>Target equilibrium temperature in Kelvin.</returns>
        /// <remarks>
        /// Temperature calculation:
        /// 1. Calculate pole temperature drop based on latitude
        ///    drop = PoleTemperatureDrop * (1 - cos(latitude))
        ///    where cos(latitude) = sqrt(1 - sin²(latitude))
        ///
        /// 2. Calculate day/night extremes with pole drop applied
        ///    night = NightTemperature - drop
        ///    day = DayTemperature - drop
        ///
        /// 3. Apply ground swing multiplier to narrow or widen the day/night range
        ///
        /// 4. Calculate current temperature based on sun position
        ///    target = night + (day - night) * sunElevation + groundOffset
        ///
        /// 5. Clamp to zero minimum (no negative absolute temperatures)
        /// </remarks>
        public static float Target(
            PlanetThermalProperties planet,
            float latitudeSine,
            float sunElevationSine,
            float groundOffset,
            float groundSwing)
        {
            if (planet == null) return 0f;
            if (groundSwing < 0f) groundSwing = 0f;

            // Calculate cosine of latitude from sine
            // cos(lat) = sqrt(1 - sin²(lat))
            float latitude = (float)Math.Sqrt(Math.Max(0f, 1f - (latitudeSine * latitudeSine)));
            
            // Temperature drop from equator to pole
            // Linear interpolation: maximum at poles, zero at equator
            float drop = planet.PoleTemperatureDrop * (1f - latitude);

            float night;
            float day;

            if (groundSwing == 1f)
            {
                // Full swing: apply pole drop directly to day/night temps
                night = planet.NightTemperature - drop;
                day = planet.DayTemperature - drop;
            }
            else
            {
                // Reduced swing: calculate mean and apply swing multiplier
                float mean = ((planet.NightTemperature + planet.DayTemperature) * 0.5f) - drop;
                float half = (planet.DayTemperature - planet.NightTemperature) * 0.5f * groundSwing;

                night = mean - half;
                day = mean + half;
            }

            // Solar insolation factor (0 at night, 1 at noon)
            float insolation = sunElevationSine <= 0f ? 0f : sunElevationSine;

            // Linear interpolation between night and day temps based on sun position
            float target = night + ((day - night) * insolation) + groundOffset;
            return target < 0f ? 0f : target;
        }


        /// <summary>
        /// Applies atmospheric lapse rate to adjust temperature with altitude.
        /// Temperature typically decreases with altitude in planetary atmospheres.
        /// </summary>
        /// <param name="target">Base temperature at reference altitude.</param>
        /// <param name="altitude">Current altitude in meters above reference.</param>
        /// <param name="lapseRatePerKm">Temperature decrease per kilometer altitude (Kelvin/km).</param>
        /// <returns>Adjusted temperature considering altitude effects.</returns>
        /// <remarks>
        /// Lapse rate formula:
        ///   cooled = target - (lapseRate * altitude / 1000)
        ///
        /// Positive lapse rate = temperature decreases with altitude (normal atmosphere).
        /// Negative lapse rate = temperature increases with altitude (inversion layer).
        /// </remarks>
        public static float Lapse(float target, float altitude, float lapseRatePerKm)
        {
            if (lapseRatePerKm == 0f || altitude == 0f) return target;

            float cooled = target - (lapseRatePerKm * altitude * 0.001f);
            return cooled < 0f ? 0f : cooled;
        }


        /// <summary>
        /// Calculates atmospheric density factor for heat transfer and radiation calculations.
        /// Represents the portion of atmosphere present that affects thermal properties.
        /// </summary>
        /// <param name="airDensity">Air density as fraction of sea-level density (0-1).</param>
        /// <returns>Atmospheric density factor (0-1). 1 = full atmosphere, 0 = vacuum.</returns>
        /// <remarks>
        /// Uses a fourth-power law based on the inverse of density:
        ///   factor = 1 - (1 - airDensity)^4
        ///
        /// This emphasizes the contribution of even small amounts of atmosphere:
        /// - Air density 0.0 = factor 0.0 (vacuum)
        /// - Air density 0.5 = factor ~0.94 (most effect from thin air)
        /// - Air density 1.0 = factor 1.0 (full atmosphere)
        /// </remarks>
        public static float AmbientDensityFactor(float airDensity)
        {
            float inverse = 1f - ThermalMath.Clamp01(airDensity);
            float squared = inverse * inverse;
            float fourth = squared * squared;
            return 1f - (fourth * fourth);
        }


        /// <summary>
        /// Adjusts target temperature for atmospheric thinness.
        /// Reduces the effect of atmosphere toward vacuum conditions.
        /// </summary>
        /// <param name="target">Target temperature with full atmosphere.</param>
        /// <param name="airDensity">Air density as fraction (0-1).</param>
        /// <param name="vacuum">Temperature in vacuum conditions.</param>
        /// <returns>Adjusted temperature considering atmospheric density.</returns>
        public static float Thin(float target, float airDensity, float vacuum)
        {
            float share = AmbientDensityFactor(airDensity);
            return vacuum + ((target - vacuum) * share);
        }


        /// <summary>
        /// Calculates underground temperature based on depth and planetary properties.
        /// Models the transition from surface temperature to underground temperature
        /// to core temperature at extreme depths.
        /// </summary>
        /// <param name="planet">Planet thermal properties.</param>
        /// <param name="surface">Surface temperature in Kelvin.</param>
        /// <param name="depth">Depth below surface in meters.</param>
        /// <param name="radius">Distance from planetary center in meters.</param>
        /// <param name="meanRadius">Planetary mean radius in meters.</param>
        /// <returns>Temperature at the given depth in Kelvin.</returns>
        /// <remarks>
        /// Two-zone model:
        /// 1. Damped transition zone (from surface to damping depth):
        ///    ambient = surface + (undergroundTemp - surface) * (depth / dampingDepth)
        ///
        /// 2. Deep zone (below sealevel deadzone):
        ///    descended = 1 - (radius / deadzoneRadius)
        ///    final = ambient + (coreTemp - ambient) * descended
        ///
        /// The sealevel deadzone represents depth where planetary radius effects become negligible.
        /// </remarks>
        public static float Underground(
            PlanetThermalProperties planet, float surface, float depth, float radius, float meanRadius)
        {
            if (planet == null) return surface;
            if (depth <= 0f) return surface;

            // Damping depth controls how quickly temperature transitions to underground
            float damping = planet.UndergroundDampingDepth;
            float buried = damping <= 0f ? 1f : depth / damping;
            if (buried > 1f) buried = 1f;

            // Linear interpolation to underground temperature
            float ambient = surface + ((planet.UndergroundTemperature - surface) * buried);

            // Sealevel deadzone - below this, planetary radius doesn't matter
            float deadzone = meanRadius - planet.SealevelDeadzone;
            if (deadzone <= 0f || radius >= deadzone) return ambient;

            // Transition from ambient to core temperature
            float descended = 1f - (radius / deadzone);
            if (descended > 1f) descended = 1f;

            return ambient + ((planet.CoreTemperature - ambient) * descended);
        }


        /// <summary>
        /// Implements first-order lag (exponential smoothing) for temperature changes.
        /// Simulates thermal inertia - how quickly a system approaches a target temperature.
        /// </summary>
        /// <param name="current">Current temperature in Kelvin.</param>
        /// <param name="target">Target temperature in Kelvin.</param>
        /// <param name="seconds">Time elapsed in seconds.</param>
        /// <param name="lagSeconds">Time constant for lag in seconds.</param>
        /// <returns>Temperature after applying lag, approaching target.</returns>
        public static float Follow(float current, float target, float seconds, float lagSeconds)
        {
            if (lagSeconds <= 0f || seconds <= 0f) return target;
            if (current <= 0f) return target;

            // Exponential approach to target
            float closed = 1f - (float)Math.Exp(-seconds / lagSeconds);
            return current + ((target - current) * closed);
        }
    }
}
