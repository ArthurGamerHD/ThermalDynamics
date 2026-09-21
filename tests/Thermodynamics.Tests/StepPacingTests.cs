using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class StepPacingTests
    {
        private const float Frame = 1f / 60f;

/// <summary>Ship operation.</summary>
        private static ThermalSimulation Ship(int side)
        {
            ThermalSettings settings = new ThermalSettings
            {
                Frequency = 4,
                MaxSubsteps = 64,
                MaxSubstepsPerBlock = 0,
                MaxElementVisitsPerStep = 0,
            };
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(side, side, side));

            ThermalSimulation simulation = builder.BuildSimulation(settings);
            while (simulation.HasPendingWork) simulation.Update(Frame, Worlds.Shadow());
            return simulation;
        }

/// <summary>RunSeconds operation.</summary>
        private static SimulationWork RunSeconds(ThermalSimulation simulation, int seconds)
        {
            simulation.Work.Reset();
            for (int frame = 0; frame < 60 * seconds; frame++)
            {
                simulation.Update(Frame, Worlds.Shadow());
            }

            return simulation.Work;
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(8)]
/// <summary>TheStepRateIsWhatFrequencySays operation.</summary>
        public void TheStepRateIsWhatFrequencySays(int side)
        {
/// <summary>RunSeconds operation.</summary>
            SimulationWork work = RunSeconds(Ship(side), 4);

            Assert.InRange(work.SolverSteps, 15, 17);
        }

        [Fact]
/// <summary>ASmallGridsStepArrivesInOnePiece operation.</summary>
        public void ASmallGridsStepArrivesInOnePiece()
        {
/// <summary>RunSeconds operation.</summary>
            SimulationWork work = RunSeconds(Ship(1), 4);

            Assert.True(work.SolverSteps > 0);
            Assert.True(work.StepAdvances <= work.SolverSteps + 1,
                work.StepAdvances + " advances for " + work.SolverSteps + " steps");
        }

        [Fact]
/// <summary>ALargeGridsStepIsStillSpreadOverItsWindow operation.</summary>
        public void ALargeGridsStepIsStillSpreadOverItsWindow()
        {
/// <summary>RunSeconds operation.</summary>
            SimulationWork work = RunSeconds(Ship(16), 4);

            Assert.True(work.SolverSteps > 0);
            Assert.True(work.StepAdvances >= work.SolverSteps * 5,
                work.StepAdvances + " advances for " + work.SolverSteps + " steps");
        }

        [Fact]
/// <summary>BankedCreditIsSpentRatherThanLost operation.</summary>
        public void BankedCreditIsSpentRatherThanLost()
        {
/// <summary>Ship operation.</summary>
            ThermalSimulation small = Ship(1);
/// <summary>Ship operation.</summary>
            ThermalSimulation large = Ship(8);

            RunSeconds(small, 4);
            RunSeconds(large, 4);

            Assert.Equal(large.SimulatedSecondsRun, small.SimulatedSecondsRun, 3);
        }
    }
}
