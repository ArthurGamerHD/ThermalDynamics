using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The room mapper is well covered for grids built in one shot through
    /// <see cref="ThermalSimulation.RebuildAll"/>. A player welding a room is a different path
    /// entirely: one <see cref="ThermalSimulation.AddBlock"/> per block, with
    /// <see cref="ThermalSimulation.Update"/> pumping the budgeted mapper in between. These
    /// tests walk that path.
    /// </summary>
    public class IncrementalRoomTests
    {
        private static List<Vector3I> ShellCells(Vector3I min, Vector3I maxExclusive)
        {
            List<Vector3I> cells = new List<Vector3I>();
            for (int z = min.Z; z < maxExclusive.Z; z++)
            {
                for (int y = min.Y; y < maxExclusive.Y; y++)
                {
                    for (int x = min.X; x < maxExclusive.X; x++)
                    {
                        bool onSurface =
                            x == min.X || x == maxExclusive.X - 1 ||
                            y == min.Y || y == maxExclusive.Y - 1 ||
                            z == min.Z || z == maxExclusive.Z - 1;
                        if (onSurface) cells.Add(new Vector3I(x, y, z));
                    }
                }
            }
            return cells;
        }

        /// <summary>Runs frames until the mapper has nothing left to do, or the guard trips.</summary>
        private static void SettleMapper(ThermalSimulation simulation, int maxFrames = 600)
        {
            for (int i = 0; i < maxFrames && (simulation.Rooms.HasWorkPending || i == 0); i++)
            {
                simulation.Update(1f / 60f, Worlds.Shadow());
            }
        }

        [Fact]
        public void WeldingAShellOneBlockAtATimeStillFindsTheRoom()
        {
            GridModel grid = new GridModel(Catalog.LargeGridSize);
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), grid);

            BlockModel armour = Catalog.LightArmor();
            List<Vector3I> cells = ShellCells(new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));

            for (int i = 0; i < cells.Count; i++)
            {
                simulation.AddBlock(new BlockInstance(armour, cells[i], BlockOrientation.Identity));

                // the game polls on the ten-frame tick; the mapper gets a budget each poll
                simulation.Update(ThermalGridTick, Worlds.Shadow());
            }

            SettleMapper(simulation);

            Assert.Equal(1, simulation.Rooms.Map.RoomCount);
            Assert.False(simulation.Rooms.Map.IsExternal(Vector3I.Zero));
        }

        private const float ThermalGridTick = 10f / 60f;

        [Fact]
        public void EveryBlockCellIsSolidNotExternalAfterAnIncrementalBuild()
        {
            GridModel grid = new GridModel(Catalog.LargeGridSize);
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), grid);

            BlockModel armour = Catalog.LightArmor();
            List<Vector3I> cells = ShellCells(new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));
            for (int i = 0; i < cells.Count; i++)
            {
                simulation.AddBlock(new BlockInstance(armour, cells[i], BlockOrientation.Identity));
                simulation.Update(ThermalGridTick, Worlds.Shadow());
            }
            SettleMapper(simulation);

            RoomMap map = simulation.Rooms.Map;
            for (int i = 0; i < cells.Count; i++)
            {
                Assert.True(map.IsSolid(cells[i]), "cell " + cells[i] + " should be structure, not open space");
            }
        }

        /// <summary>
        /// A door in the shell wall is the one shell block whose sealing depends on state. Closed,
        /// the room is still a room; open, it is not — and the interior stops being enclosed.
        ///
        /// This is the reported failure in miniature: the field grid was a shell with one sliding
        /// door, and it mapped as no rooms at all because the door never sealed in either state.
        /// </summary>
        [Fact]
        public void AClosedDoorInTheWallKeepsTheRoomSealedAndAnOpenOneDoesNot()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));

            // swap the face centre for a sliding door, facing out of the room
            BlockInstance plug = builder.Grid.GetAtCell(new Vector3I(0, 0, -1));
            builder.Grid.Remove(plug);
            builder.Place(Catalog.SlideDoor(), new Vector3I(0, 0, -1));
            BlockInstance door = builder.Last;

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings());
            Assert.Equal(1, simulation.Rooms.Map.RoomCount);

            door.IsSealedByDoorState = false;
            simulation.RefreshBlockSealing(door);

            Assert.True(simulation.Rooms.Map.IsVented(0));
            Assert.True(simulation.Rooms.Map.IsExternal(Vector3I.Zero));

            door.IsSealedByDoorState = true;
            simulation.RefreshBlockSealing(door);

            Assert.Equal(1, simulation.Rooms.Map.RoomCount);
            Assert.False(simulation.Rooms.Map.IsExternal(Vector3I.Zero));
        }

        /// <summary>
        /// The reported symptom: a sealed room whose mapper says nothing is enclosed. This asserts
        /// the classification adds up — every cell of the padded search box is external, solid, or
        /// in a room, and the counts match the geometry exactly.
        /// </summary>
        [Fact]
        public void TheClassificationOfEveryCellInTheSearchBoxAddsUp()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));
            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings());

            RoomMap map = simulation.Rooms.Map;

            // padded box is (-2..2)^3 = 125 cells; 26 shell cells, 1 room cell, 98 external
            Assert.Equal(98, map.ExternalCellCount);
            Assert.Equal(26, map.SolidCellCount);
            Assert.Equal(1, map.RoomCount);
            Assert.Single(map.Rooms[0]);
        }
    }
}
