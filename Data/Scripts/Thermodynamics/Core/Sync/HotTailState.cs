using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// What a server still owes every client about every grid, and the pruning that keeps it from
    /// being a leak.
    ///
    /// <para>
    /// **This is bookkeeping, and it is here rather than in the game adapter because bookkeeping is
    /// where the silent failures are.** A schedule that is never dropped costs one entry per grid
    /// that has ever existed per player who has ever joined — a leak that only appears on the
    /// servers this mod most wants to run on, and never in a test session with one player. A
    /// schedule dropped too eagerly is a client re-sent a hull it already has. Neither announces
    /// itself, and both are decidable without a session.
    /// </para>
    ///
    /// <para>
    /// The game adapter keeps only what needs the engine: who is connected, where they are, and
    /// putting bytes on the wire.
    /// </para>
    /// </summary>
    public class HotTailServerState
    {
        private readonly Dictionary<ulong, Dictionary<long, HotTailSchedule>> owed =
            new Dictionary<ulong, Dictionary<long, HotTailSchedule>>();

        private readonly List<ulong> departed = new List<ulong>();
        private readonly List<long> stale = new List<long>();

        /// <summary>Seconds between band updates, applied to every schedule as it is used.</summary>
        public float IntervalSeconds = HotTailSchedule.DefaultIntervalSeconds;

        /// <summary>How many client-and-grid pairs are being tracked.</summary>
        public int Tracked
        {
            get
            {
                int count = 0;
                foreach (KeyValuePair<ulong, Dictionary<long, HotTailSchedule>> entry in owed)
                {
                    count += entry.Value.Count;
                }
                return count;
            }
        }

        /// <summary>How many clients are being tracked.</summary>
        public int Clients
        {
            get { return owed.Count; }
        }

        /// <summary>
        /// Moves every schedule's clock forward. Called once a pass rather than once per grid, so a
        /// grid the pass skipped — one out of range, or one still building — keeps its place in the
        /// interval instead of restarting it.
        /// </summary>
        public void Advance(float seconds)
        {
            foreach (KeyValuePair<ulong, Dictionary<long, HotTailSchedule>> entry in owed)
            {
                foreach (KeyValuePair<long, HotTailSchedule> schedule in entry.Value)
                {
                    schedule.Value.Advance(seconds);
                }
            }
        }

        /// <summary>
        /// What this client is owed about this grid now, consuming it. Creates the schedule on
        /// first sight, which is what makes a client's first pass over a grid a band update rather
        /// than nothing: the whole hull still waits for the client to ask for it.
        /// </summary>
        public HotTailSend Next(ulong client, long gridId)
        {
            return ScheduleFor(client, gridId).Next();
        }

        /// <summary>
        /// Records a client's request for a grid's whole hull.
        /// </summary>
        /// <returns>False when the request is dropped for being a repeat inside the cooldown.</returns>
        public bool Request(ulong client, long gridId)
        {
            return ScheduleFor(client, gridId).RequestSnapshot();
        }

        /// <summary>Whether this client has an unserved request for this grid.</summary>
        public bool Wants(ulong client, long gridId)
        {
            Dictionary<long, HotTailSchedule> grids;
            if (!owed.TryGetValue(client, out grids)) return false;

            HotTailSchedule schedule;
            return grids.TryGetValue(gridId, out schedule) && schedule.SnapshotWanted;
        }

        /// <summary>
        /// Drops what is owed to clients that are gone and about grids that are gone.
        ///
        /// Both sets are what the caller can see right now: a client not in
        /// <paramref name="clients"/> has left, and a grid not in <paramref name="grids"/> has been
        /// destroyed, split or merged away. A grid that comes back comes back with a new schedule,
        /// which is correct — the client will have rebuilt it and asked again.
        /// </summary>
        public void Forget(ICollection<ulong> clients, ICollection<long> grids)
        {
            departed.Clear();

            foreach (KeyValuePair<ulong, Dictionary<long, HotTailSchedule>> entry in owed)
            {
                if (clients == null || !clients.Contains(entry.Key))
                {
                    departed.Add(entry.Key);
                    continue;
                }

                if (grids == null) continue;

                stale.Clear();
                foreach (KeyValuePair<long, HotTailSchedule> schedule in entry.Value)
                {
                    if (!grids.Contains(schedule.Key)) stale.Add(schedule.Key);
                }

                for (int i = 0; i < stale.Count; i++) entry.Value.Remove(stale[i]);
            }

            for (int i = 0; i < departed.Count; i++) owed.Remove(departed[i]);

            departed.Clear();
            stale.Clear();
        }

        /// <summary>Forgets everything, for a session ending.</summary>
        public void Clear()
        {
            owed.Clear();
        }

        private HotTailSchedule ScheduleFor(ulong client, long gridId)
        {
            Dictionary<long, HotTailSchedule> grids;
            if (!owed.TryGetValue(client, out grids))
            {
                grids = new Dictionary<long, HotTailSchedule>();
                owed[client] = grids;
            }

            HotTailSchedule schedule;
            if (!grids.TryGetValue(gridId, out schedule))
            {
                schedule = new HotTailSchedule();
                grids[gridId] = schedule;
            }

            // Read from the settings every time rather than at construction, because a world's
            // interval can be changed mid-session and a schedule built an hour ago would keep the
            // old one for the rest of the session.
            schedule.IntervalSeconds = IntervalSeconds;
            return schedule;
        }
    }

    /// <summary>
    /// Which grids this client has asked the server to state, and which are still unanswered.
    ///
    /// The mirror of <see cref="HotTailServerState"/>, and here for the same reason: the backoff
    /// and the pruning are decidable without a session, and neither fails loudly.
    /// </summary>
    public class HotTailClientState
    {
        private readonly Dictionary<long, HotTailRequest> asked = new Dictionary<long, HotTailRequest>();
        private readonly List<long> stale = new List<long>();

        /// <summary>How many grids are being tracked.</summary>
        public int Tracked
        {
            get { return asked.Count; }
        }

        /// <summary>Grids this client is still waiting to be told about.</summary>
        public int Waiting
        {
            get
            {
                int count = 0;
                foreach (KeyValuePair<long, HotTailRequest> entry in asked)
                {
                    if (!entry.Value.Answered) count++;
                }
                return count;
            }
        }

        public void Advance(float seconds)
        {
            foreach (KeyValuePair<long, HotTailRequest> entry in asked)
            {
                entry.Value.Advance(seconds);
            }
        }

        /// <summary>Whether to ask the server about this grid now, consuming the decision.</summary>
        public bool ShouldAsk(long gridId)
        {
            return RequestFor(gridId).ShouldAsk();
        }

        /// <summary>Records that the server has stated this grid's whole hull.</summary>
        public void Answered(long gridId)
        {
            HotTailRequest request;
            if (asked.TryGetValue(gridId, out request)) request.Answer();
        }

        /// <summary>Whether this grid's hull has been stated at least once.</summary>
        public bool HasHull(long gridId)
        {
            HotTailRequest request;
            return asked.TryGetValue(gridId, out request) && request.Answered;
        }

        /// <summary>
        /// Drops grids this machine no longer has.
        ///
        /// A grid that streams out and back in is asked about again, which is right: the client
        /// rebuilt it from whatever the engine had, and that is the stale state this exists for.
        /// </summary>
        public void Forget(ICollection<long> grids)
        {
            stale.Clear();

            foreach (KeyValuePair<long, HotTailRequest> entry in asked)
            {
                if (grids == null || !grids.Contains(entry.Key)) stale.Add(entry.Key);
            }

            for (int i = 0; i < stale.Count; i++) asked.Remove(stale[i]);
            stale.Clear();
        }

        /// <summary>Forgets everything, for a session ending.</summary>
        public void Clear()
        {
            asked.Clear();
        }

        private HotTailRequest RequestFor(long gridId)
        {
            HotTailRequest request;
            if (!asked.TryGetValue(gridId, out request))
            {
                request = new HotTailRequest();
                asked[gridId] = request;
            }

            return request;
        }
    }
}
