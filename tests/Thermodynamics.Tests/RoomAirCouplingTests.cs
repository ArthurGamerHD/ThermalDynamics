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

        /// <summary>
        /// **The coupling carries no pressure term, so room air is a binary input above zero.**
        ///
        /// <para>
        /// A link's conductance is `RoomConvectionCoefficient × faces × cellFaceArea` and nothing
        /// else — a compartment at a fiftieth of an atmosphere couples its walls exactly as hard as
        /// a full one. What pressure does move is the air's heat capacity, and by the same argument
        /// that settles `mass error` in the client sweep, capacity does not appear in the balance a
        /// hull settles at, only in how long it takes to get there.
        /// </para>
        ///
        /// <para>
        /// This is the mechanism behind `F21`'s finding: a client's disagreement about how full a
        /// room is is worth almost nothing until it crosses zero, and then it is worth the whole
        /// 30 kW/K of coupling. Pinned here rather than only in the lab, because it is a property of
        /// the model and it is what makes the veto chain in
        /// [thermal-model.md](../../docs/thermal-model.md) an asymmetric choice.
        /// </para>
        /// </summary>
        [Fact]
        public void ConductanceToARoomsAirDoesNotDependOnHowFullTheRoomIs()
        {
            ThermalSimulation simulation = Shell(300f);

            simulation.SetRoomPressure(Interior, 1f);
            RoomAirNode air = AirOf(simulation);

            float full = 0f;
            foreach (RoomLink link in air.Links) full += link.Conductance;
            float fullMass = air.ThermalMass;

            simulation.SetRoomPressure(Interior, 0.02f);

            float sliver = 0f;
            foreach (RoomLink link in air.Links) sliver += link.Conductance;

            Assert.True(full > 0f, "a full room has no coupling at all, so this judges nothing");
            Assert.Equal(full, sliver, 4);

            // Capacity is the half that does move, or the claim above would be that pressure
            // reaches nothing.
            Assert.True(air.ThermalMass < fullMass * 0.1f,
                "a room at 2 % pressure holds " + air.ThermalMass + " J/K against a full room's "
                + fullMass + " J/K, so pressure is not reaching the capacity either");

            // And zero is the discontinuity: the whole coupling, not a smaller one.
            simulation.SetRoomPressure(Interior, 0f);
            Assert.Empty(AirOf(simulation).Links);
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
