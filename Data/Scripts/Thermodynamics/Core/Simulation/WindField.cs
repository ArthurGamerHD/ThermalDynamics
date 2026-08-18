using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// A wind field over a planet, because the game does not have one.
    ///
    /// What the game offers is <c>MyPlanet.GetWindSpeed</c>, which is the planet definition's
    /// maximum wind speed scaled by air density — a constant for a given altitude, the same at the
    /// pole as at the equator, in the lee of a mountain as on an open plain, and with no direction
    /// at all. On an earthlike world it reads 80 m/s at sea level: not weather, a rating, and the
    /// number wind turbines are balanced against.
    ///
    /// Taken literally it does damage. Eighty metres a second is a hurricane twice over, so a
    /// parked ship reads as flying at 290 km/h: it trips the friction threshold and heats up
    /// standing still, and convection runs at nearly twice its still-air rate everywhere on the
    /// planet.
    ///
    /// So the game's figure is treated as what it is — a ceiling — and the field below decides how
    /// much of it blows and which way. The pattern is Earth's, because Earth's is the one people
    /// recognise: easterly trades either side of the equator, westerlies in the middle latitudes,
    /// easterlies again at the poles. It is not a simulation of anything. It is a map that is
    /// steady, cheap, different in different places, and recognisable when you fly across it.
    /// </summary>
    public static class WindField
    {
        /// <summary>
        /// Share of the planet's maximum wind that blows in ordinary weather.
        ///
        /// A tenth of a hurricane is a stiff breeze, which is what most of a planet has most of the
        /// time. Weather takes it up from there.
        /// </summary>
        public const float CalmFraction = 0.12f;

        /// <summary>Share blowing in the worst weather the game reports.</summary>
        public const float StormFraction = 0.55f;

        /// <summary>
        /// Wind at a point, in world space.
        /// </summary>
        /// <param name="up">Away from the planet's centre at this point, normalised.</param>
        /// <param name="axis">The planet's own north, normalised.</param>
        /// <param name="maxSpeed">The game's wind speed for this point: the ceiling, not the wind.</param>
        /// <param name="weather">Weather intensity here, 0..1.</param>
        /// <param name="variation">
        /// A steady per-place value, 0..1, that keeps one valley windier than the next. Anything
        /// deterministic in position will do; the field does not care where it came from.
        /// </param>
        public static Vector3 Velocity(
            Vector3 up, Vector3 axis, float maxSpeed, float weather, float variation)
        {
            Vector3 direction = Direction(up, axis);
            if (direction.LengthSquared() < 1e-6f) return Vector3.Zero;

            return direction * Speed(maxSpeed, weather, variation);
        }

        /// <summary>
        /// Which way the wind blows here: along the surface, in the band this latitude falls in.
        /// </summary>
        public static Vector3 Direction(Vector3 up, Vector3 axis)
        {
            if (up.LengthSquared() < 1e-6f || axis.LengthSquared() < 1e-6f) return Vector3.Zero;

            up = Vector3.Normalize(up);
            axis = Vector3.Normalize(axis);

            Vector3 east = Vector3.Cross(axis, up);

            // Standing on a pole there is no east: every direction is south. Wind there is a
            // circle around the axis and any tangent will do, so the field hands back nothing
            // rather than a normalised rounding error.
            if (east.LengthSquared() < 1e-6f) return Vector3.Zero;

            east = Vector3.Normalize(east);
            Vector3 north = Vector3.Normalize(Vector3.Cross(up, east));

            float sine = Clamp(Vector3.Dot(up, axis), -1f, 1f);
            float latitude = (float)Math.Asin(sine);
            float distance = Math.Abs(latitude);

            // Three bands per hemisphere, the way Earth has them: trades blowing west out to 30
            // degrees, westerlies to 60, polar easterlies beyond. Taken on the distance from the
            // equator, so both hemispheres have westerlies in their middle latitudes as Earth does.
            //
            // The band is a bearing rather than a pair of components. Mixing a fixed sideways
            // term into a fading one and normalising the result makes the wind snap round at every
            // band edge, because near the calms the sideways term is all that is left. Turning the
            // bearing instead, the wind swings east to north to west and back through the whole
            // pattern, which is both continuous and what it does.
            double bearing = (Math.PI / 2d) + ((Math.PI / 2d) * Math.Sin(distance * 6d));

            Vector3 alongMeridian = latitude < 0f ? -north : north;

            Vector3 direction =
                (east * (float)Math.Cos(bearing)) + (alongMeridian * (float)Math.Sin(bearing));

            return direction.LengthSquared() < 1e-6f ? Vector3.Zero : Vector3.Normalize(direction);
        }

        /// <summary>How hard it blows here, m/s.</summary>
        public static float Speed(float maxSpeed, float weather, float variation)
        {
            if (maxSpeed <= 0f) return 0f;

            weather = Clamp(weather, 0f, 1f);
            variation = Clamp(variation, 0f, 1f);

            // Calm to storm on the weather, and a steady spread either side of that so two places
            // in the same weather are not the same.
            float share = CalmFraction + ((StormFraction - CalmFraction) * weather);
            share *= 0.6f + (0.8f * variation);

            return maxSpeed * Clamp(share, 0f, 1f);
        }

        private static float Clamp(float value, float low, float high)
        {
            if (value < low) return low;
            if (value > high) return high;
            return value;
        }

        /// <summary>
        /// A steady value per place, 0..1, from the position alone.
        ///
        /// Deterministic and continuous enough that flying a kilometre changes the wind a little
        /// rather than not at all — the point is that the map has texture, not that the texture
        /// means anything.
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
