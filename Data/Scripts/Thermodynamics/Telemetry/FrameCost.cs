using System;
using System.Collections.Generic;
using System.Text;

namespace Thermodynamics
{
    /// <summary>One frame the mod cost more than it usually does, and what was happening on it.</summary>
    public struct FrameSample
    {
        public long Frame;
        public double SessionSeconds;

        /// <summary>Wall clock the mod spent on this frame, across every grid.</summary>
        public double TotalMs;

        public double TopologyMs;
        public double RoomMappingMs;
        public double ExposureMs;
        public double SolverMs;

        /// <summary>Grids that did any work, and the worst single one.</summary>
        public int Grids;
        public double WorstGridMs;
        public string WorstGrid;
        public int WorstGridBlocks;

        /// <summary>What the one-shot stages touched, summed over the frame.</summary>
        public long TopologyNodeVisits;
        public long ExposureNodeVisits;
        public long RoomCellsVisited;

        public string Describe()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("frame ").Append(Frame)
              .Append(" at ").Append(SessionSeconds.ToString("n1")).Append("s: ")
              .Append(TotalMs.ToString("n2")).Append(" ms over ").Append(Grids).Append(" grids");

            if (!string.IsNullOrEmpty(WorstGrid))
            {
                sb.Append(", worst ").Append(WorstGrid)
                  .Append(" (").Append(WorstGridBlocks.ToString("n0")).Append(" blocks) ")
                  .Append(WorstGridMs.ToString("n2")).Append(" ms");
            }

            sb.Append("\n      topology ").Append(TopologyMs.ToString("n2"))
              .Append(", rooms ").Append(RoomMappingMs.ToString("n2"))
              .Append(", exposure ").Append(ExposureMs.ToString("n2"))
              .Append(", solver ").Append(SolverMs.ToString("n2")).Append(" ms");

            sb.Append("\n      touched ").Append(TopologyNodeVisits.ToString("n0"))
              .Append(" nodes for topology, ").Append(ExposureNodeVisits.ToString("n0"))
              .Append(" for exposure, ").Append(RoomCellsVisited.ToString("n0")).Append(" cells flooded");

            return sb.ToString();
        }
    }

    /// <summary>
    /// What the mod costs per frame, across every grid, and which frames were the worst.
    ///
    /// Everything else the telemetry records is per grid. A player does not experience per grid:
    /// twenty ships each taking a tolerable two milliseconds on the same frame is forty
    /// milliseconds of one frame, and that is a stutter no per-grid figure shows. Grids tick on
    /// the ten-frame cadence and the engine calls them all on the same frame, so this is not a
    /// hypothetical — it is the shape of the cost by default.
    ///
    /// The worst frames are kept in full rather than summarised, because a stutter is a
    /// particular event with a cause. A mean says the mod is cheap, a maximum says one frame was
    /// not, and only the sample says the frame was a 300,000-block station rebuilding its
    /// conduction graph while three others flooded their room maps. That is the difference
    /// between knowing there is a problem and knowing what to change.
    ///
    /// Kept small on purpose: the worst sixteen frames of a session, one struct each, and a
    /// histogram. There is no per-frame history — a world open for hours would spend more memory
    /// on it than on the simulation.
    /// </summary>
    public class FrameCostTracker
    {
        /// <summary>How many worst frames to keep in full.</summary>
        public const int Keep = 16;

        /// <summary>
        /// Frames costing less than this are not considered for the hitch list at all.
        ///
        /// A frame at 60 fps is 16.7 ms of everything the game does, so a mod taking four of
        /// them is already worth looking at and anything under is noise. Without a floor the
        /// list fills with the sixteen most ordinary frames of a quiet session and says nothing.
        /// </summary>
        public double HitchThresholdMs = 4d;

        public readonly TimingStat Frame = new TimingStat("mod, all grids, per frame");

        private readonly List<FrameSample> worst = new List<FrameSample>();

        /// <summary>Frames on which the mod did any work at all.</summary>
        public long FramesWithWork;

        /// <summary>Frames that cost more than one 60 fps frame on their own.</summary>
        public long FramesOverBudget;

        /// <summary>One rendered frame at 60 fps, in milliseconds.</summary>
        public const double FrameBudgetMs = 1000d / 60d;

        // ---- accumulation for the frame in progress ----------------------------------------

        private double total;
        private double topology;
        private double roomMapping;
        private double exposure;
        private double solver;
        private int grids;
        private double worstGridMs;
        private string worstGrid;
        private int worstGridBlocks;
        private long topologyVisits;
        private long exposureVisits;
        private long roomCells;

        /// <summary>Adds one grid's whole update to the frame in progress.</summary>
        public void AddGrid(string name, double milliseconds, int blocks)
        {
            if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds)) return;

            total += milliseconds;
            grids++;

            if (milliseconds <= worstGridMs) return;
            worstGridMs = milliseconds;
            worstGrid = name;
            worstGridBlocks = blocks;
        }

        /// <summary>Adds a stage's cost, which is nested inside a grid's update.</summary>
        public void AddStage(int phase, double milliseconds)
        {
            if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds)) return;

            switch (phase)
            {
                case 0: topology += milliseconds; break;
                case 1: roomMapping += milliseconds; break;
                case 2: exposure += milliseconds; break;
                default: solver += milliseconds; break;
            }
        }

        /// <summary>Adds what the one-shot stages touched, so a hitch says why and not only how long.</summary>
        public void AddWork(long topologyNodes, long exposureNodes, long cellsFlooded)
        {
            topologyVisits += topologyNodes;
            exposureVisits += exposureNodes;
            roomCells += cellsFlooded;
        }

        /// <summary>
        /// Closes the frame in progress and records it.
        ///
        /// Called at the top of the next frame rather than at the end of this one, because the
        /// order the engine runs session components and entity components in is not something a
        /// mod controls — and a frame closed before the grids have run records nothing.
        /// </summary>
        public void EndFrame(long frame, double sessionSeconds)
        {
            if (total <= 0d)
            {
                Reset();
                return;
            }

            FramesWithWork++;
            Frame.Record(total);
            if (total > FrameBudgetMs) FramesOverBudget++;

            if (total >= HitchThresholdMs) Offer(BuildSample(frame, sessionSeconds));

            Reset();
        }

        private FrameSample BuildSample(long frame, double sessionSeconds)
        {
            FrameSample sample = new FrameSample();
            sample.Frame = frame;
            sample.SessionSeconds = sessionSeconds;
            sample.TotalMs = total;
            sample.TopologyMs = topology;
            sample.RoomMappingMs = roomMapping;
            sample.ExposureMs = exposure;
            sample.SolverMs = solver;
            sample.Grids = grids;
            sample.WorstGridMs = worstGridMs;
            sample.WorstGrid = worstGrid;
            sample.WorstGridBlocks = worstGridBlocks;
            sample.TopologyNodeVisits = topologyVisits;
            sample.ExposureNodeVisits = exposureVisits;
            sample.RoomCellsVisited = roomCells;
            return sample;
        }

        /// <summary>
        /// Keeps the sample if it is worse than the least bad one held, so the list is the
        /// session's worst rather than its most recent.
        /// </summary>
        private void Offer(FrameSample sample)
        {
            if (worst.Count < Keep)
            {
                worst.Add(sample);
                worst.Sort(Compare);
                return;
            }

            if (sample.TotalMs <= worst[worst.Count - 1].TotalMs) return;

            worst[worst.Count - 1] = sample;
            worst.Sort(Compare);
        }

        private static int Compare(FrameSample a, FrameSample b)
        {
            return b.TotalMs.CompareTo(a.TotalMs);
        }

        private void Reset()
        {
            total = 0d;
            topology = 0d;
            roomMapping = 0d;
            exposure = 0d;
            solver = 0d;
            grids = 0;
            worstGridMs = 0d;
            worstGrid = null;
            worstGridBlocks = 0;
            topologyVisits = 0;
            exposureVisits = 0;
            roomCells = 0;
        }

        /// <summary>The worst frames of the session, worst first.</summary>
        public IList<FrameSample> Worst
        {
            get { return worst; }
        }

        /// <summary>
        /// How spiky the session was: the worst frame against the median one.
        ///
        /// The single number worth quoting about smoothness. A mod that is uniformly expensive
        /// costs frame rate, which players tolerate; one that is cheap on average and
        /// occasionally enormous costs a stutter, which they do not. A ratio near one is the
        /// first and a ratio in the hundreds is the second, and the two want completely
        /// different fixes.
        /// </summary>
        public double SpikeRatio
        {
            get
            {
                if (worst.Count == 0 || Frame.Calls == 0) return 0d;
                double mean = Frame.MeanMilliseconds;
                return mean <= 0d ? 0d : Frame.MaxMilliseconds / mean;
            }
        }

        public void Clear()
        {
            worst.Clear();
            FramesWithWork = 0;
            FramesOverBudget = 0;
            Reset();
        }
    }
}
