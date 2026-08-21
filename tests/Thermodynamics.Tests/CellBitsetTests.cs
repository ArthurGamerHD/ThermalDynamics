using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// One bit per cell of a box, standing in for a hash set of cells.
    ///
    /// It replaces the room mapper's visited set, which by the end of a pass holds every cell of
    /// the bounding box — so its failure modes are quiet ones. A cell wrongly reported as visited
    /// stops a flood fill early and a compartment silently stops existing; a cell wrongly reported
    /// as unvisited makes the fill revisit it forever. Neither throws.
    /// </summary>
    public class CellBitsetTests
    {
        private static CellBitset Over(Vector3I min, Vector3I maxExclusive)
        {
            CellBitset set = new CellBitset();
            set.Reset(min, maxExclusive);
            return set;
        }

        [Fact]
        public void AFreshSetHoldsNothing()
        {
            CellBitset set = Over(Vector3I.Zero, new Vector3I(4, 4, 4));

            Assert.Equal(0, set.Count);
            Assert.Equal(64, set.Capacity);
            Assert.False(set.Contains(Vector3I.Zero));
            Assert.False(set.Contains(new Vector3I(3, 3, 3)));
        }

        [Fact]
        public void AddingReportsWhetherItWasNew()
        {
            CellBitset set = Over(Vector3I.Zero, new Vector3I(4, 4, 4));

            Assert.True(set.Add(new Vector3I(1, 2, 3)));
            Assert.False(set.Add(new Vector3I(1, 2, 3)));
            Assert.Equal(1, set.Count);
            Assert.True(set.Contains(new Vector3I(1, 2, 3)));
        }

        /// <summary>
        /// Every cell of a box must be distinguishable from every other. An indexing mistake shows
        /// up as two cells sharing a bit, which reads as one of them having been visited when it
        /// has not — so this walks the whole box rather than sampling it.
        /// </summary>
        [Fact]
        public void EveryCellOfTheBoxIsItsOwnBit()
        {
            Vector3I min = new Vector3I(-3, 5, -11);
            Vector3I max = new Vector3I(4, 12, -2);
            CellBitset set = Over(min, max);

            int expected = 0;
            for (int z = min.Z; z < max.Z; z++)
                for (int y = min.Y; y < max.Y; y++)
                    for (int x = min.X; x < max.X; x++)
                    {
                        Vector3I cell = new Vector3I(x, y, z);
                        Assert.False(set.Contains(cell), cell + " was set before it was added");
                        Assert.True(set.Add(cell));
                        expected++;
                        Assert.Equal(expected, set.Count);
                    }

            Assert.Equal(set.Capacity, set.Count);

            for (int z = min.Z; z < max.Z; z++)
                for (int y = min.Y; y < max.Y; y++)
                    for (int x = min.X; x < max.X; x++)
                    {
                        Assert.True(set.Contains(new Vector3I(x, y, z)));
                    }
        }

        /// <summary>
        /// Cells outside the box are not members and adding them changes nothing. The mapper
        /// bounds-checks before asking, but a set that silently wrapped an out-of-range cell onto
        /// an in-range bit would corrupt the map rather than refuse.
        /// </summary>
        [Fact]
        public void CellsOutsideTheBoxAreNotMembersAndCannotBeAdded()
        {
            Vector3I min = Vector3I.Zero;
            Vector3I max = new Vector3I(4, 4, 4);
            CellBitset set = Over(min, max);

            Vector3I[] outside =
            {
                new Vector3I(-1, 0, 0), new Vector3I(0, -1, 0), new Vector3I(0, 0, -1),
                new Vector3I(4, 0, 0), new Vector3I(0, 4, 0), new Vector3I(0, 0, 4),
                new Vector3I(100, 100, 100),
            };

            for (int i = 0; i < outside.Length; i++)
            {
                Assert.False(set.Add(outside[i]), outside[i] + " was added from outside the box");
                Assert.False(set.Contains(outside[i]));
            }

            Assert.Equal(0, set.Count);
        }

        /// <summary>
        /// A pass restarts every time a block is placed, so resetting must forget everything —
        /// including when the backing array is being reused rather than reallocated.
        /// </summary>
        [Fact]
        public void ResettingForgetsEverythingEvenWhenTheArrayIsReused()
        {
            CellBitset set = Over(Vector3I.Zero, new Vector3I(8, 8, 8));
            for (int i = 0; i < 8; i++) set.Add(new Vector3I(i, i, i));
            Assert.Equal(8, set.Count);

            // Smaller box, so the array is kept and cleared rather than replaced.
            set.Reset(Vector3I.Zero, new Vector3I(4, 4, 4));

            Assert.Equal(0, set.Count);
            for (int i = 0; i < 4; i++)
            {
                Assert.False(set.Contains(new Vector3I(i, i, i)));
            }
        }

        [Fact]
        public void MovingTheBoxMovesWhatTheBitsMean()
        {
            CellBitset set = Over(Vector3I.Zero, new Vector3I(4, 4, 4));
            set.Add(new Vector3I(1, 1, 1));

            set.Reset(new Vector3I(10, 10, 10), new Vector3I(14, 14, 14));

            Assert.False(set.Contains(new Vector3I(1, 1, 1)));
            Assert.True(set.Add(new Vector3I(11, 11, 11)));
            Assert.True(set.Contains(new Vector3I(11, 11, 11)));
        }

        /// <summary>A degenerate box holds nothing and refuses everything, rather than throwing.</summary>
        [Fact]
        public void AnEmptyBoxIsHarmless()
        {
            CellBitset set = Over(new Vector3I(5, 5, 5), new Vector3I(5, 5, 5));

            Assert.Equal(0, set.Capacity);
            Assert.False(set.Add(new Vector3I(5, 5, 5)));
            Assert.False(set.Contains(new Vector3I(5, 5, 5)));
            Assert.Equal(0, set.Count);
        }
    }
}
