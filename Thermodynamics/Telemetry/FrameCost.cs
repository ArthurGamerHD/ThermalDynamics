using System;
using System.Collections.Generic;
using System.Text;

namespace Thermodynamics
{
    public struct FrameSample
    {
        public long Frame;
        public double SessionSeconds;

        public double TotalMs;

        public double TopologyMs;
        public double RoomMappingMs;
        public double ExposureMs;
        public double SolverMs;

        public double SampleMs;
        public double AfterStepMs;

        public int Grids;
        public double WorstGridMs;
        public string WorstGrid;
        public int WorstGridBlocks;

        public long TopologyNodeVisits;
        public long ExposureNodeVisits;
        public long RoomCellsVisited;

        public double UnattributedMs
        {
            get
            {
                return TotalMs - TopologyMs - RoomMappingMs - ExposureMs - SolverMs
                    - SampleMs - AfterStepMs;
            }
        }


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
              .Append(", solver ").Append(SolverMs.ToString("n2"))
              .Append(", sample ").Append(SampleMs.ToString("n2"))
              .Append(", after step ").Append(AfterStepMs.ToString("n2"))
              .Append(", unattributed ").Append(UnattributedMs.ToString("n2")).Append(" ms");

            sb.Append("\n      touched ").Append(TopologyNodeVisits.ToString("n0"))
              .Append(" nodes for topology, ").Append(ExposureNodeVisits.ToString("n0"))
              .Append(" for exposure, ").Append(RoomCellsVisited.ToString("n0")).Append(" cells flooded");

            return sb.ToString();
        }
    }

    public class FrameCostTracker
    {
        public const int Keep = 16;

        public double HitchThresholdMs = 4d;


        public readonly TimingStat Frame = new TimingStat("mod, all grids, per frame");


        private readonly List<FrameSample> worst = new List<FrameSample>();

        public long FramesWithWork;

        public long FramesOverBudget;

        public const double FrameBudgetMs = 1000d / 60d;


        private double total;
        private double topology;
        private double roomMapping;
        private double exposure;
        private double solver;
        private double sample;
        private double afterStep;
        private int grids;
        private double worstGridMs;
        private string worstGrid;
        private int worstGridBlocks;
        private long topologyVisits;
        private long exposureVisits;
        private long roomCells;


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


        public void AddSample(double milliseconds)
        {
            if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds)) return;
            sample += milliseconds;
        }


        public void AddAfterStep(double milliseconds)
        {
            if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds)) return;
            afterStep += milliseconds;
        }


        public void AddWork(long topologyNodes, long exposureNodes, long cellsFlooded)
        {
            topologyVisits += topologyNodes;
            exposureVisits += exposureNodes;
            roomCells += cellsFlooded;
        }


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
            sample.SampleMs = this.sample;
            sample.AfterStepMs = afterStep;
            sample.Grids = grids;
            sample.WorstGridMs = worstGridMs;
            sample.WorstGrid = worstGrid;
            sample.WorstGridBlocks = worstGridBlocks;
            sample.TopologyNodeVisits = topologyVisits;
            sample.ExposureNodeVisits = exposureVisits;
            sample.RoomCellsVisited = roomCells;
            return sample;
        }


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
            sample = 0d;
            afterStep = 0d;
            grids = 0;
            worstGridMs = 0d;
            worstGrid = null;
            worstGridBlocks = 0;
            topologyVisits = 0;
            exposureVisits = 0;
            roomCells = 0;
        }

        public IList<FrameSample> Worst
        {
            get { return worst; }
        }

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
