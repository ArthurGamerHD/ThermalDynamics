using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    public class PrecomputedEnvironmentTests
    {
/// <summary>Builds the API method table.</summary>
        private static ThermalSimulation Build(bool precompute)
        {
            ThermalSimulation simulation = Hulls.Driven();
            simulation.Solver.PrecomputeEnvironment = precompute;
            return simulation;
        }

/// <summary>AssertIdentical operation.</summary>
        private static void AssertIdentical(ThermalSimulation a, ThermalSimulation b, string what)
        {
            SolverAb.AssertIdentical(
                SolverAb.Temperatures(a), SolverAb.Temperatures(b), what,
                "without the terms reused", "with them reused");
        }

        [Fact]
/// <summary>ReusingThePerStepTermsIsBitIdenticalInAnAtmosphere operation.</summary>
        public void ReusingThePerStepTermsIsBitIdenticalInAnAtmosphere()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation recomputed = Build(false);
/// <summary>Builds the method table.</summary>
            ThermalSimulation reused = Build(true);

            EnvironmentSample sample = Worlds.Ab.MildAtmosphere();

            recomputed.StepExact(40, sample);
            reused.StepExact(40, sample);

            AssertIdentical(recomputed, reused, "atmosphere");
        }

        [Fact]
/// <summary>ReusingThePerStepTermsIsBitIdenticalInFlight operation.</summary>
        public void ReusingThePerStepTermsIsBitIdenticalInFlight()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation recomputed = Build(false);
/// <summary>Builds the method table.</summary>
            ThermalSimulation reused = Build(true);

            EnvironmentSample sample = Worlds.Ab.EveryTermLive();

            recomputed.StepExact(40, sample);
            reused.StepExact(40, sample);

            Assert.True(recomputed.Solver.Nodes[0].LastFrictionWatts >= 0f);
            AssertIdentical(recomputed, reused, "flight");
        }

        [Fact]
/// <summary>ReusingThePerStepTermsIsBitIdenticalInVacuum operation.</summary>
        public void ReusingThePerStepTermsIsBitIdenticalInVacuum()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation recomputed = Build(false);
/// <summary>Builds the method table.</summary>
            ThermalSimulation reused = Build(true);

            EnvironmentSample sample = Worlds.Ab.SunlitVacuum();

            recomputed.StepExact(40, sample);
            reused.StepExact(40, sample);

            AssertIdentical(recomputed, reused, "vacuum");
        }

        [Fact]
/// <summary>TheCacheIsInvalidatedWhenTheSelfShadowPassPublishes operation.</summary>
        public void TheCacheIsInvalidatedWhenTheSelfShadowPassPublishes()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation recomputed = Build(false);
/// <summary>Builds the method table.</summary>
            ThermalSimulation reused = Build(true);

            recomputed.Solver.SunLitBudget = 64;
            reused.Solver.SunLitBudget = 64;
            recomputed.Solver.SunShadowBudget = 128;
            reused.Solver.SunShadowBudget = 128;

            for (int i = 0; i < 30; i++)
            {
                float angle = i * 0.35f;
/// <summary>Vector3 operation.</summary>
                Vector3 sun = new Vector3((float)Math.Cos(angle), (float)Math.Sin(angle), 0.2f);

                EnvironmentSample sample = Worlds.Space(sun);
                recomputed.StepExact(1, sample);
                reused.StepExact(1, sample);
            }

            AssertIdentical(recomputed, reused, "moving sun");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(97)]
        [InlineData(5000)]
/// <summary>ReusingThePerStepTermsSurvivesTheStepBeingSpread operation.</summary>
        public void ReusingThePerStepTermsSurvivesTheStepBeingSpread(int budget)
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation whole = Build(false);
/// <summary>Builds the method table.</summary>
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
