using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    public class DiagnosticBatchingTests
    {
/// <summary>Builds the API method table.</summary>
        private static ThermalSimulation Build(bool everySubstep, int maxSubsteps)
        {
            ThermalSimulation simulation = Hulls.Driven(Hulls.Uncapped(maxSubsteps));
            simulation.Solver.CollectDiagnostics = true;
            simulation.Solver.DiagnosticsOnEverySubstep = everySubstep;
            return simulation;
        }

/// <summary>AssertIdentical operation.</summary>
        private static void AssertIdentical(ThermalSimulation every, ThermalSimulation last, string what)
        {
            SolverAb.AssertIdentical(
                SolverAb.Diagnostics(every), SolverAb.Diagnostics(last), what,
                "written on every substep", "written on the last substep",
                SolverAb.Mechanisms);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(4096)]
/// <summary>OnlyTheLastSubstepIsPublishedAndItIsTheSameOne operation.</summary>
        public void OnlyTheLastSubstepIsPublishedAndItIsTheSameOne(int maxSubsteps)
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation every = Build(everySubstep: true, maxSubsteps: maxSubsteps);
/// <summary>Builds the method table.</summary>
            ThermalSimulation last = Build(everySubstep: false, maxSubsteps: maxSubsteps);

            EnvironmentSample sample = Worlds.Ab.MildAtmosphere();

            every.StepExact(20, sample);
            last.StepExact(20, sample);

            AssertIdentical(every, last, "atmosphere at " + maxSubsteps);
        }

        [Fact]
/// <summary>TheSameHoldsInVacuum operation.</summary>
        public void TheSameHoldsInVacuum()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation every = Build(everySubstep: true, maxSubsteps: 4096);
/// <summary>Builds the method table.</summary>
            ThermalSimulation last = Build(everySubstep: false, maxSubsteps: 4096);

            EnvironmentSample sample = Worlds.Ab.SunlitVacuum();

            every.StepExact(20, sample);
            last.StepExact(20, sample);

            AssertIdentical(every, last, "vacuum");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(97)]
        [InlineData(5000)]
/// <summary>BatchingSurvivesTheStepBeingSpread operation.</summary>
        public void BatchingSurvivesTheStepBeingSpread(int budget)
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation whole = Build(everySubstep: true, maxSubsteps: 4096);
/// <summary>Builds the method table.</summary>
            ThermalSimulation spread = Build(everySubstep: false, maxSubsteps: 4096);

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

        [Fact]
/// <summary>NothingIsPublishedWhenDiagnosticsAreOff operation.</summary>
        public void NothingIsPublishedWhenDiagnosticsAreOff()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation simulation = Build(everySubstep: false, maxSubsteps: 4096);
            simulation.Solver.CollectDiagnostics = false;

            simulation.StepExact(20, Worlds.Ab.MildAtmosphere());

            float[] published = SolverAb.Diagnostics(simulation);
            for (int i = 0; i < published.Length; i++)
            {
                Assert.Equal(0f, published[i]);
            }
        }
    }
}
