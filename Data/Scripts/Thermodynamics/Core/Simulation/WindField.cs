using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// A wind field over a planet, which the game does not provide: it has one number per altitude,
    /// with no latitude and no direction, and used directly it parks every grid in a permanent
    /// 290 km/h wind. That figure is taken as a ceiling and this decides how much of it blows and
    /// where. A steady position-dependent map, not a simulation. See environment.md, Wind.
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

            // The band's own strength, which is what keeps this continuous across a band edge where
            // the direction reverses. See BandStrength.
            return direction * Speed(maxSpeed, weather, variation) * BandStrength(up, axis);
        }

        /// <summary>
        /// How far a band's own bearing sits off due east or west, as the tangent of the angle.
        ///
        /// Earth's trades reach the equator twenty to thirty degrees off due west and its
        /// westerlies leave the mid latitudes about thirty degrees off due east. This is the
        /// tangent of thirty, applied at the band's own strongest point and fading with it.
        /// </summary>
        public const float BandTilt = 0.5774f;

        /// <summary>
        /// The band signal at this latitude: +1 where the band blows westward, −1 where it blows
        /// eastward, and zero at the three latitudes that separate them.
        ///
        /// Three bands per hemisphere, as on Earth, taken on distance from the equator: trades,
        /// westerlies, polar easterlies. The zeros are the doldrums, the horse latitudes and the
        /// polar front, and they are zeros rather than seams — the organised circulation of a band
        /// really does die out where the next one begins.
        /// </summary>
        private static float Band(float distance)
        {
            return (float)Math.Sin(distance * 6d);
        }

        /// <summary>
        /// How much of the ceiling this latitude's circulation is worth, 0..1. Full at a band's
        /// centre and zero at its edges.
        ///
        /// **This is what makes the field continuous.** <see cref="Direction"/> is a unit vector
        /// and it reverses end for end at every band edge, because the band on the other side blows
        /// the other way — which is true and is not a wall, because the wind has died before it
        /// turns. A consumer that multiplies the two gets a smooth field; one that reads the
        /// direction alone does not, and there is no direction to read where the strength is zero.
        /// </summary>
        public static float BandStrength(Vector3 up, Vector3 axis)
        {
            if (up.LengthSquared() < 1e-6f || axis.LengthSquared() < 1e-6f) return 0f;

            float sine = Clamp(Vector3.Dot(Vector3.Normalize(up), Vector3.Normalize(axis)), -1f, 1f);
            return Math.Abs(Band(Math.Abs((float)Math.Asin(sine))));
        }

        /// <summary>
        /// Wind direction here: along the surface, in the circulation band this latitude falls in.
        ///
        /// <para>
        /// **The sideways component follows the band, not the hemisphere.** It used to be poleward
        /// everywhere, so air diverged from the equator where Earth's trades converge, and the
        /// vector flipped end for end across latitude 0 at full strength — a wall a ship could fly
        /// through. A band's meridional component is equatorward in the trades and the polar
        /// easterlies and poleward in the westerlies, which is the same alternation the zonal
        /// component already had, so both now come off one signal.
        /// </para>
        ///
        /// <para>
        /// It vanishes at the equator and at the poles rather than reversing there: the equator is
        /// where the two hemispheres' flows meet, and a meridional component that stepped across it
        /// would be the same wall in a different place.
        /// </para>
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

            float band = Band(distance);
            if (Math.Abs(band) < 1e-6f) return Vector3.Zero;

            // Toward the nearer pole, which is what "poleward" means on either side of the equator.
            Vector3 poleward = latitude < 0f ? -north : north;

            // Zero at the equator and at the pole, largest between: the meridional component is a
            // tilt on the zonal one rather than a term of its own, so it cannot survive the zonal
            // component's death at a band edge and reverse on its own.
            float tilt = BandTilt * (float)Math.Sin(distance * 2d);

            Vector3 direction = (east * -band) + (poleward * (-band * tilt));

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
