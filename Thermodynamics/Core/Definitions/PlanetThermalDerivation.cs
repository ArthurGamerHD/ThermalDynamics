using System;

namespace Thermodynamics.Core
{
    public static class PlanetThermalDerivation
    {
        public struct Engine
        {
            public float SurfaceTemperatureLevel;

            public float SurfaceGravity;

            public bool HasAtmosphere;

            public float AtmosphereDensity;

            public bool Breathable;

            public float SolarRadiationProtection;

            public float DeepestGroundMetres;

            public float Air
            {
                get
                {
                    if (!HasAtmosphere) return 0f;
                    return AtmosphereDensity < 0f ? 0f : AtmosphereDensity;
                }
            }
        }


        public static readonly float[] LevelTemperatures = { 100f, 215f, 288f, 325f, 450f };

/// <summary>MeanTemperature operation.</summary>
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


        public const float ThickAirSwing = 11f;

        public const float AirlessSwing = 220f;

/// <summary>Swing operation.</summary>
        public static float Swing(float air)
        {
            if (air < 0f) air = 0f;
            if (air > 1f) air = 1f;

            float thin = (1f - air) * (1f - air);
            return ThickAirSwing + ((AirlessSwing - ThickAirSwing) * thin);
        }


/// <summary>SpecificHeat operation.</summary>
        public static float SpecificHeat(bool breathable)
        {
            return breathable ? 1005f : 850f;
        }

        public const float StandardGravity = 9.81f;

        public const float EnvironmentalShare = 0.66f;

/// <summary>LapseRate operation.</summary>
        public static float LapseRate(float gravity, bool breathable, float air)
        {
            if (air <= 0f) return 0f;   // no air, no lapse: there is nothing to cool as it rises

            float metresPerKelvin = (gravity * StandardGravity) / SpecificHeat(breathable);
            return metresPerKelvin * 1000f * EnvironmentalShare;
        }

/// <summary>PoleDrop operation.</summary>
        public static float PoleDrop(float air)
        {
            if (air < 0f) air = 0f;
            if (air > 1f) air = 1f;

            return 40f + (80f * (1f - air));
        }

/// <summary>LagSeconds operation.</summary>
        public static float LagSeconds(float air)
        {
            if (air < 0f) air = 0f;
            if (air > 1f) air = 1f;

            float lag = 45f * air;
            return lag < 5f ? 5f : lag;
        }

/// <summary>SolarDecay operation.</summary>
        public static float SolarDecay(float protection, float air)
        {
            if (air <= 0f) return 0f;
            if (protection < 0f) protection = 0f;

            float decay = protection * (0.30f / 1.8f);
            if (decay > 0.9f) decay = 0.9f;
            return decay;
        }

/// <summary>ConvectionCoefficient operation.</summary>
        public static float ConvectionCoefficient(float air)
        {
            if (air <= 0f) return 0f;
            if (air > 1f) air = 1f;

            return 50f * air;
        }

/// <summary>Derive operation.</summary>
        public static PlanetThermalProperties Derive(Engine engine)
        {
            float air = engine.Air;

/// <summary>MeanTemperature operation.</summary>
            float mean = MeanTemperature(engine.SurfaceTemperatureLevel);
/// <summary>Swing operation.</summary>
            float half = Swing(air) * 0.5f;

/// <summary>PlanetThermalProperties operation.</summary>
            PlanetThermalProperties properties = new PlanetThermalProperties();

            properties.DayTemperature = mean + half;
            properties.NightTemperature = mean - half;
            if (properties.NightTemperature < 0f) properties.NightTemperature = 0f;

/// <summary>PoleDrop operation.</summary>
            properties.PoleTemperatureDrop = PoleDrop(air);
/// <summary>LagSeconds operation.</summary>
            properties.AmbientLagSeconds = LagSeconds(air);
/// <summary>LapseRate operation.</summary>
            properties.AmbientLapseRate = LapseRate(engine.SurfaceGravity, engine.Breathable, air);

            properties.UndergroundTemperature = mean;

            if (engine.DeepestGroundMetres > 0f)
            {
                properties.SealevelDeadzone = engine.DeepestGroundMetres;
            }

/// <summary>SolarDecay operation.</summary>
            properties.SolarDecay = SolarDecay(engine.SolarRadiationProtection, air);
/// <summary>ConvectionCoefficient operation.</summary>
            properties.ConvectionCoefficient = ConvectionCoefficient(air);

            return properties;
        }
    }
}
