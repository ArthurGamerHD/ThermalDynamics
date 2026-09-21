using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public static class GridMath
    {
        public const int LegacyStride = 1024;
        public const int LegacyHalfStride = LegacyStride / 2;
        private const int LegacyStrideSquared = LegacyStride * LegacyStride;

        public const int WideStrideBits = 21;
        private const long WideStride = 1L << WideStrideBits;

/// <summary>LegacyFlatten operation.</summary>
        public static int LegacyFlatten(Vector3I v)
        {
            return (LegacyStrideSquared * v.Z) + (LegacyStride * v.Y) + v.X;
        }

/// <summary>IsLegacySafe operation.</summary>
        public static bool IsLegacySafe(Vector3I v)
        {
            return v.X >= -LegacyHalfStride && v.X < LegacyHalfStride
                && v.Y >= -LegacyHalfStride && v.Y < LegacyHalfStride
                && v.Z >= -LegacyHalfStride && v.Z < LegacyHalfStride;
        }

/// <summary>LegacyUnflatten operation.</summary>
        public static Vector3I LegacyUnflatten(int key)
        {
/// <summary>Wrap operation.</summary>
            int x = Wrap(key, LegacyStride);
            int rest = (key - x) / LegacyStride;
/// <summary>Wrap operation.</summary>
            int y = Wrap(rest, LegacyStride);
            int z = (rest - y) / LegacyStride;
            return new Vector3I(x, y, z);
        }

/// <summary>Key operation.</summary>
        public static long Key(Vector3I v)
        {
            return ((long)v.Z << (WideStrideBits * 2)) + ((long)v.Y << WideStrideBits) + v.X;
        }

/// <summary>Builds the method table.</summary>
        public static readonly long[] KeyByFace = BuildKeyByFace();

/// <summary>Builds the API method table.</summary>
        private static long[] BuildKeyByFace()
        {
            long[] byFace = new long[Face.Count];
            for (int face = 0; face < Face.Count; face++) byFace[face] = Key(Face.Offsets[face]);
            return byFace;
        }

/// <summary>FromKey operation.</summary>
        public static Vector3I FromKey(long key)
        {
/// <summary>WrapLong operation.</summary>
            int x = WrapLong(key, WideStride);
            long rest = (key - x) >> WideStrideBits;
/// <summary>WrapLong operation.</summary>
            int y = WrapLong(rest, WideStride);
            long z = (rest - y) >> WideStrideBits;
            return new Vector3I(x, y, (int)z);
        }

/// <summary>Wrap operation.</summary>
        private static int Wrap(int value, int stride)
        {
            int m = value % stride;
            if (m < 0) m += stride;
            if (m >= stride / 2) m -= stride;
            return m;
        }

/// <summary>WrapLong operation.</summary>
        private static int WrapLong(long value, long stride)
        {
            long m = value % stride;
            if (m < 0) m += stride;
            if (m >= stride / 2) m -= stride;
            return (int)m;
        }

/// <summary>LargestFaceArea operation.</summary>
        public static int LargestFaceArea(Vector3I extents)
        {
            int a = Math.Max(1, extents.X);
            int b = Math.Max(1, extents.Y);
            int c = Math.Max(1, extents.Z);

            int smallest = Math.Min(a, Math.Min(b, c));
            return (a * b * c) / smallest;
        }

/// <summary>Extents operation.</summary>
        public static Vector3I Extents(Vector3I min, Vector3I maxExclusive)
        {
            return maxExclusive - min;
        }

/// <summary>CellCount operation.</summary>
        public static int CellCount(Vector3I min, Vector3I maxExclusive)
        {
            Vector3I e = maxExclusive - min;
            return Math.Max(0, e.X) * Math.Max(0, e.Y) * Math.Max(0, e.Z);
        }

/// <summary>Contains operation.</summary>
        public static bool Contains(Vector3I min, Vector3I maxExclusive, Vector3I cell)
        {
            return cell.X >= min.X && cell.X < maxExclusive.X
                && cell.Y >= min.Y && cell.Y < maxExclusive.Y
                && cell.Z >= min.Z && cell.Z < maxExclusive.Z;
        }
    }
}
