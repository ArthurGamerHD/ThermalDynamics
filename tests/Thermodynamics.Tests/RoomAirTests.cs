using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Room air. A sealed room holds a mass of air that every surface bounding it exchanges with,
    /// which is the only path heat has between two walls that do not touch.
    /// </summary>
    public class RoomAirTests
    {
        /// <summary>A 5x5x5 shell with a 3x3x3 cavity, and one hot block set into a wall.</summary>
        private static ThermalSimulation SealedBox(ThermalSettings settings, out Vector3I interior)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(5, 5, 5));

            interior = new Vector3I(2, 2, 2);
            return builder.BuildSimulation(settings, 293.15f);
        }

        [Fact]
        public void ASealedRoomHasAirAndAVentedOneDoesNot()
        {
            Vector3I interior;
            ThermalSimulation simulation = SealedBox(new ThermalSettings(), out interior);

            Assert.Equal(1, simulation.Rooms.Map.RoomCount);
            Assert.Single(simulation.RoomAir);
            Assert.Equal(27f * 2.5f * 2.5f * 2.5f, simulation.RoomAir[0].Volume, 2);
        }

        [Fact]
        public void AirIsInertUntilTheHostReportsPressure()
        {
            Vector3I interior;
            ThermalSimulation simulation = SealedBox(new ThermalSettings(), out interior);

            RoomAirNode air = simulation.GetRoomAir(interior);
            Assert.NotNull(air);
            Assert.Equal(0f, air.Pressure);
            Assert.False(air.HasAir);
            Assert.Empty(air.Links);
        }

        [Fact]
        public void PressurisingARoomLinksItToEverySurfaceAroundIt()
        {
            Vector3I interior;
            ThermalSimulation simulation = SealedBox(new ThermalSettings(), out interior);

            Assert.True(simulation.SetRoomPressure(interior, 1f));

            RoomAirNode air = simulation.GetRoomAir(interior);
            Assert.True(air.HasAir);

            // each of the cavity's six faces is a 3x3 patch of wall, and no block bounds it twice
            // from the same side
            Assert.Equal(54, air.Links.Count);
            Assert.True(air.AirMass > 0f);
        }

        [Fact]
        public void AHotWallWarmsTheAirAndTheAirWarmsTheFarWall()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableConduction = false;   // the only path left is through the air
            settings.Derive();

            Vector3I interior;
            ThermalSimulation simulation = SealedBox(settings, out interior);
            simulation.SetRoomPressure(interior, 1f);

            ThermalNode hot = simulation.Solver.GetNodeAt(new Vector3I(2, 0, 2));
            ThermalNode far = simulation.Solver.GetNodeAt(new Vector3I(2, 4, 2));
            hot.Temperature = 600f;

            float farBefore = far.Temperature;
            simulation.StepExact(400, Worlds.Shadow());

            Assert.True(simulation.GetRoomAir(interior).Temperature > 293.15f);
            Assert.True(far.Temperature > farBefore,
                "the far wall should have been warmed through the air, got " + far.Temperature);
            Assert.True(hot.Temperature < 600f);
        }

        [Fact]
        public void RoomAirConservesEnergy()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            Vector3I interior;
            ThermalSimulation simulation = SealedBox(settings, out interior);
            simulation.SetRoomPressure(interior, 1f);

            simulation.Solver.GetNodeAt(new Vector3I(2, 0, 2)).Temperature = 800f;

            float before = simulation.Solver.TotalEnergy;
            simulation.StepExact(200, Worlds.Shadow());
            float after = simulation.Solver.TotalEnergy;

            Assert.Equal(before, after, before * 0.0005f);
        }

        [Fact]
        public void TheSwitchRemovesRoomAirEntirely()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableRoomAir = false;
            settings.Derive();

            Vector3I interior;
            ThermalSimulation simulation = SealedBox(settings, out interior);

            Assert.Empty(simulation.RoomAir);
            Assert.False(simulation.SetRoomPressure(interior, 1f));
        }

        [Fact]
        public void SwitchingRoomAirOnMidSessionBuildsIt()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableRoomAir = false;
            settings.Derive();

            Vector3I interior;
            ThermalSimulation simulation = SealedBox(settings, out interior);
            Assert.Empty(simulation.RoomAir);

            settings.EnableRoomAir = true;
            settings.Derive();
            simulation.StepExact(1, Worlds.Shadow());

            Assert.Single(simulation.RoomAir);
        }

        [Fact]
        public void AirTemperatureSurvivesARebuildOfTheMap()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(5, 5, 5));
            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);

            Vector3I interior = new Vector3I(2, 2, 2);
            simulation.SetRoomPressure(interior, 1f);
            simulation.GetRoomAir(interior).Temperature = 400f;

            // a block welded somewhere else entirely still remakes the whole map
            BlockInstance addition = new BlockInstance(
                Catalog.LightArmor(), new Vector3I(6, 0, 0), BlockOrientation.Identity);
            simulation.AddBlock(addition);
            simulation.RebuildAll();

            RoomAirNode air = simulation.GetRoomAir(interior);
            Assert.NotNull(air);
            Assert.Equal(400f, air.Temperature, 2);
            Assert.Equal(1f, air.Pressure, 3);
        }

        /// <summary>
        /// The load path in full: a saved temperature has to survive being restored onto a room
        /// that is not yet pressurised, because pressure arrives from the vent sweep afterwards and
        /// filling a room for the first time is what takes its temperature from the walls.
        /// </summary>
        [Fact]
        public void AirTemperatureSurvivesASaveAndLoad()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            Vector3I interior;
            ThermalSimulation saved = SealedBox(settings, out interior);
            saved.SetRoomPressure(interior, 1f);
            saved.GetRoomAir(interior).Temperature = 400f;

            string data = saved.Save();

            // a fresh world load: the same ship, built again, before any vent has reported
            Vector3I reloadedInterior;
            ThermalSimulation reloaded = SealedBox(settings, out reloadedInterior);
            Assert.False(reloaded.GetRoomAir(reloadedInterior).Initialised);

            reloaded.Load(data);

            Assert.Equal(1, reloaded.RoomsRestored);
            Assert.Equal(400f, reloaded.GetRoomAir(reloadedInterior).Temperature, 2);

            // and the first vent report must not throw it away for the average of the walls
            reloaded.SetRoomPressure(reloadedInterior, 1f);
            Assert.Equal(400f, reloaded.GetRoomAir(reloadedInterior).Temperature, 2);
        }

        /// <summary>
        /// Air that was never filled holds a placeholder, not a measurement. Saving it would turn
        /// the guess into a remembered fact and stop the room taking its temperature from its walls
        /// the first time it is actually pressurised.
        /// </summary>
        [Fact]
        public void AirThatWasNeverFilledIsNotSaved()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            Vector3I interior;
            ThermalSimulation saved = SealedBox(settings, out interior);
            string data = saved.Save();

            Vector3I reloadedInterior;
            ThermalSimulation reloaded = SealedBox(settings, out reloadedInterior);
            reloaded.Load(data);

            Assert.Equal(0, reloaded.RoomsRestored);

            // heat the walls after the load, so taking their temperature is distinguishable from
            // having kept the placeholder the save would have carried
            reloaded.Solver.SetAllTemperatures(500f);
            reloaded.SetRoomPressure(reloadedInterior, 1f);
            Assert.Equal(500f, reloaded.GetRoomAir(reloadedInterior).Temperature, 2);
        }

        /// <summary>
        /// A room is matched by its anchor cell, so a compartment that was rebuilt into a different
        /// shape while the world was closed is a different room. It starts from its walls, exactly
        /// as it would have done had the change happened mid-session.
        /// </summary>
        [Fact]
        public void ARoomThatChangedShapeDoesNotTakeTheSavedTemperature()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            Vector3I interior;
            ThermalSimulation saved = SealedBox(settings, out interior);
            saved.SetRoomPressure(interior, 1f);
            saved.GetRoomAir(interior).Temperature = 400f;

            string data = saved.Save();

            // the same shell, but with the anchor corner of the cavity filled in
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(5, 5, 5));
            builder.Place(Catalog.LightArmor(), new Vector3I(1, 1, 1));

            ThermalSimulation reloaded = builder.BuildSimulation(settings, 293.15f);
            reloaded.Load(data);

            Assert.Equal(0, reloaded.RoomsRestored);

            reloaded.SetRoomPressure(interior, 1f);
            Assert.Equal(293.15f, reloaded.GetRoomAir(interior).Temperature, 2);
        }

        [Fact]
        public void OpeningADoorTakesTheAirAwayWithTheSeal()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(5, 5, 5));

            // replace one wall cell with a door
            Vector3I doorCell = new Vector3I(2, 0, 2);
            BlockInstance wall = builder.Grid.GetAtCell(doorCell);
            builder.Grid.Remove(wall);
            builder.Place(Catalog.AirtightDoor(), doorCell);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            Vector3I interior = new Vector3I(2, 2, 2);

            Assert.True(simulation.SetRoomPressure(interior, 1f));
            Assert.True(simulation.GetRoomAir(interior).HasAir);

            BlockInstance door = simulation.Grid.GetAtCell(doorCell);
            door.IsSealedByDoorState = false;
            simulation.RefreshBlockSealing(door);

            Assert.Null(simulation.GetRoomAir(interior));
            Assert.Empty(simulation.RoomAir);
        }
    }
}
