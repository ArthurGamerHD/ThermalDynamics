using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Analytic sun-occlusion test for spherical bodies. Cheaper than a raycast and stable at
    /// planetary scale, where a ray would need millions of metres of travel.
    /// </summary>
    public static class OcclusionMath
    {
        /// <summary>
        /// Angular size of a sphere of <paramref name="radius"/> seen from
        /// <paramref name="distance"/> away, in radians.
        /// </summary>
        public static double VisualSize(double distance, double radius)
        {
            if (distance <= 0d) return Math.PI;
            return 2d * Math.Atan(radius / (2d * distance));
        }

        /// <summary>
        /// The dot product below which a body eclipses the sun — the observer's horizon as a dot
        /// product. **A fitted curve rather than a derivation**: nothing about the cube or the 0.85 is
        /// physical, they are what put sunset in the right place at the radii the game uses.
        /// </summary>
        public static double OcclusionThreshold(double visualSize)
        {
            return -1d + (0.85d * visualSize * visualSize * visualSize);
        }

        /// <summary>
        /// True when a sphere at <paramref name="bodyCentre"/> of <paramref name="bodyRadius"/>
        /// blocks the sun as seen from <paramref name="observer"/>.
        /// </summary>
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
