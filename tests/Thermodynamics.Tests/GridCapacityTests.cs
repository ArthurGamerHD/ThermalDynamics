using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **Sizing the cell table before a hull is built, and that it changes nothing but the garbage.**
    ///
    /// <para>
    /// The table doubles as it fills and every doubling copies every entry it already holds, so on a
    /// 125,000-block hull placing blocks allocated **10.5 MB** of nothing but old tables — 84 bytes a
    /// block, and most of what the `place` stage allocates. `ThermalGrid` knows the block count
    /// before it registers the first one, so it says so.
    /// </para>
    ///
    /// <para>
    /// **A hint, not a bound**: the table is keyed per cell and the count is a block count, so a
    /// hull of multi-cell blocks still grows once. Nothing is refused for exceeding it.
    /// </para>
    /// </summary>
    public class GridCapacityTests
    {
        private static GridBuilder Hull()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(14, 14, 14));
            return builder;
        }

        private static GridModel Built(GridBuilder source, int capacity)
        {
            GridModel grid = new GridModel(source.Grid.GridSize);
            if (capacity > 0) grid.EnsureCellCapacity(capacity);

            for (int i = 0; i < source.Placed.Count; i++)
            {
                BlockInstance block = source.Placed[i];
                grid.Add(new BlockInstance(block.Model, block.Min, block.Orientation));
            }

            return grid;
        }

        /// <summary>
        /// **The claim that matters: a sized grid is the same grid.** Same blocks, same lookups,
        /// same bounds — the capacity is an allocation decision and reaches nothing a caller reads.
        /// </summary>
        [Fact]
        public void SizingTheTableChangesNothingAboutTheGrid()
        {
            GridBuilder source = Hull();

            GridModel plain = Built(source, 0);
            GridModel sized = Built(source, source.Placed.Count);

            Assert.Equal(plain.BlockCount, sized.BlockCount);
            Assert.Equal(plain.Min, sized.Min);
            Assert.Equal(plain.Max, sized.Max);

            for (int i = 0; i < source.Placed.Count; i++)
            {
                Vector3I cell = source.Placed[i].Min;

                Assert.NotNull(plain.GetAtCell(cell));
                Assert.Equal(plain.GetAtCell(cell).Min, sized.GetAtCell(cell).Min);
                Assert.Equal(plain.IsOccupied(cell), sized.IsOccupied(cell));
            }
        }

        /// <summary>
        /// **And it is what removes the garbage.** Measured rather than assumed: the sized build
        /// allocates a small fraction of the unsized one, which is the whole reason for the call.
        /// </summary>
        [Fact]
        public void SizingTheTableRemovesTheRegrowth()
        {
            GridBuilder source = Hull();

            // The instances are built outside the measured region in both legs, so what is compared
            // is the table's growth and not the blocks that go into it.
            long plain = Allocated(source, 0);
            long sized = Allocated(source, source.Placed.Count);

            Assert.True(plain > 0, "the unsized build allocated nothing, so this compares nothing");
            Assert.True(sized * 4 < plain,
                "sizing the table saved little: " + sized + " B against " + plain + " B");
        }

        private static long Allocated(GridBuilder source, int capacity)
        {
            BlockInstance[] blocks = new BlockInstance[source.Placed.Count];
            for (int i = 0; i < blocks.Length; i++)
            {
                BlockInstance block = source.Placed[i];
                blocks[i] = new BlockInstance(block.Model, block.Min, block.Orientation);
            }

            GridModel grid = new GridModel(source.Grid.GridSize);
            if (capacity > 0) grid.EnsureCellCapacity(capacity);

            GC.Collect();
            GC.WaitForPendingFinalizers();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < blocks.Length; i++) grid.Add(blocks[i]);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        /// <summary>
        /// **The solver's node lists take the same hint, and it is worth 4.2 MB.** Registering
        /// 125,000 blocks allocated 187 bytes each; sized it is 153.4, which is the `ThermalNode`
        /// objects and nothing else — every byte of list growth is gone.
        /// </summary>
        [Fact]
        public void SizingTheNodeListsRemovesTheirRegrowth()
        {
            GridBuilder source = Hull();

            long plain = Registered(source, 0);
            long sized = Registered(source, source.Placed.Count);

            Assert.True(plain > sized,
                "sizing the node lists saved nothing: " + sized + " B against " + plain + " B");
        }

        private static long Registered(GridBuilder source, int capacity)
        {
            ThermalSimulation simulation =
                new ThermalSimulation(new ThermalSettings().Derive(), source.Grid);

            if (capacity > 0) simulation.EnsureCapacity(capacity);

            GC.Collect();
            GC.WaitForPendingFinalizers();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < source.Placed.Count; i++)
            {
                simulation.Solver.AddBlock(source.Placed[i], 293.15f);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        /// <summary>
        /// **The block table is deliberately *not* sized, and this records why rather than leaving
        /// it to look like an oversight.**
        ///
        /// <para>
        /// A list's order is its index order whatever its capacity, so sizing one cannot be
        /// observed. A dictionary's is not: capacity decides bucket layout and so decides
        /// enumeration order. `ThermalGrid.blocks` is enumerated by the x-ray overlay, which stops
        /// at `DebugOverlayMaxBoxes` — so a different order draws a different set of boxes on any
        /// hull over the budget. Cosmetic, and still a behaviour change.
        /// </para>
        ///
        /// <para>
        /// `GridModel`'s cell table *is* sized, because the one thing that enumerates it takes a
        /// min and a max over integers, which is order-independent.
        /// </para>
        /// </summary>
        [Fact]
        public void TheGridModelsOwnTableIsOrderIndependentWhereItIsEnumerated()
        {
            GridBuilder source = Hull();

            GridModel plain = Built(source, 0);
            GridModel sized = Built(source, source.Placed.Count);

            // The bounds are the only thing derived from enumerating the cell table, and they agree.
            Assert.Equal(plain.Min, sized.Min);
            Assert.Equal(plain.Max, sized.Max);
        }

        /// <summary>
        /// **A late call is ignored rather than obeyed**, because rebuilding a populated table would
        /// copy every entry to save copying them later — and a caller that asks twice must not lose
        /// the blocks it already placed.
        /// </summary>
        [Fact]
        public void SizingAfterTheFirstBlockIsIgnoredAndLosesNothing()
        {
            GridBuilder source = Hull();
            GridModel grid = Built(source, 0);

            int before = grid.BlockCount;
            grid.EnsureCellCapacity(100000);

            Assert.Equal(before, grid.BlockCount);
            Assert.NotNull(grid.GetAtCell(source.Placed[0].Min));
        }

        /// <summary>Nought and negatives are ignored, so a caller with no count may pass what it has.</summary>
        [Fact]
        public void ACountItDoesNotHaveIsIgnored()
        {
            GridModel grid = new GridModel(Catalog.LargeGridSize);

            grid.EnsureCellCapacity(0);
            grid.EnsureCellCapacity(-5);

            Assert.Equal(0, grid.BlockCount);
        }
    }
}
