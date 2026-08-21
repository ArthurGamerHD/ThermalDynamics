using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The properties the game's own pressurisation path relied on and did not have.
    ///
    /// Room air was sound everywhere it was tested and dead everywhere it ran. Every test and the
    /// mod API set pressure through <see cref="ThermalSimulation.SetRoomPressure"/>, which rebuilds
    /// the room's links and seeds new air from the walls holding it. The game's own sweep assigned
    /// the field directly and refreshed the mass by hand, so in a live world a pressurised room got
    /// its air mass, no links at all, and whatever temperature the last rebuild left behind — 2.7 K
    /// for a ship in vacuum. Measured: a 30-cell cabin with two vents on it, the game reporting it
    /// sealed and 99% full, running at zero pressure.
    ///
    /// These pin the two properties that made the difference, so a caller that bypasses the solver
    /// again fails here rather than in somebody's world.
    /// </summary>
    public class RoomAirCouplingTests
    {
        private static readonly Vector3I Interior = Vector3I.Zero;

        private static ThermalSimulation Shell(float blockTemperature)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));
            return builder.BuildSimulation(new ThermalSettings(), blockTemperature);
        }

        private static RoomAirNode AirOf(ThermalSimulation simulation)
        {
            IList<RoomAirNode> air = simulation.Solver.RoomAir;
            return air.Count == 0 ? null : air[0];
        }

        [Fact]
        public void AirGivenToARoomIsCoupledToTheWalls()
        {
            ThermalSimulation simulation = Shell(300f);

            RoomAirNode air = AirOf(simulation);
            Assert.NotNull(air);

            // At zero pressure a room has no air and no links, which is the point of it costing
            // nothing until something says otherwise.
            Assert.False(air.HasAir);
            Assert.Empty(air.Links);

            simulation.SetRoomPressure(Interior, 1f);

            Assert.True(air.HasAir);
            Assert.True(air.AirMass > 0f);

            // A room with mass and no links is air bolted to nothing: it holds heat that never
            // moves, and nothing about the grid changes because of it.
            Assert.NotEmpty(air.Links);
        }

        [Fact]
        public void AirAppearingForTheFirstTimeTakesTheTemperatureOfTheWalls()
        {
            // In vacuum, where ambient is 2.7 K and the walls are not. Seeding from ambient rather
            // than from the walls put 150 kg of near-absolute-zero gas inside a warm ship.
            ThermalSimulation simulation = Shell(300f);
            simulation.Update(1f, Worlds.Shadow());

            simulation.SetRoomPressure(Interior, 1f);

            RoomAirNode air = AirOf(simulation);
            Assert.NotNull(air);

            Assert.Equal(300f, air.Temperature, 0);
            Assert.True(air.Temperature > 100f,
                "air seeded from ambient rather than from the walls: " + air.Temperature + " K");
        }

        [Fact]
        public void APressurisedRoomActuallyMovesHeat()
        {
            // The end-to-end property. Air that is linked and warm exchanges with the hull; air
            // that is linked to nothing cannot, however much of it there is.
            ThermalSimulation simulation = Shell(300f);
            simulation.SetRoomPressure(Interior, 1f);

            RoomAirNode air = AirOf(simulation);
            Assert.NotNull(air);

            air.Temperature = 200f;
            float before = air.Temperature;

            for (int i = 0; i < 200; i++) simulation.Update(0.25f, Worlds.Shadow());

            Assert.True(air.Temperature > before + 1f,
                "cold air beside warm walls did not warm: " + air.Temperature + " K");
        }

        [Fact]
        public void DroppingPressureTakesTheLinksAwayAgain()
        {
            ThermalSimulation simulation = Shell(300f);
            simulation.SetRoomPressure(Interior, 1f);

            Assert.NotEmpty(AirOf(simulation).Links);

            simulation.SetRoomPressure(Interior, 0f);

            RoomAirNode air = AirOf(simulation);
            Assert.False(air.HasAir);
            Assert.Empty(air.Links);
        }
    }
}
