using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The watts row is written by the environment pass rather than zeroed first and added into,
    /// and the only thing that makes that safe is an ordering claim: within a substep, the
    /// environment pass reaches every node before anything else reads or writes the row.
    ///
    /// <para>
    /// The claim has three parts, and each is a way the change could be wrong. The environment
    /// pass must run first — conduction, the coupled stage and apply all follow it in the stage
    /// machine. It must reach <em>every</em> node, including buried ones and including the case
    /// where the whole environment is switched off and there is nothing to write. And it must
    /// still reach every node when the pass is sliced across frames, since a node the budget did
    /// not reach this frame would otherwise carry the previous substep's watts into this one.
    /// </para>
    ///
    /// <para>
    /// So the assertion is <b>bit-identical</b> rather than "close enough". A node whose watts
    /// were not reset would integrate a doubled source term, which on a settled hull is a slow
    /// drift rather than an obvious break — the failure this suite exists to catch is the one
    /// that would otherwise be found in a save game months later.
    /// </para>
    ///
    /// <para>
    /// What it is worth is measured separately, by <c>bench wattsclear</c>: 0.2–0.6 % of a step,
    /// growing with grid size. That is small, and it is not why the assertion matters — a
    /// redundant memset that is <em>nearly</em> redundant is a correctness defect at any price.
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

            EnvironmentSample sample = Worlds.PlanetSurface(1f, timeOfDay: 0.35f, windSpeed: 300f);

            cleared.StepExact(40, sample);
            fused.StepExact(40, sample);

            AssertIdentical(cleared, fused, "flight");
        }

        [Fact]
        public void WritingTheWattsRowIsBitIdenticalInAnAtmosphere()
        {
            ThermalSimulation cleared = Build(false);
            ThermalSimulation fused = Build(true);

            EnvironmentSample sample = Worlds.PlanetSurface(0.8f, timeOfDay: 0.35f, windSpeed: 22f);

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

            EnvironmentSample sample = Worlds.Space(new Vector3(0.3f, 0.9f, 0.2f));

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

            EnvironmentSample sample = Worlds.Space(new Vector3(0.3f, 0.9f, 0.2f));

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

            EnvironmentSample sample = Worlds.Space(new Vector3(0.3f, 0.9f, 0.2f));

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

            EnvironmentSample sample = Worlds.PlanetSurface(1f, timeOfDay: 0.35f, windSpeed: 300f);

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

            EnvironmentSample sample = Worlds.PlanetSurface(1f, timeOfDay: 0.35f, windSpeed: 300f);

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
