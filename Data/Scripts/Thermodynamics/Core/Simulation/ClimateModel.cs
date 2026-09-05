using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Target air temperature outside a grid, from latitude, surface material, altitude and depth.
    /// Every function here returns a *target* and retains no state; <see cref="Follow"/> is the only
    /// one that takes a previous value, and it is applied last, to a finished target. Scaling the
    /// running ambient instead compounds against the lag on every step.
    /// See environment.md, Ambient temperature.
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
        /// Temperature offset from the surface material, K, already scaled by the world's ground
        /// influence setting. Zero ignores the surface material.
        /// </param>
        public static float Target(
            PlanetThermalProperties planet, float latitudeSine, float sunElevationSine, float groundOffset)
        {
            return Target(planet, latitudeSine, sunElevationSine, groundOffset, 1f);
        }

        /// <summary>
        /// As above, with the surface material also widening or narrowing the day-night swing.
        ///
        /// Dry ground retains little heat overnight, so a desert is both hotter by day and colder by
        /// night; snow and water damp the swing instead.
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

            // Cosine of latitude: the best incidence the sun reaches at this band of the planet.
            float latitude = (float)Math.Sqrt(Math.Max(0f, 1f - (latitudeSine * latitudeSine)));
            float drop = planet.PoleTemperatureDrop * (1f - latitude);

            float night;
            float day;

            if (groundSwing == 1f)
            {
                // Taken directly from the planet's own figures rather than routed through the mean
                // and back, which would lose precision the tests read to a tenth of a kelvin.
                night = planet.NightTemperature - drop;
                day = planet.DayTemperature - drop;
            }
            else
            {
                // The swing opens and closes about the day's mean, so widening it cools the night as
                // much as it warms the noon.
                float mean = ((planet.NightTemperature + planet.DayTemperature) * 0.5f) - drop;
                float half = (planet.DayTemperature - planet.NightTemperature) * 0.5f * groundSwing;

                night = mean - half;
                day = mean + half;
            }

            // Below the horizon the sun contributes nothing, regardless of how far below. The lag,
            // not this term, is what makes the small hours colder than dusk.
            float insolation = sunElevationSine <= 0f ? 0f : sunElevationSine;

            float target = night + ((day - night) * insolation) + groundOffset;
            return target < 0f ? 0f : target;
        }

        /// <summary>
        /// The same target, cooled for altitude above sea level — the term that makes a mountain
        /// colder than the plain below it.
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
        /// How much of the climate survives at this air density: <c>1 - (1 - d)^8</c>. Much flatter
        /// than <see cref="EnvironmentSolver.AtmosphereFactor"/>, because thin air is still air with
        /// a temperature — altitude's effect on temperature is <see cref="Lapse"/>.
        /// See environment.md, Altitude and density are two facts, not one.
        /// </summary>
        public static float AmbientDensityFactor(float airDensity)
        {
            float inverse = 1f - ThermalMath.Clamp01(airDensity);
            float squared = inverse * inverse;
            float fourth = squared * squared;
            return 1f - (fourth * fourth);
        }

        /// <summary>
        /// The target, faded towards vacuum as the air runs out. Fades to
        /// <paramref name="vacuum"/> rather than zero, which is the microwave background rather than
        /// absolute zero.
        /// </summary>
        public static float Thin(float target, float airDensity, float vacuum)
        {
            float share = AmbientDensityFactor(airDensity);
            return vacuum + ((target - vacuum) * share);
        }

        /// <summary>
        /// Ambient below the surface, K: the day's swing damped out over tens of metres, and below
        /// <see cref="PlanetThermalProperties.SealevelDeadzone"/> — measured from sea level, so a
        /// tunnel into a mountainside stays cold — the rock warming toward the core.
        /// See environment.md, Underground.
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

            // Linear in the distance remaining: zero at the deadzone floor, full at the centre. A
            // steeper crust gradient is expressed as a hotter core.
            float descended = 1f - (radius / deadzone);
            if (descended > 1f) descended = 1f;

            return ambient + ((planet.CoreTemperature - ambient) * descended);
        }

        /// <summary>
        /// Ambient a moment later, approaching <paramref name="target"/> from its current value.
        ///
        /// A first-order lag: fast while the gap is wide, slower as it closes. This is what places
        /// the warmest part of the day after noon and the coldest before dawn, without either being
        /// specified directly.
        /// </summary>
        public static float Follow(float current, float target, float seconds, float lagSeconds)
        {
            if (lagSeconds <= 0f || seconds <= 0f) return target;
            if (current <= 0f) return target;

            // 1 - e^-x, so a step of one time constant closes about 63 % of the gap at any step size.
            float closed = 1f - (float)Math.Exp(-seconds / lagSeconds);
            return current + ((target - current) * closed);
        }
    }
}
