using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public class CellBitset
    {
        private long[] words = new long[0];

        private int[] setsBefore = EmptyPrefix;
        private bool ranked;

        private readonly long[] indexByFace = new long[Face.Count];

        private static readonly int[] EmptyPrefix = new int[0];

        private Vector3I min;
        private int sizeX;
        private int sizeY;
        private int sizeZ;

        public int Count { get; private set; }

        public long Capacity
        {

            get { return (long)sizeX * sizeY * sizeZ; }
        }


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
                if (needed > int.MaxValue) needed = int.MaxValue;
                words = new long[needed];
            }
            else
            {
                Array.Clear(words, 0, words.Length);
            }

            Count = 0;
            ranked = false;

            long planeStride = (long)sizeX * sizeY;
            for (int face = 0; face < Face.Count; face++)
            {
                Vector3I offset = Face.Offsets[face];
                indexByFace[face] = offset.X + (long)offset.Y * sizeX + (long)offset.Z * planeStride;
            }
        }


        public long IndexStep(int face)
        {
            return indexByFace[face];
        }


        public void Clear()
        {
            Array.Clear(words, 0, words.Length);
            Count = 0;
            ranked = false;
        }


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

        public bool IsRanked
        {
            get { return ranked; }
        }


        public int RankOfIndex(long index)
        {
            int word = (int)(index >> 6);
            if (!ranked || word < 0 || word >= setsBefore.Length) return -1;

            long below = words[word] & ((1L << (int)(index & 63)) - 1L);
            return setsBefore[word] + PopCount(below);
        }


        private static int PopCount(long value)
        {
            ulong v = (ulong)value;
            v = v - ((v >> 1) & 0x5555555555555555UL);
            v = (v & 0x3333333333333333UL) + ((v >> 2) & 0x3333333333333333UL);
            v = (v + (v >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((v * 0x0101010101010101UL) >> 56);
        }


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

                if ((~bits >> offset) == 0L)
                {
                    index = (word + 1) << 6;
                    continue;
                }

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


        public bool ContainsIndex(long index)
        {
            if (index < 0) return false;

            long word = index >> 6;
            if (word >= words.Length) return false;

            return (words[word] & (1L << (int)(index & 63))) != 0L;
        }


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
