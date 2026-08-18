using System.Collections.Generic;

namespace Thermodynamics
{
    /// <summary>
    /// Runs every grid on every frame, each doing its share of the step it is part way through.
    ///
    /// <para>
    /// A grid advances by one solver step every <c>1 / StepsPerSecond</c> of a second — fifteen
    /// frames at the default <c>Frequency 4</c>. What matters is not which frame a grid works on
    /// but that it works on all of them: a fifteenth of the step per frame costs the same in total
    /// as the whole step on one frame and is felt as a steady frame rate rather than as a stutter
    /// fifteen frames wide. <c>Frequency</c> and <c>SimulationSpeed</c> set the size of that
    /// share, through <c>StepsPerSecond</c>, so turning either up makes every frame do
    /// proportionally more instead of making the lumps arrive closer together.
    /// </para>
    ///
    /// <para>
    /// The engine's own ten-frame callback cannot do this: it fires every grid together, so a
    /// 203-grid world did all of its thermal work on one frame in ten and none on the other nine —
    /// 1,392 of 13,915 frames doing anything, averaging 117 ms, two in three over a 60 fps frame.
    /// An earlier attempt gave each grid one of ten phases so the fleet was at least spread across
    /// the cycle. That helped and was still the wrong shape: it spread grids, and what needed
    /// spreading was the work inside each of them. One large ship on its own frame is a stutter no
    /// arrangement of the others can fix.
    /// </para>
    ///
    /// <para>
    /// Driven from the session component rather than from the grid entity, because
    /// <c>MyCubeGrid</c> clears <c>EACH_FRAME</c> from its own update flags whenever its
    /// scheduled-work queue empties — a mod hanging its cadence on that flag stops running,
    /// silently.
    /// </para>
    /// </summary>
    public static class ThermalGridScheduler
    {
        /// <summary>
        /// Real seconds a frame is assumed to be. Space Engineers simulates at a fixed sixty
        /// frames a second, and this is the interval a grid divides its step across.
        /// </summary>
        public const float FrameSeconds = 1f / 60f;

        /// <summary>
        /// Gives every grid its share of this frame.
        ///
        /// Walked backwards because a grid can be closed by what its own update does — a ship
        /// destroyed by its own overheating — and a removal must not move an element the loop has
        /// not reached yet.
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
