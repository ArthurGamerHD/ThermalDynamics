using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public static class BoxGeometry
    {
        private static readonly int[] PositiveFace = { Face.Right, Face.Up, Face.Backward };

        private static readonly int[] NegativeFace = { Face.Left, Face.Down, Face.Forward };


        public static int Component(Vector3I v, int axis)
        {
            switch (axis)
            {
                case 0: return v.X;
                case 1: return v.Y;
                default: return v.Z;
            }
        }

        public struct FaceSpan
        {
            public Vector3I Offset;

            public int Axis;

            public bool Positive;

            public int Slab;

            public int U;
            public int V;

            public int MinU;
            public int MaxExclusiveU;
            public int MinV;
            public int MaxExclusiveV;
        }


        public static FaceSpan Span(Vector3I min, Vector3I maxExclusive, int face)
        {
            Vector3I offset = Face.Offsets[face];
            int axis = Face.Axis(face);

            bool positive = Component(offset, axis) > 0;

            FaceSpan span;
            span.Offset = offset;
            span.Axis = axis;
            span.Positive = positive;

            span.Slab = positive ? Component(maxExclusive, axis) - 1 : Component(min, axis);
            span.U = (axis + 1) % 3;
            span.V = (axis + 2) % 3;

            span.MinU = Component(min, span.U);

            span.MaxExclusiveU = Component(maxExclusive, span.U);

            span.MinV = Component(min, span.V);

            span.MaxExclusiveV = Component(maxExclusive, span.V);
            return span;
        }


        public static int Overlap(int minA, int maxExclusiveA, int minB, int maxExclusiveB)
        {
            int low = Math.Max(minA, minB);
            int high = Math.Min(maxExclusiveA, maxExclusiveB);
            return high > low ? high - low : 0;
        }


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


        public static int ContactCells(
            Vector3I minA, Vector3I maxExclusiveA,
            Vector3I minB, Vector3I maxExclusiveB)
        {

            int face = TouchingFace(minA, maxExclusiveA, minB, maxExclusiveB);
            if (face < 0) return 0;
            return ContactCells(minA, maxExclusiveA, minB, maxExclusiveB, Face.Axis(face));
        }


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


        public static int SurfaceAreaCells(Vector3I extents)
        {
            int x = Math.Max(0, extents.X);
            int y = Math.Max(0, extents.Y);
            int z = Math.Max(0, extents.Z);
            return 2 * ((x * y) + (y * z) + (x * z));
        }


        public static float Depth(Vector3I extents, int axis, float latticeSize)
        {
            return Math.Max(1, Component(extents, axis)) * latticeSize;
        }


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
