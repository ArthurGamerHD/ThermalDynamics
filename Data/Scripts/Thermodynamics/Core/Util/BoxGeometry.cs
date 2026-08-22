using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Analytic geometry on integer, axis-aligned block bounds — every function O(1) in the size of the
    /// blocks, which is what lets one grid mix block sizes. Bounds are half-open and areas are counted
    /// in lattice cell faces. See scale-design.md, Cell-centric to boundary-centric.
    /// </summary>
    public static class BoxGeometry
    {
        /// <summary>Face index for the +axis direction: X → Right, Y → Up, Z → Backward.</summary>
        private static readonly int[] PositiveFace = { Face.Right, Face.Up, Face.Backward };

        /// <summary>Face index for the -axis direction: X → Left, Y → Down, Z → Forward.</summary>
        private static readonly int[] NegativeFace = { Face.Left, Face.Down, Face.Forward };

        /// <summary>Component of a vector on an axis: 0 = X, 1 = Y, 2 = Z.</summary>
        public static int Component(Vector3I v, int axis)
        {
            switch (axis)
            {
                case 0: return v.X;
                case 1: return v.Y;
                default: return v.Z;
            }
        }

        /// <summary>Length of the overlap of two half-open intervals, or 0 when they do not overlap.</summary>
        public static int Overlap(int minA, int maxExclusiveA, int minB, int maxExclusiveB)
        {
            int low = Math.Max(minA, minB);
            int high = Math.Min(maxExclusiveA, maxExclusiveB);
            return high > low ? high - low : 0;
        }

        /// <summary>
        /// The face of box A that touches box B, or -1 when they do not share a face.
        ///
        /// Two boxes touch when they abut on one axis and overlap on both of the others. Boxes
        /// that merely meet along an edge or at a corner do not touch, because their shared area
        /// is zero.
        /// </summary>
        public static int TouchingFace(
            Vector3I minA, Vector3I maxExclusiveA,
            Vector3I minB, Vector3I maxExclusiveB)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                int aMin = Component(minA, axis);
                int aMax = Component(maxExclusiveA, axis);
                int bMin = Component(minB, axis);
                int bMax = Component(maxExclusiveB, axis);

                bool positive = aMax == bMin;
                bool negative = bMax == aMin;
                if (!positive && !negative) continue;

                if (ContactCells(minA, maxExclusiveA, minB, maxExclusiveB, axis) <= 0) continue;

                return positive ? PositiveFace[axis] : NegativeFace[axis];
            }
            return -1;
        }

        /// <summary>
        /// Shared area of two boxes abutting on <paramref name="axis"/>, in lattice cell faces.
        /// This is the product of their overlaps on the two perpendicular axes.
        /// </summary>
        public static int ContactCells(
            Vector3I minA, Vector3I maxExclusiveA,
            Vector3I minB, Vector3I maxExclusiveB,
            int axis)
        {
            int area = 1;
            for (int other = 0; other < 3; other++)
            {
                if (other == axis) continue;

                int overlap = Overlap(
                    Component(minA, other), Component(maxExclusiveA, other),
                    Component(minB, other), Component(maxExclusiveB, other));

                if (overlap <= 0) return 0;
                area *= overlap;
            }
            return area;
        }

        /// <summary>
        /// Shared area of two boxes in lattice cell faces, whichever axis they abut on, or 0
        /// when they do not touch.
        /// </summary>
        public static int ContactCells(
            Vector3I minA, Vector3I maxExclusiveA,
            Vector3I minB, Vector3I maxExclusiveB)
        {
            int face = TouchingFace(minA, maxExclusiveA, minB, maxExclusiveB);
            if (face < 0) return 0;
            return ContactCells(minA, maxExclusiveA, minB, maxExclusiveB, Face.Axis(face));
        }

        /// <summary>
        /// Area of one face of a box, in lattice cell faces: the product of the two extents
        /// perpendicular to that face.
        /// </summary>
        public static int FaceAreaCells(Vector3I extents, int face)
        {
            int axis = Face.Axis(face);
            int area = 1;
            for (int other = 0; other < 3; other++)
            {
                if (other == axis) continue;
                area *= Math.Max(0, Component(extents, other));
            }
            return area;
        }

        /// <summary>
        /// Total boundary area of a box, in lattice cell faces. Equal to the sum of
        /// <see cref="FaceAreaCells"/> over all six faces.
        /// </summary>
        public static int SurfaceAreaCells(Vector3I extents)
        {
            int x = Math.Max(0, extents.X);
            int y = Math.Max(0, extents.Y);
            int z = Math.Max(0, extents.Z);
            return 2 * ((x * y) + (y * z) + (x * z));
        }

        /// <summary>
        /// Depth of a box along an axis, in metres — the distance heat travels straight through
        /// it. Used as twice the half-depth in the series-conduction formula.
        /// </summary>
        public static float Depth(Vector3I extents, int axis, float latticeSize)
        {
            return Math.Max(1, Component(extents, axis)) * latticeSize;
        }

        /// <summary>
        /// Enumerates the lattice cells of one face of a box, in grid space. This is the box's
        /// two-dimensional boundary on that side, not its volume.
        /// </summary>
        /// <remarks>
        /// Ordering is stable: the two perpendicular axes are walked in ascending index order.
        /// </remarks>
        public static void ForEachFaceCell(
            Vector3I min, Vector3I maxExclusive, int face, Action<Vector3I> action)
        {
            if (action == null) return;

            int axis = Face.Axis(face);
            bool positive = Face.Offsets[face].X + Face.Offsets[face].Y + Face.Offsets[face].Z > 0;

            // The slab of cells on this side of the box.
            int fixedValue = positive
                ? Component(maxExclusive, axis) - 1
                : Component(min, axis);

            int u = (axis + 1) % 3;
            int v = (axis + 2) % 3;

            for (int a = Component(min, u); a < Component(maxExclusive, u); a++)
            {
                for (int b = Component(min, v); b < Component(maxExclusive, v); b++)
                {
                    Vector3I cell = Vector3I.Zero;
                    cell = WithComponent(cell, axis, fixedValue);
                    cell = WithComponent(cell, u, a);
                    cell = WithComponent(cell, v, b);
                    action(cell);
                }
            }
        }

        /// <summary>Returns a copy of a vector with one axis replaced.</summary>
        public static Vector3I WithComponent(Vector3I v, int axis, int value)
        {
            switch (axis)
            {
                case 0: v.X = value; break;
                case 1: v.Y = value; break;
                default: v.Z = value; break;
            }
            return v;
        }
    }
}
