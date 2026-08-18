using Thermodynamics.Core;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Whether a room holds air, decided from the game's answers rather than this model's.
    ///
    /// Every one of those answers can veto air and none can insist on it, because the two mistakes
    /// are not equal: air is heat capacity, so a room wrongly given it warms and cools like a room
    /// with a tonne of gas in it and drags every surface around it along, while a room wrongly
    /// denied it only loses a little inertia.
    /// </summary>
    public class RoomPressureTests
    {
        [Fact]
        public void AWorldWithoutPressurisationHasNoAirAnywhere()
        {
            // The setting the player turned off is the whole answer: a sealed room with a vent
            // reporting a full tank still holds nothing.
            Assert.Equal(0f, RoomPressure.Level(false, true, 1f), 5);
        }

        [Fact]
        public void ARoomTheGameDoesNotCallSealedHoldsNothing()
        {
            // The game knows the real shape of a sloped block where this model knows a cell, so
            // where they disagree the game wins.
            Assert.Equal(0f, RoomPressure.Level(true, false, 1f), 5);
        }

        [Fact]
        public void ASealedRoomHoldsWhatItsVentReports()
        {
            Assert.Equal(1f, RoomPressure.Level(true, true, 1f), 5);
            Assert.Equal(0.4f, RoomPressure.Level(true, true, 0.4f), 5);
        }

        [Fact]
        public void NothingReportingIsNotTheSameAsReportingEmptyButGivesTheSameAnswer()
        {
            // A room with no vent cannot be measured, and an unmeasured room is treated as empty
            // rather than guessed at. Distinct in the input so the caller can tell the two apart.
            Assert.Equal(0f, RoomPressure.Level(true, true, RoomPressure.NotReported), 5);
            Assert.Equal(0f, RoomPressure.Level(true, true, 0f), 5);
            Assert.True(RoomPressure.NotReported < 0f);
        }

        [Fact]
        public void AnImpossibleReadingIsClampedRatherThanTrusted()
        {
            Assert.Equal(1f, RoomPressure.Level(true, true, 4f), 5);
            Assert.Equal(0f, RoomPressure.Level(true, true, -3f), 5);
        }
    }

    /// <summary>
    /// What pressure does to the air itself, once decided.
    /// </summary>
    public class RoomAirPressureTests
    {
        private static RoomAirNode Room(float pressure)
        {
            RoomAirNode air = new RoomAirNode();
            air.RoomIndex = 0;
            air.CellCount = 8;
            air.Volume = 8f * 2.5f * 2.5f * 2.5f;
            air.Temperature = 293.15f;
            air.Pressure = pressure;
            air.AirDensity = 1.225f;
            air.RefreshThermalMass();
            return air;
        }

        [Fact]
        public void AnEmptyRoomHasNoAirAndOnlyTheSolverFloorOfHeatCapacity()
        {
            RoomAirNode air = Room(0f);

            Assert.False(air.HasAir);
            Assert.Equal(0f, air.AirMass, 5);

            // Not zero: heat capacity is a divisor in the step, so it has a floor. The room still
            // exchanges nothing, because HasAir is what gates that.
            Assert.Equal(ThermalConstants.MinimumThermalMass, air.ThermalMass, 6);
        }

        [Fact]
        public void HalfPressureIsHalfTheAirAndHalfTheHeatCapacity()
        {
            RoomAirNode full = Room(1f);
            RoomAirNode half = Room(0.5f);

            Assert.True(half.HasAir);
            Assert.Equal(full.AirMass * 0.5f, half.AirMass, 3);
            Assert.Equal(full.ThermalMass * 0.5f, half.ThermalMass, 3);
        }
    }
}
