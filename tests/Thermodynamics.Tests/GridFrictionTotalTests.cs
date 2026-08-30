using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The grid's drag power, which is the one figure a drag force needs for its magnitude.**
    ///
    /// <para>
    /// What the solver computes per node is `FrictionScale x rho x v_rel^3 x area x windward
    /// exposure`, and real drag power is `1/2 C_d rho A v^3` — the same expression. The mod turns
    /// that power into heat in the hull and takes nothing from the ship's motion, so energy enters
    /// the world with nothing paying for it: measured over the 8,144 published blueprints at
    /// `reentry`, the median hull absorbs 5.05 MW and the largest 4.91 GW, on 100 % of them. At
    /// 300 m/s a median 5.05 MW is 16.8 kN of force never applied. See backlog.md `K1`.
    /// </para>
    ///
    /// <para>
    /// `K2` is the part that costs nothing and makes the rest measurable: sum it per grid and
    /// publish it. Until now the friction term existed per node, in the harness's per-scenario
    /// outcome, and per block type in the telemetry — everywhere except the one place a force would
    /// read it from.
    /// </para>
    /// </summary>
    public class GridFrictionTotalTests
    {
        private const float ThickAir = 1f;

        /// <summary>A hull in still thick air, with everything but friction switched off.</summary>
        private static ThermalSimulation Rig(bool diagnostics)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableDamage = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.Solver.CollectDiagnostics = diagnostics;
            simulation.Planet = PlanetThermalProperties.Default();
            return simulation;
        }

        /// <summary>
        /// **The total is the sum of the nodes**, which is the claim that makes it the drag power
        /// of the grid rather than a number of the right size.
        /// </summary>
        [Fact]
        public void TheGridTotalIsTheSumOfItsNodes()
        {
            ThermalSimulation simulation = Rig(true);
            simulation.StepExact(1, Worlds.Flight(ThickAir, 120f));

            float summed = 0f;
            for (int i = 0; i < simulation.Solver.Nodes.Count; i++)
            {
                summed += simulation.Solver.Nodes[i].LastFrictionWatts;
            }

            Assert.True(summed > 0f, "nothing was heated by friction, so this proves nothing");
            Assert.Equal(summed, simulation.Solver.LastFrictionWatts, 3);
        }

        /// <summary>
        /// **Summed whether or not diagnostics are on**, which is the whole reason it exists apart
        /// from the per-node figures.
        ///
        /// <para>
        /// `ThermalNode.LastFrictionWatts` is filled only under `CollectDiagnostics`, so a force
        /// derived from walking the nodes would be a force that existed while somebody was looking
        /// at a debug panel and not otherwise. That is the shape of defect this repository calls
        /// *wired to nothing*, arriving with the wire the other way round.
        /// </para>
        /// </summary>
        [Fact]
        public void TheTotalDoesNotDependOnDiagnosticsBeingOn()
        {
            ThermalSimulation watched = Rig(true);
            ThermalSimulation unwatched = Rig(false);

            watched.StepExact(1, Worlds.Flight(ThickAir, 120f));
            unwatched.StepExact(1, Worlds.Flight(ThickAir, 120f));

            Assert.True(watched.Solver.LastFrictionWatts > 0f);
            Assert.Equal(watched.Solver.LastFrictionWatts, unwatched.Solver.LastFrictionWatts, 3);

            // And the per-node figures really are absent without diagnostics, or the test above
            // would be comparing two of the same thing.
            Assert.Equal(0f, unwatched.Solver.Nodes[0].LastFrictionWatts);
        }

        /// <summary>
        /// **It is a rate from the last substep, not a sum that grows with the session**, which is
        /// what the per-node figures beside it are and what a force would need.
        /// </summary>
        [Fact]
        public void TheTotalIsARateRatherThanAnAccumulation()
        {
            ThermalSimulation simulation = Rig(false);

            simulation.StepExact(1, Worlds.Flight(ThickAir, 120f));
            float first = simulation.Solver.LastFrictionWatts;

            simulation.StepExact(1, Worlds.Flight(ThickAir, 120f));
            float second = simulation.Solver.LastFrictionWatts;

            Assert.True(first > 0f);
            Assert.Equal(first, second, 3);
        }

        /// <summary>
        /// **It holds across the substeps of one step.** The row it is read from is filled on the
        /// first substep and re-read by the rest, so a total that only the summing substep fills
        /// would read its true value once and nought twenty-three times — a stutter a force built
        /// on it would inherit and nobody could trace.
        /// </summary>
        [Fact]
        public void TheTotalSurvivesEverySubstepOfAStep()
        {
            ThermalSimulation simulation = Rig(false);
            simulation.StepExact(1, Worlds.Flight(ThickAir, 120f));
            float once = simulation.Solver.LastFrictionWatts;

            ThermalSimulation many = Rig(false);
            many.StepExact(8, Worlds.Flight(ThickAir, 120f));

            Assert.True(once > 0f);
            Assert.Equal(once, many.Solver.LastFrictionWatts, 3);
        }

        /// <summary>
        /// **Still air is no drag**, which is the case a force must not invent one in.
        /// </summary>
        [Fact]
        public void AGridThatIsNotMovingThroughAirHasNoDragPower()
        {
            ThermalSimulation simulation = Rig(false);
            simulation.StepExact(1, Worlds.Flight(ThickAir, 0f));

            Assert.Equal(0f, simulation.Solver.LastFrictionWatts);
        }

        /// <summary>
        /// **Drag power rises with speed far faster than linearly**, which is what makes it the
        /// cube law the drag expression is. Not asserted as an exponent — the windward exposure
        /// moves with the flow too — but as the direction and the order, which a linear term or a
        /// square one could not produce.
        /// </summary>
        [Fact]
        public void DragPowerRisesSteeplyWithSpeed()
        {
            ThermalSimulation slow = Rig(false);
            ThermalSimulation fast = Rig(false);

            slow.StepExact(1, Worlds.Flight(ThickAir, 60f));
            fast.StepExact(1, Worlds.Flight(ThickAir, 120f));

            float ratio = fast.Solver.LastFrictionWatts / Math.Max(1e-6f, slow.Solver.LastFrictionWatts);
            Assert.True(ratio > 4f,
                "doubling the speed multiplied the drag power by " + ratio + ", which is not a cube law");
        }

        /// <summary>
        /// **It is one term of the heat gain rather than a figure beside it**, so a caller adding
        /// the two counts it twice. Pinned because the API returns them separately and nothing else
        /// says how they relate.
        /// </summary>
        [Fact]
        public void TheFrictionTotalIsInsideTheHeatGain()
        {
            ThermalSimulation simulation = Rig(false);
            simulation.StepExact(1, Worlds.Flight(ThickAir, 120f));

            Assert.True(simulation.Solver.LastFrictionWatts > 0f);
            Assert.True(simulation.HeatGainWatts >= simulation.Solver.LastFrictionWatts,
                "the heat gain is smaller than the friction inside it, so they are not the same sum");
            Assert.Equal(simulation.Solver.LastFrictionWatts, simulation.FrictionWatts, 3);
        }
    }
}
