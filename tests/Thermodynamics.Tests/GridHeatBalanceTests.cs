using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// A grid's heat balance as two figures: what it is making and what it is shedding.
    ///
    /// Per-block temperatures cannot answer "is this ship able to cool itself at all", which is
    /// the first question a player asks when a ship overheats. These are accumulated on the hot
    /// path rather than behind the diagnostics switch, so they are available on a working ship
    /// rather than only on an instrumented one — which means the accounting has to be right in
    /// every path through the environment pass, including the ones that skip it.
    /// </summary>
    public class GridHeatBalanceTests
    {
        /// <summary>Waste heat the grid's blocks actually make, which is a fraction of throughput.</summary>
        private static float Generation(ThermalSimulation simulation)
        {
            float total = 0f;
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++) total += nodes[i].HeatGenerationWatts;
            return total;
        }

        private static ThermalSimulation Rig(
            float watts, out EnvironmentSample sample, bool solar = false, int blocks = 1)
        {
            ThermalSettings settings = new ThermalSettings
            {
                MaxSubsteps = 64,
                MaxElementVisitsPerStep = 0,
                EnableSolarHeat = solar,
            };
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(blocks, 1, 1));
            builder.Place(Catalog.Reactor(), new Vector3I(-1, 0, 0)).Producing(watts);

            ThermalSimulation simulation = builder.BuildSimulation(settings);
            sample = Worlds.Space(Vector3.Up);
            return simulation;
        }

        /// <summary>
        /// A grid making nothing sheds what it has, and the figure falls as it cools.
        ///
        /// It does not reach zero, and a test asserting that it does would be wrong rather than
        /// strict: radiation to a 2.7 K sky is a fourth power, so the tail is long — the same rig
        /// still vents 41 W at 89 K after twenty thousand steps. What is worth pinning is the
        /// direction and that nothing invents heat on a grid with no sources.
        /// </summary>
        [Fact]
        public void AGridMakingNoHeatShedsWhatItHasAndSlowsAsItCools()
        {
            EnvironmentSample sample;
            ThermalSimulation simulation = Rig(0f, out sample);

            // Everything starts at 293 K against a 2.7 K sky, so it sheds immediately.
            simulation.StepExact(1, sample);

            float first = simulation.VentedWatts;
            Assert.True(first > 0f);
            Assert.Equal(0f, simulation.HeatGainWatts, 3);

            for (int step = 0; step < 1000; step++) simulation.StepExact(1, sample);
            float later = simulation.VentedWatts;

            for (int step = 0; step < 3000; step++) simulation.StepExact(1, sample);
            float latest = simulation.VentedWatts;

            Assert.True(later < first);
            Assert.True(latest < later);
            Assert.True(latest > 0f);

            // And still nothing made, all the way down.
            Assert.Equal(0f, simulation.HeatGainWatts, 3);
        }

        /// <summary>
        /// The claim the readout exists to support: at equilibrium a ship vents exactly what it
        /// makes. If these two figures do not converge, the pair is worthless as a diagnosis.
        /// </summary>
        [Fact]
        public void AtEquilibriumVentingEqualsGeneration()
        {
            EnvironmentSample sample;
            ThermalSimulation simulation = Rig(50000f, out sample);

            for (int step = 0; step < 20000; step++) simulation.StepExact(1, sample);

            // Throughput is not heat: a reactor turns a fraction of what it delivers into waste,
            // so the figure to check against is what the blocks actually make.
            float made = Generation(simulation);
            Assert.True(made > 0f);

            Assert.Equal(made, simulation.HeatGainWatts, 0);
            Assert.True(simulation.VentedWatts > made * 0.98f);
            Assert.True(simulation.VentedWatts < made * 1.02f);
        }

        /// <summary>
        /// Generation is counted for buried blocks too, which take an entirely different path
        /// through the environment pass — the one that skips it. A reactor inside a hull is the
        /// normal case, so getting this wrong would under-report most real ships.
        /// </summary>
        [Fact]
        public void HeatMadeInsideAHullIsStillCounted()
        {
            ThermalSettings settings = new ThermalSettings
            {
                MaxSubsteps = 64,
                MaxElementVisitsPerStep = 0,
            };
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Producing(80000f);

            ThermalSimulation simulation = builder.BuildSimulation(settings);
            simulation.RebuildAll();

            simulation.StepExact(1, Worlds.Space(Vector3.Up));

            // The reactor has no exposed face at all, and its watts still appear. The shell's
            // blocks are lit, so the gain figure is the waste heat plus that sunlight — the claim
            // being pinned is that the buried block is in there at all.
            ThermalNode reactor = simulation.Solver.GetNode(simulation.Grid.GetAtCell(Vector3I.Zero));
            Assert.Equal(0, reactor.TotalExposedFaces);
            Assert.True(reactor.HeatGenerationWatts > 0f);
            Assert.True(simulation.HeatGainWatts >= reactor.HeatGenerationWatts);
        }

        /// <summary>
        /// And when the environment is switched off entirely, the fast path that only adds waste
        /// heat still accounts for it — the figure must not depend on which optimisation ran.
        /// </summary>
        [Fact]
        public void GenerationIsCountedWithTheEnvironmentSwitchedOff()
        {
            ThermalSettings settings = new ThermalSettings
            {
                MaxSubsteps = 64,
                MaxElementVisitsPerStep = 0,
                EnableEnvironment = false,

                // Solar is its own switch and would otherwise be counted here too; this test is
                // about the generation-only fast path, so nothing else is left on.
                EnableSolarHeat = false,
            };
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Producing(30000f);

            ThermalSimulation simulation = builder.BuildSimulation(settings);
            simulation.StepExact(1, Worlds.Space(Vector3.Up));

            Assert.Equal(Generation(simulation), simulation.HeatGainWatts, 0);
            Assert.Equal(0f, simulation.VentedWatts, 3);
        }

        /// <summary>
        /// Sunlight counts as heat made, because from a cooling point of view it is: a hull in
        /// sunlight has to shed it like any other watt.
        /// </summary>
        [Fact]
        public void SunlightCountsAsHeatGained()
        {
            EnvironmentSample sample;
            ThermalSimulation simulation = Rig(0f, out sample, true, 4);

            simulation.StepExact(1, sample);

            Assert.True(simulation.HeatGainWatts > 0f);
        }

        /// <summary>
        /// A cold grid in warm surroundings is absorbing, not venting. The signed figure says so
        /// and the venting one reads zero rather than going negative, because "vented" is a rate
        /// of loss and a negative loss is a confusing way to say gain.
        /// </summary>
        [Fact]
        public void AGridAbsorbingFromWarmAirVentsNothing()
        {
            ThermalSettings settings = new ThermalSettings
            {
                MaxSubsteps = 64,
                MaxElementVisitsPerStep = 0,
                EnableSolarHeat = false,
            };
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 200f);
            simulation.Planet = PlanetThermalProperties.Default();

            simulation.StepExact(1, Worlds.PlanetSurface(1f, 0.5f));

            Assert.True(simulation.EnvironmentWatts > 0f);
            Assert.Equal(0f, simulation.VentedWatts, 3);
        }
    }
}
