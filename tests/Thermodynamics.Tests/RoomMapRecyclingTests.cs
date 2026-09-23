using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    public class RoomMapRecyclingTests
    {

        private static ThermalSimulation Shell()
        {
            GridBuilder builder = RoomFixtures.DooredShell();

            ThermalSimulation simulation = new ThermalSimulation(Hulls.Uncapped(), builder.Grid);
            for (int i = 0; i < builder.Placed.Count; i++) simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            simulation.RebuildAll();
            return simulation;
        }


        private static ThermalSimulation Census()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", 8000));

            ThermalSimulation simulation = new ThermalSimulation(Hulls.Uncapped(), builder.Grid);
            for (int i = 0; i < builder.Placed.Count; i++) simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            simulation.RebuildAll();
            return simulation;
        }


        private static RoomMap RunPass(ThermalSimulation simulation)
        {
            simulation.Rooms.RequestRestart(simulation.Grid);
            Assert.True(simulation.Rooms.RunToCompletion(), "the room pass did not finish");
            return simulation.Rooms.Map;
        }

        [Fact]

        public void TheMapperRotatesThreeMapsAndAllocatesNoFourth()
        {

            ThermalSimulation simulation = Shell();


            List<RoomMap> published = new List<RoomMap>();
            published.Add(simulation.Rooms.Map);
            Assert.True(published[0].RoomCount > 0, "the shell mapped no rooms, so the rotation would cycle empty maps");

            for (int i = 0; i < 8; i++) published.Add(RunPass(simulation));

            Assert.NotSame(published[0], published[1]);
            Assert.NotSame(published[1], published[2]);
            Assert.NotSame(published[0], published[2]);

            for (int i = 3; i < published.Count; i++)
            {
                Assert.Same(published[i - 3], published[i]);
            }
        }

        [Fact]

        public void APublishedMapAnswersUntilThePublishAfterNext()
        {

            ThermalSimulation simulation = Shell();
            RoomMap held = simulation.Rooms.Map;

            int roomCount = held.RoomCount;
            Assert.True(roomCount > 0, "the held map has no rooms, so nothing below would notice it being reset");
            int solidCells = held.SolidCellCount;
            int roomCells = held.RoomCellCount;
            bool[] vented = new bool[roomCount];
            Vector3I[] probes = new Vector3I[roomCount];
            for (int r = 0; r < roomCount; r++)
            {
                vented[r] = held.IsVented(r);
                probes[r] = held.CellsOf(r)[0];
            }

            RunPass(simulation);

            simulation.Rooms.RequestRestart(simulation.Grid);
            simulation.Rooms.Step(64);

            Assert.True(held.RoomCount == roomCount, "the held map lost its rooms while the second pass after it was in flight");
            Assert.Equal(solidCells, held.SolidCellCount);
            Assert.Equal(roomCells, held.RoomCellCount);
            for (int r = 0; r < roomCount; r++)
            {
                Assert.Equal(vented[r], held.IsVented(r));
                Assert.Equal(r, held.RoomIndexOf(probes[r]));
            }

            Assert.True(simulation.Rooms.RunToCompletion(), "the in-flight pass did not finish");

            Assert.NotSame(held, simulation.Rooms.Map);
        }

        [Fact]

        public void ARecycledMapIsCellForCellIdenticalToAFreshOne()
        {

            ThermalSimulation recycled = Census();
            RoomMap firstMap = recycled.Rooms.Map;

            RoomMap current = firstMap;
            for (int i = 0; i < 6; i++) current = RunPass(recycled);

            Assert.Same(firstMap, current);


            ThermalSimulation fresh = Census();
            RoomMapAssert.SameMap(fresh.Rooms.Map, current, "census hull, recycled against fresh");
        }

        [Fact]

        public void AWarmPassAllocatesAnOrderLessThanAColdOne()
        {

            ThermalSimulation simulation = Census();


            long cold = AllocatedBy(() => RunPass(simulation));

            RunPass(simulation);
            RunPass(simulation);

            long warm = AllocatedBy(() => RunPass(simulation));

            Assert.True(cold > 100_000, "the cold pass allocated " + cold + " B — too little for the ratio below to mean anything");
            Assert.True(warm * 10 < cold, "a warm pass allocated " + warm + " B against the cold pass's " + cold + " B; recycling is not recycling");
        }


        private static long AllocatedBy(Action pass)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            pass();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }
}
