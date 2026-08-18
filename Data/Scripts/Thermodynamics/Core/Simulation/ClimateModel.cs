using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// What the air outside a grid should be, given where on the planet it is and what the sun is
    /// doing.
    ///
    /// The model this replaces had one pair of temperatures for a whole world and interpolated
    /// between them on the sun's height. Measured against a test world it produced a 9 K day-night
    /// swing on a desert, a snowfield sitting at +4 to +14 C, and the same climate at 7 degrees of
    /// latitude as at 41 — every difference between those sites came from air density, which is a
    /// coincidence of altitude rather than a climate.
    ///
    /// Three terms fix that, and each is separately switchable because each is a guess about feel
    /// rather than a law:
    ///
    /// <list type="bullet">
    /// <item>Latitude, because the poles get their sunlight at a glancing angle all year.</item>
    /// <item>The ground itself, because snow is not warm and sand is not cool, and the game will
    /// tell us which we are standing on.</item>
    /// <item>Lag, because air takes hours to answer the sun. Without it the hottest moment of the
    /// day is exactly noon, which is wrong everywhere on Earth.</item>
    /// </list>
    /// </summary>
    public static class ClimateModel
    {
        /// <summary>
        /// The ambient this place is heading toward, K, before the atmosphere thins it.
        /// </summary>
        /// <param name="planet">The world's own climate figures, which are its equatorial ones.</param>
        /// <param name="latitudeSine">Sine of latitude: 0 at the equator, ±1 at the poles.</param>
        /// <param name="sunElevationSine">
        /// Sine of the sun's height above the horizon: negative at night, 1 with the sun overhead.
        /// </param>
        /// <param name="groundOffset">
        /// What the ground underfoot is worth, K. Already scaled by whatever influence the world
        /// gives it, so zero means "do not care what it is made of".
        /// </param>
        public static float Target(
            PlanetThermalProperties planet, float latitudeSine, float sunElevationSine, float groundOffset)
        {
            return Target(planet, latitudeSine, sunElevationSine, groundOffset, 1f);
        }

        /// <summary>
        /// As above, with the ground also widening or narrowing the day-night swing.
        ///
        /// A desert is not simply hot: it is hot by day and cold by night, because dry sand holds
        /// nothing overnight. Snow and water are the opposite, and flat days are as much a part of
        /// their character as the cold is.
        /// </summary>
        public static float Target(
            PlanetThermalProperties planet,
            float latitudeSine,
            float sunElevationSine,
            float groundOffset,
            float groundSwing)
        {
            if (planet == null) return 0f;
            if (groundSwing < 0f) groundSwing = 0f;

            // Cosine of latitude: how square the sun gets to this band of the planet at its best.
            float latitude = (float)Math.Sqrt(Math.Max(0f, 1f - (latitudeSine * latitudeSine)));
            float drop = planet.PoleTemperatureDrop * (1f - latitude);

            float night;
            float day;

            if (groundSwing == 1f)
            {
                // The ordinary case, taken straight from the planet's own figures. Routing it
                // through the mean and back costs a bit of precision for nothing, and the mod's
                // own tests read these values to a tenth of a kelvin.
                night = planet.NightTemperature - drop;
                day = planet.DayTemperature - drop;
            }
            else
            {
                // The swing opens and closes about the day's mean, so widening it cools the night
                // as much as it warms the noon — which is the whole character of a desert.
                float mean = ((planet.NightTemperature + planet.DayTemperature) * 0.5f) - drop;
                float half = (planet.DayTemperature - planet.NightTemperature) * 0.5f * groundSwing;

                night = mean - half;
                day = mean + half;
            }

            // Night is night: below the horizon the sun contributes nothing, and how far below is
            // not the question. What makes the small hours colder than dusk is the lag, not this.
            float insolation = sunElevationSine <= 0f ? 0f : sunElevationSine;

            float target = night + ((day - night) * insolation) + groundOffset;
            return target < 0f ? 0f : target;
        }

        /// <summary>
        /// Ambient a moment later, chasing <paramref name="target"/> from where it is now.
        ///
        /// A first-order lag, which is the shape air has: fast while the gap is wide, slow as it
        /// closes. The consequence worth having is that the warmest part of the day lands after
        /// noon and the coldest lands before dawn, without either being written down anywhere.
        /// </summary>
        public static float Follow(float current, float target, float seconds, float lagSeconds)
        {
            if (lagSeconds <= 0f || seconds <= 0f) return target;
            if (current <= 0f) return target;

            // 1 - e^-x, so a step of one lag closes about 63% of the gap however big the step is.
            float closed = 1f - (float)Math.Exp(-seconds / lagSeconds);
            return current + ((target - current) * closed);
        }
    }
}
