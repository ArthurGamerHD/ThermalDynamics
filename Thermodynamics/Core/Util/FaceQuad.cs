using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public static class FaceQuad
    {
/// <summary>Tangents operation.</summary>
        public static void Tangents(int face, out Vector3 left, out Vector3 up)
        {
            switch (Face.Axis(face))
            {
                case 0:
                    left = Vector3.Up;
                    up = Vector3.Backward;
                    return;
                case 1:
                    left = Vector3.Right;
                    up = Vector3.Backward;
                    return;
                default:
                    left = Vector3.Right;
                    up = Vector3.Up;
                    return;
            }
        }

/// <summary>Extent operation.</summary>
        public static float Extent(ref Vector3 half, ref Vector3 axis)
        {
            return (half.X * Math.Abs(axis.X))
                 + (half.Y * Math.Abs(axis.Y))
                 + (half.Z * Math.Abs(axis.Z));
        }

/// <summary>HalfExtents operation.</summary>
        public static Vector3 HalfExtents(Vector3I min, Vector3I max, float gridSize)
        {
            return ((Vector3)(max - min + Vector3I.One)) * (gridSize * 0.5f);
        }
    }
}
