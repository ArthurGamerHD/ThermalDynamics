using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Finding the compartments the game seals and this model does not.
    ///
    /// This exists because of a field report the mod could not answer. A player stood in a room
    /// with an air vent reading full, and the mod's room overlay drew nothing — no room, no air,
    /// no fault. The map had 12 rooms totalling 61 cells on a 1,293 block ship, with only 24 of
    /// 8,702 block faces bounding a room at all, and 1,298 block cells classified as outdoors.
    /// Every one of those numbers was in the report and none of them said the thing that mattered,
    /// which is that the game had rooms there and this model did not.
    ///
    /// The failure is silent by construction: pressurisation is only ever asked about rooms the
    /// map already found, so a compartment the fill walks into from outside is never compared
    /// against anything.
    /// </summary>
    public class UnmappedRoomTests
    {
        /// <summary>
        /// A sealed shell with one wall block swapped for a lattice — mounts everywhere, seals
        /// nothing. The fill walks in, the room disappears, and this is exactly the shape of the
        /// real failure: the game seals what this model does not.
        /// </summary>
        private static ThermalSimulation LeakyShell()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));

            BlockInstance solid = builder.Grid.GetAtCell(new Vector3I(0, 0, -1));
            builder.Grid.Remove(solid);
            builder.Place(Catalog.Grating(), new Vector3I(0, 0, -1));

            return builder.BuildSimulation(new ThermalSettings());
        }

        private static ThermalSimulation SealedShell()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));
            return builder.BuildSimulation(new ThermalSettings());
        }

        /// <summary>A stand-in for the game: these cells are airtight, whatever the map thinks.</summary>
        private static Func<Vector3I, bool> GameSeals(params Vector3I[] cells)
        {
            HashSet<Vector3I> sealedCells = new HashSet<Vector3I>(cells, Vector3I.Comparer);
            return delegate (Vector3I cell) { return sealedCells.Contains(cell); };
        }

        [Fact]
        public void TheCompartmentTheModelLostIsFound()
        {
            ThermalSimulation simulation = LeakyShell();
            Assert.Equal(0, simulation.Rooms.Map.RoomCount);

            List<UnmappedRooms.Region> found = new List<UnmappedRooms.Region>();
            bool complete = UnmappedRooms.Find(
                simulation.Rooms.Map,
                simulation.Surfaces,
                GameSeals(Vector3I.Zero),
                found);

            Assert.True(complete);
            Assert.Single(found);
            Assert.Equal(Vector3I.Zero, found[0].Anchor);
            Assert.Equal(1, found[0].CellCount);
        }

        [Fact]
        public void AModelThatAgreesWithTheGameReportsNothing()
        {
            ThermalSimulation simulation = SealedShell();
            Assert.Equal(1, simulation.Rooms.Map.RoomCount);

            List<UnmappedRooms.Region> found = new List<UnmappedRooms.Region>();

            // The interior is a room here, so it is not an external cell and is never offered.
            // Even a game that calls it airtight produces no disagreement, because there is none.
            UnmappedRooms.Find(
                simulation.Rooms.Map, simulation.Surfaces, GameSeals(Vector3I.Zero), found);

            Assert.Empty(found);
        }

        [Fact]
        public void AGameThatSealsNothingReportsNothing()
        {
            ThermalSimulation simulation = LeakyShell();

            List<UnmappedRooms.Region> found = new List<UnmappedRooms.Region>();
            UnmappedRooms.Find(
                simulation.Rooms.Map,
                simulation.Surfaces,
                delegate (Vector3I cell) { return false; },
                found);

            Assert.Empty(found);
        }

        [Fact]
        public void TheLeakNamesTheFaceTheModelLeavesOpen()
        {
            ThermalSimulation simulation = LeakyShell();

            List<UnmappedRooms.Region> found = new List<UnmappedRooms.Region>();
            UnmappedRooms.Find(
                simulation.Rooms.Map, simulation.Surfaces, GameSeals(Vector3I.Zero), found);

            Assert.Single(found);

            // Five of the six faces are solid armour and seal. The sixth is the lattice, and it is
            // the only face that should be reported — a diagnostic that named all six would be
            // telling the player to fix five blocks that are working.
            Assert.Single(found[0].Leaks);

            UnmappedRooms.Leak leak = found[0].Leaks[0];
            Assert.Equal(Vector3I.Zero, leak.Cell);
            Assert.Equal(new Vector3I(0, 0, -1), leak.Neighbour);
        }

        [Fact]
        public void SeparateCompartmentsStaySeparateAndKeepTheirOrder()
        {
            // Two leaky shells side by side, far enough apart not to touch.
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));
            builder.Shell(Catalog.LightArmor(), new Vector3I(5, -1, -1), new Vector3I(8, 2, 2));

            BlockInstance first = builder.Grid.GetAtCell(new Vector3I(0, 0, -1));
            builder.Grid.Remove(first);
            builder.Place(Catalog.Grating(), new Vector3I(0, 0, -1));

            BlockInstance second = builder.Grid.GetAtCell(new Vector3I(6, 0, -1));
            builder.Grid.Remove(second);
            builder.Place(Catalog.Grating(), new Vector3I(6, 0, -1));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings());

            List<UnmappedRooms.Region> found = new List<UnmappedRooms.Region>();
            UnmappedRooms.Find(
                simulation.Rooms.Map,
                simulation.Surfaces,
                GameSeals(Vector3I.Zero, new Vector3I(6, 0, 0)),
                found);

            Assert.Equal(2, found.Count);

            // Ordered by anchor, so a region keeps its index between two dumps of an unchanged
            // grid — which is the whole reason a player can name one and have it mean something.
            Assert.Equal(Vector3I.Zero, found[0].Anchor);
            Assert.Equal(new Vector3I(6, 0, 0), found[1].Anchor);
            Assert.Equal(1, found[0].CellCount);
            Assert.Equal(1, found[1].CellCount);
        }

        [Fact]
        public void AdjacentCellsAreOneCompartment()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(3, 2, 2));

            BlockInstance solid = builder.Grid.GetAtCell(new Vector3I(0, 0, -1));
            builder.Grid.Remove(solid);
            builder.Place(Catalog.Grating(), new Vector3I(0, 0, -1));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings());

            List<UnmappedRooms.Region> found = new List<UnmappedRooms.Region>();
            UnmappedRooms.Find(
                simulation.Rooms.Map,
                simulation.Surfaces,
                GameSeals(Vector3I.Zero, new Vector3I(1, 0, 0)),
                found);

            Assert.Single(found);
            Assert.Equal(2, found[0].CellCount);
            Assert.Equal(Vector3I.Zero, found[0].Anchor);
        }

        [Fact]
        public void TheCellLimitTruncatesRatherThanStalls()
        {
            ThermalSimulation simulation = LeakyShell();

            List<UnmappedRooms.Region> found = new List<UnmappedRooms.Region>();
            bool complete = UnmappedRooms.Find(
                simulation.Rooms.Map,
                simulation.Surfaces,
                GameSeals(Vector3I.Zero),
                found,
                1);

            Assert.False(complete);
        }

        [Fact]
        public void AnUnmappedGridIsNotADisagreement()
        {
            List<UnmappedRooms.Region> found = new List<UnmappedRooms.Region>();

            // Nothing has been classified, so every query answers "external" as a default rather
            // than as a measurement. Reporting the whole world as a lost room would be worse than
            // useless — see the same exclusion in the room audit.
            bool complete = UnmappedRooms.Find(
                RoomMap.AllExternal,
                new SurfaceMap(),
                delegate (Vector3I cell) { return true; },
                found);

            Assert.True(complete);
            Assert.Empty(found);
        }
    }
}
