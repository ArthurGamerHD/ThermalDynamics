using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    public static class SolarOcclusionSampler
    {
        public const int MaxSamples = 9;

        public const double CornerInset = 0.6;

        private static readonly Vector3D[] Corners = new Vector3D[]
        {
/// <summary>Vector3D operation.</summary>
            new Vector3D(-1, -1, -1),
/// <summary>Vector3D operation.</summary>
            new Vector3D(1, 1, 1),
/// <summary>Vector3D operation.</summary>
            new Vector3D(-1, 1, -1),
/// <summary>Vector3D operation.</summary>
            new Vector3D(1, -1, 1),
/// <summary>Vector3D operation.</summary>
            new Vector3D(-1, -1, 1),
/// <summary>Vector3D operation.</summary>
            new Vector3D(1, 1, -1),
/// <summary>Vector3D operation.</summary>
            new Vector3D(-1, 1, 1),
/// <summary>Vector3D operation.</summary>
            new Vector3D(1, -1, -1),
        };

/// <summary>Points operation.</summary>
        public static void Points(BoundingBoxD bounds, int samples, List<Vector3D> results)
        {
            if (results == null) return;

            results.Clear();

            if (samples > MaxSamples) samples = MaxSamples;
            if (samples < 1) samples = 1;

            Vector3D centre = bounds.Center;
            results.Add(centre);

            Vector3D reach = bounds.HalfExtents * CornerInset;

            for (int i = 0; i < samples - 1; i++)
            {
                results.Add(centre + (Corners[i] * reach));
            }
        }
    }
}
