using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class UnmappedRoomTests
    {
/// <summary>LeakyShell operation.</summary>
        private static ThermalSimulation LeakyShell()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));

            BlockInstance solid = builder.Grid.GetAtCell(new Vector3I(0, 0, -1));
            builder.Grid.Remove(solid);
            builder.Place(Catalog.Grating(), new Vector3I(0, 0, -1));

            return builder.BuildSimulation(new ThermalSettings());
        }

/// <summary>SealedShell operation.</summary>
        private static ThermalSimulation SealedShell()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));
            return builder.BuildSimulation(new ThermalSettings());
        }

/// <summary>GameSeals operation.</summary>
        private static Func<Vector3I, bool> GameSeals(params Vector3I[] cells)
        {
/// <summary>HashSet operation.</summary>
            HashSet<Vector3I> sealedCells = new HashSet<Vector3I>(cells, Vector3I.Comparer);
/// <summary>delegate operation.</summary>
            return delegate (Vector3I cell) { return sealedCells.Contains(cell); };
        }

        [Fact]
/// <summary>TheCompartmentTheModelLostIsFound operation.</summary>
        public void TheCompartmentTheModelLostIsFound()
        {
/// <summary>LeakyShell operation.</summary>
            ThermalSimulation simulation = LeakyShell();
            Assert.Equal(0, simulation.Rooms.Map.RoomCount);

/// <summary>List operation.</summary>
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
/// <summary>AModelThatAgreesWithTheGameReportsNothing operation.</summary>
        public void AModelThatAgreesWithTheGameReportsNothing()
        {
/// <summary>SealedShell operation.</summary>
            ThermalSimulation simulation = SealedShell();
            Assert.Equal(1, simulation.Rooms.Map.RoomCount);

/// <summary>List operation.</summary>
            List<UnmappedRooms.Region> found = new List<UnmappedRooms.Region>();

            UnmappedRooms.Find(
/// <summary>GameSeals operation.</summary>
                simulation.Rooms.Map, simulation.Surfaces, GameSeals(Vector3I.Zero), found);

            Assert.Empty(found);
        }

        [Fact]
/// <summary>AGameThatSealsNothingReportsNothing operation.</summary>
        public void AGameThatSealsNothingReportsNothing()
        {
/// <summary>LeakyShell operation.</summary>
            ThermalSimulation simulation = LeakyShell();

/// <summary>List operation.</summary>
            List<UnmappedRooms.Region> found = new List<UnmappedRooms.Region>();
            UnmappedRooms.Find(
                simulation.Rooms.Map,
                simulation.Surfaces,
                delegate (Vector3I cell) { return false; },
                found);

            Assert.Empty(found);
        }

        [Fact]
/// <summary>TheLeakNamesTheFaceTheModelLeavesOpen operation.</summary>
        public void TheLeakNamesTheFaceTheModelLeavesOpen()
        {
/// <summary>LeakyShell operation.</summary>
            ThermalSimulation simulation = LeakyShell();

/// <summary>List operation.</summary>
            List<UnmappedRooms.Region> found = new List<UnmappedRooms.Region>();
            UnmappedRooms.Find(
/// <summary>GameSeals operation.</summary>
                simulation.Rooms.Map, simulation.Surfaces, GameSeals(Vector3I.Zero), found);

            Assert.Single(found);

            Assert.Single(found[0].Leaks);

            UnmappedRooms.Leak leak = found[0].Leaks[0];
            Assert.Equal(Vector3I.Zero, leak.Cell);
            Assert.Equal(new Vector3I(0, 0, -1), leak.Neighbour);
        }

        [Fact]
/// <summary>SeparateCompartmentsStaySeparateAndKeepTheirOrder operation.</summary>
        public void SeparateCompartmentsStaySeparateAndKeepTheirOrder()
        {
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

/// <summary>List operation.</summary>
            List<UnmappedRooms.Region> found = new List<UnmappedRooms.Region>();
            UnmappedRooms.Find(
                simulation.Rooms.Map,
                simulation.Surfaces,
                GameSeals(Vector3I.Zero, new Vector3I(6, 0, 0)),
                found);

            Assert.Equal(2, found.Count);

            Assert.Equal(Vector3I.Zero, found[0].Anchor);
            Assert.Equal(new Vector3I(6, 0, 0), found[1].Anchor);
            Assert.Equal(1, found[0].CellCount);
            Assert.Equal(1, found[1].CellCount);
        }

        [Fact]
/// <summary>AdjacentCellsAreOneCompartment operation.</summary>
        public void AdjacentCellsAreOneCompartment()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(3, 2, 2));

            BlockInstance solid = builder.Grid.GetAtCell(new Vector3I(0, 0, -1));
            builder.Grid.Remove(solid);
            builder.Place(Catalog.Grating(), new Vector3I(0, 0, -1));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings());

/// <summary>List operation.</summary>
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
/// <summary>TheCellLimitTruncatesRatherThanStalls operation.</summary>
        public void TheCellLimitTruncatesRatherThanStalls()
        {
/// <summary>LeakyShell operation.</summary>
            ThermalSimulation simulation = LeakyShell();

/// <summary>List operation.</summary>
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
/// <summary>AnUnmappedGridIsNotADisagreement operation.</summary>
        public void AnUnmappedGridIsNotADisagreement()
        {
/// <summary>List operation.</summary>
            List<UnmappedRooms.Region> found = new List<UnmappedRooms.Region>();

            bool complete = UnmappedRooms.Find(
                RoomMap.AllExternal,
/// <summary>SurfaceMap operation.</summary>
                new SurfaceMap(),
                delegate (Vector3I cell) { return true; },
                found);

            Assert.True(complete);
            Assert.Empty(found);
        }
    }
}
