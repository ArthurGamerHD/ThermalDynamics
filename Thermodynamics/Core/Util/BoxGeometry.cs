using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Geometric utility functions for cubic block calculations.
    /// Provides face analysis, cell enumeration, and bounding box operations
    /// used for surface area calculations and neighbor detection.
    /// </summary>
    public static class BoxGeometry
    {
        /// <summary>
        /// Positive face indices for each axis (Right=4, Up=2, Backward=5).
        /// Used when determining which faces point in the positive direction.
        /// </summary>
        private static readonly int[] PositiveFace = { Face.Right, Face.Up, Face.Backward };

        /// <summary>
        /// Negative face indices for each axis (Left=1, Down=3, Forward=0).
        /// Used when determining which faces point in the negative direction.
        /// </summary>
        private static readonly int[] NegativeFace = { Face.Left, Face.Down, Face.Forward };


        /// <summary>
        /// Gets the component value of a Vector3I for a given axis.
        /// Axis 0=X, 1=Y, 2=Z.
        /// </summary>
        /// <param name="v">The vector to extract component from.</param>
        /// <param name="axis">Axis index (0=X, 1=Y, 2=Z).</param>
        /// <returns>The component value for the specified axis.</returns>
        public static int Component(Vector3I v, int axis)
        {
            switch (axis)
            {
                case 0: return v.X;
                case 1: return v.Y;
                default: return v.Z;
            }
        }

        /// <summary>
        /// Represents the geometric span of a face on a box.
        /// Contains all information needed to iterate over cells in a face.
        /// </summary>
        public struct FaceSpan
        {
            /// <summary>
            /// Direction offset from the box to reach the face.
            /// Points outward from the box center toward the face.
            /// </summary>
            public Vector3I Offset;

            /// <summary>
            /// Axis perpendicular to the face (0=X, 1=Y, 2=Z).
            /// </summary>
            public int Axis;

            /// <summary>
            /// True if this is the positive face (points in +axis direction).
            /// </summary>
            public bool Positive;

            /// <summary>
            /// Fixed coordinate value for this face.
            /// For positive faces: maxExclusive[axis] - 1
            /// For negative faces: min[axis]
            /// </summary>
            public int Slab;

            /// <summary>
            /// First in-plane axis index (u-axis of the face).
            /// Used for face iteration.
            /// </summary>
            public int U;

            /// <summary>
            /// Second in-plane axis index (v-axis of the face).
            /// Used for face iteration.
            /// </summary>
            public int V;

            /// <summary>
            /// Minimum coordinate along the U axis (inclusive).
            /// </summary>
            public int MinU;

            /// <summary>
            /// Maximum coordinate along the U axis (exclusive).
            /// </summary>
            public int MaxExclusiveU;

            /// <summary>
            /// Minimum coordinate along the V axis (inclusive).
            /// </summary>
            public int MinV;

            /// <summary>
            /// Maximum coordinate along the V axis (exclusive).
            /// </summary>
            public int MaxExclusiveV;
        }


        /// <summary>
        /// Calculates the geometric span of a face on a box.
        /// Returns a FaceSpan struct with all information needed to iterate
        /// over cells in that face.
        /// </summary>
        /// <param name="min">Minimum corner of the box.</param>
        /// <param name="maxExclusive">Maximum corner of the box (exclusive).</param>
        /// <param name="face">Face index (0-5).</param>
        /// <returns>FaceSpan with face geometry information.</returns>
        public static FaceSpan Span(Vector3I min, Vector3I maxExclusive, int face)
        {
            Vector3I offset = Face.Offsets[face];
            int axis = Face.Axis(face);

            bool positive = Component(offset, axis) > 0;

            FaceSpan span;
            span.Offset = offset;
            span.Axis = axis;
            span.Positive = positive;

            // Fixed coordinate for this face
            span.Slab = positive ? Component(maxExclusive, axis) - 1 : Component(min, axis);
            
            // In-plane axes
            span.U = (axis + 1) % 3;
            span.V = (axis + 2) % 3;

            // In-plane bounds
            span.MinU = Component(min, span.U);
            span.MaxExclusiveU = Component(maxExclusive, span.U);
            span.MinV = Component(min, span.V);
            span.MaxExclusiveV = Component(maxExclusive, span.V);
            return span;
        }


        /// <summary>
        /// Calculates the overlap length between two intervals.
        /// Used for determining shared face area between adjacent blocks.
        /// </summary>
        /// <param name="minA">Start of first interval.</param>
        /// <param name="maxExclusiveA">End of first interval (exclusive).</param>
        /// <param name="minB">Start of second interval.</param>
        /// <param name="maxExclusiveB">End of second interval (exclusive).</param>
        /// <returns>Length of overlap, or 0 if no overlap.</returns>
        public static int Overlap(int minA, int maxExclusiveA, int minB, int maxExclusiveB)
        {
            int low = Math.Max(minA, minB);
            int high = Math.Min(maxExclusiveA, maxExclusiveB);
            return high > low ? high - low : 0;
        }


        /// <summary>
        /// Finds the face that connects two boxes, if any.
        /// Checks all six faces to see if box A and box B share a face.
        /// </summary>
        /// <param name="minA">Minimum corner of box A.</param>
        /// <param name="maxExclusiveA">Maximum corner of box A (exclusive).</param>
        /// <param name="minB">Minimum corner of box B.</param>
        /// <param name="maxExclusiveB">Maximum corner of box B (exclusive).</param>
        /// <returns>Face index (0-5) if boxes share a face, or -1 otherwise.</returns>
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

                bool positive = aMax == bMin;  // A is on the positive side of B
                bool negative = bMax == aMin;  // B is on the positive side of A
                if (!positive && !negative) continue;

                // Check if the faces overlap in the other two dimensions
                if (ContactCells(minA, maxExclusiveA, minB, maxExclusiveB, axis) <= 0) continue;

                return positive ? PositiveFace[axis] : NegativeFace[axis];
            }
            return -1;
        }


        /// <summary>
        /// Calculates the number of cells in the contact area between two boxes
        /// along a specific axis (face normal direction).
        /// </summary>
        /// <param name="minA">Minimum corner of box A.</param>
        /// <param name="maxExclusiveA">Maximum corner of box A (exclusive).</param>
        /// <param name="minB">Minimum corner of box B.</param>
        /// <param name="maxExclusiveB">Maximum corner of box B (exclusive).</param>
        /// <param name="axis">The axis perpendicular to the contact face.</param>
        /// <returns>Number of cells in the contact area.</returns>
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
        /// Calculates the total face contact area between two adjacent boxes.
        /// </summary>
        /// <param name="minA">Minimum corner of box A.</param>
        /// <param name="maxExclusiveA">Maximum corner of box A (exclusive).</param>
        /// <param name="minB">Minimum corner of box B.</param>
        /// <param name="maxExclusiveB">Maximum corner of box B (exclusive).</param>
        /// <returns>Number of cells in shared face area.</returns>
        public static int ContactCells(
            Vector3I minA, Vector3I maxExclusiveA,
            Vector3I minB, Vector3I maxExclusiveB)
        {
            int face = TouchingFace(minA, maxExclusiveA, minB, maxExclusiveB);
            if (face < 0) return 0;
            return ContactCells(minA, maxExclusiveA, minB, maxExclusiveB, Face.Axis(face));
        }


        /// <summary>
        /// Calculates the number of cells on a single face of a box.
        /// </summary>
        /// <param name="extents">Dimensions of the box (size in each axis).</param>
        /// <param name="face">Face index (0-5).</param>
        /// <returns>Number of cells on the specified face.</returns>
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
        /// Calculates the total surface area of a box in cells.
        /// Sum of all six face areas.
        /// </summary>
        /// <param name="extents">Dimensions of the box.</param>
        /// <returns>Total surface area in cells.</returns>
        public static int SurfaceAreaCells(Vector3I extents)
        {
            int x = Math.Max(0, extents.X);
            int y = Math.Max(0, extents.Y);
            int z = Math.Max(0, extents.Z);
            return 2 * ((x * y) + (y * z) + (x * z));
        }


        /// <summary>
        /// Calculates the depth of a box along a specific axis.
        /// </summary>
        /// <param name="extents">Dimensions of the box.</param>
        /// <param name="axis">Axis index (0=X, 1=Y, 2=Z).</param>
        /// <param name="latticeSize">Size of a single cell in meters.</param>
        /// <returns>Depth in meters.</returns>
        public static float Depth(Vector3I extents, int axis, float latticeSize)
        {
            return Math.Max(1, Component(extents, axis)) * latticeSize;
        }


        /// <summary>
        /// Iterates over all cells in a box face and calls an action for each.
        /// </summary>
        /// <param name="min">Minimum corner of the box.</param>
        /// <param name="maxExclusive">Maximum corner of the box (exclusive).</param>
        /// <param name="face">Face index (0-5).</param>
        /// <param name="action">Action to call for each cell on the face.</param>
        public static void ForEachFaceCell(
            Vector3I min, Vector3I maxExclusive, int face, Action<Vector3I> action)
        {
            if (action == null) return;

            int axis = Face.Axis(face);
            bool positive = Face.Offsets[face].X + Face.Offsets[face].Y + Face.Offsets[face].Z > 0;

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


        /// <summary>
        /// Creates a new Vector3I with one component replaced.
        /// </summary>
        /// <param name="v">Original vector.</param>
        /// <param name="axis">Axis to replace (0=X, 1=Y, 2=Z).</param>
        /// <param name="value">New value for the specified axis.</param>
        /// <returns>Copy of vector with one component replaced.</returns>
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
