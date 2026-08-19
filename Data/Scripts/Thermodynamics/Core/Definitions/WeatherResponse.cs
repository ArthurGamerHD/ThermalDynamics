using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The thermal effect of the weather over a grid: its temperature offset, how much sunlight it
    /// admits, how hard it blows, and how much faster it removes heat from a hull.
    ///
    /// Three of the four are authored by the game. Each weather in <c>WeatherEffects.sbc</c> carries
    /// a <c>TemperatureModifier</c>, a <c>SolarOutputModifier</c> and a <c>WindOutputModifier</c>.
    /// Those are the values in the table below, converted to this scale: the multipliers pass
    /// through unchanged, and <c>TemperatureModifier</c>, a factor on the game's 0..1 comfort
    /// figure, becomes kelvin as <c>clamp(modifier - 1, -3, 3) * 6 K</c>, so heavy snow's -2
    /// becomes -18 K and a sandstorm's 3 becomes +12 K.
    ///
    /// Convection has no authored source: nothing in the definitions records that rain is wet, and
    /// humid air removes heat from a hull faster than dry air at the same speed, so that column is
    /// a balance choice as <see cref="GroundTemperature"/> is.
    ///
    /// Matched on a keyword in the name rather than the exact subtype, as the ground table is: the
    /// base game has thirty-three weathers, most of them a handful of kinds with a prefix
    /// (<c>AlienRainHeavy</c>, <c>MarsStormLight</c>), and mods add their own. A name containing
    /// <c>light</c> takes half the departure from calm, matching the game's own light/heavy pairs.
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
