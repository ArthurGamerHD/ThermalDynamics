using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The quad that covers one face of a block: where its centre sits, which way its two edges
    /// run, and how long they are.
    ///
    /// <para>
    /// Two things draw a block's face — the solar debug overlay and the glow — and both need the
    /// same four answers from the same block extents. Kept here rather than in either of them
    /// because a second copy of this arithmetic is a copy that can disagree, and because arithmetic
    /// in <c>Core</c> can be tested without a session.
    /// </para>
    /// </summary>
    public static class FaceQuad
    {
        /// <summary>
        /// The two in-plane unit axes of a face, in block-local space.
        ///
        /// Which pair is chosen does not matter — a quad is symmetric under swapping them — only
        /// that both are perpendicular to the face and to each other.
        /// </summary>
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

        /// <summary>
        /// The block's half-extent along a single-axis unit vector: how far its surface is from its
        /// centre in that direction.
        /// </summary>
        public static float Extent(ref Vector3 half, ref Vector3 axis)
        {
            return (half.X * Math.Abs(axis.X))
                 + (half.Y * Math.Abs(axis.Y))
                 + (half.Z * Math.Abs(axis.Z));
        }

        /// <summary>
        /// Half the block's extents, in metres, for a block spanning <paramref name="min"/> to
        /// <paramref name="max"/> in cells on a grid of <paramref name="gridSize"/> metre cells.
        /// </summary>
        public static Vector3 HalfExtents(Vector3I min, Vector3I max, float gridSize)
        {
            return ((Vector3)(max - min + Vector3I.One)) * (gridSize * 0.5f);
        }
    }
}
