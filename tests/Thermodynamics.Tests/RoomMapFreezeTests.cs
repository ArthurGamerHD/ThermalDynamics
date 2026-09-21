using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    public class RoomMapFreezeTests
    {
        private readonly ITestOutputHelper output;

/// <summary>RoomMapFreezeTests operation.</summary>
        public RoomMapFreezeTests(ITestOutputHelper output)
        {
            this.output = output;
        }

/// <summary>TwoRooms operation.</summary>
        private static ThermalSimulation TwoRooms()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-2, -2, -2), new Vector3I(3, 3, 3));
            builder.Shell(Catalog.LightArmor(), new Vector3I(6, -2, -2), new Vector3I(11, 3, 3));

            return builder.BuildSimulation(new ThermalSettings());
        }

        [Fact]
/// <summary>TheSolidAndRoomSetsIndexACellTheSameWay operation.</summary>
        public void TheSolidAndRoomSetsIndexACellTheSameWay()
        {
/// <summary>CellBitset operation.</summary>
            CellBitset solid = new CellBitset();
/// <summary>CellBitset operation.</summary>
            CellBitset roomCells = new CellBitset();
/// <summary>Vector3I operation.</summary>
            Vector3I min = new Vector3I(-9, 4, -2);
/// <summary>Vector3I operation.</summary>
            Vector3I max = min + new Vector3I(11, 7, 5);
            solid.Reset(min, max);
            roomCells.Reset(min, max);

            int inside = 0, outside = 0;
            for (int x = min.X - 2; x < max.X + 2; x++)
            for (int y = min.Y - 2; y < max.Y + 2; y++)
            for (int z = min.Z - 2; z < max.Z + 2; z++)
            {
/// <summary>Vector3I operation.</summary>
                Vector3I cell = new Vector3I(x, y, z);
                Assert.Equal(solid.IndexOf(cell), roomCells.IndexOf(cell));
                if (solid.IndexOf(cell) >= 0) inside++; else outside++;
            }

            Assert.Equal(11 * 7 * 5, inside);
            Assert.True(outside > 0, "no cell outside the box, so the refusal is untested");

/// <summary>Vector3I operation.</summary>
            Vector3I probe = min + new Vector3I(3, 2, 1);
            Assert.True(roomCells.Add(probe));
            Assert.True(roomCells.ContainsIndex(roomCells.IndexOf(probe)));
            Assert.False(solid.ContainsIndex(solid.IndexOf(probe)));
        }

        [Fact]
/// <summary>MembershipAgreesWithTheSearchOverEveryCellOfTheBox operation.</summary>
        public void MembershipAgreesWithTheSearchOverEveryCellOfTheBox()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", 4000));
            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings());
            RoomMap map = simulation.Rooms.Map;
            Assert.True(map.RoomCount > 3, "the hull mapped only " + map.RoomCount + " rooms");

/// <summary>Vector3I operation.</summary>
            Vector3I min = simulation.Grid.Min - new Vector3I(2, 2, 2);
/// <summary>Vector3I operation.</summary>
            Vector3I max = simulation.Grid.Max + new Vector3I(2, 2, 2);

            int inRoom = 0, external = 0, solid = 0;
            for (int x = min.X; x <= max.X; x++)
            for (int y = min.Y; y <= max.Y; y++)
            for (int z = min.Z; z <= max.Z; z++)
            {
/// <summary>Vector3I operation.</summary>
                Vector3I cell = new Vector3I(x, y, z);
                int room = map.RoomIndexOf(cell);

                bool expected = !map.IsSolid(cell) && (room < 0 || map.IsVented(room));
                Assert.True(expected == map.IsExternal(cell),
                    cell + " is " + (map.IsExternal(cell) ? "external" : "not external")
                    + " and the search says room " + room + ", solid " + map.IsSolid(cell));

                if (map.IsSolid(cell)) solid++;
                else if (room >= 0) inRoom++;
                else external++;
            }

            Assert.True(inRoom > 0 && external > 0 && solid > 0,
                "the box held " + inRoom + " room cells, " + external + " external and " + solid + " solid");
            Assert.Equal(inRoom, map.RoomCellCount);
        }

        [Fact]
/// <summary>EveryRoomCellStillResolvesToItsOwnRoom operation.</summary>
        public void EveryRoomCellStillResolvesToItsOwnRoom()
        {
/// <summary>TwoRooms operation.</summary>
            ThermalSimulation simulation = TwoRooms();
            RoomMap map = simulation.Rooms.Map;

            Assert.True(map.RoomCount >= 2, "the rig built " + map.RoomCount + " rooms");

            int judged = 0;
            for (int r = 0; r < map.RoomCount; r++)
            {
                RoomMap.RoomCells cells = map.CellsOf(r);
                Assert.NotEqual(0, cells.Count);

                for (int i = 0; i < cells.Count; i++)
                {
                    judged++;
                    Assert.Equal(r, map.RoomIndexOf(cells[i]));
                    Assert.Equal(r, map.RegionOf(cells[i]));
                }
            }

            output.WriteLine("{0} cells over {1} rooms", judged, map.RoomCount);
            Assert.True(judged > 0, "no room cell was judged (`E8`)");
            Assert.Equal(judged, map.RoomCellCount);
        }

        [Fact]
/// <summary>ACellInNoRoomResolvesToNothing operation.</summary>
        public void ACellInNoRoomResolvesToNothing()
        {
/// <summary>TwoRooms operation.</summary>
            ThermalSimulation simulation = TwoRooms();
            RoomMap map = simulation.Rooms.Map;

            Vector3I[] outside =
            {
/// <summary>Vector3I operation.</summary>
                new Vector3I(4, 0, 0),          // the gap between the two boxes
/// <summary>Vector3I operation.</summary>
                new Vector3I(-40, -40, -40),    // far outside the search box
/// <summary>Vector3I operation.</summary>
                new Vector3I(2, 0, 0),          // structure: a wall of the first box
            };

            for (int i = 0; i < outside.Length; i++)
            {
                Assert.Equal(-1, map.RoomIndexOf(outside[i]));
                Assert.Equal(RoomMap.ExternalRegion, map.RegionOf(outside[i]));
            }

            Assert.False(map.IsExternal(map.CellsOf(0)[0]));
            Assert.True(map.IsExternal(new Vector3I(4, 0, 0)));
        }

        [Fact]
/// <summary>ThePublishedLookupAgreesWithTheDictionaryItReplaced operation.</summary>
        public void ThePublishedLookupAgreesWithTheDictionaryItReplaced()
        {
/// <summary>TwoRooms operation.</summary>
            ThermalSimulation simulation = TwoRooms();
            RoomMap map = simulation.Rooms.Map;

            Dictionary<Vector3I, int> byCell = new Dictionary<Vector3I, int>(Vector3I.Comparer);
            for (int r = 0; r < map.RoomCount; r++)
            {
                RoomMap.RoomCells cells = map.CellsOf(r);
                for (int i = 0; i < cells.Count; i++) byCell[cells[i]] = r;
            }

            Assert.NotEmpty(byCell);

            int judged = 0;
            foreach (KeyValuePair<Vector3I, int> entry in byCell)
            {
                Assert.Equal(entry.Value, map.RoomIndexOf(entry.Key));
                judged++;

                for (int face = 0; face < Face.Count; face++)
                {
                    Vector3I neighbour = entry.Key + Face.Offsets[face];

                    int expected;
                    if (!byCell.TryGetValue(neighbour, out expected)) expected = -1;

                    Assert.Equal(expected, map.RoomIndexOf(neighbour));
                    judged++;
                }
            }

            output.WriteLine("{0} lookups agreed", judged);
            Assert.True(judged > 20, "only " + judged + " lookups were compared (`E8`)");
        }
    }
}
