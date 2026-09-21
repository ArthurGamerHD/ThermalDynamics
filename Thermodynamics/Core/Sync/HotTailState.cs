using System.Collections.Generic;

namespace Thermodynamics.Core
{
    public class HotTailServerState
    {
        private readonly Dictionary<ulong, Dictionary<long, HotTailSchedule>> owed =
            new Dictionary<ulong, Dictionary<long, HotTailSchedule>>();

/// <summary>List operation.</summary>
        private readonly List<ulong> departed = new List<ulong>();
/// <summary>List operation.</summary>
        private readonly List<long> stale = new List<long>();

        public float IntervalSeconds = HotTailSchedule.DefaultIntervalSeconds;

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

        public int Clients
        {
            get { return owed.Count; }
        }

/// <summary>Advance operation.</summary>
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

/// <summary>Next operation.</summary>
        public HotTailSend Next(ulong client, long gridId)
        {
            return ScheduleFor(client, gridId).Next();
        }

/// <summary>Request operation.</summary>
        public bool Request(ulong client, long gridId)
        {
            return ScheduleFor(client, gridId).RequestSnapshot();
        }

/// <summary>Wants operation.</summary>
        public bool Wants(ulong client, long gridId)
        {
            Dictionary<long, HotTailSchedule> grids;
            if (!owed.TryGetValue(client, out grids)) return false;

            HotTailSchedule schedule;
            return grids.TryGetValue(gridId, out schedule) && schedule.SnapshotWanted;
        }

/// <summary>Forget operation.</summary>
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

/// <summary>Clear operation.</summary>
        public void Clear()
        {
            owed.Clear();
        }

/// <summary>ScheduleFor operation.</summary>
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
/// <summary>HotTailSchedule operation.</summary>
                schedule = new HotTailSchedule();
                grids[gridId] = schedule;
            }

            schedule.IntervalSeconds = IntervalSeconds;
            return schedule;
        }
    }

    public class HotTailClientState
    {
        private readonly Dictionary<long, HotTailRequest> asked = new Dictionary<long, HotTailRequest>();
/// <summary>List operation.</summary>
        private readonly List<long> stale = new List<long>();

        public int Tracked
        {
            get { return asked.Count; }
        }

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

/// <summary>Advance operation.</summary>
        public void Advance(float seconds)
        {
            foreach (KeyValuePair<long, HotTailRequest> entry in asked)
            {
                entry.Value.Advance(seconds);
            }
        }

/// <summary>ShouldAsk operation.</summary>
        public bool ShouldAsk(long gridId)
        {
            return RequestFor(gridId).ShouldAsk();
        }

/// <summary>Answered operation.</summary>
        public void Answered(long gridId)
        {
            HotTailRequest request;
            if (asked.TryGetValue(gridId, out request)) request.Answer();
        }

/// <summary>HasHull operation.</summary>
        public bool HasHull(long gridId)
        {
            HotTailRequest request;
            return asked.TryGetValue(gridId, out request) && request.Answered;
        }

/// <summary>Forget operation.</summary>
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

/// <summary>Clear operation.</summary>
        public void Clear()
        {
            asked.Clear();
        }

/// <summary>RequestFor operation.</summary>
        private HotTailRequest RequestFor(long gridId)
        {
            HotTailRequest request;
            if (!asked.TryGetValue(gridId, out request))
            {
/// <summary>HotTailRequest operation.</summary>
                request = new HotTailRequest();
                asked[gridId] = request;
            }

            return request;
        }
    }
}
