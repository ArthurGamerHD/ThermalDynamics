using System;
using System.Collections.Generic;
using Sandbox.ModAPI;

namespace Thermodynamics
{
    /// <summary>
    /// Runs every grid on every frame, each doing its share of the step it is part way through, so the
    /// cost of a step is spread rather than landed whole. Driven from the session component rather
    /// than the grid entity, because <c>MyCubeGrid</c> clears <c>EACH_FRAME</c> from its own update
    /// flags whenever its scheduled-work queue empties. See load-and-hitching.md, 9 and 10.
    /// </summary>
    public static class ThermalGridScheduler
    {
        /// <summary>
        /// Real seconds per frame. Space Engineers simulates at a fixed sixty frames a second, and
        /// this is the interval a grid divides its step across.
        /// </summary>
        public const float FrameSeconds = 1f / 60f;

        /// <summary>
        /// Gives every grid its share of this frame.
        ///
        /// Walked backwards because a grid can be closed by its own update, such as one destroyed by
        /// overheating, and a removal must not move an element the loop has not yet reached.
        /// </summary>
        public static void Tick()
        {
            IList<ThermalGrid> grids = ThermalGrid.LiveGrids;
            if (grids == null) return;

            if (Settings.Instance != null && Settings.Instance.ParallelGrids)
            {
                TickInParallel(grids);
                return;
            }

            for (int i = grids.Count - 1; i >= 0; i--)
            {
                ThermalGrid grid = grids[i];
                if (grid == null) continue;

                grid.Tick(FrameSeconds);
            }
        }

        /// <summary>
        /// The same frame, with the solving half fanned out across the engine's own worker threads.
        ///
        /// <para>
        /// **Solve in parallel, apply on the game thread**, which is the shape the engine's API is
        /// built for and the one the solver's invariants allow: order independence is one of the
        /// three, so a fleet stepped one grid per work item lands on the same numbers as a fleet
        /// stepped in order — `FleetParallelTests` asserts it bit for bit. What may *not* move off
        /// the game thread is anything that reads or writes the game, and that is exactly what
        /// `PrepareTick` and `PublishTick` are: the world sample either side of a step, the pump
        /// state, the damage, and every shared telemetry total.
        /// </para>
        ///
        /// <para>
        /// **Measured before it was built** (backlog.md `D19`): a 242-grid
        /// fleet is **10.17×** faster on 32 threads and 7.09× on eight, for a hand-off of 1.6–6.8 µs
        /// against a grid's own 0.54 ms — and an uneven fleet gives 3.35×, because a frame cannot
        /// finish before its largest grid does. One grid alone is 0.99×, so this is a fleet's
        /// mechanism and costs a single-grid world nothing measurable.
        /// </para>
        ///
        /// <para>
        /// **It ships off** (`ParallelGrids`), and what a session has to answer before that changes
        /// is written beside the setting in configuration.md: the engine's scheduler is not the
        /// framework's, a mod shares a machine with the game it is running inside, and neither of
        /// those is answerable from a harness.
        /// </para>
        /// </summary>
        private static void TickInParallel(IList<ThermalGrid> grids)
        {
            // Rebuilt each frame rather than kept, because the live list changes under it: a grid
            // destroyed by its own overheating is removed during the publish half.
            ready.Clear();

            for (int i = grids.Count - 1; i >= 0; i--)
            {
                ThermalGrid grid = grids[i];
                if (grid == null) continue;

                if (grid.PrepareTick(FrameSeconds)) ready.Add(grid);
            }

            if (ready.Count == 0) return;

            // One grid is not worth a fan-out: the hand-off is microseconds and a grid's own step is
            // hundreds of them, but a world with one ship in it should not pay even that.
            if (ready.Count == 1)
            {
                ready[0].SolveTick();
            }
            else if (MyAPIGateway.Parallel != null)
            {
                MyAPIGateway.Parallel.ForEach(ready, Solve);
            }
            else
            {
                // No parallel facility — a unit test, or an engine that did not provide one. The
                // work still has to happen, and it still has to happen before the publishes.
                for (int i = 0; i < ready.Count; i++) ready[i].SolveTick();
            }

            for (int i = 0; i < ready.Count; i++) ready[i].PublishTick();
        }

        /// <summary>The fan-out body, held as a field so a frame does not allocate a delegate.</summary>
        private static readonly Action<ThermalGrid> Solve = grid => grid.SolveTick();

        /// <summary>
        /// Grids prepared this frame and waiting to be solved.
        ///
        /// One list reused, and it is only ever touched from the game thread: the fan-out reads it
        /// and the publishes walk it, both while the game thread is inside `Tick`.
        /// </summary>
        private static readonly List<ThermalGrid> ready = new List<ThermalGrid>();
    }
}
