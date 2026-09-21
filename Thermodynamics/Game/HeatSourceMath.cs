using System;
using VRageMath;

namespace Thermodynamics
{
    public static class HeatSourceMath
    {
        public const double NearFieldMetres = 1.0;

/// <summary>Irradiance operation.</summary>
        public static float Irradiance(Vector3D source, float watts, float range, Vector3D at)
        {
            if (watts <= 0f || range <= 0f) return 0f;

            double distanceSquared = (source - at).LengthSquared();
            if (distanceSquared > range * (double)range) return 0f;

            if (distanceSquared < NearFieldMetres) distanceSquared = NearFieldMetres;

            return (float)(watts / (4.0 * Math.PI * distanceSquared));
        }

/// <summary>Direction operation.</summary>
        public static Vector3 Direction(Vector3D source, Vector3D at, ref MatrixD worldToLocal)
        {
            Vector3D delta = source - at;
            if (delta.LengthSquared() <= 0.0) return Vector3.Zero;

            return (Vector3)Vector3D.TransformNormal(Vector3D.Normalize(delta), worldToLocal);
        }
    }
}
