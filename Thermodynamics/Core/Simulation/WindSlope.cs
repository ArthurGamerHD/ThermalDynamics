using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public static class WindSlope
    {
        public const float AnabaticSpeed = 4f;

        public const float KatabaticSpeed = 6f;

        public const float FullSlope = 0.25f;

        public const float AnabaticDepth = 300f;

        public const float KatabaticDepth = 80f;

        public const float CalmSpeed = 5f;

/// <summary>Velocity operation.</summary>
        public static Vector3 Velocity(
            Vector3 downhill, float slope, float heating, float height,
            float ambientSpeed, float strength)
        {
            if (strength <= 0f) return Vector3.Zero;
            if (slope <= 0f) return Vector3.Zero;
            if (downhill.LengthSquared() < 1e-8f) return Vector3.Zero;

            if (heating < 0f) heating = 0f;
            if (heating > 1f) heating = 1f;
            if (height < 0f) height = 0f;

            float swing = (2f * heating) - 1f;
            if (swing > -1e-4f && swing < 1e-4f) return Vector3.Zero;

            bool upslope = swing > 0f;

            float depth = upslope ? AnabaticDepth : KatabaticDepth;
            if (height >= depth) return Vector3.Zero;

            float share = slope / FullSlope;
            if (share > 1f) share = 1f;

            float speed = (upslope ? AnabaticSpeed : KatabaticSpeed)
                * Math.Abs(swing)
                * share
                * (1f - (height / depth))
/// <summary>Suppression operation.</summary>
                * Suppression(ambientSpeed)
                * (strength > 1f ? 1f : strength);

            if (speed <= 0f) return Vector3.Zero;

            Vector3 direction = upslope ? -downhill : downhill;
            return Vector3.Normalize(direction) * speed;
        }

/// <summary>Suppression operation.</summary>
        public static float Suppression(float ambientSpeed)
        {
            if (ambientSpeed <= 0f) return 1f;
            return CalmSpeed / (CalmSpeed + ambientSpeed);
        }

/// <summary>Sense operation.</summary>
        public static int Sense(float heating, float height)
        {
            float swing = (2f * heating) - 1f;
            if (swing > 0f) return height < AnabaticDepth ? 1 : 0;
            if (swing < 0f) return height < KatabaticDepth ? -1 : 0;
            return 0;
        }
    }
}
