using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    public class RoomMapSolidTests
    {
/// <summary>Builds the API method table.</summary>
        private static ThermalSimulation Build(string which)
        {
            GridBuilder builder = GridBuilder.Large();
            if (which == "census")
            {
                builder.PlaceCensus(LoadShapes.Build("ship", 4000));
            }
            else
            {
                RoomFixtures.AddDooredShell(builder);
            }
            return builder.BuildSimulation(Hulls.Uncapped(), 293.15f);
        }

        [Theory]
        [InlineData("shell")]
        [InlineData("census")]
/// <summary>ACellIsSolidExactlyWhenTheSurfaceMapSealsItOnEveryFaceAndItIsNoDoor operation.</summary>
        public void ACellIsSolidExactlyWhenTheSurfaceMapSealsItOnEveryFaceAndItIsNoDoor(string which)
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation simulation = Build(which);
            RoomMap map = simulation.Rooms.Map;
            SurfaceMap surfaces = simulation.Surfaces;

/// <summary>HashSet operation.</summary>
            HashSet<Vector3I> doorCells = new HashSet<Vector3I>(Vector3I.Comparer);
            foreach (BlockInstance door in simulation.Grid.StateDependentBlocks)
            {
                foreach (Vector3I cell in door.Cells) doorCells.Add(cell);
            }

/// <summary>Vector3I operation.</summary>
            Vector3I min = simulation.Grid.Min - new Vector3I(2, 2, 2);
/// <summary>Vector3I operation.</summary>
            Vector3I max = simulation.Grid.Max + new Vector3I(2, 2, 2);

            int solid = 0;
            int asked = 0;
            for (int x = min.X; x <= max.X; x++)
            for (int y = min.Y; y <= max.Y; y++)
            for (int z = min.Z; z <= max.Z; z++)
            {
/// <summary>Vector3I operation.</summary>
                Vector3I cell = new Vector3I(x, y, z);
                bool expected = surfaces.IsFullySealedStructurally(cell) && !doorCells.Contains(cell);
                Assert.True(expected == map.IsSolid(cell),
                    which + ": " + cell + " is " + (map.IsSolid(cell) ? "solid" : "not solid")
                    + " in the map and " + (expected ? "sealed on every face" : "not") + " in the surface map");
                asked++;
                if (expected) solid++;
            }

            Assert.True(solid > 0, which + ": no cell is structure, so the set is untested");
            Assert.True(asked > solid, which + ": every cell asked was structure, so the outside is untested");
            Assert.Equal(solid, map.SolidCellCount);
        }

        [Fact]
/// <summary>ASecondPassStartsFromAnEmptySet operation.</summary>
        public void ASecondPassStartsFromAnEmptySet()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation simulation = Build("shell");
            int before = simulation.Rooms.Map.SolidCellCount;

            BlockInstance last = simulation.Grid.Blocks[0];
            simulation.RemoveBlock(last);
            simulation.RebuildAll();

            Assert.True(before > 0);
            Assert.Equal(before - 1, simulation.Rooms.Map.SolidCellCount);
            Assert.False(simulation.Rooms.Map.IsSolid(last.Cells[0]));
        }
    }
}
