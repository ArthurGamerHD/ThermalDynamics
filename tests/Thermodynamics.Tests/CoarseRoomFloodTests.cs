using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    public class CoarseRoomFloodTests
    {
/// <summary>Prepared operation.</summary>
        private static ThermalSimulation Prepared(GridBuilder builder)
        {
/// <summary>ThermalSimulation operation.</summary>
            ThermalSimulation simulation = new ThermalSimulation(Hulls.Uncapped(), builder.Grid);
            for (int i = 0; i < builder.Placed.Count; i++)
            {
                simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            }
            simulation.Surfaces.Rebuild(simulation.Grid);
            simulation.Rooms.RequestRestart(simulation.Grid);
            Assert.True(simulation.Rooms.RunToCompletion(), "the reference pass did not finish");
            return simulation;
        }

/// <summary>AssertMatches operation.</summary>
        private static void AssertMatches(GridBuilder builder, int edge, string what)
        {
/// <summary>Prepared operation.</summary>
            ThermalSimulation simulation = Prepared(builder);
            RoomMap oracle = simulation.Rooms.Map;
            Assert.True(oracle.RoomCount > 0,
                what + ": the mapper found no rooms, so agreement proves nothing");

/// <summary>CoarseRoomFlood operation.</summary>
            CoarseRoomFlood flood = new CoarseRoomFlood(edge);
            flood.Run(simulation.Grid, simulation.Surfaces);

            string mismatch = CoarseRoomLab.Verify(oracle, flood);
            Assert.True(mismatch == null, what + " at edge " + edge + ": " + mismatch);
        }

        [Fact]
/// <summary>OneRoomShellMatchesAtEveryEdge operation.</summary>
        public void OneRoomShellMatchesAtEveryEdge()
        {
            foreach (int edge in new[] { 2, 3, 4, 7 })
            {
                GridBuilder builder = GridBuilder.Large();
                builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(5, 5, 5));
                AssertMatches(builder, edge, "shell");
            }
        }

        [Fact]
/// <summary>AWallInsideOneSupercellStillSplitsTheRooms operation.</summary>
        public void AWallInsideOneSupercellStillSplitsTheRooms()
        {
            GridBuilder builder = GridBuilder.Large();
            BlockModel armour = Catalog.LightArmor();
            builder.Shell(armour, new Vector3I(0, 0, 0), new Vector3I(9, 4, 4));
            for (int y = 1; y < 3; y++)
            for (int z = 1; z < 3; z++)
            {
                builder.Place(armour, new Vector3I(4, y, z));
            }
            AssertMatches(builder, 4, "divided shell");
        }

        [Fact]
/// <summary>AnOpenFrameCellJoinsItsRoom operation.</summary>
        public void AnOpenFrameCellJoinsItsRoom()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(5, 5, 5));
            builder.Place(BlockModel.Open("Frame", Vector3I.One, 100f, null), new Vector3I(2, 2, 2));
            AssertMatches(builder, 3, "framed pocket");
        }

        [Fact]
/// <summary>AClosedDoorCellIsARoomNotStructure operation.</summary>
        public void AClosedDoorCellIsARoomNotStructure()
        {
            GridBuilder builder = RoomFixtures.DooredShell();

            AssertMatches(builder, 3, "shell with a door");
        }

        [Fact]
/// <summary>ACensusHullMatchesTheMapper operation.</summary>
        public void ACensusHullMatchesTheMapper()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", 8000));
            AssertMatches(builder, 4, "census hull");
        }

        [Fact]
/// <summary>ARefinedHullMatchesTheMapperAtTheLatticeFactor operation.</summary>
        public void ARefinedHullMatchesTheMapperAtTheLatticeFactor()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", 2000));
            GridBuilder fine = Se2Refine.Refined(builder, 3);
            AssertMatches(fine, 3, "refined census hull");
        }
    }
}
