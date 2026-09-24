using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// A compressed bitset for tracking occupied cells in a 3D grid.
    /// Uses a 1D array of 64-bit words to represent a 3D volume, with each
    /// bit representing one cell. Optimized for fast add, contains, and rank operations.
    /// </summary>
    /// <remarks>
    /// Coordinate system:
    /// - Cells are represented as Vector3I (int X, Y, Z)
    /// - The bitset covers a rectangular volume from min to maxExclusive
    /// - Index = ((z * sizeY + y) * sizeX + x)
    /// - 64 cells per 64-bit word
    /// 
    /// Performance characteristics:
    /// - Add/Contains: O(1) with bit manipulation
    /// - Rank operations (Population Count): O(n/64) with precomputed prefix sums
    /// - Memory: 1 bit per cell, plus ~1 word per 64 cells for rank index
    /// </remarks>
    public class CellBitset
    {
        /// <summary>
        /// Array of 64-bit words storing the bitset.
        /// Each bit represents one cell. words[i] stores cells i*64 to i*64+63.
        /// </summary>
        private long[] words = new long[0];

        /// <summary>
        /// Prefix sum array for rank operations (population count).
        /// setsBefore[i] = count of set bits in words[0..i-1].
        /// Used to quickly compute how many cells are occupied before a given index.
        /// </summary>
        private int[] setsBefore = EmptyPrefix;

        /// <summary>
        /// True if the prefix sum (setsBefore) is up to date.
        /// Set to false when bits are added; must be recomputed before rank operations.
        /// </summary>
        private bool ranked;

        /// <summary>
        /// Precomputed index offsets for each face direction.
        /// Used for fast neighbor lookups: index + indexByFace[face] = neighbor index
        /// </summary>
        private readonly long[] indexByFace = new long[Face.Count];

        /// <summary>
        /// Empty prefix array used for initialization.
        /// </summary>
        private static readonly int[] EmptyPrefix = new int[0];

        /// <summary>
        /// Minimum corner of the covered volume (inclusive).
        /// All cell coordinates are relative to this origin.
        /// </summary>
        private Vector3I min;

        /// <summary>
        /// Width of the volume in X dimension (cells).
        /// </summary>
        private int sizeX;

        /// <summary>
        /// Width of the volume in Y dimension (cells).
        /// </summary>
        private int sizeY;

        /// <summary>
        /// Width of the volume in Z dimension (cells).
        /// </summary>
        private int sizeZ;

        /// <summary>
        /// Number of bits set (occupied cells) in the bitset.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Total capacity of the bitset in cells.
        /// Equals sizeX * sizeY * sizeZ.
        /// </summary>
        public long Capacity
        {
            get { return (long)sizeX * sizeY * sizeZ; }
        }


        /// <summary>
        /// Resizes and clears the bitset to cover a new volume.
        /// Initializes the bitset with the given bounding box.
        /// </summary>
        /// <param name="boxMin">Minimum corner of the volume (inclusive).</param>
        /// <param name="boxMaxExclusive">Maximum corner of the volume (exclusive).</param>
        /// <remarks>
        /// After Reset(), the bitset is empty (Count=0) but ready to accept cells.
        /// The volume is defined as [boxMin, boxMaxExclusive) in all three dimensions.
        /// </remarks>
        public void Reset(Vector3I boxMin, Vector3I boxMaxExclusive)
        {
            min = boxMin;
            sizeX = Math.Max(0, boxMaxExclusive.X - boxMin.X);
            sizeY = Math.Max(0, boxMaxExclusive.Y - boxMin.Y);
            sizeZ = Math.Max(0, boxMaxExclusive.Z - boxMin.Z);

            long cells = Capacity;
            long needed = (cells + 63) / 64;  // Round up to nearest 64

            if (needed > words.Length)
            {
                if (needed > int.MaxValue) needed = int.MaxValue;
                words = new long[needed];
            }
            else
            {
                Array.Clear(words, 0, words.Length);
            }

            Count = 0;
            ranked = false;

            // Precompute face offsets for fast neighbor lookups
            long planeStride = (long)sizeX * sizeY;
            for (int face = 0; face < Face.Count; face++)
            {
                Vector3I offset = Face.Offsets[face];
                indexByFace[face] = offset.X + (long)offset.Y * sizeX + (long)offset.Z * planeStride;
            }
        }


        /// <summary>
        /// Gets the index offset for moving to a neighboring cell in a given face direction.
        /// </summary>
        /// <param name="face">Face index (0-5).</param>
        /// <returns>Index offset to reach the adjacent cell in that direction.</returns>
        public long IndexStep(int face)
        {
            return indexByFace[face];
        }


        /// <summary>
        /// Clears all bits in the bitset, making it empty.
        /// Count becomes 0, all cells are unoccupied.
        /// </summary>
        public void Clear()
        {
            Array.Clear(words, 0, words.Length);
            Count = 0;
            ranked = false;
        }


        /// <summary>
        /// Builds the prefix sum array for fast rank operations.
        /// After calling BuildRanks(), RankOfIndex() can compute how many
        /// bits are set before any index in O(1) time.
        /// </summary>
        /// <remarks>
        /// Uses the standard parallel prefix sum algorithm:
        /// 1. For each word, count set bits (PopCount)
        /// 2. Compute running sum of set bits per word
        /// 3. Store cumulative count before each word in setsBefore[]
        ///
        /// setsBefore[i] = total bits set in words[0] through words[i-1]
        /// </remarks>
        public void BuildRanks()
        {
            int wordCount = (int)((Capacity + 63) / 64);
            if (wordCount > words.Length) wordCount = words.Length;

            if (setsBefore.Length < wordCount) setsBefore = new int[wordCount];

            int running = 0;
            for (int w = 0; w < wordCount; w++)
            {
                setsBefore[w] = running;
                running += PopCount(words[w]);
            }

            ranked = true;
        }

        /// <summary>
        /// True if BuildRanks() has been called since the last modification.
        /// </summary>
        public bool IsRanked
        {
            get { return ranked; }
        }


        /// <summary>
        /// Computes how many bits are set before a given bit index.
        /// Requires BuildRanks() to have been called first.
        /// </summary>
        /// <param name="index">Bit index (0 to Capacity-1).</param>
        /// <returns>Number of set bits with indices less than the given index.</returns>
        /// <exception cref="InvalidOperationException">If BuildRanks() has not been called.</exception>
        public int RankOfIndex(long index)
        {
            int word = (int)(index >> 6);  // index / 64
            if (!ranked || word < 0 || word >= setsBefore.Length) return -1;

            // Count bits in this word that are before the index
            long below = words[word] & ((1L << (int)(index & 63)) - 1L);
            return setsBefore[word] + PopCount(below);
        }

        /// <summary>
        /// Counts the number of set bits (1s) in a 64-bit value.
        /// Uses the parallel bit counting algorithm (Hacker's Delight).
        /// </summary>
        /// <param name="value">The 64-bit value to count bits in.</param>
        /// <returns>Number of bits set to 1.</returns>
        private static int PopCount(long value)
        {
            ulong v = (ulong)value;
            // Parallel reduction: group bits, count in pairs, then quads, etc.
            v = v - ((v >> 1) & 0x5555555555555555UL);
            v = (v & 0x3333333333333333UL) + ((v >> 2) & 0x3333333333333333UL);
            v = (v + (v >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((v * 0x0101010101010101UL) >> 56);
        }


        /// <summary>
        /// Converts a 3D cell coordinate to a 1D bit index.
        /// </summary>
        /// <param name="cell">The cell coordinate (relative to min).</param>
        /// <returns>Linear index (0 to Capacity-1), or -1 if cell is outside bounds.</returns>
        public long IndexOf(Vector3I cell)
        {
            int x = cell.X - min.X;
            if (x < 0 || x >= sizeX) return -1;

            int y = cell.Y - min.Y;
            if (y < 0 || y >= sizeY) return -1;

            int z = cell.Z - min.Z;
            if (z < 0 || z >= sizeZ) return -1;

            // Row-major order: ((z * sizeY + y) * sizeX + x)
            return (((long)z * sizeY) + y) * sizeX + x;
        }


        /// <summary>
        /// Finds the next clear (unoccupied) bit starting from a given index.
        /// Used for efficient iteration over empty cells.
        /// </summary>
        /// <param name="index">Starting bit index.</param>
        /// <param name="endExclusive">Upper bound (exclusive) for search.</param>
        /// <param name="wordsExamined">Outputs number of words checked.</param>
        /// <returns>Index of first clear bit, or endExclusive if no clear bits found.</returns>
        public long NextClearIndex(long index, long endExclusive, out int wordsExamined)
        {
            wordsExamined = 0;
            if (index < 0) index = 0;

            while (index < endExclusive)
            {
                long word = index >> 6;  // index / 64
                if (word >= words.Length) return index;

                wordsExamined++;
                long bits = words[word];
                int offset = (int)(index & 63);  // index % 64

                // Check if all remaining bits in this word are set
                if ((~bits >> offset) == 0L)
                {
                    index = (word + 1) << 6;  // Move to next word
                    continue;
                }

                // Find first clear bit in this word
                for (int bit = offset; bit < 64; bit++)
                {
                    if ((bits & (1L << bit)) == 0L)
                    {
                        long found = (word << 6) + bit;
                        return found < endExclusive ? found : endExclusive;
                    }
                }

                index = (word + 1) << 6;
            }

            return endExclusive;
        }


        /// <summary>
        /// Checks if a specific bit index is set (occupied).
        /// </summary>
        /// <param name="index">Bit index to check (0 to Capacity-1).</param>
        /// <returns>True if the bit is set.</returns>
        public bool ContainsIndex(long index)
        {
            if (index < 0) return false;

            long word = index >> 6;
            if (word >= words.Length) return false;
            return (words[word] & (1L << (int)(index & 63))) != 0L;
        }


        /// <summary>
        /// Sets a bit at a specific index (marks a cell as occupied).
        /// </summary>
        /// <param name="index">Bit index to set.</param>
        /// <returns>True if the bit was cleared and is now set (first time). False if already set.</returns>
        public bool AddIndex(long index)
        {
            if (index < 0) return false;

            long word = index >> 6;
            if (word >= words.Length) return false;
            long bit = 1L << (int)(index & 63);
            if ((words[word] & bit) != 0L) return false;

            words[word] |= bit;
            Count++;
            ranked = false;  // Invalidate prefix sum
            return true;
        }


        /// <summary>
        /// Checks if a 3D cell is occupied.
        /// </summary>
        /// <param name="cell">Cell coordinate (relative to min).</param>
        /// <returns>True if cell is within bounds and occupied.</returns>
        public bool Contains(Vector3I cell)
        {
            long index = IndexOf(cell);
            if (index < 0) return false;

            long word = index >> 6;
            if (word >= words.Length) return false;
            return (words[word] & (1L << (int)(index & 63))) != 0L;
        }


        /// <summary>
        /// Marks a 3D cell as occupied.
        /// </summary>
        /// <param name="cell">Cell coordinate (relative to min).</param>
        /// <returns>True if cell was not previously occupied, false if already set.</returns>
        public bool Add(Vector3I cell)
        {
            long index = IndexOf(cell);
            if (index < 0) return false;

            long word = index >> 6;
            if (word >= words.Length) return false;
            long bit = 1L << (int)(index & 63);
            if ((words[word] & bit) != 0L) return false;

            words[word] |= bit;
            Count++;
            ranked = false;
            return true;
        }
    }
}
