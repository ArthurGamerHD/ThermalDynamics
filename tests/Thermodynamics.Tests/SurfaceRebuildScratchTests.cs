using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class SurfaceRebuildScratchTests
    {
/// <summary>Hull operation.</summary>
        private static GridBuilder Hull(int side, int hole)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(side, side, side));

            if (hole > 0)
            {
                for (int x = 1; x <= hole; x++)
                    for (int y = 1; y <= hole; y++)
                        for (int z = 1; z <= hole; z++)
                            builder.Remove(new Vector3I(x, y, z));
            }

            return builder;
        }

/// <summary>State operation.</summary>
        private static string State(SurfaceMap map, GridModel grid)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            foreach (BlockInstance block in grid.Blocks)
            {
                Vector3I[] cells = block.Cells;
                for (int i = 0; i < cells.Length; i++)
                {
                    text.Append(GridMath.Key(cells[i])).Append(':')
                        .Append(map.GetState(cells[i])).Append(';')
                        .Append(map.GetStructuralState(cells[i])).Append('\n');
                }
            }

            return text.ToString();
        }

        [Fact]
/// <summary>ASecondRebuildProducesTheSameMap operation.</summary>
        public void ASecondRebuildProducesTheSameMap()
        {
/// <summary>Hull operation.</summary>
            GridBuilder source = Hull(8, 4);
            GridModel grid = source.Grid;

/// <summary>SurfaceMap operation.</summary>
            SurfaceMap once = new SurfaceMap();
            once.Rebuild(grid);
/// <summary>State operation.</summary>
            string first = State(once, grid);

            once.Rebuild(grid);
            Assert.Equal(first, State(once, grid));

/// <summary>SurfaceMap operation.</summary>
            SurfaceMap fresh = new SurfaceMap();
            fresh.Rebuild(grid);
            Assert.Equal(first, State(fresh, grid));
        }

        [Fact]
/// <summary>ASmallHullDoesNotReadTheBuffersALargeOneSized operation.</summary>
        public void ASmallHullDoesNotReadTheBuffersALargeOneSized()
        {
/// <summary>SurfaceMap operation.</summary>
            SurfaceMap map = new SurfaceMap();

/// <summary>Hull operation.</summary>
            GridBuilder big = Hull(10, 5);
            map.Rebuild(big.Grid);

/// <summary>Hull operation.</summary>
            GridBuilder small = Hull(4, 0);
            map.Rebuild(small.Grid);

/// <summary>SurfaceMap operation.</summary>
            SurfaceMap fresh = new SurfaceMap();
            fresh.Rebuild(small.Grid);

            Assert.Equal(State(fresh, small.Grid), State(map, small.Grid));
        }

        [Fact]
/// <summary>ARepeatedRebuildStopsAllocating operation.</summary>
        public void ARepeatedRebuildStopsAllocating()
        {
/// <summary>Hull operation.</summary>
            GridBuilder source = Hull(12, 6);
/// <summary>SurfaceMap operation.</summary>
            SurfaceMap map = new SurfaceMap();

            map.Rebuild(source.Grid);

            GC.Collect();
            GC.WaitForPendingFinalizers();

            long before = GC.GetAllocatedBytesForCurrentThread();
            map.Rebuild(source.Grid);
            long after = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(after < 4096,
                "a repeated rebuild allocated " + after + " B, so the scratch is not being reused");
        }
    }
}
