using System.Collections.Generic;

namespace Thermodynamics
{
    /// <summary>
    /// Runs every grid on every frame, each doing its share of the step it is part way through.
    ///
    /// <para>
    /// A grid advances by one solver step every <c>1 / StepsPerSecond</c> of a second, fifteen frames
    /// at the default <c>Frequency 4</c>. Each grid does a fifteenth of its step per frame, which
    /// costs the same in total as the whole step on one frame and is felt as a steady frame rate.
    /// <c>Frequency</c> and <c>SimulationSpeed</c> set the size of that share through
    /// <c>StepsPerSecond</c>, so raising either makes every frame do proportionally more work.
    /// </para>
    ///
    /// <para>
    /// The engine's ten-frame callback cannot do this: it fires every grid together, so a 203-grid
    /// world did all of its thermal work on one frame in ten — 1,392 of 13,915 frames doing
    /// anything, averaging 117 ms, two in three exceeding a 60 fps frame. Assigning each grid one of
    /// ten phases spreads the grids across the cycle but not the work inside each of them, and one
    /// large grid on its own frame remains a stutter.
    /// </para>
    ///
    /// <para>
    /// Driven from the session component rather than the grid entity: <c>MyCubeGrid</c> clears
    /// <c>EACH_FRAME</c> from its update flags whenever its scheduled-work queue empties, so a mod
    /// depending on that flag silently stops running.
    /// </para>
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
            for (int i = grids.Count - 1; i >= 0; i--)
            {
                grids[i].Tick(FrameSeconds);
            }
        }
    }
}
