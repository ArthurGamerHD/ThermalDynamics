using System.Diagnostics;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// A stopwatch behind <see cref="ISimulationProfiler"/>, so a scenario can report the same
    /// per-stage split the in-game telemetry reports: topology, room mapping, exposure, solver.
    ///
    /// The stress test reports showed the interesting part is not the mean but the one-shot
    /// stages — a capital ship rebuilds topology and rooms once, and that single call is the
    /// frame stall. Keeping worst-call alongside the total is the whole point.
    /// </summary>
    public class StageTimings : ISimulationProfiler
    {
        private readonly Stopwatch[] running = new Stopwatch[(int)SimulationPhase.Count];
        private readonly double[] total = new double[(int)SimulationPhase.Count];
        private readonly double[] worst = new double[(int)SimulationPhase.Count];
        private readonly int[] calls = new int[(int)SimulationPhase.Count];

        public StageTimings()
        {
            for (int i = 0; i < running.Length; i++) running[i] = new Stopwatch();
        }

        public void Begin(SimulationPhase phase)
        {
            running[(int)phase].Restart();
        }

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

        public double TotalMs(SimulationPhase phase)
        {
            return total[(int)phase];
        }

        /// <summary>The longest single call — the frame stall, if there is one.</summary>
        public double WorstMs(SimulationPhase phase)
        {
            return worst[(int)phase];
        }

        public int Calls(SimulationPhase phase)
        {
            return calls[(int)phase];
        }

        public string Describe(SimulationPhase phase)
        {
            return phase.ToString().ToLowerInvariant() + " "
                + TotalMs(phase).ToString("n1") + " ms over " + Calls(phase)
                + " calls (worst " + WorstMs(phase).ToString("n1") + " ms)";
        }
    }
}
