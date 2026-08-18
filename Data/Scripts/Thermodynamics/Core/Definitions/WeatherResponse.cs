using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// What the weather over a grid is worth: how much it cools the air, how much of the sun it
    /// keeps out, how hard it blows, and how much faster it strips heat off a hull.
    ///
    /// The game already has an opinion about every one of those except the last. Each weather in
    /// <c>WeatherEffects.sbc</c> carries a <c>TemperatureModifier</c>, a <c>SolarOutputModifier</c>
    /// and a <c>WindOutputModifier</c>, authored per effect — a heavy snowstorm is colder, darker
    /// and windier than light rain because Keen said so, not because this mod guessed. Those are
    /// the numbers below, converted from their scale to this one: the multipliers pass through as
    /// multipliers, and <c>TemperatureModifier</c> — a factor on the game's 0..1 comfort figure —
    /// becomes kelvin as <c>clamp(modifier - 1, -3, 3) x 6 K</c>, so heavy snow's -2 lands at
    /// -18 K and a sandstorm's 3 at +12 K.
    ///
    /// Convection is the one column with no source. Nothing in the definitions records that rain
    /// is wet, and wet air pulls heat off a hull far faster than dry air of the same speed, so
    /// those figures are opinions in the way the whole of <see cref="GroundTemperature"/> is.
    ///
    /// Matched on the kind word in the name rather than on the exact subtype, for the same reason
    /// the ground table is: there are thirty-three weathers in the base game, most of them the
    /// same handful of kinds with a prefix — <c>AlienRainHeavy</c>, <c>MarsStormLight</c> — and
    /// other mods add their own. A name carrying <c>light</c> gets half the departure from calm,
    /// which is about what Keen's own light/heavy pairs differ by.
    /// </summary>
    public static class WeatherResponse
    {
        /// <summary>
        /// One weather's effect at full intensity. Every multiplier is 1 and every offset 0 for
        /// <see cref="Calm"/>, so a grid in clear air pays nothing for any of this.
        /// </summary>
        public struct Weather
        {
            /// <summary>How much colder or warmer than the clear-sky climate, K.</summary>
            public float TemperatureOffset;

            /// <summary>Share of the sun that still reaches the ground, 0..1 and occasionally more.</summary>
            public float SolarMultiplier;

            /// <summary>What it does to the wind the field would otherwise produce.</summary>
            public float WindMultiplier;

            /// <summary>What wet or dusty air does to the convective coefficient.</summary>
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
        /// The kinds, at their heavy strength, first match winning.
        ///
        /// Order is load-bearing where one word contains another: every storm has to be tested
        /// before the plain kinds it would otherwise fall into, and <c>lowwind</c> before
        /// <c>wind</c>, or <c>LowWinds</c> reads as a gale.
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
        /// What this weather is worth at full intensity. <see cref="Calm"/> for anything the table
        /// has no opinion about, which is the right answer for a weather nobody has thought about
        /// yet — and for the empty string the game returns when there is no weather at all.
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
        /// The same weather, part of the way back toward calm. Used for the light variants and by
        /// the solver for intensity, which is the same operation: a weather at a tenth of its
        /// strength is a tenth of the way from clear air to itself.
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
        /// Not a column of its own, because it is the same fact as the solar one: cloud that keeps
        /// the sun off by day keeps the heat in by night, and a sky that has stopped 90% of the
        /// sunlight has flattened the day with it. Half the swing at full overcast, none of it
        /// removed in clear air.
        /// </summary>
        public static float SwingMultiplier(Weather weather)
        {
            float swing = 0.5f + (0.5f * weather.SolarMultiplier);
            return swing < 0f ? 0f : swing;
        }
    }
}
