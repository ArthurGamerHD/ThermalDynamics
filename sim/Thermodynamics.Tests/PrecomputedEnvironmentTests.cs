using System;
using System.Collections.Generic;
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
    public class PrecomputedEnvironmentTests
    {
        /// <summary>A hull with everything a substep touches, in a world that exercises all of it.</summary>
        private static ThermalSimulation Build(bool precompute)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubsteps = 4096;
            settings.MaxLinkVisitsPerStep = 0;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", 2000));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.RebuildAll();
            simulation.Solver.PrecomputeEnvironment = precompute;

            Census.DriveCensus(simulation);
            LoadBenchmarks.SeedSpread(simulation);
            return simulation;
        }

        private static float[] Temperatures(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            float[] values = new float[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) values[i] = nodes[i].Temperature;
            return values;
        }

        private static void AssertIdentical(ThermalSimulation a, ThermalSimulation b, string what)
        {
            float[] expected = Temperatures(a);
            float[] actual = Temperatures(b);

            Assert.Equal(expected.Length, actual.Length);

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.True(expected[i].Equals(actual[i]),
                    what + ": block " + i + " is " + actual[i].ToString("r")
                    + " with the terms reused and " + expected[i].ToString("r") + " without");
            }
        }

        /// <summary>
        /// In sunlight and wind, where every one of the reused terms is non-zero.
        /// </summary>
        [Fact]
        public void ReusingThePerStepTermsIsBitIdenticalInAnAtmosphere()
        {
            ThermalSimulation recomputed = Build(false);
            ThermalSimulation reused = Build(true);

            EnvironmentSample sample = Worlds.PlanetSurface(0.8f, timeOfDay: 0.35f, windSpeed: 22f);

            recomputed.StepExact(40, sample);
            reused.StepExact(40, sample);

            AssertIdentical(recomputed, reused, "atmosphere");
        }

        [Fact]
        public void ReusingThePerStepTermsIsBitIdenticalInVacuum()
        {
            ThermalSimulation recomputed = Build(false);
            ThermalSimulation reused = Build(true);

            EnvironmentSample sample = Worlds.Space(new Vector3(0.3f, 0.9f, 0.2f));

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
                Worlds.PlanetSurface(0.8f, timeOfDay: 0.35f, windSpeed: 22f));

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
