using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The room mapper reads a per-pass snapshot of the structural sealing instead of the surface
    /// map's dictionaries, and the map it publishes is held identical to the one the dictionaries
    /// produce — cell for cell, room for room, portal for portal (`D8`).
    ///
    /// <para>
    /// A map is not a temperature, so "close" has no meaning: one cell classified differently is a
    /// different room, a different exposure, a different ship. The comparison runs on a census
    /// hull with compartments, on a shell with a door in it — the case with a portal — and on the
    /// air the pressurised hull then holds, stepped both ways to the same temperatures. Every
    /// fixture asserts it found rooms, because two empty maps agree about nothing (`E8`).
    /// </para>
    /// </summary>
    [Trait("speed", "slow")]
    public class RoomMapSnapshotTests
    {
        private static ThermalSimulation Shell(bool snapshot)
        {
            GridBuilder builder = GridBuilder.Large();
            BlockModel armour = Catalog.LightArmor();
            BlockModel door = Catalog.SlideDoor();

            // A 6x6x6 shell around a 4x4x4 pocket, one door in a wall, a second sealed pocket beside it.
            for (int x = -1; x <= 4; x++)
            for (int y = -1; y <= 4; y++)
            for (int z = -1; z <= 4; z++)
            {
                bool wall = x == -1 || x == 4 || y == -1 || y == 4 || z == -1 || z == 4;
                if (!wall) continue;
                bool isDoor = x == 2 && y == 1 && z == -1;
                builder.Place(isDoor ? door : armour, new Vector3I(x, y, z), BlockOrientation.Identity);
            }
            for (int x = 6; x <= 8; x++)
            for (int y = 0; y <= 2; y++)
            for (int z = 0; z <= 2; z++)
            {
                if (x == 7 && y == 1 && z == 1) continue;
                builder.Place(armour, new Vector3I(x, y, z));
            }

            ThermalSimulation simulation = new ThermalSimulation(Hulls.Uncapped(), builder.Grid);
            simulation.Rooms.SnapshotSealing = snapshot;
            for (int i = 0; i < builder.Placed.Count; i++) simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            simulation.RebuildAll();
            return simulation;
        }

        private static ThermalSimulation Census(bool snapshot)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", 8000));
            ThermalSimulation simulation = new ThermalSimulation(Hulls.Uncapped(), builder.Grid);
            simulation.Rooms.SnapshotSealing = snapshot;
            for (int i = 0; i < builder.Placed.Count; i++) simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            simulation.RebuildAll();
            return simulation;
        }

        private static void AssertSameMap(RoomMap expected, RoomMap actual, string what)
        {
            Assert.True(expected.RoomCount > 0, what + ": the dictionary path found no rooms, so agreement proves nothing");
            Assert.Equal(expected.RoomCount, actual.RoomCount);
            Assert.Equal(expected.SolidCellCount, actual.SolidCellCount);
            Assert.Equal(expected.ExternalCellCount, actual.ExternalCellCount);
            Assert.Equal(expected.RoomCellCount, actual.RoomCellCount);

            for (int r = 0; r < expected.RoomCount; r++)
            {
                List<Vector3I> a = expected.Rooms[r];
                List<Vector3I> b = actual.Rooms[r];
                Assert.True(a.Count == b.Count, what + ": room " + r + " has " + b.Count + " cells by snapshot and " + a.Count + " by dictionary");
                for (int i = 0; i < a.Count; i++)
                {
                    Assert.True(a[i] == b[i], what + ": room " + r + " cell " + i + " is " + b[i] + " by snapshot and " + a[i] + " by dictionary");
                }
                Assert.Equal(expected.IsVented(r), actual.IsVented(r));
            }

            Assert.Equal(expected.Portals.Count, actual.Portals.Count);
            for (int p = 0; p < expected.Portals.Count; p++)
            {
                Assert.Equal(expected.Portals[p].Block.Model.Name, actual.Portals[p].Block.Model.Name);
                Assert.Equal(expected.Portals[p].Block.Min, actual.Portals[p].Block.Min);
                Assert.Equal(expected.Portals[p].Face, actual.Portals[p].Face);
                Assert.Equal(expected.Portals[p].RegionA, actual.Portals[p].RegionA);
                Assert.Equal(expected.Portals[p].RegionB, actual.Portals[p].RegionB);
            }
        }

        /// <summary>
        /// The claim the interior scan's index rests on, tested where it is made rather than through
        /// the map: walking a box x fastest, then y, then z, the index advances by exactly one at
        /// every step — including the two wraps, which is the case an off-by-one would survive on a
        /// box one row deep.
        /// </summary>
        [Theory]
        [InlineData(3, 4, 5)]
        [InlineData(1, 7, 2)]
        [InlineData(9, 1, 1)]
        public void WalkingABoxInScanOrderAdvancesTheIndexByOne(int sizeX, int sizeY, int sizeZ)
        {
            Vector3I min = new Vector3I(-3, 11, -7);
            Vector3I maxExclusive = min + new Vector3I(sizeX, sizeY, sizeZ);

            CellBitset box = new CellBitset();
            box.Reset(min, maxExclusive);

            long expected = 0;
            int wraps = 0;
            for (int z = min.Z; z < maxExclusive.Z; z++)
            for (int y = min.Y; y < maxExclusive.Y; y++)
            for (int x = min.X; x < maxExclusive.X; x++)
            {
                Vector3I cell = new Vector3I(x, y, z);
                Assert.True(expected == box.IndexOf(cell),
                    cell + " is index " + box.IndexOf(cell) + " and the scan is at " + expected);
                if (x == min.X && expected > 0) wraps++;
                expected++;
            }

            Assert.Equal((long)sizeX * sizeY * sizeZ, expected);
            Assert.True(sizeY * sizeZ == 1 || wraps > 0, "the box has no wrap in it, so the wrapping case is untested");
        }

        /// <summary>
        /// The skip has to have happened for the comparison above to be a comparison of the skip:
        /// the interior scan over a hull's box is mostly visited cells, and skipping them a word at a
        /// time must charge far fewer units than walking them one by one did. Counted on the same
        /// hull both ways (`E8`).
        /// </summary>
        [Fact]
        public void TheSnapshotScanChargesFarFewerUnitsThanTheCellByCellScan()
        {
            ThermalSimulation byDictionary = Census(false);
            ThermalSimulation bySnapshot = Census(true);

            long walked = byDictionary.Work.RoomCellsVisited;
            long skipped = bySnapshot.Work.RoomCellsVisited;

            // The counter covers the flood as well as the scan, and the flood charges the same
            // either way; what the skip can remove is the scan's own walk over the box, which is
            // one unit per cell of the bounding volume. Most of that must go.
            Vector3I extents = (byDictionary.Grid.Max - byDictionary.Grid.Min) + new Vector3I(3, 3, 3);
            long volume = (long)extents.X * extents.Y * extents.Z;

            Assert.True(walked > 0 && skipped > 0, "a pass charged nothing");
            Assert.True(walked - skipped > volume / 2,
                "the snapshot scan charged " + skipped + " units against " + walked
                + " cell by cell over a box of " + volume + " cells, so it skipped less than half the box");
        }

        /// <summary>
        /// The bitset's word skip, on the shapes an off-by-one would survive the map tests on: a
        /// clear bit at the start of a word, in the middle, at the last position, past a run of
        /// full words, and none before the end bound.
        /// </summary>
        [Fact]
        public void NextClearIndexFindsTheFirstClearBitAtOrAfterAnyOffset()
        {
            CellBitset bits = new CellBitset();
            Vector3I min = Vector3I.Zero;
            bits.Reset(min, new Vector3I(200, 1, 1));

            // Cells 0..149 set, except 37 and 129; 150..199 clear.
            for (int i = 0; i < 150; i++)
            {
                if (i == 37 || i == 129) continue;
                bits.AddIndex(i);
            }

            int examined;
            Assert.Equal(37L, bits.NextClearIndex(0, 200, out examined));
            Assert.Equal(1, examined);
            Assert.Equal(37L, bits.NextClearIndex(37, 200, out examined));
            Assert.Equal(129L, bits.NextClearIndex(38, 200, out examined));
            Assert.Equal(3, examined); // the rest of word 0, all of word 1, and the word holding 129
            Assert.Equal(150L, bits.NextClearIndex(130, 200, out examined));
            Assert.Equal(151L, bits.NextClearIndex(151, 200, out examined));
            Assert.Equal(150L, bits.NextClearIndex(150, 150, out examined)); // at the bound, the bound

            // Fill the tail: from past the last clear bit, the answer is the bound; from the
            // start it is still 37.
            for (int i = 150; i < 200; i++) bits.AddIndex(i);
            Assert.Equal(200L, bits.NextClearIndex(130, 200, out examined));
            Assert.Equal(37L, bits.NextClearIndex(0, 200, out examined));
        }

        [Fact]
        public void AShellWithADoorMapsToTheSameRoomsAndPortals()
        {
            ThermalSimulation byDictionary = Shell(false);
            ThermalSimulation bySnapshot = Shell(true);

            Assert.True(byDictionary.Rooms.Map.Portals.Count > 0, "the shell's door made no portal, so the portal half is untested");
            AssertSameMap(byDictionary.Rooms.Map, bySnapshot.Rooms.Map, "shell");
        }

        [Fact]
        public void ACensusHullMapsToTheSameCompartments()
        {
            ThermalSimulation byDictionary = Census(false);
            ThermalSimulation bySnapshot = Census(true);

            AssertSameMap(byDictionary.Rooms.Map, bySnapshot.Rooms.Map, "census hull");
        }

        [Fact]
        public void APressurisedHullStepsToTheSameTemperaturesEitherWay()
        {
            ThermalSimulation byDictionary = Census(false);
            ThermalSimulation bySnapshot = Census(true);

            int pressurised = 0;
            foreach (ThermalSimulation simulation in new[] { byDictionary, bySnapshot })
            {
                IList<List<Vector3I>> rooms = simulation.Rooms.Map.Rooms;
                for (int r = 0; r < rooms.Count; r++)
                {
                    if (simulation.SetRoomPressure(rooms[r][0], 1f)) pressurised++;
                }
                LoadBenchmarks.SeedSpread(simulation);
                Thermodynamics.Harness.Census.DriveCensus(simulation);
            }
            Assert.True(pressurised > 0, "no room took air, so the air half is untested");

            EnvironmentSample sample = Worlds.Ab.MildAtmosphere();
            byDictionary.StepExact(20, sample);
            bySnapshot.StepExact(20, sample);

            SolverAb.AssertIdentical(
                SolverAb.Temperatures(byDictionary), SolverAb.Temperatures(bySnapshot),
                "pressurised census hull", "by dictionary", "by snapshot");
        }
    }
}
