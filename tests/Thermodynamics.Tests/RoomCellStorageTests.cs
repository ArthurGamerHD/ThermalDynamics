using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// A room's cells are a list, and the two things that makes safe.
    ///
    /// <para>
    /// The map used to hold each room as a `HashSet&lt;Vector3I&gt;` and pay about seventeen bytes
    /// a cell over a list for it. Nothing searched a room — the map answers "which room is this
    /// cell in" from `roomIndexByCell`, which answers for every room at once — so the set was
    /// buying deduplication and a lookup, and neither was wanted. On a 500,000-block hull that was
    /// 39 MB.
    /// </para>
    ///
    /// <para>
    /// A list carries no duplicate check, so the property the set was silently providing has to be
    /// asserted instead: the flood offers each cell exactly once, because every `AddToRoom` is
    /// behind a visited bitset that is tested and set in the same breath. These pin that, and the
    /// agreement between a room's own cells and the index that finds them, on hulls with real
    /// compartments rather than on a shape built to have one.
    /// </para>
    /// </summary>
    public class RoomCellStorageTests
    {
        private static ThermalSimulation CompartmentedHull(int blocks)
        {
            ThermalSettings settings = Hulls.Uncapped();
            ThermalSimulation simulation = Hulls.Driven(settings, blocks);

            Assert.True(simulation.Rooms.Map.RoomCount > 0,
                "the hull mapped no rooms, so nothing below is being checked");

            return simulation;
        }

        [Fact]
        public void NoRoomCarriesACellTwice()
        {
            RoomMap map = CompartmentedHull(4000).Rooms.Map;

            HashSet<Vector3I> seen = new HashSet<Vector3I>(Vector3I.Comparer);
            IList<List<Vector3I>> rooms = map.Rooms;

            for (int r = 0; r < rooms.Count; r++)
            {
                List<Vector3I> cells = rooms[r];
                for (int i = 0; i < cells.Count; i++)
                {
                    Assert.True(seen.Add(cells[i]),
                        "cell " + cells[i] + " appears more than once across the room cell lists,"
                        + " so a list is the wrong structure to hold them in");
                }
            }

            Assert.Equal(map.RoomCellCount, seen.Count);
        }

        /// <summary>
        /// Every cell a room lists resolves back to that room through the index, and the index
        /// names no cell the rooms do not list. The two halves are written by different code paths
        /// and are the only reason the lists never have to be searched.
        /// </summary>
        [Fact]
        public void TheIndexAndTheListsDescribeTheSameRooms()
        {
            RoomMap map = CompartmentedHull(4000).Rooms.Map;
            IList<List<Vector3I>> rooms = map.Rooms;

            int listed = 0;

            for (int r = 0; r < rooms.Count; r++)
            {
                List<Vector3I> cells = rooms[r];
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

        /// <summary>
        /// No room survives a completed pass with spare capacity, which is the other half of the
        /// saving: a list doubles as it grows, so an untrimmed room holds up to as much empty
        /// capacity as it does cells, for as long as the grid exists.
        /// </summary>
        [Fact]
        public void ACompletedPassLeavesNoSpareCapacityInARoom()
        {
            RoomMap map = CompartmentedHull(4000).Rooms.Map;
            IList<List<Vector3I>> rooms = map.Rooms;

            int trimmed = 0;

            for (int r = 0; r < rooms.Count; r++)
            {
                if (rooms[r].Count == 0) continue;

                Assert.Equal(rooms[r].Count, rooms[r].Capacity);
                trimmed++;
            }

            Assert.True(trimmed > 0, "no non-empty room was found, so nothing was checked");
        }

        /// <summary>
        /// The counts the memory benchmark attributes the room map's size to add up to the volume
        /// the pass walked. A row that reported half the cells would read as half the cost.
        /// </summary>
        [Fact]
        public void SolidRoomAndExternalCellsAccountForTheWholeSearchVolume()
        {
            ThermalSimulation simulation = CompartmentedHull(4000);
            RoomMap map = simulation.Rooms.Map;

            int classified = map.SolidCellCount + map.RoomCellCount + map.ExternalCellCount;

            Assert.Equal(RoomAuditor.SearchVolumeOf(simulation.Grid), classified);
        }
    }
}
