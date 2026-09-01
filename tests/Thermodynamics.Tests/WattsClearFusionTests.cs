using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The watts row is written by the environment pass rather than zeroed first, and the only thing
    /// that makes that safe is an ordering claim in three parts: the pass runs first, reaches
    /// **every** node including buried ones and a switched-off environment, and still does so when
    /// sliced across frames.
    ///
    /// <para>
    /// The assertion is **bit-identical** rather than close enough (`D8`), because a node whose watts
    /// were not reset integrates a doubled source term — a slow drift found in a save months later.
    /// A memset that is *nearly* redundant is a correctness defect at any price, whatever it saves.
    /// See benchmarks.md, The watts row is written.
    /// </para>
    /// </summary>
    public class WattsClearFusionTests
    {
        private static ThermalSimulation Build(bool fused)
        {
            ThermalSimulation simulation = Hulls.Driven();
            simulation.Solver.FuseWattsClear = fused;
            return simulation;
        }

        private static void AssertIdentical(ThermalSimulation a, ThermalSimulation b, string what)
        {
            SolverAb.AssertIdentical(
                SolverAb.Temperatures(a), SolverAb.Temperatures(b), what,
                "with the row cleared first", "with the environment pass writing it");
        }

        /// <summary>
        /// In flight, where every term the environment pass can write is live: solar, friction,
        /// wind-weighted convection, radiation and waste heat.
        /// </summary>
        [Fact]
        public void WritingTheWattsRowIsBitIdenticalInFlight()
        {
            ThermalSimulation cleared = Build(false);
            ThermalSimulation fused = Build(true);

            EnvironmentSample sample = Worlds.Ab.EveryTermLive();

            cleared.StepExact(40, sample);
            fused.StepExact(40, sample);

            AssertIdentical(cleared, fused, "flight");
        }

        [Fact]
        public void WritingTheWattsRowIsBitIdenticalInAnAtmosphere()
        {
            ThermalSimulation cleared = Build(false);
            ThermalSimulation fused = Build(true);

            EnvironmentSample sample = Worlds.Ab.MildAtmosphere();

            cleared.StepExact(40, sample);
            fused.StepExact(40, sample);

            AssertIdentical(cleared, fused, "atmosphere");
        }

        /// <summary>
        /// In vacuum in shadow, where solar and friction are both zero and radiation is the only
        /// environment term — the case where the row's contents are smallest and a stale watt
        /// would be proportionally largest.
        /// </summary>
        [Fact]
        public void WritingTheWattsRowIsBitIdenticalInVacuum()
        {
            ThermalSimulation cleared = Build(false);
            ThermalSimulation fused = Build(true);

            EnvironmentSample sample = Worlds.Ab.SunlitVacuum();

            cleared.StepExact(40, sample);
            fused.StepExact(40, sample);

            AssertIdentical(cleared, fused, "vacuum");
        }

        /// <summary>
        /// The generation-only path, which the pass takes when the environment is switched off.
        ///
        /// It is the one path with no per-node write of its own: with no heat being generated
        /// either, nothing in the pass touches the row, and the fused path has to zero the range
        /// itself. Without that branch this is the configuration that would carry watts forward
        /// from one substep to the next — and it is a configuration a server operator can reach,
        /// since it is what disabling the environment mechanisms produces.
        /// </summary>
        [Fact]
        public void WritingTheWattsRowIsBitIdenticalWithTheEnvironmentOff()
        {
            ThermalSettings settings = Hulls.Uncapped();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.Derive();

            ThermalSimulation cleared = Hulls.Driven(settings);
            ThermalSimulation fused = Hulls.Driven(settings);
            cleared.Solver.FuseWattsClear = false;
            fused.Solver.FuseWattsClear = true;

            EnvironmentSample sample = Worlds.Ab.SunlitVacuum();

            cleared.StepExact(40, sample);
            fused.StepExact(40, sample);

            AssertIdentical(cleared, fused, "environment off");
        }

        /// <summary>
        /// The same, with waste heat off as well, so the generation-only path has nothing at all
        /// to write and takes its clear-the-range branch on every substep. Conduction alone moves
        /// the hull, which is why the fixture is seeded with a spread rather than driven.
        /// </summary>
        [Fact]
        public void WritingTheWattsRowIsBitIdenticalWithNothingToWrite()
        {
            ThermalSettings settings = Hulls.Uncapped();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableWasteHeat = false;
            settings.Derive();

            ThermalSimulation cleared = Hulls.Driven(settings);
            ThermalSimulation fused = Hulls.Driven(settings);
            cleared.Solver.FuseWattsClear = false;
            fused.Solver.FuseWattsClear = true;

            EnvironmentSample sample = Worlds.Ab.SunlitVacuum();

            cleared.StepExact(40, sample);
            fused.StepExact(40, sample);

            AssertIdentical(cleared, fused, "nothing to write");
        }

        /// <summary>
        /// Spread across frames, which is the case the ordering claim is weakest in: the
        /// environment pass is sliced by a work budget, so "every node before anything reads the
        /// row" has to hold across a slice boundary as well as within one.
        ///
        /// The three budgets are a slice per node, a slice that lands mid-array on an odd stride,
        /// and one large enough to take the pass whole.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(97)]
        [InlineData(5000)]
        public void WritingTheWattsRowSurvivesTheStepBeingSpread(int budget)
        {
            ThermalSimulation whole = Build(false);
            ThermalSimulation spread = Build(true);

            EnvironmentState state = EnvironmentSolver.Solve(
                whole.Settings, whole.Planet,
                Worlds.Ab.MildAtmosphere());

            for (int step = 0; step < 8; step++)
            {
                whole.Solver.Step(whole.Settings.StepSeconds, state);

                spread.Solver.BeginStep(spread.Settings.StepSeconds, state);
                while (!spread.Solver.AdvanceStep(budget)) { }
            }

            AssertIdentical(whole, spread, "spread over " + budget);
        }

        /// <summary>
        /// The per-mechanism watt figures as well as the temperatures, since the fused path also
        /// changed where the exposed branch reads its source term from — it now reuses the value
        /// it already loaded rather than reading the row a second time.
        /// </summary>
        [Fact]
        public void TheDiagnosticsAgreeAsWell()
        {
            ThermalSimulation cleared = Build(false);
            ThermalSimulation fused = Build(true);

            cleared.Solver.CollectDiagnostics = true;
            fused.Solver.CollectDiagnostics = true;

            EnvironmentSample sample = Worlds.Ab.EveryTermLive();

            cleared.StepExact(40, sample);
            fused.StepExact(40, sample);

            SolverAb.AssertIdentical(
                SolverAb.Diagnostics(cleared), SolverAb.Diagnostics(fused), "diagnostics",
                "with the row cleared first", "with the environment pass writing it");
        }

        /// <summary>
        /// The whole-grid heat totals, which are accumulated in the same loop the change touched.
        /// A grid's vented and made watts are what the cockpit panel and the mod API report, so
        /// they are a published result rather than an internal one.
        /// </summary>
        [Fact]
        public void TheGridHeatTotalsAgree()
        {
            ThermalSimulation cleared = Build(false);
            ThermalSimulation fused = Build(true);

            EnvironmentSample sample = Worlds.Ab.EveryTermLive();

            cleared.StepExact(40, sample);
            fused.StepExact(40, sample);

            Assert.Equal(cleared.Solver.LastHeatGainWatts, fused.Solver.LastHeatGainWatts);
            Assert.Equal(cleared.Solver.LastEnvironmentWatts, fused.Solver.LastEnvironmentWatts);

            // A total of zero would let the assertion above pass on a grid that did nothing.
            Assert.True(fused.Solver.LastHeatGainWatts > 0f,
                "the driven hull generated no heat, so the totals prove nothing");
        }
    }
}
