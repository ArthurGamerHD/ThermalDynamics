using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// How a step is spread across the frames of its window.
    ///
    /// <para>
    /// Pacing exists so one grid's step does not arrive as a lump. It has a floor because
    /// re-entering the resumable stage machine costs the same whatever it carries, and a grid
    /// small enough that its whole step is a dozen element visits was paying that entry on every
    /// frame of its window. The floor must remove the entries without moving the rate: a grid that
    /// completes four steps a second before must complete four after.
    /// </para>
    /// </summary>
    public class StepPacingTests
    {
        private const float Frame = 1f / 60f;

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

        private static SimulationWork RunSeconds(ThermalSimulation simulation, int seconds)
        {
            simulation.Work.Reset();
            for (int frame = 0; frame < 60 * seconds; frame++)
            {
                simulation.Update(Frame, Worlds.Shadow());
            }

            return simulation.Work;
        }

        /// <summary>
        /// The rate is the whole point. Four steps a second at `Frequency 4`, whatever the grid's
        /// size does to how the work is sliced.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(8)]
        public void TheStepRateIsWhatFrequencySays(int side)
        {
            SimulationWork work = RunSeconds(Ship(side), 4);

            // Four seconds at four steps a second, allowing one for a step in flight at either end.
            Assert.InRange(work.SolverSteps, 15, 17);
        }

        /// <summary>
        /// A grid whose whole step fits inside the floor takes it in one piece rather than in one
        /// piece per frame. Fifteen frames of entering the stage machine to integrate a single
        /// block is the case the floor exists for.
        /// </summary>
        [Fact]
        public void ASmallGridsStepArrivesInOnePiece()
        {
            SimulationWork work = RunSeconds(Ship(1), 4);

            Assert.True(work.SolverSteps > 0);
            Assert.True(work.StepAdvances <= work.SolverSteps + 1,
                work.StepAdvances + " advances for " + work.SolverSteps + " steps");
        }

        /// <summary>
        /// A grid large enough for the floor to be a small share of its step is still sliced
        /// across the frames of its window, which is what stops it landing as a lump.
        /// </summary>
        [Fact]
        public void ALargeGridsStepIsStillSpreadOverItsWindow()
        {
            SimulationWork work = RunSeconds(Ship(16), 4);

            Assert.True(work.SolverSteps > 0);
            Assert.True(work.StepAdvances >= work.SolverSteps * 5,
                work.StepAdvances + " advances for " + work.SolverSteps + " steps");
        }

        /// <summary>
        /// Banking is not discarding. A grid that skips an advance because its share was below the
        /// floor must still have integrated the same simulated time when the second is up.
        /// </summary>
        [Fact]
        public void BankedCreditIsSpentRatherThanLost()
        {
            ThermalSimulation small = Ship(1);
            ThermalSimulation large = Ship(8);

            RunSeconds(small, 4);
            RunSeconds(large, 4);

            Assert.Equal(large.SimulatedSecondsRun, small.SimulatedSecondsRun, 3);
        }
    }
}
