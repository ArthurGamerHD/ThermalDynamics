using System.Collections.Generic;

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

            for (int i = grids.Count - 1; i >= 0; i--)
            {
                ThermalGrid grid = grids[i];
                if (grid == null) continue;

                grid.Tick(FrameSeconds);
            }
        }
    }
}
