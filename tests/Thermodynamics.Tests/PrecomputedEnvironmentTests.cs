using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The environment pass reuses per-node terms across the substeps of a step instead of
    /// recomputing them, and the only thing that makes that safe is that recomputing them would
    /// have produced the same bits.
    ///
    /// <para>
    /// Solar gain, friction and the convection coefficient are functions of exposure, area,
    /// emissivity and the sun and wind directions — none of which a substep changes, because a
    /// substep changes temperature and nothing else. The relaxation factor is
    /// <c>mass / (h * conductance)</c>, whose three inputs are fixed for the length of a step.
    /// So sixteen substeps were doing the same arithmetic sixteen times, including a float divide
    /// per node per substep.
    /// </para>
    ///
    /// <para>
    /// The assertion is therefore <b>bit-identical</b>, not "close enough". Anything less means
    /// something in that list does move between substeps, and a cache that is nearly right is
    /// worse than none: it would drift silently, on exactly the grids nobody benchmarks.
    /// </para>
    /// </summary>
    [Trait("speed", "slow")]
    public class PrecomputedEnvironmentTests
    {
        /// <summary>A hull with everything a substep touches, in a world that exercises all of it.</summary>
        private static ThermalSimulation Build(bool precompute)
        {
            ThermalSimulation simulation = Hulls.Driven();
            simulation.Solver.PrecomputeEnvironment = precompute;
            return simulation;
        }

        private static void AssertIdentical(ThermalSimulation a, ThermalSimulation b, string what)
        {
            SolverAb.AssertIdentical(
                SolverAb.Temperatures(a), SolverAb.Temperatures(b), what,
                "without the terms reused", "with them reused");
        }

        /// <summary>
        /// In sunlight and wind, where every one of the reused terms is non-zero.
        /// </summary>
        [Fact]
        public void ReusingThePerStepTermsIsBitIdenticalInAnAtmosphere()
        {
            ThermalSimulation recomputed = Build(false);
            ThermalSimulation reused = Build(true);

            EnvironmentSample sample = Worlds.Ab.MildAtmosphere();

            recomputed.StepExact(40, sample);
            reused.StepExact(40, sample);

            AssertIdentical(recomputed, reused, "atmosphere");
        }

        /// <summary>
        /// In flight, which is the only world where wind convection and aerodynamic friction are
        /// both live. They weight a node against the same six faces and used to ask for that sum
        /// separately; the suite ran at 22 m/s, below the 50 m/s friction threshold, so no
        /// bit-identity test had ever had both of them on at once.
        /// </summary>
        [Fact]
        public void ReusingThePerStepTermsIsBitIdenticalInFlight()
        {
            ThermalSimulation recomputed = Build(false);
            ThermalSimulation reused = Build(true);

            EnvironmentSample sample = Worlds.Ab.EveryTermLive();

            recomputed.StepExact(40, sample);
            reused.StepExact(40, sample);

            Assert.True(recomputed.Solver.Nodes[0].LastFrictionWatts >= 0f);
            AssertIdentical(recomputed, reused, "flight");
        }

        [Fact]
        public void ReusingThePerStepTermsIsBitIdenticalInVacuum()
        {
            ThermalSimulation recomputed = Build(false);
            ThermalSimulation reused = Build(true);

            EnvironmentSample sample = Worlds.Ab.SunlitVacuum();

            recomputed.StepExact(40, sample);
            reused.StepExact(40, sample);

            AssertIdentical(recomputed, reused, "vacuum");
        }

        /// <summary>
        /// The one input to the cached rows that moves under them: the self-shadow pass publishes
        /// new lit fractions on a budget of its own, and can do it part way through a step. The
        /// cache is invalidated exactly when that happens, and this is what says so — a hull large
        /// enough that the lit refresh takes several substeps to walk, stepped while it does.
        /// </summary>
        [Fact]
        public void TheCacheIsInvalidatedWhenTheSelfShadowPassPublishes()
        {
            ThermalSimulation recomputed = Build(false);
            ThermalSimulation reused = Build(true);

            recomputed.Solver.SunLitBudget = 64;
            reused.Solver.SunLitBudget = 64;
            recomputed.Solver.SunShadowBudget = 128;
            reused.Solver.SunShadowBudget = 128;

            // A sun that keeps moving, so the shadow map keeps restarting and the lit fractions
            // are never left alone for long.
            for (int i = 0; i < 30; i++)
            {
                float angle = i * 0.35f;
                Vector3 sun = new Vector3((float)Math.Cos(angle), (float)Math.Sin(angle), 0.2f);

                EnvironmentSample sample = Worlds.Space(sun);
                recomputed.StepExact(1, sample);
                reused.StepExact(1, sample);
            }

            AssertIdentical(recomputed, reused, "moving sun");
        }

        /// <summary>
        /// Spread across frames as well, because the rows are filled by a pass that is itself
        /// sliced — a row half filled by one frame must not be read by the next.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(97)]
        [InlineData(5000)]
        public void ReusingThePerStepTermsSurvivesTheStepBeingSpread(int budget)
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
    }
}
