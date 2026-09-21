using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public static class WindTerrain
    {
        public const int Bearings = 8;

        public const int Radii = 2;

        public const int SampleCount = Bearings * Radii;

        public const float MaximumSpeedUp = 0.6f;

        public const float MaximumSlowDown = 0.45f;

        public const float SpeedUpPerSlope = 2f;

        public const float MaximumShelter = 0.75f;

        public const float FullShelterDegrees = 40f;

        public const float FullChannelSlope = 0.33f;

/// <summary>BearingDirection operation.</summary>
        public static Vector3 BearingDirection(int bearing, Vector3 north, Vector3 east)
        {
            double angle = (bearing % Bearings) * (2d * Math.PI / Bearings);
            return (north * (float)Math.Cos(angle)) + (east * (float)Math.Sin(angle));
        }

/// <summary>Index operation.</summary>
        public static int Index(int radius, int bearing)
        {
            return (radius * Bearings) + (bearing % Bearings);
        }

/// <summary>Saturate operation.</summary>
        public static float Saturate(float value, float bound)
        {
            if (bound <= 0f) return 0f;
            return bound * (float)Math.Tanh(value / bound);
        }

/// <summary>Relief operation.</summary>
        public static float Relief(float[] heights, float radius)
        {
            if (heights == null || heights.Length < SampleCount || radius <= 0f) return 0f;

            float total = 0f;
            for (int bearing = 0; bearing < Bearings; bearing++)
            {
                total += heights[Index(Radii - 1, bearing)];
            }

            return -(total / Bearings) / radius;
        }

/// <summary>SpeedUp operation.</summary>
        public static float SpeedUp(float relief)
        {
            float change = SpeedUpPerSlope * relief;

            change = change >= 0f
/// <summary>Saturate operation.</summary>
                ? Saturate(change, MaximumSpeedUp)
                : -Saturate(-change, MaximumSlowDown);

            return 1f + change;
        }

/// <summary>Shelter operation.</summary>
        public static float Shelter(
            float[] heights, float innerRadius, float outerRadius,
            Vector3 wind, Vector3 north, Vector3 east)
        {
            if (heights == null || heights.Length < SampleCount) return 1f;
            if (innerRadius <= 0f || outerRadius <= 0f) return 1f;
            if (wind.LengthSquared() < 1e-8f) return 1f;

            Vector3 upwind = -Vector3.Normalize(wind);

/// <summary>Bearing operation.</summary>
            double bearing = Bearing(upwind, north, east);
            float steepest = 0f;

            for (int radius = 0; radius < Radii; radius++)
            {
/// <summary>Interpolate operation.</summary>
                float height = Interpolate(heights, radius, bearing);
                if (height <= 0f) continue;

                float distance = radius == 0 ? innerRadius : outerRadius;
                float angle = (float)(Math.Atan2(height, distance) * 180d / Math.PI);

                if (angle > steepest) steepest = angle;
            }

            if (steepest <= 0f) return 1f;

/// <summary>Saturate operation.</summary>
            float share = Saturate(steepest / FullShelterDegrees, 1f);

            return 1f - (MaximumShelter * share);
        }

/// <summary>Channel operation.</summary>
        public static Vector3 Channel(
            float[] heights, float radius, Vector3 wind, Vector3 north, Vector3 east, float strength)
        {
            if (wind.LengthSquared() < 1e-8f) return Vector3.Zero;

            Vector3 direction = Vector3.Normalize(wind);
            if (heights == null || heights.Length < SampleCount) return direction;
            if (radius <= 0f || strength <= 0f) return direction;

            double cosine = 0d;
            double sine = 0d;

            for (int bearing = 0; bearing < Bearings; bearing++)
            {
                double angle = bearing * (2d * Math.PI / Bearings);
                float height = heights[Index(Radii - 1, bearing)];

                cosine += height * Math.Cos(2d * angle);
                sine += height * Math.Sin(2d * angle);
            }

            cosine *= 2d / Bearings;
            sine *= 2d / Bearings;

            double amplitude = Math.Sqrt((cosine * cosine) + (sine * sine));
            if (amplitude < 1e-4d) return direction;

/// <summary>Saturate operation.</summary>
            float confinement = Saturate((float)(amplitude / radius) / FullChannelSlope, 1f);
            confinement *= strength > 1f ? 1f : strength;

            if (confinement <= 0f) return direction;

            double high = 0.5d * Math.Atan2(sine, cosine);
            double along = high + (Math.PI * 0.5d);

            Vector3 axis = (north * (float)Math.Cos(along)) + (east * (float)Math.Sin(along));
            if (axis.LengthSquared() < 1e-8f) return direction;

            axis = Vector3.Normalize(axis);
            if (Vector3.Dot(axis, direction) < 0f) axis = -axis;

            Vector3 turned = (direction * (1f - confinement)) + (axis * confinement);
            return turned.LengthSquared() < 1e-8f ? direction : Vector3.Normalize(turned);
        }

/// <summary>Downhill operation.</summary>
        public static Vector3 Downhill(
            float[] heights, float radius, Vector3 north, Vector3 east, out float slope)
        {
            slope = 0f;

            if (heights == null || heights.Length < SampleCount) return Vector3.Zero;
            if (radius <= 0f) return Vector3.Zero;

            double cosine = 0d;
            double sine = 0d;

            for (int bearing = 0; bearing < Bearings; bearing++)
            {
                double angle = bearing * (2d * Math.PI / Bearings);
                float height = heights[Index(Radii - 1, bearing)];

                cosine += height * Math.Cos(angle);
                sine += height * Math.Sin(angle);
            }

            cosine *= 2d / Bearings;
            sine *= 2d / Bearings;

            double amplitude = Math.Sqrt((cosine * cosine) + (sine * sine));
            if (amplitude < 1e-4d) return Vector3.Zero;

            slope = (float)(amplitude / radius);

            double high = Math.Atan2(sine, cosine);
            double down = high + Math.PI;

            Vector3 direction = (north * (float)Math.Cos(down)) + (east * (float)Math.Sin(down));
            return direction.LengthSquared() < 1e-8f ? Vector3.Zero : Vector3.Normalize(direction);
        }

/// <summary>Bearing operation.</summary>
        private static double Bearing(Vector3 direction, Vector3 north, Vector3 east)
        {
            double angle = Math.Atan2(Vector3.Dot(direction, east), Vector3.Dot(direction, north));
            if (angle < 0d) angle += 2d * Math.PI;
            return angle;
        }

/// <summary>Interpolate operation.</summary>
        private static float Interpolate(float[] heights, int radius, double bearing)
        {
            double step = 2d * Math.PI / Bearings;
            double position = bearing / step;

            int low = (int)Math.Floor(position);
            float fraction = (float)(position - low);

            int a = ((low % Bearings) + Bearings) % Bearings;
            int b = (a + 1) % Bearings;

            return (heights[Index(radius, a)] * (1f - fraction))
                + (heights[Index(radius, b)] * fraction);
        }
    }
}
