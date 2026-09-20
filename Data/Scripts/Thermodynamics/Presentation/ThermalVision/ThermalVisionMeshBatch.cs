using System;
using VRageMath;

namespace Thermodynamics.Presentation
{
    /// <summary>Conservative interval bound for a contiguous set of triangle planes.</summary>
    public struct ThermalVisionMeshBatch
    {
        public const int Size = 64;
        public int Start, Count;
        private Vector3 minNormal, maxNormal;
        private double minPlane;
        private bool unsafeBound;

        public void Add(int index, ThermalVisionTriangle triangle)
        {
            Vector3 normal = triangle.LocalNormal;
            double plane = Vector3D.Dot(normal, triangle.A);
            if (Count == 0)
            {
                Start = index; minNormal = maxNormal = normal; minPlane = plane;
            }
            else
            {
                minNormal = Vector3.Min(minNormal, normal);
                maxNormal = Vector3.Max(maxNormal, normal);
                minPlane = Math.Min(minPlane, plane);
            }
            // Degenerate/non-finite input keeps the original per-triangle path.
            unsafeBound |= !(normal.LengthSquared() > 1e-20f) || double.IsNaN(plane) || double.IsInfinity(plane);
            Count++;
        }

        public bool IsEntirelyBackFacing(Vector3D eye)
        {
            if (Count == 0 || unsafeBound) return false;
            double x = eye.X * (eye.X >= 0 ? maxNormal.X : minNormal.X);
            double y = eye.Y * (eye.Y >= 0 ? maxNormal.Y : minNormal.Y);
            double z = eye.Z * (eye.Z >= 0 ? maxNormal.Z : minNormal.Z);
            double upper = x + y + z - minPlane;
            // Keep uncertain/grazing bounds; false negatives only cost work, false positives lose surfaces.
            double tolerance = 1e-6 * (1 + Math.Abs(x) + Math.Abs(y) + Math.Abs(z) + Math.Abs(minPlane));
            return upper < -tolerance;
        }
    }

    public sealed class ThermalVisionMesh
    {
        public readonly ThermalVisionTriangle[] Triangles;
        public readonly ThermalVisionMeshBatch[] Batches;
        public ThermalVisionMesh(ThermalVisionTriangle[] triangles, ThermalVisionMeshBatch[] batches)
        { Triangles = triangles; Batches = batches; }
    }
}
