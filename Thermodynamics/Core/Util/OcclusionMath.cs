using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public static class OcclusionMath
    {
/// <summary>VisualSize operation.</summary>
        public static double VisualSize(double distance, double radius)
        {
            if (distance <= 0d) return Math.PI;
            return 2d * Math.Atan(radius / (2d * distance));
        }

/// <summary>OcclusionThreshold operation.</summary>
        public static double OcclusionThreshold(double visualSize)
        {
            return -1d + (0.85d * visualSize * visualSize * visualSize);
        }

/// <summary>IsOccludedBySphere operation.</summary>
        public static bool IsOccludedBySphere(Vector3D observer, Vector3D bodyCentre, double bodyRadius, Vector3 sunDirection)
        {
            Vector3D local = observer - bodyCentre;
            double distance = local.Length();
            if (distance <= 0d) return true;

            Vector3D toBody = local / distance;
            double dot = Vector3D.Dot(toBody, (Vector3D)sunDirection);
            return dot < OcclusionThreshold(VisualSize(distance, bodyRadius));
        }
    }
}
