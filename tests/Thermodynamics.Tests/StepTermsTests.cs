using Thermodynamics.Core;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    public class StepTermsTests
    {

        private static ThermalSimulation Hull(int maxSubsteps)
        {
            return Hulls.Driven(Hulls.Uncapped(maxSubsteps), 600);
        }


        private static EnvironmentSample Flight()
        {
            return Worlds.Ab.EveryTermLive();
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        [InlineData(32)]

        public void TheEnvironmentRowsAreFilledOnceAStep(int maxSubsteps)
        {

            ThermalSimulation simulation = Hull(maxSubsteps);
            simulation.StepExact(1, Flight());

            simulation.Work.Reset();
            simulation.StepExact(6, Flight());

            Assert.Equal(6, simulation.Work.SolverSteps);
            Assert.True(simulation.Work.SolverSubsteps >= 6);
            Assert.Equal(6, simulation.Work.EnvironmentRowFills);
        }

        [Fact]

        public void SwitchingTheCacheOffFillsOnEverySubstep()
        {

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

        public void AFillSpreadAcrossFramesIsStillOneFill()
        {

            ThermalSimulation simulation = Hull(32);
            EnvironmentState state = EnvironmentSolver.Solve(

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
