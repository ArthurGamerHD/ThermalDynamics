using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The thermal effect of the weather over a grid. Three of its four columns are the game's own
    /// authored modifiers converted to this scale; convection is a balance choice, since nothing in a
    /// definition records that rain is wet. Matched on a keyword in the name rather than the exact
    /// subtype, so a mod's <c>AlienRainHeavy</c> gets rain behaviour without annotating anything.
    /// See environment.md, Weather.
    /// </summary>
    public static class WeatherResponse
    {
        /// <summary>
        /// One weather's effect at full intensity. For <see cref="Calm"/> every multiplier is 1 and
        /// every offset 0, so clear air changes nothing.
        /// </summary>
        public struct Weather
        {
            /// <summary>How much colder or warmer than the clear-sky climate, K.</summary>
            public float TemperatureOffset;

            /// <summary>Share of the sun that still reaches the ground, 0..1 and occasionally more.</summary>
            public float SolarMultiplier;

            /// <summary>Multiplier on the wind the field would otherwise produce.</summary>
            public float WindMultiplier;

            /// <summary>Multiplier on the convective coefficient, for humid or dusty air.</summary>
            public float ConvectionMultiplier;

            public Weather(float temperature, float solar, float wind, float convection)
            {
                TemperatureOffset = temperature;
                SolarMultiplier = solar;
                WindMultiplier = wind;
                ConvectionMultiplier = convection;
            }
        }

        /// <summary>Clear air: the climate as the planet describes it, unmodified.</summary>
        public static readonly Weather Calm = new Weather(0f, 1f, 1f, 1f);

        /// <summary>
        /// The weather kinds at their heavy strength, first match winning.
        ///
        /// Order matters where one keyword contains another: every storm must be tested before the
        /// plain kinds it would otherwise match, and <c>lowwind</c> before <c>wind</c>, or
        /// <c>LowWinds</c> matches as a gale.
        /// </summary>
        private static readonly KeyValuePair<string, Weather>[] Weathers =
        {
            //                                                    temp K  solar  wind  convection
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

        /// <summary>Share of a heavy weather's departure from calm that a light one carries.</summary>
        public const float LightFraction = 0.5f;

        /// <summary>
        /// The entry for a weather name at full intensity. <see cref="Calm"/> for any weather the
        /// table does not cover, and for the empty string the game returns when there is no weather.
        /// </summary>
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

        /// <summary>
        /// The same weather interpolated back towards calm. Used for the light variants and by the
        /// solver for intensity, which is the same operation.
        /// </summary>
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

        /// <summary>
        /// How much of the day-night swing survives this weather.
        ///
        /// Derived from the solar multiplier rather than stored: cloud that blocks the sun by day
        /// also retains heat at night, so a sky stopping 90 % of the sunlight flattens the day with
        /// it. Half the swing at full overcast, unchanged in clear air.
        /// </summary>
        public static float SwingMultiplier(Weather weather)
        {
            float swing = 0.5f + (0.5f * weather.SolarMultiplier);
            return swing < 0f ? 0f : swing;
        }
    }
}
