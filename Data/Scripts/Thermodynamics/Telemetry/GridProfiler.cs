using Thermodynamics.Core;

namespace Thermodynamics
{
    /// <summary>
    /// Stage timing for one grid, attached to its simulation only while telemetry is collecting.
    ///
    /// Splitting the update by stage separates the two cost profiles: topology and room mapping run
    /// on block changes and are the expensive pair, while the solver runs every step and is usually
    /// not the bottleneck. A single grid-simulation figure cannot distinguish them.
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

            // Also added to the frame's total, so a stall can be split by stage across every grid
            // that ran on it rather than only within the worst one.
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
