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

        /// <summary>
        /// The room pressure sweep, which the host drives rather than the simulation, so it is
        /// timed by an explicit Begin/End rather than through <see cref="SimulationPhase"/>.
        ///
        /// It was unattributed until it was measured: it sits inside a grid's update with two game
        /// API calls per compartment and a walk over every air vent, and neither is bounded by
        /// anything the step budget can see. Bounded by compartment count rather than block count,
        /// so it is small on a ship and unmeasured on a station.
        /// </summary>
        public readonly TimingStat RoomPressure = new TimingStat("  of which room pressure");

        /// <summary>
        /// Reading the world a step is about to run against: planet, air, wind, weather, sun, and
        /// the solar raycast nested inside it.
        ///
        /// Host-driven for the same reason the pressure sweep is — it happens around the simulation
        /// rather than inside it — and it is taken once a step rather than once a frame.
        /// </summary>
        public readonly TimingStat EnvironmentSample = new TimingStat("  of which environment sample");

        /// <summary>
        /// Everything that reads a step's output: overheat damage, threshold crossings, heat pump
        /// demand, the mass sweep, the pressure sweep, the hottest-node scan and the health check.
        ///
        /// Deliberately off the stepping path, which is why none of it was timed. The consequence
        /// was that a worst frame in a field dump could attribute five per cent of itself and leave
        /// the rest unexplained.
        /// </summary>
        public readonly TimingStat AfterStep = new TimingStat("  of which after step");

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
