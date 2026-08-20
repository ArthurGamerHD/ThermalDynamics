using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The per-mechanism watt figures are written on the last substep of a step and on no other.
    ///
    /// <para>
    /// Each substep overwrites what the one before it wrote, and every consumer — the telemetry
    /// report, the crosshair readout, the debug overlay, the mod API — reads between steps. So
    /// only the last substep's writes were ever observed, and the earlier ones were five stores
    /// into a node object per node and two into another per link, discarded by the next substep.
    /// A step at nineteen substeps did that eighteen times for nothing, and switching diagnostics
    /// on nearly doubled the step: 3.93 ms to 7.51 ms on a 32,800-block hull.
    /// </para>
    ///
    /// <para>
    /// The assertion is bit-identical against writing on every substep, because "the last substep's
    /// figures" has to mean exactly that. Anything less means the batching changed which substep is
    /// being reported, which would make every watt figure in a telemetry dump describe a different
    /// moment than the one it claims.
    /// </para>
    /// </summary>
    public class DiagnosticBatchingTests
    {
        private static ThermalSimulation Build(bool everySubstep, int maxSubsteps)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubsteps = maxSubsteps;
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", 2000));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.RebuildAll();
            simulation.Solver.CollectDiagnostics = true;
            simulation.Solver.DiagnosticsOnEverySubstep = everySubstep;

            Census.DriveCensus(simulation);
            LoadBenchmarks.SeedSpread(simulation);
            return simulation;
        }

        private static float[] Published(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            float[] values = new float[nodes.Count * 6];

            for (int i = 0; i < nodes.Count; i++)
            {
                ThermalNode node = nodes[i];
                int b = i * 6;
                values[b] = node.LastRadiationWatts;
                values[b + 1] = node.LastConvectionWatts;
                values[b + 2] = node.LastSolarWatts;
                values[b + 3] = node.LastFrictionWatts;
                values[b + 4] = node.LastHeatSourceWatts;
                values[b + 5] = node.LastConductionWatts;
            }

            return values;
        }

        private static readonly string[] Mechanisms =
        {
            "radiation", "convection", "solar", "friction", "heat source", "conduction",
        };

        private static void AssertIdentical(ThermalSimulation every, ThermalSimulation last, string what)
        {
            float[] expected = Published(every);
            float[] actual = Published(last);

            Assert.Equal(expected.Length, actual.Length);

            bool anythingAtAll = false;
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != 0f) anythingAtAll = true;

                Assert.True(expected[i].Equals(actual[i]),
                    what + ": block " + (i / 6) + " " + Mechanisms[i % 6] + " is "
                    + actual[i].ToString("r") + " written on the last substep and "
                    + expected[i].ToString("r") + " written on every substep");
            }

            // Two grids of zeros agree perfectly and prove nothing.
            Assert.True(anythingAtAll, what + ": every published figure is zero");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(4096)]
        public void OnlyTheLastSubstepIsPublishedAndItIsTheSameOne(int maxSubsteps)
        {
            ThermalSimulation every = Build(everySubstep: true, maxSubsteps: maxSubsteps);
            ThermalSimulation last = Build(everySubstep: false, maxSubsteps: maxSubsteps);

            EnvironmentSample sample = Worlds.PlanetSurface(0.8f, timeOfDay: 0.35f, windSpeed: 22f);

            every.StepExact(20, sample);
            last.StepExact(20, sample);

            AssertIdentical(every, last, "atmosphere at " + maxSubsteps);
        }

        /// <summary>
        /// In vacuum, where convection is off and solar is the term that moves — a different set of
        /// the six is non-zero, so a batching mistake confined to one mechanism still shows.
        /// </summary>
        [Fact]
        public void TheSameHoldsInVacuum()
        {
            ThermalSimulation every = Build(everySubstep: true, maxSubsteps: 4096);
            ThermalSimulation last = Build(everySubstep: false, maxSubsteps: 4096);

            EnvironmentSample sample = Worlds.Space(new Vector3(0.3f, 0.9f, 0.2f));

            every.StepExact(20, sample);
            last.StepExact(20, sample);

            AssertIdentical(every, last, "vacuum");
        }

        /// <summary>
        /// Spread across frames as well. The flag is set when a substep begins and read by every
        /// slice of it, so a slice boundary must not be able to see a different answer.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(97)]
        [InlineData(5000)]
        public void BatchingSurvivesTheStepBeingSpread(int budget)
        {
            ThermalSimulation whole = Build(everySubstep: true, maxSubsteps: 4096);
            ThermalSimulation spread = Build(everySubstep: false, maxSubsteps: 4096);

            EnvironmentState state = EnvironmentSolver.Solve(
                whole.Settings, whole.Planet,
                Worlds.PlanetSurface(0.8f, timeOfDay: 0.35f, windSpeed: 22f));

            for (int step = 0; step < 8; step++)
            {
                whole.Solver.Step(whole.Settings.StepSeconds, state);

                spread.Solver.BeginStep(spread.Settings.StepSeconds, state);
                while (!spread.Solver.AdvanceStep(budget)) { }
            }

            AssertIdentical(whole, spread, "spread over " + budget);
        }

        /// <summary>
        /// Switching diagnostics off still switches them off. The batching decides which substep
        /// writes, not whether any does.
        /// </summary>
        [Fact]
        public void NothingIsPublishedWhenDiagnosticsAreOff()
        {
            ThermalSimulation simulation = Build(everySubstep: false, maxSubsteps: 4096);
            simulation.Solver.CollectDiagnostics = false;

            simulation.StepExact(20, Worlds.PlanetSurface(0.8f, timeOfDay: 0.35f, windSpeed: 22f));

            float[] published = Published(simulation);
            for (int i = 0; i < published.Length; i++)
            {
                Assert.Equal(0f, published[i]);
            }
        }
    }
}
