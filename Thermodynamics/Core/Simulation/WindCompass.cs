using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public static class WindCompass
    {
        private const float MinimumLengthSquared = 1e-4f;

/// <summary>Bearing operation.</summary>
        public static bool Bearing(Vector3 wind, Vector3 forward, Vector3 up, out float degrees)
        {
            degrees = 0f;

            if (up.LengthSquared() < MinimumLengthSquared) return false;
            up = Vector3.Normalize(up);

/// <summary>Flatten operation.</summary>
            Vector3 windFlat = Flatten(wind, up);
/// <summary>Flatten operation.</summary>
            Vector3 faceFlat = Flatten(forward, up);

            if (windFlat.LengthSquared() < MinimumLengthSquared) return false;
            if (faceFlat.LengthSquared() < MinimumLengthSquared) return false;

            windFlat = Vector3.Normalize(windFlat);
            faceFlat = Vector3.Normalize(faceFlat);

            Vector3 right = Vector3.Cross(faceFlat, up);
            if (right.LengthSquared() < MinimumLengthSquared) return false;
            right = Vector3.Normalize(right);

            double angle = Math.Atan2(Vector3.Dot(windFlat, right), Vector3.Dot(windFlat, faceFlat));
/// <summary>Normalise operation.</summary>
            degrees = Normalise((float)(angle * 180d / Math.PI));
            return true;
        }

/// <summary>Along operation.</summary>
        public static float Along(Vector3 wind, Vector3 forward, Vector3 up)
        {
            if (up.LengthSquared() < MinimumLengthSquared) return 0f;
            up = Vector3.Normalize(up);

/// <summary>Flatten operation.</summary>
            Vector3 faceFlat = Flatten(forward, up);
            if (faceFlat.LengthSquared() < MinimumLengthSquared) return 0f;

            return Vector3.Dot(Flatten(wind, up), Vector3.Normalize(faceFlat));
        }

/// <summary>Across operation.</summary>
        public static float Across(Vector3 wind, Vector3 forward, Vector3 up)
        {
            if (up.LengthSquared() < MinimumLengthSquared) return 0f;
            up = Vector3.Normalize(up);

/// <summary>Flatten operation.</summary>
            Vector3 faceFlat = Flatten(forward, up);
            if (faceFlat.LengthSquared() < MinimumLengthSquared) return 0f;

            Vector3 right = Vector3.Cross(Vector3.Normalize(faceFlat), up);
            if (right.LengthSquared() < MinimumLengthSquared) return 0f;

            return Vector3.Dot(Flatten(wind, up), Vector3.Normalize(right));
        }

/// <summary>Sector operation.</summary>
        public static int Sector(float degrees)
        {
/// <summary>Normalise operation.</summary>
            degrees = Normalise(degrees);

            return ((int)((degrees + 22.5f) / 45f)) & 7;
        }

/// <summary>SectorName operation.</summary>
        public static string SectorName(int sector)
        {
            switch (sector & 7)
            {
                case 0: return "ahead";
                case 1: return "ahead right";
                case 2: return "right";
                case 3: return "astern right";
                case 4: return "astern";
                case 5: return "astern left";
                case 6: return "left";
                default: return "ahead left";
            }
        }

/// <summary>Flatten operation.</summary>
        private static Vector3 Flatten(Vector3 vector, Vector3 up)
        {
            return vector - (up * Vector3.Dot(vector, up));
        }

/// <summary>Normalise operation.</summary>
        public static float Normalise(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return 0f;

            degrees = degrees % 360f;
            return degrees < 0f ? degrees + 360f : degrees;
        }
    }
}
