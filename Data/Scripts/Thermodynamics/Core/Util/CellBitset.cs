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
