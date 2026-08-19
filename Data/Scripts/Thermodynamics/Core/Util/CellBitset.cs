using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// One bit per cell of a box, for membership over a dense region.
    ///
    /// <para>
    /// A <c>HashSet&lt;Vector3I&gt;</c> costs about thirty-one bytes per cell — a bucket, a hash
    /// code, a next pointer, the twelve-byte vector, and load-factor slack. That suits a sparse set
    /// scattered through space, but a flood fill accumulates every cell of its bounding box, so by
    /// the end it holds the whole volume.
    /// </para>
    ///
    /// <para>
    /// This is 248 times smaller than the set it replaces. The room mapper's visited set was the
    /// mod's peak allocation: 121 MB of a 400 MB peak on a 127,000-block grid, scaling with the
    /// bounding box rather than the grid, so a mostly empty hull paid for the empty space.
    /// </para>
    ///
    /// <para>
    /// The constraint is that the region must be known in advance and indexable, which a flood fill
    /// over a padded bounding box satisfies. Cells outside it are not members; callers already
    /// bounds-check before asking, since a fill that wandered outside its box would not terminate.
    /// </para>
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

        private long IndexOf(Vector3I cell)
        {
            int x = cell.X - min.X;
            if (x < 0 || x >= sizeX) return -1;

            int y = cell.Y - min.Y;
            if (y < 0 || y >= sizeY) return -1;

            int z = cell.Z - min.Z;
            if (z < 0 || z >= sizeZ) return -1;

            return (((long)z * sizeY) + y) * sizeX + x;
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
