using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The bit in front of the probe says exactly what the probe would have said.**
    ///
    /// <para>
    /// The air rebuild and the room-side exposure refresh walk the six faces of every cell of every
    /// room and ask the grid what stands across each. Nine faces in ten hold nothing, so a bitset
    /// over the padded bounding box answers first and the dictionary is asked only where the bit is
    /// set (performance.md, Pass 4, Iteration 6). That is a fast path in front of a lookup, which is
    /// the shape that goes wrong quietly: a bit clear where a block stands is a wall that stops
    /// bounding a room, and nothing else in the model would report it.
    /// </para>
    ///
    /// <para>
    /// So these check the bit against the dictionary — two structures written by different code —
    /// over every cell of a real hull, and check that the set is not allowed to go stale.
    /// </para>
    /// </summary>
    public class GridOccupancyTests
    {
        /// <summary>
        /// Over the whole padded box, the bit and the dictionary agree cell for cell. Not only over
        /// the occupied cells: a bit set where nothing stands is the other half of the failure.
        /// </summary>
        [Fact]
        public void TheOccupancyBitAgreesWithTheBlockTableOverTheWholeBox()
        {
            ThermalSimulation simulation = Hulls.Driven(Hulls.Uncapped(), 4000);
            GridModel grid = simulation.Grid;
            CellBitset occupied = grid.Occupancy();

            Vector3I min = grid.Min - Vector3I.One;
            Vector3I maxExclusive = grid.Max + new Vector3I(2, 2, 2);

            int set = 0;
            int judged = 0;

            for (int z = min.Z; z < maxExclusive.Z; z++)
            {
                for (int y = min.Y; y < maxExclusive.Y; y++)
                {
                    for (int x = min.X; x < maxExclusive.X; x++)
                    {
                        Vector3I cell = new Vector3I(x, y, z);
                        bool bit = occupied.Contains(cell);
                        bool block = grid.GetAtCell(cell) != null;

                        Assert.True(bit == block,
                            "cell " + cell + " is " + (bit ? "set" : "clear")
                            + " in the occupancy bitset and " + (block ? "occupied" : "empty")
                            + " in the block table");

                        if (bit) set++;
                        judged++;
                    }
                }
            }

            Assert.True(set > 3000, "only " + set + " cells were occupied, so the hull is not the one this means to check");
            Assert.True(judged > set * 2, "the box is barely larger than the hull, so no empty cells were judged");
        }

        /// <summary>
        /// **Stepping by index lands on the neighbour the offset names.** The walk adds a per-face
        /// constant to a cell's index rather than deriving the neighbour's, which is only sound
        /// away from the box's own boundary — so it is checked over every cell of a real hull, on
        /// every face, against deriving it.
        /// </summary>
        [Fact]
        public void SteppingAnIndexByAFaceLandsOnThatFacesNeighbour()
        {
            ThermalSimulation simulation = Hulls.Driven(Hulls.Uncapped(), 4000);
            GridModel grid = simulation.Grid;
            CellBitset occupied = grid.Occupancy();

            int judged = 0;
            IList<BlockInstance> blocks = grid.Blocks;

            for (int b = 0; b < blocks.Count; b++)
            {
                Vector3I[] cells = blocks[b].Cells;
                for (int c = 0; c < cells.Length; c++)
                {
                    long slot = occupied.IndexOf(cells[c]);
                    Assert.True(slot >= 0, "cell " + cells[c] + " is outside the padded box");

                    for (int face = 0; face < Face.Count; face++)
                    {
                        long stepped = slot + occupied.IndexStep(face);
                        long derived = occupied.IndexOf(cells[c] + Face.Offsets[face]);

                        Assert.True(stepped == derived,
                            "from " + cells[c] + " across face " + face + " the step gives index "
                            + stepped + " and deriving the neighbour gives " + derived);
                        judged++;
                    }
                }
            }

            Assert.True(judged > 10000, "only " + judged + " steps were judged");
        }

        /// <summary>
        /// A block placed after the set was built is in it the next time it is asked for. Without
        /// this the set is a cache with no invalidation, which is the same defect as a fast path
        /// that answers wrongly — only later.
        /// </summary>
        [Fact]
        public void PlacingOrRemovingABlockRebuildsTheSet()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(0, 0, 0), new Vector3I(4, 4, 4));

            GridModel grid = builder.Grid;
            Assert.True(grid.Occupancy().Contains(new Vector3I(0, 0, 0)));

            // Inside the shell's hollow, so the box does not change and only the bit can.
            Vector3I inside = new Vector3I(1, 1, 1);
            Assert.False(grid.Occupancy().Contains(inside));

            builder.Place(Catalog.LightArmor(), inside);
            BlockInstance added = grid.GetAtCell(inside);
            Assert.NotNull(added);
            Assert.True(grid.Occupancy().Contains(inside),
                "the block placed at " + inside + " is not in the occupancy set, so the set is a cache nothing invalidates");

            Assert.True(grid.Remove(added));
            Assert.False(grid.Occupancy().Contains(inside),
                "the removed block is still in the occupancy set");
        }
    }
}
