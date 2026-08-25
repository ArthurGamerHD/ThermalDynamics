using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Counts a grid's completed steps, and sizes the budgets for its resumable passes.
    ///
    /// <para>
    /// **It does not decide when a step runs, and it used to look as though it did.** The pacing
    /// lives in <see cref="ThermalSimulation.Update"/>, which banks work credit against the frame
    /// it is given and spends it a slice at a time; this class carried a second, parallel
    /// step-credit accumulator — `StepsDue`, `WouldStep` and the frame-length arithmetic under
    /// them — that no shipped path ever called. Two pages and both client labs described *that*
    /// mechanism as the live one, so a degraded-input row named a backlog drop the mod cannot
    /// perform. Removed 2026-08-24; see backlog.md `F23`.
    /// </para>
    ///
    /// <para>
    /// The budget helpers are static because they are arithmetic over a grid's size rather than
    /// state, and what remains of the instance is the step count the host reads to know whether a
    /// tick stepped.
    /// </para>
    /// </summary>
    public class SimulationScheduler
    {
        public SimulationScheduler(ThermalSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
        }

        /// <summary>Steps completed since construction.</summary>
        public long StepsRun { get; private set; }

        /// <summary>
        /// Cell budget for the room mapper this frame. Scales with grid size so a large grid is
        /// mapped in roughly constant wall-clock time, with a floor so small grids finish quickly.
        /// </summary>
        public static int RoomMappingBudget(int gridCellVolume)
        {
            int budget = gridCellVolume / 60;
            if (budget < 64) budget = 64;
            if (budget > 4096) budget = 4096;
            return budget;
        }

        /// <summary>
        /// Node budget for one slice of an exposure pass. Scaled with grid size like the mapping
        /// budget, so a large grid spreads the pass over more ticks, with a floor that lets a small
        /// grid finish within one tick.
        /// </summary>
        public static int ExposureBudget(int nodeCount)
        {
            int budget = nodeCount / 40;
            if (budget < 256) budget = 256;
            if (budget > 4096) budget = 4096;
            return budget;
        }

        /// <summary>
        /// How many items a rolling sweep should visit this tick: the share completing a full pass in
        /// <paramref name="interval"/> steps, capped so no tick exceeds its share however large the
        /// grid. Below the cap a grid still sweeps whole in exactly that many steps.
        /// </summary>
        /// <param name="count">Items in the rota.</param>
        /// <param name="steps">Solver steps this tick advanced.</param>
        /// <param name="interval">Steps a full pass should take, when affordable.</param>
        /// <param name="cap">Most items one tick may visit.</param>
        public static int SweepSlice(int count, int steps, int interval, int cap)
        {
            if (count <= 0 || steps <= 0 || cap <= 0) return 0;
            if (interval < 1) interval = 1;

            // Computed in long arithmetic: a million blocks times four steps overflows an int
            // before the division brings it back down.
            long share = ((long)count * steps) / interval;

            // Never zero while there is anything to sweep: a grid large enough for the division to
            // round down to zero would otherwise never be swept.
            if (share < 1) share = 1;
            if (share > cap) share = cap;
            if (share > count) share = count;

            return (int)share;
        }

        /// <summary>
        /// Records that a step completed. Step pacing belongs to the simulation, which spreads one
        /// step over the frames of its window, so this only maintains the running count a host
        /// reads.
        /// </summary>
        public void CountStep()
        {
            StepsRun++;
        }

        public void Reset()
        {
            StepsRun = 0;
        }
    }
}
