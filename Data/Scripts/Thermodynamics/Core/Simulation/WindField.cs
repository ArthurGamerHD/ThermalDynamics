using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// A wind field over a planet, which the game does not provide.
    ///
    /// <c>MyPlanet.GetWindSpeed</c> returns the planet definition's maximum wind speed scaled by
    /// air density: a constant for a given altitude, identical at the pole and the equator, with no
    /// direction. On an earthlike world that is 80 m/s at sea level — a rating that wind turbines
    /// are balanced against rather than a wind speed.
    ///
    /// Used directly it places a parked grid in a permanent 290 km/h wind, tripping the friction
    /// threshold and running convection at nearly twice its still-air rate everywhere.
    ///
    /// The game's figure is therefore treated as a ceiling, and this field decides how much of it
    /// blows and in which direction. The pattern follows Earth's: easterly trades either side of
    /// the equator, westerlies in the middle latitudes, easterlies again at the poles. It is not a
    /// simulation — it is a steady, cheap, position-dependent map.
    /// </summary>
    public static class WindField
    {
        /// <summary>
        /// Share of the planet's maximum wind that blows in clear weather. Weather scales up from
        /// here towards <see cref="StormShare"/>.
        /// </summary>
        public const float CalmFraction = 0.12f;

        /// <summary>Share blowing in the worst weather the game reports.</summary>
        public const float StormFraction = 0.55f;

        /// <summary>Wind at a point, in world space.</summary>
        /// <param name="up">Away from the planet's centre at this point, normalised.</param>
        /// <param name="axis">The planet's own north, normalised.</param>
        /// <param name="maxSpeed">The game's wind figure for this point, used as a ceiling.</param>
        /// <param name="weather">Weather intensity here, 0..1.</param>
        /// <param name="variation">
        /// A steady per-position value, 0..1, giving one place more wind than another. Any function
        /// deterministic in position is acceptable.
        /// </param>
        public static Vector3 Velocity(
            Vector3 up, Vector3 axis, float maxSpeed, float weather, float variation)
        {
            Vector3 direction = Direction(up, axis);
            if (direction.LengthSquared() < 1e-6f) return Vector3.Zero;

            return direction * Speed(maxSpeed, weather, variation);
        }

        /// <summary>
        /// Wind direction here: along the surface, in the circulation band this latitude falls in.
        /// </summary>
        public static Vector3 Direction(Vector3 up, Vector3 axis)
        {
            if (up.LengthSquared() < 1e-6f || axis.LengthSquared() < 1e-6f) return Vector3.Zero;

            up = Vector3.Normalize(up);
            axis = Vector3.Normalize(axis);

            Vector3 east = Vector3.Cross(axis, up);

            // At a pole there is no east: every direction is south, so any tangent would do.
            // Returns zero rather than a normalised rounding error.
            if (east.LengthSquared() < 1e-6f) return Vector3.Zero;

            east = Vector3.Normalize(east);
            Vector3 north = Vector3.Normalize(Vector3.Cross(up, east));

            float sine = Clamp(Vector3.Dot(up, axis), -1f, 1f);
            float latitude = (float)Math.Asin(sine);
            float distance = Math.Abs(latitude);

            // Three bands per hemisphere, as on Earth: trades blowing west out to 30 degrees,
            // westerlies to 60, polar easterlies beyond. Taken on distance from the equator, so both
            // hemispheres carry westerlies in their middle latitudes.
            //
            // The band is expressed as a bearing rather than a pair of components. Mixing a fixed
            // sideways term into a fading one and normalising makes the wind jump at every band
            // edge, because near the calms the sideways term is all that remains. Rotating the
            // bearing instead keeps the direction continuous across the whole pattern.
            double bearing = (Math.PI / 2d) + ((Math.PI / 2d) * Math.Sin(distance * 6d));

            Vector3 alongMeridian = latitude < 0f ? -north : north;

            Vector3 direction =
                (east * (float)Math.Cos(bearing)) + (alongMeridian * (float)Math.Sin(bearing));

            return direction.LengthSquared() < 1e-6f ? Vector3.Zero : Vector3.Normalize(direction);
        }

        /// <summary>How hard it blows here, m/s.</summary>
        public static float Speed(float maxSpeed, float weather, float variation)
        {
            return Speed(maxSpeed, weather, variation, 1f);
        }

        /// <summary>
        /// As above, with the specific weather's effect on wind applied.
        ///
        /// Intensity alone is insufficient: fog and a sandstorm are both weather at full strength
        /// and one of them is still. The multiplier is the game's <c>WindOutputModifier</c> for the
        /// effect over the grid, already faded in with its intensity.
        /// </summary>
        public static float Speed(float maxSpeed, float weather, float variation, float weatherWind)
        {
            if (maxSpeed <= 0f) return 0f;
            if (weatherWind < 0f) weatherWind = 0f;

            weather = Clamp(weather, 0f, 1f);
            variation = Clamp(variation, 0f, 1f);

            // Interpolates calm to storm on the weather, with a steady per-position spread so two
            // places under the same weather differ.
            float share = CalmFraction + ((StormFraction - CalmFraction) * weather);
            share *= 0.6f + (0.8f * variation);
            share *= weatherWind;

            return maxSpeed * Clamp(share, 0f, 1f);
        }

        private static float Clamp(float value, float low, float high)
        {
            if (value < low) return low;
            if (value > high) return high;
            return value;
        }

        /// <summary>
        /// A steady value per position, 0..1, derived from the position alone. Deterministic and
        /// continuous enough that moving a kilometre changes the wind slightly.
        /// </summary>
        public static float Variation(Vector3D position, double scale = 900d)
        {
            if (scale <= 0d) scale = 1d;

            double x = position.X / scale;
            double y = position.Y / scale;
            double z = position.Z / scale;

            double sum = Math.Sin(x) + Math.Sin(y * 1.7d + 1.3d) + Math.Sin(z * 0.6d + 2.9d);

            return (float)((sum + 3d) / 6d);
        }
    }
}
