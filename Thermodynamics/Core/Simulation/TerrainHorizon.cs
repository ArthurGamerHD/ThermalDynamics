using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public static class TerrainHorizon
    {
        public const double NearDistance = 50d;

        public const double Tolerance = 2d;


        public static bool Occluded(
            Vector3D origin,
            Vector3D sunDirection,
            Vector3D planetCentre,
            double range,
            int samples,
            Func<Vector3D, double> surfaceRadius)
        {
            if (surfaceRadius == null || samples < 1 || range <= NearDistance) return false;

            if (sunDirection.LengthSquared() < 1e-12) return false;
            sunDirection = Vector3D.Normalize(sunDirection);

            double ratio = Math.Pow(range / NearDistance, 1d / samples);
            double distance = NearDistance;

            for (int i = 0; i < samples; i++)
            {
                distance *= ratio;

                Vector3D point = origin + (sunDirection * distance);
                double height = (point - planetCentre).Length();

                if (height + Tolerance < surfaceRadius(point)) return true;
            }

            return false;
        }
    }
}
