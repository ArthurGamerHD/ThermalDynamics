using System.Collections.Generic;
using Thermodynamics.Core;

namespace Thermodynamics
{
    /// <summary>
    /// Decides which frame each grid does its thermal work on, and drives them.
    ///
    /// <para>
    /// A grid ticks once every ten rendered frames, and until this existed all of them ticked on
    /// the <em>same</em> ten. That is not something the mod chose: the engine calls every entity's
    /// ten-frame update together, so a world of two hundred grids did all of its thermal work on
    /// one frame and none on the other nine.
    /// </para>
    ///
    /// <para>
    /// A field run of a 203-grid save measured what that costs. Of 13,915 frames, 1,392 did any
    /// work at all — one in ten, exactly — and those averaged 117 ms with a worst of 611 ms, with
    /// two frames in three exceeding a 60 fps frame. The total was 20 % of real time, which is a
    /// throughput problem; arriving in one lump every tenth frame is a stutter problem, and they
    /// are not the same problem. Spread evenly the same work is about twelve milliseconds a frame.
    /// </para>
    ///
    /// <para>
    /// So the cadence is the mod's own. Grids are held in ten buckets, each frame ticks one
    /// bucket, and a grid joins the bucket carrying the least work — measured in blocks, because
    /// grids differ in size by three orders of magnitude and balancing by count would leave a
    /// capital ship sharing a frame with two hundred fighters.
    /// </para>
    ///
    /// <para>
    /// Driven from the session component rather than from the grid entity. The entity route would
    /// mean asking for a per-frame callback and gating on the phase inside it, and
    /// <c>MyCubeGrid</c> clears <c>EACH_FRAME</c> from its own update flags whenever its
    /// scheduled-work queue empties — so a mod hanging its cadence on that flag would silently
    /// stop running. The session's own per-frame call belongs to this mod and nothing else edits
    /// it.
    /// </para>
    /// </summary>
    public static class ThermalGridScheduler
    {
        private static readonly List<ThermalGrid>[] Buckets = CreateBuckets();

        private static readonly UpdatePhases Phases = new UpdatePhases();

        private static List<ThermalGrid>[] CreateBuckets()
        {
            List<ThermalGrid>[] buckets = new List<ThermalGrid>[UpdatePhases.Count];
            for (int i = 0; i < buckets.Length; i++) buckets[i] = new List<ThermalGrid>();
            return buckets;
        }

        /// <summary>The load balance across phases, for the telemetry report.</summary>
        public static UpdatePhases Balance
        {
            get { return Phases; }
        }

        /// <summary>
        /// Puts a grid on the emptiest phase. Called once, when the grid starts simulating.
        /// </summary>
        public static void Register(ThermalGrid grid, int blocks)
        {
            if (grid == null || grid.UpdatePhase >= 0) return;

            int phase = Phases.Claim(blocks);
            grid.UpdatePhase = phase;
            Buckets[phase].Add(grid);
        }

        public static void Unregister(ThermalGrid grid, int blocks)
        {
            if (grid == null) return;

            int phase = grid.UpdatePhase;
            if (phase < 0 || phase >= UpdatePhases.Count) return;

            Buckets[phase].Remove(grid);
            Phases.Release(phase, blocks);
            grid.UpdatePhase = -1;
        }

        /// <summary>Corrects a phase's recorded load after a grid has grown or shrunk.</summary>
        public static void Reweigh(ThermalGrid grid, int previousBlocks, int currentBlocks)
        {
            if (grid == null) return;
            Phases.Reweigh(grid.UpdatePhase, previousBlocks, currentBlocks);
        }

        /// <summary>
        /// Ticks the grids belonging to this frame's phase.
        ///
        /// One bucket per frame, so the cost of choosing is the length of that bucket rather than
        /// the length of the world. A grid removed from the bucket while it is being walked is the
        /// case to be careful about — a ship destroyed by its own overheating does exactly that —
        /// so the walk runs backwards, where a removal cannot move an element the loop has not
        /// reached yet.
        /// </summary>
        public static void Tick(long frame)
        {
            int phase = (int)(frame % UpdatePhases.Count);
            if (phase < 0) phase += UpdatePhases.Count;

            List<ThermalGrid> bucket = Buckets[phase];
            for (int i = bucket.Count - 1; i >= 0; i--)
            {
                bucket[i].Tick();
            }
        }

        /// <summary>Drops everything. Called when the session unloads.</summary>
        public static void Clear()
        {
            for (int i = 0; i < Buckets.Length; i++) Buckets[i].Clear();
            Phases.Clear();
        }
    }
}
