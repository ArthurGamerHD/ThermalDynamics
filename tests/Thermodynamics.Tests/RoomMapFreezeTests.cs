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
        /// The freeze sorts by radix rather than by comparison, and the result must be the same
        /// order to the element (`D8`): held against `Array.Sort` on random keys spanning every
        /// digit, on keys already sorted, reversed, all equal, and on the offsets a real box
        /// produces — with the rooms carried along by both.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(1000)]
        [InlineData(100003)]
        public void TheRadixSortOrdersKeysAsTheComparisonSortDoes(int count)
        {
            uint state = 0x2545F491u ^ (uint)count;
            long origin = GridMath.Key(new Vector3I(-40, -7, -13));
            long[] keys = new long[count];
            int[] rooms = new int[count];
            for (int i = 0; i < count; i++)
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                // Cells scattered across a box wide enough to exercise five digits of offset.
                Vector3I cell = new Vector3I(-40 + (int)(state % 500), -7 + (int)((state >> 9) % 300), -13 + (int)((state >> 18) % 200));
                keys[i] = GridMath.Key(cell);
                rooms[i] = (int)(state % 97);
            }

            long[] expectedKeys = (long[])keys.Clone();
            int[] expectedRooms = (int[])rooms.Clone();
            System.Array.Sort(expectedKeys, expectedRooms);

            RoomMap.RadixSortByKey(keys, rooms, count, origin);

            Assert.Equal(expectedKeys, keys);
            // Equal keys carry equal rooms only if both sorts are stable on ties; the keys here are
            // cells and may repeat, so rooms are compared where the key is unique.
            for (int i = 0; i < count; i++)
            {
                bool unique = (i == 0 || expectedKeys[i - 1] != expectedKeys[i])
                    && (i == count - 1 || expectedKeys[i + 1] != expectedKeys[i]);
                if (unique) Assert.Equal(expectedRooms[i], rooms[i]);
            }
        }

        [Fact]
        public void TheRadixSortHandlesTheDegenerateOrders()
        {
            long origin = 0;
            long[] sorted = { 1, 2, 3, 4, 5, 6, 7, 8 };
            int[] r1 = { 0, 1, 2, 3, 4, 5, 6, 7 };
            RoomMap.RadixSortByKey(sorted, r1, sorted.Length, origin);
            Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 8 }, sorted);
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, r1);

            long[] reversed = { 8, 7, 6, 5, 4, 3, 2, 1 };
            int[] r2 = { 0, 1, 2, 3, 4, 5, 6, 7 };
            RoomMap.RadixSortByKey(reversed, r2, reversed.Length, origin);
            Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 8 }, reversed);
            Assert.Equal(new[] { 7, 6, 5, 4, 3, 2, 1, 0 }, r2);

            long[] wide = { 1L << 50, 3, 1L << 40, 1L << 20, 0 };
            int[] r3 = { 0, 1, 2, 3, 4 };
            RoomMap.RadixSortByKey(wide, r3, wide.Length, origin);
            Assert.Equal(new long[] { 0, 3, 1L << 20, 1L << 40, 1L << 50 }, wide);
            Assert.Equal(new[] { 4, 1, 3, 2, 0 }, r3);
        }

        /// <summary>
        /// `IsExternal` answers a cell in no room from a membership bitset rather than from the
        /// search over room keys, so the two must agree everywhere: over every cell of the search
        /// box — room cells, solid cells, open air and the padding — the bitset says *in a room*
        /// exactly when the search finds one (`D3`, two things that exist twice).
        /// </summary>
        [Fact]
        public void MembershipAgreesWithTheSearchOverEveryCellOfTheBox()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", 4000));
            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings());
            RoomMap map = simulation.Rooms.Map;
            Assert.True(map.RoomCount > 3, "the hull mapped only " + map.RoomCount + " rooms");

            Vector3I min = simulation.Grid.Min - new Vector3I(2, 2, 2);
            Vector3I max = simulation.Grid.Max + new Vector3I(2, 2, 2);

            int inRoom = 0, external = 0, solid = 0;
            for (int x = min.X; x <= max.X; x++)
            for (int y = min.Y; y <= max.Y; y++)
            for (int z = min.Z; z <= max.Z; z++)
            {
                Vector3I cell = new Vector3I(x, y, z);
                int room = map.RoomIndexOf(cell);

                // What IsExternal must say, derived from the search alone.
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
