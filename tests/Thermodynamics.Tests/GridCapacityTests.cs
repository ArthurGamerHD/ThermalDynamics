using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class GridCapacityTests
    {
/// <summary>Hull operation.</summary>
        private static GridBuilder Hull()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(14, 14, 14));
            return builder;
        }

/// <summary>Built operation.</summary>
        private static GridModel Built(GridBuilder source, int capacity)
        {
/// <summary>GridModel operation.</summary>
            GridModel grid = new GridModel(source.Grid.GridSize);
            if (capacity > 0) grid.EnsureCellCapacity(capacity);

            for (int i = 0; i < source.Placed.Count; i++)
            {
                BlockInstance block = source.Placed[i];
                grid.Add(new BlockInstance(block.Model, block.Min, block.Orientation));
            }

            return grid;
        }

        [Fact]
/// <summary>SizingTheTableChangesNothingAboutTheGrid operation.</summary>
        public void SizingTheTableChangesNothingAboutTheGrid()
        {
/// <summary>Hull operation.</summary>
            GridBuilder source = Hull();

/// <summary>Built operation.</summary>
            GridModel plain = Built(source, 0);
/// <summary>Built operation.</summary>
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

        [Fact]
/// <summary>SizingTheTableRemovesTheRegrowth operation.</summary>
        public void SizingTheTableRemovesTheRegrowth()
        {
/// <summary>Hull operation.</summary>
            GridBuilder source = Hull();

/// <summary>Allocated operation.</summary>
            long plain = Allocated(source, 0);
/// <summary>Allocated operation.</summary>
            long sized = Allocated(source, source.Placed.Count);

            Assert.True(plain > 0, "the unsized build allocated nothing, so this compares nothing");
            Assert.True(sized * 4 < plain,
                "sizing the table saved little: " + sized + " B against " + plain + " B");
        }

/// <summary>Allocated operation.</summary>
        private static long Allocated(GridBuilder source, int capacity)
        {
            BlockInstance[] blocks = new BlockInstance[source.Placed.Count];
            for (int i = 0; i < blocks.Length; i++)
            {
                BlockInstance block = source.Placed[i];
/// <summary>BlockInstance operation.</summary>
                blocks[i] = new BlockInstance(block.Model, block.Min, block.Orientation);
            }

/// <summary>GridModel operation.</summary>
            GridModel grid = new GridModel(source.Grid.GridSize);
            if (capacity > 0) grid.EnsureCellCapacity(capacity);

            GC.Collect();
            GC.WaitForPendingFinalizers();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < blocks.Length; i++) grid.Add(blocks[i]);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        [Fact]
/// <summary>SizingTheNodeListsRemovesTheirRegrowth operation.</summary>
        public void SizingTheNodeListsRemovesTheirRegrowth()
        {
/// <summary>Hull operation.</summary>
            GridBuilder source = Hull();

/// <summary>Registers and opens communication.</summary>
            long plain = Registered(source, 0);
/// <summary>Registers and opens communication.</summary>
            long sized = Registered(source, source.Placed.Count);

            Assert.True(plain > sized,
                "sizing the node lists saved nothing: " + sized + " B against " + plain + " B");
        }

/// <summary>Registers the API and message handler.</summary>
        private static long Registered(GridBuilder source, int capacity)
        {
            ThermalSimulation simulation =
/// <summary>ThermalSimulation operation.</summary>
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

        [Fact]
/// <summary>TheGridModelsOwnTableIsOrderIndependentWhereItIsEnumerated operation.</summary>
        public void TheGridModelsOwnTableIsOrderIndependentWhereItIsEnumerated()
        {
/// <summary>Hull operation.</summary>
            GridBuilder source = Hull();

/// <summary>Built operation.</summary>
            GridModel plain = Built(source, 0);
/// <summary>Built operation.</summary>
            GridModel sized = Built(source, source.Placed.Count);

            Assert.Equal(plain.Min, sized.Min);
            Assert.Equal(plain.Max, sized.Max);
        }

        [Fact]
/// <summary>SizingAfterTheFirstBlockIsIgnoredAndLosesNothing operation.</summary>
        public void SizingAfterTheFirstBlockIsIgnoredAndLosesNothing()
        {
/// <summary>Hull operation.</summary>
            GridBuilder source = Hull();
/// <summary>Built operation.</summary>
            GridModel grid = Built(source, 0);

            int before = grid.BlockCount;
            grid.EnsureCellCapacity(100000);

            Assert.Equal(before, grid.BlockCount);
            Assert.NotNull(grid.GetAtCell(source.Placed[0].Min));
        }

        [Fact]
/// <summary>ACountItDoesNotHaveIsIgnored operation.</summary>
        public void ACountItDoesNotHaveIsIgnored()
        {
/// <summary>GridModel operation.</summary>
            GridModel grid = new GridModel(Catalog.LargeGridSize);

            grid.EnsureCellCapacity(0);
            grid.EnsureCellCapacity(-5);

            Assert.Equal(0, grid.BlockCount);
        }
    }
}
