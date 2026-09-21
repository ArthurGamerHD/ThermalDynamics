using System.Collections.Generic;

namespace Thermodynamics.Core
{
    public static class WeatherResponse
    {
        public struct Weather
        {
            public float TemperatureOffset;

            public float SolarMultiplier;

            public float WindMultiplier;

            public float ConvectionMultiplier;

/// <summary>Weather operation.</summary>
            public Weather(float temperature, float solar, float wind, float convection)
            {
                TemperatureOffset = temperature;
                SolarMultiplier = solar;
                WindMultiplier = wind;
                ConvectionMultiplier = convection;
            }
        }

/// <summary>Weather operation.</summary>
        public static readonly Weather Calm = new Weather(0f, 1f, 1f, 1f);

        private static readonly KeyValuePair<string, Weather>[] Weathers =
        {
            new KeyValuePair<string, Weather>("thunderstorm", new Weather(-3.6f, 0.30f, 1.75f, 2.6f)),
            new KeyValuePair<string, Weather>("sandstorm", new Weather(12.0f, 0.10f, 2.25f, 1.4f)),
            new KeyValuePair<string, Weather>("marsstorm", new Weather(0.0f, 0.10f, 2.50f, 1.4f)),
            new KeyValuePair<string, Weather>("electricstorm", new Weather(18.0f, 0.10f, 2.25f, 1.5f)),

            new KeyValuePair<string, Weather>("hail", new Weather(-5.4f, 0.25f, 2.00f, 2.8f)),
            new KeyValuePair<string, Weather>("rain", new Weather(-3.6f, 0.30f, 1.45f, 2.5f)),
            new KeyValuePair<string, Weather>("snow", new Weather(-18.0f, 0.10f, 2.00f, 2.2f)),
            new KeyValuePair<string, Weather>("dust", new Weather(3.6f, 0.80f, 1.25f, 1.2f)),
            new KeyValuePair<string, Weather>("fog", new Weather(-4.2f, 0.15f, 0.10f, 1.3f)),

            new KeyValuePair<string, Weather>("cold", new Weather(-12.0f, 0.80f, 1.55f, 1.1f)),
            new KeyValuePair<string, Weather>("heat", new Weather(6.0f, 1.75f, 0.10f, 1.0f)),

            new KeyValuePair<string, Weather>("lowwind", new Weather(0.0f, 1.00f, 0.30f, 1.0f)),
            new KeyValuePair<string, Weather>("wind", new Weather(0.0f, 1.00f, 1.45f, 1.0f)),
        };

        public const float LightFraction = 0.5f;


/// <summary>For operation.</summary>
        public static Weather For(string weather)
        {
            if (string.IsNullOrEmpty(weather)) return Calm;

            string lowered = weather.ToLowerInvariant();

            for (int i = 0; i < Weathers.Length; i++)
            {
                if (!lowered.Contains(Weathers[i].Key)) continue;

                Weather found = Weathers[i].Value;
                return lowered.Contains("light") ? Soften(found, LightFraction) : found;
            }

            return Calm;
        }

/// <summary>Soften operation.</summary>
        public static Weather Soften(Weather weather, float fraction)
        {
            if (fraction <= 0f) return Calm;
            if (fraction >= 1f) return weather;

            return new Weather(
                weather.TemperatureOffset * fraction,
                1f + ((weather.SolarMultiplier - 1f) * fraction),
                1f + ((weather.WindMultiplier - 1f) * fraction),
                1f + ((weather.ConvectionMultiplier - 1f) * fraction));
        }

/// <summary>SwingMultiplier operation.</summary>
        public static float SwingMultiplier(Weather weather)
        {
            float swing = 0.5f + (0.5f * weather.SolarMultiplier);
            return swing < 0f ? 0f : swing;
        }
    }
}
