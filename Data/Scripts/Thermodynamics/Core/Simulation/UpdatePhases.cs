namespace Thermodynamics.Core
{
    /// <summary>
    /// Which frame of the update cycle a grid does its work on.
    ///
    /// <para>
    /// A grid steps once every ten rendered frames. <em>Which</em> of the ten is free, and it is
    /// the difference between a world that ticks smoothly and one that stutters ten times a
    /// second. A field run of a 203-grid world measured the whole fleet landing on the same
    /// frame: 1,392 of 13,915 frames did any work at all, and those that did averaged 117 ms and
    /// exceeded a 60 fps frame two times in three. The same work spread evenly is about twelve
    /// milliseconds a frame, every frame, for exactly the same total.
    /// </para>
    ///
    /// <para>
    /// Balancing by grid count is not enough, because grids are not the same size — one capital
    /// ship in that world held 42,051 blocks and the median grid held a few hundred. A phase is
    /// therefore chosen by the work already on it, and work is measured in blocks. A grid is
    /// atomic and cannot be split across frames, so the largest one sets a floor on the worst
    /// frame no matter how the rest are arranged; what this can do is stop the other two hundred
    /// piling on top of it.
    /// </para>
    /// </summary>
    public class UpdatePhases
    {
        /// <summary>Frames in one cycle. Matches the ten-frame tick the grids are paced at.</summary>
        public const int Count = 10;

        private readonly long[] load = new long[Count];
        private readonly int[] members = new int[Count];

        /// <summary>Blocks currently assigned to a phase.</summary>
        public long LoadOf(int phase)
        {
            return (phase < 0 || phase >= Count) ? 0 : load[phase];
        }

        /// <summary>Grids currently assigned to a phase.</summary>
        public int MembersOf(int phase)
        {
            return (phase < 0 || phase >= Count) ? 0 : members[phase];
        }

        /// <summary>
        /// Claims the emptiest phase for a grid of this size, and records it there.
        ///
        /// Ties go to the lower-numbered phase, which keeps the assignment deterministic — a
        /// world reloaded with its grids arriving in the same order lays them out the same way,
        /// and a test can say what the answer should be.
        /// </summary>
        public int Claim(int blocks)
        {
            if (blocks < 0) blocks = 0;

            int best = 0;
            for (int i = 1; i < Count; i++)
            {
                if (load[i] < load[best]) best = i;
            }

            load[best] += blocks;
            members[best]++;
            return best;
        }

        /// <summary>Gives a phase back when a grid closes.</summary>
        public void Release(int phase, int blocks)
        {
            if (phase < 0 || phase >= Count) return;

            load[phase] -= blocks;
            if (load[phase] < 0) load[phase] = 0;

            members[phase]--;
            if (members[phase] < 0) members[phase] = 0;
        }

        /// <summary>
        /// Corrects a phase's recorded load after a grid has grown or been ground down.
        ///
        /// A grid registers its phase when it starts, and a ship being built goes from one block
        /// to forty thousand without ever re-registering. Without this the balance reflects what
        /// every grid looked like when it first appeared, which for a projector's output is
        /// nothing at all.
        /// </summary>
        public void Reweigh(int phase, int previousBlocks, int currentBlocks)
        {
            if (phase < 0 || phase >= Count) return;
            if (previousBlocks < 0) previousBlocks = 0;
            if (currentBlocks < 0) currentBlocks = 0;

            load[phase] += currentBlocks - previousBlocks;
            if (load[phase] < 0) load[phase] = 0;
        }

        /// <summary>
        /// The heaviest and lightest phases, for diagnostics. A well-spread world has them close
        /// together; a world where one phase carries everything is the state this class exists to
        /// prevent, and a report should be able to say so.
        /// </summary>
        public long HeaviestLoad
        {
            get
            {
                long worst = 0;
                for (int i = 0; i < Count; i++)
                {
                    if (load[i] > worst) worst = load[i];
                }
                return worst;
            }
        }

        public long LightestLoad
        {
            get
            {
                long best = long.MaxValue;
                for (int i = 0; i < Count; i++)
                {
                    if (load[i] < best) best = load[i];
                }
                return best == long.MaxValue ? 0 : best;
            }
        }

        public long TotalLoad
        {
            get
            {
                long total = 0;
                for (int i = 0; i < Count; i++) total += load[i];
                return total;
            }
        }

        /// <summary>
        /// How lopsided the spread is: the heaviest phase against the average one.
        ///
        /// One is perfectly even. Ten is everything on a single frame, which is what the engine's
        /// own arrangement produced. The figure only means anything once there are more grids than
        /// phases; below that a single large grid legitimately dominates and no arrangement helps.
        /// </summary>
        public double Imbalance
        {
            get
            {
                long total = TotalLoad;
                if (total <= 0) return 1d;

                double mean = total / (double)Count;
                return mean <= 0d ? 1d : HeaviestLoad / mean;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < Count; i++)
            {
                load[i] = 0;
                members[i] = 0;
            }
        }
    }
}
