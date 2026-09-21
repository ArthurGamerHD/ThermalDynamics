using Thermodynamics.Core;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    public class StepTermsTests
    {
/// <summary>Hull operation.</summary>
        private static ThermalSimulation Hull(int maxSubsteps)
        {
            return Hulls.Driven(Hulls.Uncapped(maxSubsteps), 600);
        }

/// <summary>Flight operation.</summary>
        private static EnvironmentSample Flight()
        {
            return Worlds.Ab.EveryTermLive();
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        [InlineData(32)]
/// <summary>TheEnvironmentRowsAreFilledOnceAStep operation.</summary>
        public void TheEnvironmentRowsAreFilledOnceAStep(int maxSubsteps)
        {
/// <summary>Hull operation.</summary>
            ThermalSimulation simulation = Hull(maxSubsteps);
            simulation.StepExact(1, Flight());

            simulation.Work.Reset();
            simulation.StepExact(6, Flight());

            Assert.Equal(6, simulation.Work.SolverSteps);
            Assert.True(simulation.Work.SolverSubsteps >= 6);
            Assert.Equal(6, simulation.Work.EnvironmentRowFills);
        }

        [Fact]
/// <summary>SwitchingTheCacheOffFillsOnEverySubstep operation.</summary>
        public void SwitchingTheCacheOffFillsOnEverySubstep()
        {
/// <summary>Hull operation.</summary>
            ThermalSimulation simulation = Hull(32);
            simulation.Solver.PrecomputeEnvironment = false;
            simulation.StepExact(1, Flight());

            simulation.Work.Reset();
            simulation.StepExact(4, Flight());

            Assert.True(simulation.Work.SolverSubsteps > 4,
                "the hull took one substep a step, so this asserts nothing");
            Assert.Equal(simulation.Work.SolverSubsteps, simulation.Work.EnvironmentRowFills);
        }

        [Fact]
/// <summary>AFillSpreadAcrossFramesIsStillOneFill operation.</summary>
        public void AFillSpreadAcrossFramesIsStillOneFill()
        {
/// <summary>Hull operation.</summary>
            ThermalSimulation simulation = Hull(32);
            EnvironmentState state = EnvironmentSolver.Solve(
/// <summary>Flight operation.</summary>
                simulation.Settings, simulation.Planet, Flight());

            simulation.Solver.Step(simulation.Settings.StepSeconds, state);

            simulation.Work.Reset();
            simulation.Solver.BeginStep(simulation.Settings.StepSeconds, state);
            while (!simulation.Solver.AdvanceStep(97)) { }

            Assert.True(simulation.Work.StepAdvances > 1, "the step landed in one piece");
            Assert.Equal(1, simulation.Work.EnvironmentRowFills);
        }
    }
}
