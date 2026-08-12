using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Decides when a grid's solver runs.
    ///
    /// The original spread individual cell updates across frames, which meant a block's
    /// neighbours could be one simulation frame ahead or behind it and the sweep direction had
    /// to alternate to keep that fair. Because the solver is now order-independent and
    /// energy-conserving, the whole grid can step at once; what needs budgeting is how often,
    /// not which blocks.
    /// </summary>
    public class SimulationScheduler
    {
        private readonly ThermalSettings settings;

        private float accumulator;

        public SimulationScheduler(ThermalSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            this.settings = settings;
        }

        /// <summary>Simulated seconds each step advances.</summary>
        public float StepSeconds
        {
            get { return settings.StepSeconds; }
        }

        /// <summary>Steps owed per real second.</summary>
        public float StepsPerSecond
        {
            get { return settings.StepsPerSecond; }
        }

        /// <summary>Steps completed since construction.</summary>
        public long StepsRun { get; private set; }

        /// <summary>Fractional step credit carried between frames.</summary>
        public float Pending
        {
            get { return accumulator; }
        }

        /// <summary>
        /// Adds the credit earned by one render frame and reports how many solver steps are due.
        /// </summary>
        /// <param name="frameSeconds">Real seconds since the previous call.</param>
        /// <param name="maxStepsPerFrame">
        /// Upper bound so a long frame or a paused session cannot produce a burst of steps.
        /// </param>
        public int StepsDue(float frameSeconds, int maxStepsPerFrame = 4)
        {
            if (frameSeconds <= 0f) return 0;

            accumulator += frameSeconds * settings.StepsPerSecond;

            int due = (int)accumulator;
            if (due <= 0) return 0;

            if (due > maxStepsPerFrame)
            {
                // Drop the backlog rather than trying to catch up; heat is not worth a stall.
                due = maxStepsPerFrame;
                accumulator = 0f;
            }
            else
            {
                accumulator -= due;
            }

            StepsRun += due;
            return due;
        }

        /// <summary>
        /// Whether <see cref="StepsDue"/> would return anything for this frame, without
        /// consuming the credit.
        ///
        /// The host uses this to skip building an environment sample on frames that will not
        /// step: sampling means a planet lookup and possibly a raycast, and it is wasted work if
        /// nothing is going to integrate.
        /// </summary>
        public bool WouldStep(float frameSeconds)
        {
            if (frameSeconds <= 0f) return false;
            return accumulator + (frameSeconds * settings.StepsPerSecond) >= 1f;
        }

        /// <summary>
        /// Cell budget for the room mapper this frame. Scales with grid size so a big grid is
        /// mapped in roughly constant wall-clock time, with a floor so small grids finish fast.
        /// </summary>
        public static int RoomMappingBudget(int gridCellVolume)
        {
            int budget = gridCellVolume / 60;
            if (budget < 64) budget = 64;
            if (budget > 4096) budget = 4096;
            return budget;
        }

        public void Reset()
        {
            accumulator = 0f;
            StepsRun = 0;
        }
    }
}
