using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    public class Se2RefineTests
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
            simulation.Solver.RebuildLinks();
            simulation.Rooms.RequestRestart(simulation.Grid);
            Assert.True(simulation.Rooms.RunToCompletion(), "the room pass did not finish");
            return simulation;
        }

        [Fact]
/// <summary>NodesAndLinksHoldWhileCellsCube operation.</summary>
        public void NodesAndLinksHoldWhileCellsCube()
        {
            GridBuilder source = GridBuilder.Large();
            source.PlaceCensus(LoadShapes.Build("ship", 2000));
/// <summary>Prepared operation.</summary>
            ThermalSimulation coarse = Prepared(source);

            const int factor = 3;
            GridBuilder refined = Se2Refine.Refined(source, factor);
/// <summary>Prepared operation.</summary>
            ThermalSimulation fine = Prepared(refined);

            Assert.Equal(coarse.Solver.Nodes.Count, fine.Solver.Nodes.Count);
            Assert.Equal(coarse.Solver.LinkCount, fine.Solver.LinkCount);

            long coarseCells = 0;
            for (int i = 0; i < source.Placed.Count; i++) coarseCells += source.Placed[i].CellCount;
            long fineCells = 0;
            for (int i = 0; i < refined.Placed.Count; i++) fineCells += refined.Placed[i].CellCount;
            Assert.Equal(coarseCells * factor * factor * factor, fineCells);
        }

        [Fact]
/// <summary>RoomsSurviveRefinementWithTheirVolumesCubed operation.</summary>
        public void RoomsSurviveRefinementWithTheirVolumesCubed()
        {
            GridBuilder source = GridBuilder.Large();
            BlockModel armour = Catalog.LightArmor();
            source.Shell(armour, new Vector3I(0, 0, 0), new Vector3I(9, 4, 4));
            for (int y = 1; y < 3; y++)
            for (int z = 1; z < 3; z++)
            {
                source.Place(armour, new Vector3I(4, y, z));
            }

/// <summary>Prepared operation.</summary>
            ThermalSimulation coarse = Prepared(source);
            Assert.True(coarse.Rooms.Map.RoomCount > 0, "no rooms, so invariance proves nothing");

            const int factor = 3;
            GridBuilder refined = Se2Refine.Refined(source, factor);
/// <summary>Prepared operation.</summary>
            ThermalSimulation fine = Prepared(refined);

            RoomMap before = coarse.Rooms.Map;
            RoomMap after = fine.Rooms.Map;
            Assert.Equal(before.RoomCount, after.RoomCount);

/// <summary>List operation.</summary>
            List<int> coarseSizes = new List<int>();
/// <summary>List operation.</summary>
            List<int> fineSizes = new List<int>();
            for (int r = 0; r < before.RoomCount; r++) coarseSizes.Add(before.CellsInRoom(r) * factor * factor * factor);
            for (int r = 0; r < after.RoomCount; r++) fineSizes.Add(after.CellsInRoom(r));
            coarseSizes.Sort();
            fineSizes.Sort();
            Assert.Equal(coarseSizes, fineSizes);
        }

        [Fact]
/// <summary>PartialSealingRefinesWithoutPhantomPockets operation.</summary>
        public void PartialSealingRefinesWithoutPhantomPockets()
        {
            GridBuilder source = GridBuilder.Large();
            BlockModel armour = Catalog.LightArmor();
            source.Shell(armour, new Vector3I(-1, -1, -1), new Vector3I(5, 5, 5));

            BlockModel vented = BlockModel.Solid("Vented", Vector3I.One, 100f, null);
            int open = CellSurface.WithSelfAirtight(
                CellSurface.SelfAirtightMask | CellSurface.SelfMountMask, 0, false);
            vented.SetLocalSurface(Vector3I.Zero, open);
            source.Place(vented, new Vector3I(2, 2, 2));

/// <summary>Prepared operation.</summary>
            ThermalSimulation coarse = Prepared(source);

            const int factor = 3;
            GridBuilder refined = Se2Refine.Refined(source, factor);
/// <summary>Prepared operation.</summary>
            ThermalSimulation fine = Prepared(refined);

            Assert.Equal(coarse.Rooms.Map.RoomCount, fine.Rooms.Map.RoomCount);
        }
    }
}
