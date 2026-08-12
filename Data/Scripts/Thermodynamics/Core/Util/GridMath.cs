using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Grid-space integer maths: position keys and block geometry helpers.
    /// </summary>
    public static class GridMath
    {
        /// <summary>
        /// Stride used by the legacy 32-bit position key. A coordinate must stay inside
        /// [-512, 511] on X and Y or two different positions collapse onto the same key.
        /// </summary>
        public const int LegacyStride = 1024;
        public const int LegacyHalfStride = LegacyStride / 2;
        private const int LegacyStrideSquared = LegacyStride * LegacyStride;

        /// <summary>
        /// Stride used by the 64-bit position key. Safe for coordinates in
        /// [-1048576, 1048575], which is far beyond any buildable grid.
        /// </summary>
        public const int WideStrideBits = 21;
        private const long WideStride = 1L << WideStrideBits;
        private const long WideHalfStride = WideStride / 2;
        private const long WideMask = WideStride - 1;

        /// <summary>
        /// The legacy 32-bit position key, bit-compatible with the original mod's
        /// <c>Vector3I.Flatten()</c> so that existing save data keeps working.
        /// Prefer <see cref="Key"/> for anything new.
        /// </summary>
        public static int LegacyFlatten(Vector3I v)
        {
            return (LegacyStrideSquared * v.Z) + (LegacyStride * v.Y) + v.X;
        }

        /// <summary>
        /// True when a position can survive a <see cref="LegacyFlatten"/> round trip.
        /// Positions outside the range alias onto other positions.
        /// </summary>
        public static bool IsLegacySafe(Vector3I v)
        {
            return v.X >= -LegacyHalfStride && v.X < LegacyHalfStride
                && v.Y >= -LegacyHalfStride && v.Y < LegacyHalfStride
                && v.Z >= -LegacyHalfStride && v.Z < LegacyHalfStride;
        }

        /// <summary>
        /// Inverse of <see cref="LegacyFlatten"/>. Correct for negative coordinates, which the
        /// original implementation was not — it used truncating division.
        /// </summary>
        public static Vector3I LegacyUnflatten(int key)
        {
            int x = Wrap(key, LegacyStride);
            int rest = (key - x) / LegacyStride;
            int y = Wrap(rest, LegacyStride);
            int z = (rest - y) / LegacyStride;
            return new Vector3I(x, y, z);
        }

        /// <summary>64-bit position key. Injective over any realistic grid.</summary>
        public static long Key(Vector3I v)
        {
            return ((long)v.Z << (WideStrideBits * 2)) + ((long)v.Y << WideStrideBits) + v.X;
        }

        /// <summary>Inverse of <see cref="Key"/>.</summary>
        public static Vector3I FromKey(long key)
        {
            int x = WrapLong(key, WideStride);
            long rest = (key - x) >> WideStrideBits;
            int y = WrapLong(rest, WideStride);
            long z = (rest - y) >> WideStrideBits;
            return new Vector3I(x, y, (int)z);
        }

        private static int Wrap(int value, int stride)
        {
            int m = value % stride;
            if (m < 0) m += stride;
            if (m >= stride / 2) m -= stride;
            return m;
        }

        private static int WrapLong(long value, long stride)
        {
            long m = value % stride;
            if (m < 0) m += stride;
            if (m >= stride / 2) m -= stride;
            return (int)m;
        }

        /// <summary>
        /// Area, in square grid cells, of the largest face of a box with the given cell extents.
        /// That is the product of the two largest dimensions.
        /// </summary>
        /// <remarks>
        /// The original implementation seeded both running maxima at 1 and only updated the
        /// second when a new maximum appeared, so a shape like 1x5x2 returned 5 instead of 10.
        /// </remarks>
        public static int LargestFaceArea(Vector3I extents)
        {
            int a = Math.Max(1, extents.X);
            int b = Math.Max(1, extents.Y);
            int c = Math.Max(1, extents.Z);

            int smallest = Math.Min(a, Math.Min(b, c));
            return (a * b * c) / smallest;
        }

        /// <summary>
        /// Extent of a box in cells, given an inclusive minimum and an exclusive maximum.
        /// </summary>
        public static Vector3I Extents(Vector3I min, Vector3I maxExclusive)
        {
            return maxExclusive - min;
        }

        /// <summary>Number of cells occupied by a box.</summary>
        public static int CellCount(Vector3I min, Vector3I maxExclusive)
        {
            Vector3I e = maxExclusive - min;
            return Math.Max(0, e.X) * Math.Max(0, e.Y) * Math.Max(0, e.Z);
        }

        /// <summary>True when <paramref name="cell"/> lies inside the half-open box.</summary>
        public static bool Contains(Vector3I min, Vector3I maxExclusive, Vector3I cell)
        {
            return cell.X >= min.X && cell.X < maxExclusive.X
                && cell.Y >= min.Y && cell.Y < maxExclusive.Y
                && cell.Z >= min.Z && cell.Z < maxExclusive.Z;
        }
    }
}
