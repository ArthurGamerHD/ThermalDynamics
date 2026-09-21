using System;
using VRageMath;

namespace Thermodynamics.Presentation
{
    public struct ThermalVisionMeshBatch
    {
        public const int Size = 64;
        public int Start, Count;
        private Vector3 minNormal, maxNormal;
        private double minPlane;
        private bool unsafeBound;

/// <summary>Adds a .</summary>
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
            unsafeBound |= !(normal.LengthSquared() > 1e-20f) || double.IsNaN(plane) || double.IsInfinity(plane);
            Count++;
        }

/// <summary>IsEntirelyBackFacing operation.</summary>
        public bool IsEntirelyBackFacing(Vector3D eye)
        {
            if (Count == 0 || unsafeBound) return false;
            double x = eye.X * (eye.X >= 0 ? maxNormal.X : minNormal.X);
            double y = eye.Y * (eye.Y >= 0 ? maxNormal.Y : minNormal.Y);
            double z = eye.Z * (eye.Z >= 0 ? maxNormal.Z : minNormal.Z);
            double upper = x + y + z - minPlane;
            double tolerance = 1e-6 * (1 + Math.Abs(x) + Math.Abs(y) + Math.Abs(z) + Math.Abs(minPlane));
            return upper < -tolerance;
        }
    }

    public sealed class ThermalVisionMesh
    {
        public readonly ThermalVisionTriangle[] Triangles;
        public readonly ThermalVisionMeshBatch[] Batches;
/// <summary>ThermalVisionMesh operation.</summary>
        public ThermalVisionMesh(ThermalVisionTriangle[] triangles, ThermalVisionMeshBatch[] batches)
        { Triangles = triangles; Batches = batches; }
    }
}
