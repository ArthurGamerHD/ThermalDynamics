using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    public class RoomCellStorageTests
    {
/// <summary>CompartmentedHull operation.</summary>
        private static ThermalSimulation CompartmentedHull(int blocks)
        {
            ThermalSettings settings = Hulls.Uncapped();
            ThermalSimulation simulation = Hulls.Driven(settings, blocks);

            Assert.True(simulation.Rooms.Map.RoomCount > 0,
                "the hull mapped no rooms, so nothing below is being checked");

            return simulation;
        }

        [Fact]
/// <summary>NoRoomCarriesACellTwice operation.</summary>
        public void NoRoomCarriesACellTwice()
        {
/// <summary>CompartmentedHull operation.</summary>
            RoomMap map = CompartmentedHull(4000).Rooms.Map;

/// <summary>HashSet operation.</summary>
            HashSet<Vector3I> seen = new HashSet<Vector3I>(Vector3I.Comparer);

            for (int r = 0; r < map.RoomCount; r++)
            {
                RoomMap.RoomCells cells = map.CellsOf(r);
                for (int i = 0; i < cells.Count; i++)
                {
                    Assert.True(seen.Add(cells[i]),
                        "cell " + cells[i] + " appears more than once across the room ranges,"
                        + " so an array without a duplicate check is the wrong place to hold them");
                }
            }

            Assert.Equal(map.RoomCellCount, seen.Count);
        }

        [Fact]
/// <summary>TheIndexAndTheRoomsDescribeTheSameCells operation.</summary>
        public void TheIndexAndTheRoomsDescribeTheSameCells()
        {
/// <summary>CompartmentedHull operation.</summary>
            RoomMap map = CompartmentedHull(4000).Rooms.Map;

            int listed = 0;

            for (int r = 0; r < map.RoomCount; r++)
            {
                RoomMap.RoomCells cells = map.CellsOf(r);
                Assert.Equal(cells.Count, map.CellsInRoom(r));
                listed += cells.Count;

                for (int i = 0; i < cells.Count; i++)
                {
                    Assert.Equal(r, map.RoomIndexOf(cells[i]));
                    Assert.False(map.IsExternal(cells[i]));
                    Assert.False(map.IsSolid(cells[i]));
                }
            }

            Assert.Equal(map.RoomCellCount, listed);
        }

        [Fact]
/// <summary>ACompletedPassLeavesNoSpareCapacityInTheCellStore operation.</summary>
        public void ACompletedPassLeavesNoSpareCapacityInTheCellStore()
        {
/// <summary>CompartmentedHull operation.</summary>
            RoomMap map = CompartmentedHull(4000).Rooms.Map;

            Assert.True(map.RoomCellCount > 0, "the hull mapped no room cells, so nothing was checked");
            Assert.Equal(map.RoomCellCount, map.RoomCellCapacity);
        }

        [Fact]
/// <summary>TheRoomsTileTheCellStoreWithoutGapOrOverlap operation.</summary>
        public void TheRoomsTileTheCellStoreWithoutGapOrOverlap()
        {
/// <summary>CompartmentedHull operation.</summary>
            RoomMap map = CompartmentedHull(4000).Rooms.Map;

            int expected = 0;
            for (int r = 0; r < map.RoomCount; r++)
            {
                RoomMap.RoomCells cells = map.CellsOf(r);
                Assert.NotEqual(0, cells.Count);

                for (int i = 0; i < cells.Count; i++)
                {
                    Assert.Equal(r, map.RoomIndexOf(cells[i]));
                }

                expected += cells.Count;
            }

            Assert.Equal(map.RoomCellCount, expected);
        }

        [Fact]
/// <summary>AHintedFillNeitherGrowsNorTrimsItsCellStore operation.</summary>
        public void AHintedFillNeitherGrowsNorTrimsItsCellStore()
        {
            const int Cells = 3000;

/// <summary>FillAndPublish operation.</summary>
            long untold = FillAndPublish(0, Cells);
/// <summary>FillAndPublish operation.</summary>
            long told = FillAndPublish(Cells, Cells);

            long oneStore = Cells * 12L;
            Assert.True(untold - told >= oneStore,
                "a fill told its size saved " + (untold - told).ToString("n0")
                + " bytes where one store of " + Cells.ToString("n0") + " cells is "
                + oneStore.ToString("n0") + "; the hint is reaching nothing");
        }

/// <summary>FillAndPublish operation.</summary>
        private static long FillAndPublish(int hint, int cells)
        {
/// <summary>RoomMap operation.</summary>
            RoomMap map = new RoomMap();
            map.SetSearchBounds(new Vector3I(0, 0, 0), new Vector3I(20, 20, 20));
            if (hint > 0) map.HintRoomCells(hint);

            long before = GC.GetAllocatedBytesForCurrentThread();

            int room = map.BeginRoom();
            int added = 0;
            for (int z = 0; z < 20 && added < cells; z++)
            {
                for (int y = 0; y < 20 && added < cells; y++)
                {
                    for (int x = 0; x < 20 && added < cells; x++)
                    {
                        map.AddToRoom(room, new Vector3I(x, y, z));
                        added++;
                    }
                }
            }

            map.DropEmptyRooms();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(cells, map.RoomCellCount);
            Assert.Equal(cells, map.RoomCellCapacity);
            return allocated;
        }

        [Fact]
/// <summary>TheMapperSizesARebuildFromThePassBeforeIt operation.</summary>
        public void TheMapperSizesARebuildFromThePassBeforeIt()
        {
/// <summary>Unmapped operation.</summary>
            ThermalSimulation simulation = Unmapped(4000);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunPass(simulation);
            long first = GC.GetAllocatedBytesForCurrentThread() - before;

            int cells = simulation.Rooms.Map.RoomCellCount;
            Assert.True(cells > 0, "the hull mapped no room cells, so nothing below is measured");

            before = GC.GetAllocatedBytesForCurrentThread();
            RunPass(simulation);
            long second = GC.GetAllocatedBytesForCurrentThread() - before;

            RoomMap map = simulation.Rooms.Map;
            Assert.Equal(cells, map.RoomCellCount);
            Assert.Equal(map.RoomCellCount, map.RoomCellCapacity);

            long bound = cells * 34L;
            Assert.True(second < bound,
                "the rebuild allocated " + second.ToString("n0") + " bytes for "
                + cells.ToString("n0") + " room cells — " + (second / (double)cells).ToString("n1")
                + " a cell, against a bar of 34 — so it grew into its store and trimmed it back"
/// <summary>it operation.</summary>
                + " rather than being sized from the pass before it (the first pass, buffers and"
                + " all, allocated " + first.ToString("n0") + ")");
        }

/// <summary>Unmapped operation.</summary>
        private static ThermalSimulation Unmapped(int blocks)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", blocks));

/// <summary>ThermalSimulation operation.</summary>
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings().Derive(), builder.Grid);
            for (int i = 0; i < builder.Placed.Count; i++)
            {
                simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            }

            simulation.Surfaces.Rebuild(simulation.Grid);
            return simulation;
        }

/// <summary>RunPass operation.</summary>
        private static void RunPass(ThermalSimulation simulation)
        {
            simulation.Rooms.RequestRestart(simulation.Grid);
            Assert.True(simulation.Rooms.RunToCompletion(), "the room pass did not finish");
        }

        [Fact]
/// <summary>AClosedRoomCannotBeAddedTo operation.</summary>
        public void AClosedRoomCannotBeAddedTo()
        {
/// <summary>RoomMap operation.</summary>
            RoomMap map = new RoomMap();
            map.SetSearchBounds(new Vector3I(0, 0, 0), new Vector3I(8, 8, 8));

            int first = map.BeginRoom();
            map.AddToRoom(first, new Vector3I(1, 1, 1));
            int second = map.BeginRoom();
            map.AddToRoom(second, new Vector3I(2, 2, 2));

            Assert.Throws<InvalidOperationException>(delegate
            {
                map.AddToRoom(first, new Vector3I(3, 3, 3));
            });
        }

        [Fact]
/// <summary>SolidRoomAndExternalCellsAccountForTheWholeSearchVolume operation.</summary>
        public void SolidRoomAndExternalCellsAccountForTheWholeSearchVolume()
        {
/// <summary>CompartmentedHull operation.</summary>
            ThermalSimulation simulation = CompartmentedHull(4000);
            RoomMap map = simulation.Rooms.Map;

            int classified = map.SolidCellCount + map.RoomCellCount + map.ExternalCellCount;

            Assert.Equal(RoomAuditor.SearchVolumeOf(simulation.Grid), classified);
        }
    }
}
