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

        /// <summary>
        /// Set bits in every word before this one, or empty until <see cref="BuildRanks"/> runs.
        /// Four bytes a word — a sixteenth of a bit a cell — and it turns "which member is this"
        /// into two loads and a popcount. See performance.md, Pass 4, Iteration 3.
        /// </summary>
        private int[] setsBefore = EmptyPrefix;
        private bool ranked;

        private static readonly int[] EmptyPrefix = new int[0];

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
            ranked = false;
        }

        public void Clear()
        {
            Array.Clear(words, 0, words.Length);
            Count = 0;
            ranked = false;
        }

        /// <summary>
        /// Builds the rank index: after this, <see cref="RankOfIndex"/> says how many members come
        /// before a cell, in the box's own index order. Any change to the set puts it away again,
        /// because a rank read from a stale prefix is wrong without being obviously wrong.
        ///
        /// <para>
        /// Costs one pass over the words — a sixty-fourth of the box — and no comparison at all,
        /// which is what makes it an alternative to sorting the members and searching them.
        /// </para>
        /// </summary>
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

        /// <summary>Whether <see cref="BuildRanks"/> has run and nothing has changed since.</summary>
        public bool IsRanked
        {
            get { return ranked; }
        }

        /// <summary>
        /// How many members precede the cell at <paramref name="index"/>, which for a member is its
        /// position in the box's index order — 0 for the first, <see cref="Count"/> − 1 for the
        /// last. Undefined for a cell that is not a member; callers test
        /// <see cref="ContainsIndex"/> first, which they have to do anyway.
        /// </summary>
        public int RankOfIndex(long index)
        {
            int word = (int)(index >> 6);
            if (!ranked || word < 0 || word >= setsBefore.Length) return -1;

            // Bits below this one in its own word: for bit 63 the mask is every lower bit, which is
            // what (1 << 63) - 1 is as a signed long.
            long below = words[word] & ((1L << (int)(index & 63)) - 1L);
            return setsBefore[word] + PopCount(below);
        }

        /// <summary>
        /// Bits set in a word, by the usual SWAR halving. `System.Numerics.BitOperations` is not on
        /// the script whitelist, so this is written out. See script-whitelist notes in rules.md.
        /// </summary>
        private static int PopCount(long value)
        {
            ulong v = (ulong)value;
            v = v - ((v >> 1) & 0x5555555555555555UL);
            v = (v & 0x3333333333333333UL) + ((v >> 2) & 0x3333333333333333UL);
            v = (v + (v >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((v * 0x0101010101010101UL) >> 56);
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
            ranked = false;
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
            ranked = false;
            return true;
        }
    }
}
