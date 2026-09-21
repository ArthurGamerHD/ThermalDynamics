using System.Diagnostics;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    public class StageTimings : ISimulationProfiler
    {
        private readonly Stopwatch[] running = new Stopwatch[(int)SimulationPhase.Count];
        private readonly double[] total = new double[(int)SimulationPhase.Count];
        private readonly double[] worst = new double[(int)SimulationPhase.Count];
        private readonly int[] calls = new int[(int)SimulationPhase.Count];

/// <summary>StageTimings operation.</summary>
        public StageTimings()
        {
            for (int i = 0; i < running.Length; i++) running[i] = new Stopwatch();
        }

/// <summary>Begin operation.</summary>
        public void Begin(SimulationPhase phase)
        {
            running[(int)phase].Restart();
        }

/// <summary>End operation.</summary>
        public void End(SimulationPhase phase)
        {
            Stopwatch watch = running[(int)phase];
            watch.Stop();

            double ms = watch.Elapsed.TotalMilliseconds;
            int i = (int)phase;

            total[i] += ms;
            calls[i]++;
            if (ms > worst[i]) worst[i] = ms;
        }

/// <summary>TotalMs operation.</summary>
        public double TotalMs(SimulationPhase phase)
        {
            return total[(int)phase];
        }

/// <summary>WorstMs operation.</summary>
        public double WorstMs(SimulationPhase phase)
        {
            return worst[(int)phase];
        }

/// <summary>Calls operation.</summary>
        public int Calls(SimulationPhase phase)
        {
            return calls[(int)phase];
        }

/// <summary>Describe operation.</summary>
        public string Describe(SimulationPhase phase)
        {
            return phase.ToString().ToLowerInvariant() + " "
/// <summary>TotalMs operation.</summary>
                + TotalMs(phase).ToString("n1") + " ms over " + Calls(phase)
/// <summary>calls operation.</summary>
                + " calls (worst " + WorstMs(phase).ToString("n1") + " ms)";
        }
    }
}
