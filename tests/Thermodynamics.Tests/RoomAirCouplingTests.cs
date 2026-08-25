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
        /// thermal-model.md an asymmetric choice.
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

        /// <summary>
        /// **A sliver of air is the stiffest thing on a grid, and refusing its substeps approximates
        /// rather than diverges.**
        ///
        /// <para>
        /// Room air is the coolant loop's twin: one lumped mass carrying a link to every surface
        /// bounding it, which is the shape the pairwise overshoot clamp cannot hold on its own — it
        /// bounds each wall against the air and lets six walls together take six times the energy
        /// that equalises the air between them. At 2 % pressure the capacity is a fiftieth and the
        /// coupling is the whole 30 kW/K, so this is the case that makes it visible.
        /// </para>
        ///
        /// <para>
        /// Written with backlog.md `A10`, which was the same defect on the
        /// coolant path and was found there first. The bound is the per-node relaxation applied to
        /// the coupled passes: with it a substep is a convex combination of the temperatures
        /// pulling on a node, and cannot leave the range they span.
        /// </para>
        /// </summary>
        [Fact]
        public void RefusingTheAirsDemandApproximatesRatherThanDiverging()
        {
            // The rig starts 300 K apart, and nothing in it makes heat: a bounded integrator can
            // only ever narrow that, whatever it is granted. The unclamped run is the control —
            // without it, a clamp that had quietly stopped working would read as a pass.
            float refused = AirSpreadAtCeiling(1, true);
            float unclamped = AirSpreadAtCeiling(1, false);

            Assert.True(refused <= 300f,
                "the hull started 300 K apart with nothing making heat and reached " + refused
                + " K apart, so a substep left the range the temperatures pulling on it span");

            Assert.True(unclamped > 300f,
                "the unclamped run stayed inside " + unclamped + " K, so this rig no longer"
                + " over-subscribes the air and the clamped figure above is proving nothing");
        }

        /// <summary>
        /// A hot shell around a thin, cold room, run for five simulated minutes at a chosen substep
        /// ceiling. The answer is how far the air ended from the walls.
        /// </summary>
        private static float AirSpreadAtCeiling(int ceiling, bool clamp)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.Frequency = 1;                  // a one second step, so one substep is one h
            settings.EnableEnvironment = false;
            settings.EnableDamage = false;
            settings.MaxSubsteps = ceiling;
            settings.MaxSubstepsPerBlock = 0;
            settings.MaxElementVisitsPerStep = 0;
            settings.ClampConductionOvershoot = clamp;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 600f);
            simulation.SetRoomPressure(Interior, 0.02f);

            // The walls hot and the air cold. Left alike there is nothing to exchange and the run
            // reports zero however it was integrated, which is a green test measuring nothing.
            RoomAirNode air = AirOf(simulation);
            air.Temperature = 300f;

            // The widest the hull ever got, rather than where it ended: a bounded run relaxes back
            // to one temperature, so the end of it is the same number for every ceiling.
            float widest = 0f;
            IList<ThermalNode> nodes = simulation.Solver.Nodes;

            for (int step = 0; step < 300; step++)
            {
                simulation.StepExact(1, Worlds.Shadow());

                float hottest = air.Temperature;
                float coldest = air.Temperature;

                for (int i = 0; i < nodes.Count; i++)
                {
                    if (nodes[i].Temperature > hottest) hottest = nodes[i].Temperature;
                    if (nodes[i].Temperature < coldest) coldest = nodes[i].Temperature;
                }

                if (hottest - coldest > widest) widest = hottest - coldest;
            }

            return widest;
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
