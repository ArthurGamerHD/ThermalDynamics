using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The room map answers the same questions once it stops holding a dictionary to answer
    /// them with.**
    ///
    /// <para>
    /// Room cells were held twice: once per room, and once in a `Dictionary&lt;Vector3I, int&gt;`
    /// costing about 31 bytes a cell — the one memory row that still climbs with grid size
    /// (backlog.md `E3`). A map is written once and read for the life of
    /// the grid, so when a pass completes the dictionary is replaced by a sorted `long[]` of cell
    /// keys and a parallel `int[]` of rooms: twelve bytes a cell, and a binary search over
    /// contiguous memory instead of a hash and a bucket chase.
    /// </para>
    ///
    /// <para>
    /// **The other copy is the oracle.** `Rooms` still holds each room's cells, so the frozen
    /// lookup can be checked against the thing it was derived from, cell by cell (`D8`).
    /// </para>
    /// </summary>
    public class RoomMapFreezeTests
    {
        private readonly ITestOutputHelper output;

        public RoomMapFreezeTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>
        /// Two sealed boxes with a gap between them: more than one room, and rooms of more than one
        /// cell, so a lookup has something to search through rather than to stumble onto.
        /// </summary>
        private static ThermalSimulation TwoRooms()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-2, -2, -2), new Vector3I(3, 3, 3));
            builder.Shell(Catalog.LightArmor(), new Vector3I(6, -2, -2), new Vector3I(11, 3, 3));

            return builder.BuildSimulation(new ThermalSettings());
        }

        /// <summary>
        /// The frozen arrays come from a walk of the room-cell bitset in box-index order, on the
        /// claim that box-index order is key order. The claim is checked inside the freeze and a
        /// failure falls back to sorting; this asserts the fallback was not taken, on a hull with
        /// tens of rooms and on the two-room rig — because a freeze that always fell back would
        /// pass every other test here at the old price.
        /// </summary>
        [Fact]
        public void TheFreezeNeverFallsBackToSorting()
        {
            Assert.False(TwoRooms().Rooms.Map.FrozeByFallback, "two rooms: the bitset walk was out of order");

            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", 8000));
            RoomMap map = builder.BuildSimulation(new ThermalSettings()).Rooms.Map;
            Assert.True(map.RoomCount > 5, "the census hull mapped only " + map.RoomCount + " rooms");
            Assert.False(map.FrozeByFallback, "census hull: the bitset walk was out of order");
        }

        /// <summary>The bitset walk the freeze rests on: next set bit and the cell at an index, at word edges.</summary>
        [Fact]
        public void NextSetIndexAndCellAtAgreeWithTheCellsThatWereAdded()
        {
            CellBitset bits = new CellBitset();
            Vector3I min = new Vector3I(-5, 3, -9);
            bits.Reset(min, min + new Vector3I(7, 5, 4));

            Vector3I[] added =
            {
                min, min + new Vector3I(6, 0, 0), min + new Vector3I(0, 1, 0), min + new Vector3I(3, 4, 3),
                min + new Vector3I(6, 4, 3), min + new Vector3I(1, 2, 2),
            };
            foreach (Vector3I cell in added) Assert.True(bits.Add(cell));

            List<Vector3I> walked = new List<Vector3I>();
            long end = bits.Capacity;
            for (long i = bits.NextSetIndex(0, end); i < end; i = bits.NextSetIndex(i + 1, end))
            {
                walked.Add(bits.CellAt(i));
                Assert.Equal(i, bits.IndexOf(bits.CellAt(i)));
            }

            Assert.Equal(added.Length, walked.Count);
            for (int i = 1; i < walked.Count; i++)
            {
                Assert.True(GridMath.Key(walked[i]) > GridMath.Key(walked[i - 1]),
                    "walk order is not key order at " + walked[i - 1] + " -> " + walked[i]);
            }
            foreach (Vector3I cell in added) Assert.Contains(cell, walked);
        }

        /// <summary>
        /// Every cell of every room resolves to the room that holds it, and nothing else does.
        /// </summary>
        [Fact]
        public void EveryRoomCellStillResolvesToItsOwnRoom()
        {
            ThermalSimulation simulation = TwoRooms();
            RoomMap map = simulation.Rooms.Map;

            Assert.True(map.RoomCount >= 2, "the rig built " + map.RoomCount + " rooms");

            int judged = 0;
            for (int r = 0; r < map.Rooms.Count; r++)
            {
                List<Vector3I> cells = map.Rooms[r];
                Assert.NotEmpty(cells);

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

        /// <summary>
        /// And a cell in no room answers so — including one just outside a room, which is the
        /// neighbour a binary search lands next to.
        /// </summary>
        [Fact]
        public void ACellInNoRoomResolvesToNothing()
        {
            ThermalSimulation simulation = TwoRooms();
            RoomMap map = simulation.Rooms.Map;

            Vector3I[] outside =
            {
                new Vector3I(4, 0, 0),          // the gap between the two boxes
                new Vector3I(-40, -40, -40),    // far outside the search box
                new Vector3I(2, 0, 0),          // structure: a wall of the first box
            };

            for (int i = 0; i < outside.Length; i++)
            {
                Assert.Equal(-1, map.RoomIndexOf(outside[i]));
                Assert.Equal(RoomMap.ExternalRegion, map.RegionOf(outside[i]));
            }

            // A sealed room is not external; the cells above are. Both directions, or the test
            // passes on a map that answers "nothing" to everything.
            Assert.False(map.IsExternal(map.Rooms[0][0]));
            Assert.True(map.IsExternal(new Vector3I(4, 0, 0)));
        }

        /// <summary>
        /// **The frozen answer is the dictionary's answer**, checked against one rebuilt from the
        /// copy that remains — which is the code the arrays replaced.
        /// </summary>
        [Fact]
        public void TheFrozenLookupAgreesWithTheDictionaryItReplaced()
        {
            ThermalSimulation simulation = TwoRooms();
            RoomMap map = simulation.Rooms.Map;

            Dictionary<Vector3I, int> byCell = new Dictionary<Vector3I, int>(Vector3I.Comparer);
            for (int r = 0; r < map.Rooms.Count; r++)
            {
                List<Vector3I> cells = map.Rooms[r];
                for (int i = 0; i < cells.Count; i++) byCell[cells[i]] = r;
            }

            Assert.NotEmpty(byCell);

            // Every cell the dictionary knows, and a shell of cells around each one so the misses
            // are judged too rather than only the hits.
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
