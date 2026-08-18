using Thermodynamics.Core;

namespace Thermodynamics
{
    /// <summary>
    /// Stage timing for one grid, attached to its simulation only while telemetry is collecting.
    ///
    /// Splitting the update by stage is what makes the cost numbers actionable: topology and
    /// room mapping run on block changes and are the expensive pair, while the solver runs every
    /// step and is usually not the problem. A single "grid simulation" figure hides which is
    /// which, and that was the main gap in the old report.
    /// </summary>
    public class GridProfiler : ISimulationProfiler
    {
        public readonly TimingStat Topology = new TimingStat("  of which topology rebuild");
        public readonly TimingStat RoomMapping = new TimingStat("  of which room mapping");
        public readonly TimingStat Exposure = new TimingStat("  of which exposure refresh");
        public readonly TimingStat Solver = new TimingStat("  of which solver");

        public void Begin(SimulationPhase phase)
        {
            Stat(phase).Begin();
        }

        public void End(SimulationPhase phase)
        {
            TimingStat stat = Stat(phase);
            stat.End();

            // Also into the frame's total, so a stall can be split by stage across every grid
            // that ran on it rather than only within the grid that happened to be worst.
            Telemetry.FrameCost.AddStage((int)phase, stat.LastMilliseconds);
        }

        public TimingStat Stat(SimulationPhase phase)
        {
            switch (phase)
            {
                case SimulationPhase.Topology: return Topology;
                case SimulationPhase.RoomMapping: return RoomMapping;
                case SimulationPhase.Exposure: return Exposure;
                default: return Solver;
            }
        }
    }
}
