using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// One bit per cell of a box, for membership over a dense region: 248 times smaller than the
    /// <c>HashSet&lt;Vector3I&gt;</c> it replaces, which was the mod's peak allocation. The region has
    /// to be known in advance and indexable, which a flood fill over a padded bounding box satisfies;
    /// cells outside it are not members. See memory.md, 1a.
    /// </summary>
    public class CellBitset
    {
        private long[] words = new long[0];

        private Vector3I min;
        private int sizeX;
        private int sizeY;
        private int sizeZ;

        /// <summary>Cells currently set.</summary>
        public int Count { get; private set; }

        /// <summary>Cells the region covers.</summary>
        public long Capacity
        {
            get { return (long)sizeX * sizeY * sizeZ; }
        }

        /// <summary>
        /// Points the set at a box and clears it. The backing array is retained when already large
        /// enough: a room mapping pass restarts on every block placement, and reallocating a
        /// megabyte each time would be its own cost.
        /// </summary>
        public void Reset(Vector3I boxMin, Vector3I boxMaxExclusive)
        {
            min = boxMin;
            sizeX = Math.Max(0, boxMaxExclusive.X - boxMin.X);
            sizeY = Math.Max(0, boxMaxExclusive.Y - boxMin.Y);
            sizeZ = Math.Max(0, boxMaxExclusive.Z - boxMin.Z);

            long cells = Capacity;
            long needed = (cells + 63) / 64;

            if (needed > words.Length)
            {
                // int.MaxValue bits is 268 million cells, far beyond any grid. A larger box
                // indicates a fault elsewhere, and clamping is preferable to an overflowed length.
                if (needed > int.MaxValue) needed = int.MaxValue;
                words = new long[needed];
            }
            else
            {
                Array.Clear(words, 0, words.Length);
            }

            Count = 0;
        }

        public void Clear()
        {
            Array.Clear(words, 0, words.Length);
            Count = 0;
        }

        /// <summary>
        /// Index of a cell in this box, or -1 outside it: <c>((z * sizeY) + y) * sizeX + x</c> from
        /// the box's minimum, which is the order every dense per-cell buffer over the same box uses,
        /// so a caller that walks neighbours can step an index by ±1, ±sizeX and ±sizeX·sizeY
        /// instead of deriving it six times a cell. See performance.md, Iteration 8.
        /// </summary>
        public long IndexOf(Vector3I cell)
        {
            int x = cell.X - min.X;
            if (x < 0 || x >= sizeX) return -1;

            int y = cell.Y - min.Y;
            if (y < 0 || y >= sizeY) return -1;

            int z = cell.Z - min.Z;
            if (z < 0 || z >= sizeZ) return -1;

            return (((long)z * sizeY) + y) * sizeX + x;
        }

        /// <summary>
        /// The first index at or after <paramref name="index"/> whose cell is not set, or
        /// <paramref name="endExclusive"/> if every cell up to there is. Walks whole words while
        /// they are all ones — sixty-four cells a step — and then the bits of the first word that
        /// is not, so a scan over a box that is mostly visited pays per word rather than per cell.
        /// See performance.md, Pass 2, Iteration 4.
        /// </summary>
        /// <param name="wordsExamined">How many words were looked at, which is what the caller charges.</param>
        public long NextClearIndex(long index, long endExclusive, out int wordsExamined)
        {
            wordsExamined = 0;
            if (index < 0) index = 0;

            while (index < endExclusive)
            {
                long word = index >> 6;
                if (word >= words.Length) return index;

                wordsExamined++;
                long bits = words[word];
                int offset = (int)(index & 63);

                // Every bit from this offset to the end of the word set: skip the rest of the word.
                if ((~bits >> offset) == 0L)
                {
                    index = (word + 1) << 6;
                    continue;
                }

                // Otherwise the next clear bit is in this word, at or after the offset.
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
        /// The first index at or after <paramref name="index"/> whose cell is set, or
        /// <paramref name="endExclusive"/> if none is. Skips whole words that are all zeros, so a
        /// sparse set over a large box is walked per word rather than per cell.
        /// </summary>
        public long NextSetIndex(long index, long endExclusive)
        {
            if (index < 0) index = 0;

            while (index < endExclusive)
            {
                long word = index >> 6;
                if (word >= words.Length) return endExclusive;

                long bits = words[word] >> (int)(index & 63);
                if (bits == 0L)
                {
                    index = (word + 1) << 6;
                    continue;
                }

                while ((bits & 1L) == 0L)
                {
                    bits >>= 1;
                    index++;
                }

                return index < endExclusive ? index : endExclusive;
            }

            return endExclusive;
        }

        /// <summary>The cell at an index from <see cref="IndexOf"/>: its inverse.</summary>
        public Vector3I CellAt(long index)
        {
            long plane = (long)sizeX * sizeY;
            int z = (int)(index / plane);
            long rest = index - (z * plane);
            int y = (int)(rest / sizeX);
            int x = (int)(rest - ((long)y * sizeX));
            return new Vector3I(min.X + x, min.Y + y, min.Z + z);
        }

        /// <summary>Whether the cell at an index from <see cref="IndexOf"/> is set. Negative and out-of-range indices are not.</summary>
        public bool ContainsIndex(long index)
        {
            if (index < 0) return false;

            long word = index >> 6;
            if (word >= words.Length) return false;

            return (words[word] & (1L << (int)(index & 63))) != 0L;
        }

        /// <summary>Sets the cell at an index from <see cref="IndexOf"/>. Returns false when already set or out of range.</summary>
        public bool AddIndex(long index)
        {
            if (index < 0) return false;

            long word = index >> 6;
            if (word >= words.Length) return false;

            long bit = 1L << (int)(index & 63);
            if ((words[word] & bit) != 0L) return false;

            words[word] |= bit;
            Count++;
            return true;
        }

        public bool Contains(Vector3I cell)
        {
            long index = IndexOf(cell);
            if (index < 0) return false;

            long word = index >> 6;
            if (word >= words.Length) return false;

            return (words[word] & (1L << (int)(index & 63))) != 0L;
        }

        /// <summary>Adds a cell. Returns false when it was already present, as a set would.</summary>
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
            return true;
        }
    }
}
