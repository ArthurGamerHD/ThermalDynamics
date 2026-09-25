using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Utility class for grid coordinate transformations and hashing.
    /// Provides methods for converting between Vector3I coordinates and unique keys,
    /// as well as legacy coordinate handling for save file compatibility.
    /// </summary>
    public static class GridMath
    {
        /// <summary>
        /// Legacy coordinate grid stride (1024 cells in each direction).
        /// Used for backward compatibility with older save files.
        /// Range: -512 to +511 in each axis.
        /// </summary>
        public const int LegacyStride = 1024;

        /// <summary>
        /// Half of legacy stride (512).
        /// Used for range checking in IsLegacySafe.
        /// </summary>
        public const int LegacyHalfStride = LegacyStride / 2;

        /// <summary>
        /// Square of legacy stride (1024 * 1024 = 1,048,576).
        /// Used as weight for Z coordinate in legacy key calculation.
        /// </summary>
        private const int LegacyStrideSquared = LegacyStride * LegacyStride;

        /// <summary>
        /// Bit shift for wide coordinate grid (21 bits per axis).
        /// Allows for larger grids than legacy system (2^21 = 2,097,152 cells per axis).
        /// </summary>
        public const int WideStrideBits = 21;

        /// <summary>
        /// Stride value for wide coordinate system (2^21 = 2,097,152).
        /// Used as base for combining X, Y, Z coordinates into a single key.
        /// </summary>
        private const long WideStride = 1L << WideStrideBits;


        /// <summary>
        /// Converts a 3D coordinate to a legacy 32-bit key.
        /// Legacy system uses 1024x1024 grid in X and Y directions.
        /// </summary>
        /// <param name="v">The 3D coordinate to convert.</param>
        /// <returns>32-bit key encoding the coordinate.</returns>
        /// <remarks>
        /// Formula: key = (Z * 1024²) + (Y * 1024) + X
        /// This allows coordinates from -512 to +511 in each axis.
        /// Values outside this range wrap around due to the modulo operation.
        /// </remarks>
        public static int LegacyFlatten(Vector3I v)
        {
            return (LegacyStrideSquared * v.Z) + (LegacyStride * v.Y) + v.X;
        }


        /// <summary>
        /// Checks if a coordinate is within the safe range for legacy encoding.
        /// Legacy system can only represent coordinates from -512 to +511.
        /// </summary>
        /// <param name="v">The coordinate to check.</param>
        /// <returns>True if coordinate is within legacy range.</returns>
        public static bool IsLegacySafe(Vector3I v)
        {
            return v.X >= -LegacyHalfStride && v.X < LegacyHalfStride
                && v.Y >= -LegacyHalfStride && v.Y < LegacyHalfStride
                && v.Z >= -LegacyHalfStride && v.Z < LegacyHalfStride;
        }


        /// <summary>
        /// Converts a legacy 32-bit key back to a 3D coordinate.
        /// Reverse of LegacyFlatten operation.
        /// </summary>
        /// <param name="key">The legacy key to decode.</param>
        /// <returns>The decoded 3D coordinate.</returns>
        /// <remarks>
        /// Decoding uses modulo arithmetic with the stride value:
        /// 1. Extract X by finding remainder when divided by stride
        /// 2. Remove X contribution and extract Y
        /// 3. Remove X and Y contributions to get Z
        /// 
        /// The Wrap function handles negative values and ensures
        /// coordinates stay within the -512 to +511 range.
        /// </remarks>
        public static Vector3I LegacyUnflatten(int key)
        {
            // Extract X component using modulo
            int x = Wrap(key, LegacyStride);
            // Remove X contribution
            int rest = (key - x) / LegacyStride;

            // Extract Y component
            int y = Wrap(rest, LegacyStride);
            // Remove Y contribution to get Z
            int z = (rest - y) / LegacyStride;
            return new Vector3I(x, y, z);
        }


        /// <summary>
        /// Encodes a 3D coordinate into a 64-bit key using bit shifting.
        /// Modern encoding that supports much larger grids than legacy.
        /// </summary>
        /// <param name="v">The 3D coordinate to encode.</param>
        /// <returns>64-bit key with X in low bits, Y in middle, Z in high bits.</returns>
        /// <remarks>
        /// Encoding scheme:
        ///   bits 0-20: X coordinate
        ///   bits 21-41: Y coordinate
        ///   bits 42-63: Z coordinate
        ///
        /// This allows coordinates from 0 to 2,097,151 (2^21 - 1) in each axis.
        /// The Key is used for efficient dictionary lookups of blocks by position.
        /// </remarks>
        public static long Key(Vector3I v)
        {
            return ((long)v.Z << (WideStrideBits * 2)) + ((long)v.Y << WideStrideBits) + v.X;
        }


        /// <summary>
        /// Precomputed key offsets for each face direction.
        /// Used for fast neighbor lookups in the grid.
        /// Index by Face enum value, returns key offset to adjacent cell.
        /// </summary>
        public static readonly long[] KeyByFace = BuildKeyByFace();


        /// <summary>
        /// Builds the KeyByFace lookup table at initialization.
        /// Each face direction has a corresponding offset in coordinate space.
        /// </summary>
        private static long[] BuildKeyByFace()
        {
            long[] byFace = new long[Face.Count];
            for (int face = 0; face < Face.Count; face++) byFace[face] = Key(Face.Offsets[face]);
            return byFace;
        }


        /// <summary>
        /// Decodes a 64-bit key back to a 3D coordinate.
        /// Reverse of Key operation.
        /// </summary>
        /// <param name="key">The 64-bit key to decode.</param>
        /// <returns>The decoded 3D coordinate.</returns>
        public static Vector3I FromKey(long key)
        {
            // Extract X using modulo
            int x = WrapLong(key, WideStride);
            // Shift right to get Y and Z
            long rest = (key - x) >> WideStrideBits;

            // Extract Y
            int y = WrapLong(rest, WideStride);
            // Shift right to get Z
            long z = (rest - y) >> WideStrideBits;
            return new Vector3I(x, y, (int)z);
        }


        /// <summary>
        /// Wraps an integer value into the range [-stride/2, stride/2).
        /// Handles negative values and overflow by wrapping around.
        /// </summary>
        private static int Wrap(int value, int stride)
        {
            int m = value % stride;
            if (m < 0) m += stride;
            if (m >= stride / 2) m -= stride;
            return m;
        }


        /// <summary>
        /// Wraps a long integer value into the range [-stride/2, stride/2).
        /// Handles negative values and overflow by wrapping around.
        /// </summary>
        private static int WrapLong(long value, long stride)
        {
            long m = value % stride;
            if (m < 0) m += stride;
            if (m >= stride / 2) m -= stride;
            return (int)m;
        }


        /// <summary>
        /// Calculates the largest face area for a block with given extents.
        /// Used for estimating exposed surface area for heat transfer calculations.
        /// </summary>
        /// <param name="extents">Block dimensions in cells.</param>
        /// <returns>Cell count of the largest face.</returns>
        public static int LargestFaceArea(Vector3I extents)
        {
            int a = Math.Max(1, extents.X);
            int b = Math.Max(1, extents.Y);
            int c = Math.Max(1, extents.Z);

            // Largest face = total volume / smallest dimension
            int smallest = Math.Min(a, Math.Min(b, c));
            return (a * b * c) / smallest;
        }


        /// <summary>
        /// Calculates the extent (size) of a bounding box.
        /// </summary>
        /// <param name="min">Minimum corner of the box.</param>
        /// <param name="maxExclusive">Maximum corner (exclusive) of the box.</param>
        /// <returns>The extent (size) of the box.</returns>
        public static Vector3I Extents(Vector3I min, Vector3I maxExclusive)
        {
            return maxExclusive - min;
        }


        /// <summary>
        /// Calculates the total cell count in a bounding box.
        /// </summary>
        /// <param name="min">Minimum corner of the box.</param>
        /// <param name="maxExclusive">Maximum corner (exclusive) of the box.</param>
        /// <returns>Total number of cells in the box.</returns>
        public static int CellCount(Vector3I min, Vector3I maxExclusive)
        {
            Vector3I e = maxExclusive - min;
            return Math.Max(0, e.X) * Math.Max(0, e.Y) * Math.Max(0, e.Z);
        }


        /// <summary>
        /// Checks if a cell is inside a bounding box.
        /// </summary>
        /// <param name="min">Minimum corner (inclusive).</param>
        /// <param name="maxExclusive">Maximum corner (exclusive).</param>
        /// <param name="cell">The cell to test.</param>
        /// <returns>True if cell is inside the box.</returns>
        public static bool Contains(Vector3I min, Vector3I maxExclusive, Vector3I cell)
        {
            return cell.X >= min.X && cell.X < maxExclusive.X
                && cell.Y >= min.Y && cell.Y < maxExclusive.Y
                && cell.Z >= min.Z && cell.Z < maxExclusive.Z;
        }
    }
}
