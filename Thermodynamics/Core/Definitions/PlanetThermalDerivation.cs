using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Derives planet thermal properties from engine configuration data.
    /// Used to generate realistic planet profiles based on surface temperature level,
    /// atmosphere density, gravity, and other environmental factors.
    /// </summary>
    public static class PlanetThermalDerivation
    {
        /// <summary>
        /// Engine configuration data used for planet thermal derivation.
        /// Contains planetary properties like temperature, gravity, atmosphere, and terrain depth.
        /// </summary>
        public struct Engine
        {
            /// <summary>
            /// Surface temperature level (0-1 scale).
            /// Maps to predefined temperature ranges via LevelTemperatures.
            /// </summary>
            public float SurfaceTemperatureLevel;

            /// <summary>
            /// Surface gravity as a multiple of Earth's gravity (StandardGravity).
            /// Affects lapse rate and atmospheric behavior.
            /// </summary>
            public float SurfaceGravity;

            /// <summary>
            /// True if the planet has a breathable atmosphere.
            /// Affects specific heat capacity calculations.
            /// </summary>
            public bool HasAtmosphere;

            /// <summary>
            /// Atmosphere density (0-1 scale).
            /// Higher values indicate denser atmosphere with better heat retention.
            /// </summary>
            public float AtmosphereDensity;

            /// <summary>
            /// True if the atmosphere is breathable by humans.
            /// Used for specific heat capacity selection.
            /// </summary>
            public bool Breathable;

            /// <summary>
            /// Solar radiation protection factor (0-1).
            /// Higher values indicate more protection from solar radiation (thicker atmosphere/clouds).
            /// Affects SolarDecay calculation.
            /// </summary>
            public float SolarRadiationProtection;

            /// <summary>
            /// Deepest ground penetration in meters.
            /// Used to set SealevelDeadzone property.
            /// </summary>
            public float DeepestGroundMetres;

            /// <summary>
            /// Gets the effective air value, normalized to 0 or positive.
            /// Returns 0 if atmosphere is not present or density is negative.
            /// </summary>
            public float Air
            {
                get
                {
                    if (!HasAtmosphere) return 0f;
                    return AtmosphereDensity < 0f ? 0f : AtmosphereDensity;
                }
            }
        }


        /// <summary>
        /// Predefined temperature levels for different planet types.
        /// Indexed by SurfaceTemperatureLevel (0-1 scale).
        /// Values represent base temperatures in Kelvin.
        /// </summary>
        public static readonly float[] LevelTemperatures = { 100f, 215f, 288f, 325f, 450f };


        /// <summary>
        /// Interpolates mean temperature based on surface temperature level.
        /// Uses linear interpolation between predefined temperature levels.
        /// </summary>
        /// <param name="level">Temperature level (0-1, maps to LevelTemperatures array).</param>
        /// <returns>Interpolated mean temperature in Kelvin.</returns>
        public static float MeanTemperature(float level)
        {
            if (level < 0f) level = 0f;
            if (level > 1f) level = 1f;

            float position = level * (LevelTemperatures.Length - 1);

            int low = (int)position;
            if (low >= LevelTemperatures.Length - 1) return LevelTemperatures[LevelTemperatures.Length - 1];

            float fraction = position - low;
            return LevelTemperatures[low] + ((LevelTemperatures[low + 1] - LevelTemperatures[low]) * fraction);
        }


        /// <summary>
        /// Temperature swing for thick atmosphere (day-night difference).
        /// With atmosphere, swings are smaller due to better heat distribution.
        /// </summary>
        public const float ThickAirSwing = 11f;

        /// <summary>
        /// Temperature swing for airless bodies (day-night difference).
        /// Without atmosphere, surfaces experience extreme temperature variations.
        /// </summary>
        public const float AirlessSwing = 220f;


        /// <summary>
        /// Calculates temperature swing based on atmosphere density.
        /// Airless bodies (low air) have larger day-night temperature swings.
        /// Thicker atmospheres (high air) moderate temperature extremes.
        /// </summary>
        /// <param name="air">Atmosphere density (0-1).</param>
        /// <returns>Temperature swing in Kelvin.</returns>
        public static float Swing(float air)
        {
            if (air < 0f) air = 0f;
            if (air > 1f) air = 1f;

            // Square falloff: thin air has exponentially larger swings
            float thin = (1f - air) * (1f - air);
            return ThickAirSwing + ((AirlessSwing - ThickAirSwing) * thin);
        }


        /// <summary>
        /// Calculates atmospheric specific heat capacity.
        /// Breathable air (N2/O2 mix) has higher specific heat than non-breathable atmospheres.
        /// </summary>
        /// <param name="breathable">True for Earth-like atmosphere, false for other gases.</param>
        /// <returns>Specific heat capacity in J/kg·K.</returns>
        public static float SpecificHeat(bool breathable)
        {
            return breathable ? 1005f : 850f;
        }

        /// <summary>
        /// Standard Earth surface gravity in m/s².
        /// Used as reference for calculating atmospheric lapse rates.
        /// </summary>
        public const float StandardGravity = 9.81f;

        /// <summary>
        /// Fraction of lapse rate that contributes to environmental cooling.
        /// Only a portion of the theoretical lapse rate affects surface conditions.
        /// </summary>
        public const float EnvironmentalShare = 0.66f;


        /// <summary>
        /// Calculates atmospheric lapse rate (temperature change per altitude).
        /// Higher gravity and lower specific heat result in steeper lapse rates.
        /// </summary>
        /// <param name="gravity">Surface gravity as multiple of StandardGravity.</param>
        /// <param name="breathable">True for Earth-like atmosphere.</param>
        /// <param name="air">Atmosphere density (0-1).</param>
        /// <returns>Lapse rate in meters per Kelvin (how many meters to change 1K).</returns>
        public static float LapseRate(float gravity, bool breathable, float air)
        {
            if (air <= 0f) return 0f;

            // Higher gravity = steeper lapse rate
            // Lower specific heat = steeper lapse rate
            float metresPerKelvin = (gravity * StandardGravity) / SpecificHeat(breathable);
            return metresPerKelvin * 1000f * EnvironmentalShare;
        }


        /// <summary>
        /// Calculates polar temperature drop (equator-to-pole difference).
        /// Atmosphere reduces temperature differences between equator and poles.
        /// Airless bodies have larger polar drops.
        /// </summary>
        /// <param name="air">Atmosphere density (0-1).</param>
        /// <returns>Polar temperature drop in Kelvin.</returns>
        public static float PoleDrop(float air)
        {
            if (air < 0f) air = 0f;
            if (air > 1f) air = 1f;

            // Atmosphere transports heat from equator to poles
            // More atmosphere = smaller polar drop
            return 40f + (80f * (1f - air));
        }


        /// <summary>
        /// Calculates ambient temperature lag time (how quickly temperature changes).
        /// Atmosphere provides thermal inertia, causing temperature to lag behind solar input.
        /// </summary>
        /// <param name="air">Atmosphere density (0-1).</param>
        /// <returns>Lag time in seconds.</returns>
        public static float LagSeconds(float air)
        {
            if (air < 0f) air = 0f;
            if (air > 1f) air = 1f;

            float lag = 45f * air;
            return lag < 5f ? 5f : lag;
        }


        /// <summary>
        /// Calculates solar radiation decay through atmosphere.
        /// Protection factors (clouds, ozone) reduce the amount of solar energy reaching the surface.
        /// </summary>
        /// <param name="protection">Solar radiation protection factor (0-1).</param>
        /// <param name="air">Atmosphere density (0-1).</param>
        /// <returns>Decay factor (0-0.9, where 0 = no decay, 0.9 = most decay).</returns>
        public static float SolarDecay(float protection, float air)
        {
            if (air <= 0f) return 0f;
            if (protection < 0f) protection = 0f;

            float decay = protection * (0.30f / 1.8f);
            if (decay > 0.9f) decay = 0.9f;
            return decay;
        }


        /// <summary>
        /// Calculates convection coefficient for heat transfer to atmosphere.
        /// Higher atmosphere density = better convective heat transfer.
        /// </summary>
        /// <param name="air">Atmosphere density (0-1).</param>
        /// <returns>Convection coefficient in W/m²·K.</returns>
        public static float ConvectionCoefficient(float air)
        {
            if (air <= 0f) return 0f;
            if (air > 1f) air = 1f;

            // Proportional to atmosphere density
            return 50f * air;
        }


        /// <summary>
        /// Derives complete planet thermal properties from engine configuration.
        /// Calculates day/night temperatures, pole drops, lag times, lapse rates,
        /// and other thermal properties based on the planet's environmental configuration.
        /// </summary>
        /// <param name="engine">The engine configuration with planet properties.</param>
        /// <returns>A PlanetThermalProperties with derived values.</returns>
        public static PlanetThermalProperties Derive(Engine engine)
        {
            float air = engine.Air;


            float mean = MeanTemperature(engine.SurfaceTemperatureLevel);

            float half = Swing(air) * 0.5f;


            PlanetThermalProperties properties = new PlanetThermalProperties();

            properties.DayTemperature = mean + half;
            properties.NightTemperature = mean - half;
            if (properties.NightTemperature < 0f) properties.NightTemperature = 0f;


            properties.PoleTemperatureDrop = PoleDrop(air);

            properties.AmbientLagSeconds = LagSeconds(air);

            properties.AmbientLapseRate = LapseRate(engine.SurfaceGravity, engine.Breathable, air);

            properties.UndergroundTemperature = mean;

            if (engine.DeepestGroundMetres > 0f)
            {
                properties.SealevelDeadzone = engine.DeepestGroundMetres;
            }


            properties.SolarDecay = SolarDecay(engine.SolarRadiationProtection, air);

            properties.ConvectionCoefficient = ConvectionCoefficient(air);

            return properties;
        }
    }
}
