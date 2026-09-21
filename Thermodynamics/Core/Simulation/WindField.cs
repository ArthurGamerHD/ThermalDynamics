using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public static class WindField
    {
        public const float CalmFraction = 0.12f;

        public const float StormFraction = 0.55f;

/// <summary>Velocity operation.</summary>
        public static Vector3 Velocity(
            Vector3 up, Vector3 axis, float maxSpeed, float weather, float variation)
        {
/// <summary>Direction operation.</summary>
            Vector3 direction = Direction(up, axis);
            if (direction.LengthSquared() < 1e-6f) return Vector3.Zero;

            return direction * Speed(maxSpeed, weather, variation) * BandStrength(up, axis);
        }

        public const float BandTilt = 0.5774f;

/// <summary>Band operation.</summary>
        private static float Band(float distance)
        {
            return (float)Math.Sin(distance * 6d);
        }

/// <summary>BandStrength operation.</summary>
        public static float BandStrength(Vector3 up, Vector3 axis)
        {
            if (up.LengthSquared() < 1e-6f || axis.LengthSquared() < 1e-6f) return 0f;

/// <summary>Clamp operation.</summary>
            float sine = Clamp(Vector3.Dot(Vector3.Normalize(up), Vector3.Normalize(axis)), -1f, 1f);
            return Math.Abs(Band(Math.Abs((float)Math.Asin(sine))));
        }

/// <summary>Direction operation.</summary>
        public static Vector3 Direction(Vector3 up, Vector3 axis)
        {
            if (up.LengthSquared() < 1e-6f || axis.LengthSquared() < 1e-6f) return Vector3.Zero;

            up = Vector3.Normalize(up);
            axis = Vector3.Normalize(axis);

            Vector3 east = Vector3.Cross(axis, up);

            if (east.LengthSquared() < 1e-6f) return Vector3.Zero;

            east = Vector3.Normalize(east);
            Vector3 north = Vector3.Normalize(Vector3.Cross(up, east));

/// <summary>Clamp operation.</summary>
            float sine = Clamp(Vector3.Dot(up, axis), -1f, 1f);
            float latitude = (float)Math.Asin(sine);
            float distance = Math.Abs(latitude);

/// <summary>Band operation.</summary>
            float band = Band(distance);
            if (Math.Abs(band) < 1e-6f) return Vector3.Zero;

            Vector3 poleward = latitude < 0f ? -north : north;

            float tilt = BandTilt * (float)Math.Sin(distance * 2d);

            Vector3 direction = (east * -band) + (poleward * (-band * tilt));

            return direction.LengthSquared() < 1e-6f ? Vector3.Zero : Vector3.Normalize(direction);
        }

/// <summary>Speed operation.</summary>
        public static float Speed(float maxSpeed, float weather, float variation)
        {
/// <summary>Speed operation.</summary>
            return Speed(maxSpeed, weather, variation, 1f);
        }

/// <summary>Speed operation.</summary>
        public static float Speed(float maxSpeed, float weather, float variation, float weatherWind)
        {
            if (maxSpeed <= 0f) return 0f;

/// <summary>Clamp operation.</summary>
            weather = Clamp(weather, 0f, 1f);
/// <summary>Clamp operation.</summary>
            variation = Clamp(variation, 0f, 1f);

            float range = StormFraction - CalmFraction;
            float above = range * weather * (weatherWind < 0f ? 0f : weatherWind);
            if (above > range) above = range;

            float share = CalmFraction + above;
            share *= 0.6f + (0.8f * variation);

            return maxSpeed * Clamp(share, 0f, 1f);
        }

/// <summary>Clamp operation.</summary>
        private static float Clamp(float value, float low, float high)
        {
            if (value < low) return low;
            if (value > high) return high;
            return value;
        }

/// <summary>Variation operation.</summary>
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
