using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class RoomAirTests
    {
/// <summary>SealedBox operation.</summary>
        private static ThermalSimulation SealedBox(ThermalSettings settings, out Vector3I interior)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(5, 5, 5));

/// <summary>Vector3I operation.</summary>
            interior = new Vector3I(2, 2, 2);
            return builder.BuildSimulation(settings, 293.15f);
        }

        [Fact]
/// <summary>ASealedRoomHasAirAndAVentedOneDoesNot operation.</summary>
        public void ASealedRoomHasAirAndAVentedOneDoesNot()
        {
            Vector3I interior;
/// <summary>SealedBox operation.</summary>
            ThermalSimulation simulation = SealedBox(new ThermalSettings(), out interior);

            Assert.Equal(1, simulation.Rooms.Map.RoomCount);
            Assert.Single(simulation.RoomAir);
            Assert.Equal(27f * 2.5f * 2.5f * 2.5f, simulation.RoomAir[0].Volume, 2);
        }

        [Fact]
/// <summary>AirIsInertUntilTheHostReportsPressure operation.</summary>
        public void AirIsInertUntilTheHostReportsPressure()
        {
            Vector3I interior;
/// <summary>SealedBox operation.</summary>
            ThermalSimulation simulation = SealedBox(new ThermalSettings(), out interior);

            RoomAirNode air = simulation.GetRoomAir(interior);
            Assert.NotNull(air);
            Assert.Equal(0f, air.Pressure);
            Assert.False(air.HasAir);
            Assert.Empty(air.Links);
        }

        [Fact]
/// <summary>PressurisingARoomLinksItToEverySurfaceAroundIt operation.</summary>
        public void PressurisingARoomLinksItToEverySurfaceAroundIt()
        {
            Vector3I interior;
/// <summary>SealedBox operation.</summary>
            ThermalSimulation simulation = SealedBox(new ThermalSettings(), out interior);

            Assert.True(simulation.SetRoomPressure(interior, 1f));

            RoomAirNode air = simulation.GetRoomAir(interior);
            Assert.True(air.HasAir);

            Assert.Equal(54, air.Links.Count);
            Assert.True(air.AirMass > 0f);
        }

        [Fact]
/// <summary>AHotWallWarmsTheAirAndTheAirWarmsTheFarWall operation.</summary>
        public void AHotWallWarmsTheAirAndTheAirWarmsTheFarWall()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableConduction = false;   // the only path left is through the air
            settings.Derive();

            Vector3I interior;
/// <summary>SealedBox operation.</summary>
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
/// <summary>RoomAirConservesEnergy operation.</summary>
        public void RoomAirConservesEnergy()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            Vector3I interior;
/// <summary>SealedBox operation.</summary>
            ThermalSimulation simulation = SealedBox(settings, out interior);
            simulation.SetRoomPressure(interior, 1f);

            simulation.Solver.GetNodeAt(new Vector3I(2, 0, 2)).Temperature = 800f;

            float before = simulation.Solver.TotalEnergy;
            simulation.StepExact(200, Worlds.Shadow());
            float after = simulation.Solver.TotalEnergy;

            Assert.Equal(before, after, before * 0.0005f);
        }

        [Fact]
/// <summary>TheSwitchRemovesRoomAirEntirely operation.</summary>
        public void TheSwitchRemovesRoomAirEntirely()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableRoomAir = false;
            settings.Derive();

            Vector3I interior;
/// <summary>SealedBox operation.</summary>
            ThermalSimulation simulation = SealedBox(settings, out interior);

            Assert.Empty(simulation.RoomAir);
            Assert.False(simulation.SetRoomPressure(interior, 1f));
        }

        [Fact]
/// <summary>SwitchingRoomAirOnMidSessionBuildsIt operation.</summary>
        public void SwitchingRoomAirOnMidSessionBuildsIt()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableRoomAir = false;
            settings.Derive();

            Vector3I interior;
/// <summary>SealedBox operation.</summary>
            ThermalSimulation simulation = SealedBox(settings, out interior);
            Assert.Empty(simulation.RoomAir);

            settings.EnableRoomAir = true;
            settings.Derive();
            simulation.StepExact(1, Worlds.Shadow());

            Assert.Single(simulation.RoomAir);
        }

        [Fact]
/// <summary>AirTemperatureSurvivesARebuildOfTheMap operation.</summary>
        public void AirTemperatureSurvivesARebuildOfTheMap()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(5, 5, 5));
            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);

/// <summary>Vector3I operation.</summary>
            Vector3I interior = new Vector3I(2, 2, 2);
            simulation.SetRoomPressure(interior, 1f);
            simulation.GetRoomAir(interior).Temperature = 400f;

/// <summary>BlockInstance operation.</summary>
            BlockInstance addition = new BlockInstance(
                Catalog.LightArmor(), new Vector3I(6, 0, 0), BlockOrientation.Identity);
            simulation.AddBlock(addition);
            simulation.RebuildAll();

            RoomAirNode air = simulation.GetRoomAir(interior);
            Assert.NotNull(air);
            Assert.Equal(400f, air.Temperature, 2);
            Assert.Equal(1f, air.Pressure, 3);
        }

        [Fact]
/// <summary>AirTemperatureSurvivesASaveAndLoad operation.</summary>
        public void AirTemperatureSurvivesASaveAndLoad()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            Vector3I interior;
/// <summary>SealedBox operation.</summary>
            ThermalSimulation saved = SealedBox(settings, out interior);
            saved.SetRoomPressure(interior, 1f);
            saved.GetRoomAir(interior).Temperature = 400f;

            string data = saved.Save();

            Vector3I reloadedInterior;
/// <summary>SealedBox operation.</summary>
            ThermalSimulation reloaded = SealedBox(settings, out reloadedInterior);
            Assert.False(reloaded.GetRoomAir(reloadedInterior).Initialised);

            reloaded.Load(data);

            Assert.Equal(1, reloaded.RoomsRestored);
            Assert.Equal(400f, reloaded.GetRoomAir(reloadedInterior).Temperature, 2);

            reloaded.SetRoomPressure(reloadedInterior, 1f);
            Assert.Equal(400f, reloaded.GetRoomAir(reloadedInterior).Temperature, 2);
        }

        [Fact]
/// <summary>AirThatWasNeverFilledIsNotSaved operation.</summary>
        public void AirThatWasNeverFilledIsNotSaved()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            Vector3I interior;
/// <summary>SealedBox operation.</summary>
            ThermalSimulation saved = SealedBox(settings, out interior);
            string data = saved.Save();

            Vector3I reloadedInterior;
/// <summary>SealedBox operation.</summary>
            ThermalSimulation reloaded = SealedBox(settings, out reloadedInterior);
            reloaded.Load(data);

            Assert.Equal(0, reloaded.RoomsRestored);

            reloaded.Solver.SetAllTemperatures(500f);
            reloaded.SetRoomPressure(reloadedInterior, 1f);
            Assert.Equal(500f, reloaded.GetRoomAir(reloadedInterior).Temperature, 2);
        }

        [Fact]
/// <summary>ARoomThatChangedShapeDoesNotTakeTheSavedTemperature operation.</summary>
        public void ARoomThatChangedShapeDoesNotTakeTheSavedTemperature()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            Vector3I interior;
/// <summary>SealedBox operation.</summary>
            ThermalSimulation saved = SealedBox(settings, out interior);
            saved.SetRoomPressure(interior, 1f);
            saved.GetRoomAir(interior).Temperature = 400f;

            string data = saved.Save();

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
/// <summary>OpeningADoorTakesTheAirAwayWithTheSeal operation.</summary>
        public void OpeningADoorTakesTheAirAwayWithTheSeal()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(5, 5, 5));

/// <summary>Vector3I operation.</summary>
            Vector3I doorCell = new Vector3I(2, 0, 2);
            BlockInstance wall = builder.Grid.GetAtCell(doorCell);
            builder.Grid.Remove(wall);
            builder.Place(Catalog.AirtightDoor(), doorCell);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
/// <summary>Vector3I operation.</summary>
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
