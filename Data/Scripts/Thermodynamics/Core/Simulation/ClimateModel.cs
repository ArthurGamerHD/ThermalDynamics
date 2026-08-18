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
    /// <item>Altitude, because air cools as it thins and a mountain top is not its valley.</item>
    /// <item>Depth, because rock does not have weather: a metre down the day is already blunted
    /// and a kilometre down there is no day at all, only the heat coming up from below.</item>
    /// </list>
    ///
    /// Every one of these produces a <em>target</em>. Nothing here integrates and nothing here
    /// remembers: <see cref="Follow"/> is the only function with a previous value in it, and it is
    /// applied once, last, to whatever the rest of this class decided the air should be heading
    /// toward. That ordering is not a style preference. Applying a scale to the running ambient
    /// instead of to its target compounds the scale against the lag on every step, and a factor of
    /// 0.977 applied four times a second against a 45 second lag settles at 14% of the intended
    /// temperature rather than 98% of it.
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
        /// The same target, cooled for how far above sea level it is.
        ///
        /// Air cools as it rises because it expands, at something near 6.5 K per kilometre on
        /// Earth. This is the term that makes a mountain colder than the plain it stands on, and
        /// without it the only thing altitude did to the climate was thin the air.
        /// </summary>
        /// <param name="target">Ambient at this latitude and hour at sea level, K.</param>
        /// <param name="altitude">Metres above the planet's mean radius. Negative below it.</param>
        /// <param name="lapseRatePerKm">How much colder a kilometre up is, K.</param>
        public static float Lapse(float target, float altitude, float lapseRatePerKm)
        {
            if (lapseRatePerKm == 0f || altitude == 0f) return target;

            float cooled = target - (lapseRatePerKm * altitude * 0.001f);
            return cooled < 0f ? 0f : cooled;
        }

        /// <summary>
        /// How much of the climate survives at this air density.
        ///
        /// Deliberately blunter than <see cref="EnvironmentSolver.AtmosphereFactor"/>, which is
        /// the curve convection and solar decay run on. Those two genuinely scale with how much
        /// air there is. Ambient does not: the top of Earth's troposphere holds a third of sea
        /// level's air and sits at 217 K, not at a third of 288. What altitude does to the air's
        /// temperature is <see cref="Lapse"/>; what density does is decide when there stops being
        /// air to have a temperature at all, and that happens at the edge of space rather than
        /// gradually all the way up.
        ///
        /// 1 - (1 - d)^8: still 99.9% at two thirds density, half gone by a twelfth, and only
        /// properly vacuum when the air is.
        /// </summary>
        public static float AmbientDensityFactor(float airDensity)
        {
            float inverse = 1f - Clamp01(airDensity);
            float squared = inverse * inverse;
            float fourth = squared * squared;
            return 1f - (fourth * fourth);
        }

        /// <summary>
        /// The target, faded toward vacuum as the air runs out.
        ///
        /// Toward <paramref name="vacuum"/> rather than toward zero, because that is where a body
        /// with nothing around it ends up — the microwave background, not absolute zero.
        /// </summary>
        public static float Thin(float target, float airDensity, float vacuum)
        {
            float share = AmbientDensityFactor(airDensity);
            return vacuum + ((target - vacuum) * share);
        }

        /// <summary>
        /// Ambient below the surface, K.
        ///
        /// Two things happen going down and they happen at very different scales. The first is
        /// that the day stops: rock is slow, so the further down a tunnel goes the less of the
        /// surface's day-night swing reaches it, until a few tens of metres in there is no day
        /// left and the temperature is simply the planet's own underground figure. The second is
        /// that the planet is hot inside. Below <see cref="PlanetThermalProperties.SealevelDeadzone"/>
        /// the rock starts warming toward <see cref="PlanetThermalProperties.CoreTemperature"/>,
        /// reaching it at the centre.
        ///
        /// The deadzone is measured from sea level rather than from the surface, which is what
        /// makes a tunnel bored into a mountainside stay cold however deep it goes: it is a long
        /// way inside the rock and still a long way above the hot part.
        /// </summary>
        /// <param name="planet">The world's own figures.</param>
        /// <param name="surface">Ambient in the open air directly above, K.</param>
        /// <param name="depth">Metres below the surface. Zero or less is not underground.</param>
        /// <param name="radius">Metres from the planet's centre to the point.</param>
        /// <param name="meanRadius">The planet's mean radius — its sea level.</param>
        public static float Underground(
            PlanetThermalProperties planet, float surface, float depth, float radius, float meanRadius)
        {
            if (planet == null) return surface;
            if (depth <= 0f) return surface;

            // ---- the day dies out -----------------------------------------------------------
            float damping = planet.UndergroundDampingDepth;
            float buried = damping <= 0f ? 1f : depth / damping;
            if (buried > 1f) buried = 1f;

            float ambient = surface + ((planet.UndergroundTemperature - surface) * buried);

            // ---- and the planet warms up ----------------------------------------------------
            float deadzone = meanRadius - planet.SealevelDeadzone;
            if (deadzone <= 0f || radius >= deadzone) return ambient;

            // Linear in the distance still to fall: nothing at the deadzone floor, all of it at
            // the centre. A planet that wants a steeper crust says so with a hotter core.
            float descended = 1f - (radius / deadzone);
            if (descended > 1f) descended = 1f;

            return ambient + ((planet.CoreTemperature - ambient) * descended);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
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
