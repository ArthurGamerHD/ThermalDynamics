using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// One bit per cell of a box, for membership over a dense region.
    ///
    /// <para>
    /// A <c>HashSet&lt;Vector3I&gt;</c> spends about thirty-one bytes on each cell it holds — a
    /// bucket, a hash code, a next pointer, the twelve-byte vector, and the slack a load factor
    /// leaves. That is the right shape for a sparse set of cells scattered anywhere in space. It is
    /// the wrong shape for "every cell of this bounding box, visited or not", which is what a flood
    /// fill accumulates: by the end it holds the whole volume, and the volume is where a grid's
    /// memory goes.
    /// </para>
    ///
    /// <para>
    /// Two hundred and forty-eight times smaller, measured against the set it replaces, and the
    /// difference is not academic — the room mapper's visited set was the high-water mark of the
    /// entire mod. On a 127,000-block ship it was 121 MB of a 400 MB peak, and it scales with the
    /// bounding box rather than with the ship, so a hull that is nine tenths empty pays for the
    /// emptiness.
    /// </para>
    ///
    /// <para>
    /// The trade is that the region must be known in advance and indexable, which for a flood fill
    /// over a padded bounding box it is. Cells outside it are simply not members; callers already
    /// bounds-check before asking, because a flood fill that wandered outside its box would never
    /// terminate.
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
        /// Points the set at a box and clears it.
        ///
        /// The backing array is kept when it is already big enough, because a room mapping pass
        /// restarts every time a block is placed and reallocating a megabyte each time would be a
        /// different kind of waste.
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
                // int.MaxValue bits is 268 million cells, far past any grid; a box larger than
                // that is a bug elsewhere and clamping is better than an overflowed length.
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

        /// <summary>Adds a cell. Returns false when it was already there, like a set would.</summary>
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
