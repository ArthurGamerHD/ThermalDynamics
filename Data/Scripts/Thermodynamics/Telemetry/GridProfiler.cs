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

        // The after-step's own parts. It was one figure, and a field dump recorded a frame that
        // spent 108 of its 111 milliseconds inside it with nothing to say which of the six things
        // it does was responsible. Each is host-driven and bounded by something different — the
        // damage list, the block count, the compartment count, the external-cell count — so one
        // total cannot be read.

        /// <summary>Applying overheat damage, which is one engine call per critical block.</summary>
        public readonly TimingStat Damage = new TimingStat("    of which overheat damage");

        /// <summary>The rolling block-mass refresh.</summary>
        public readonly TimingStat MassSweep = new TimingStat("    of which mass sweep");

        /// <summary>
        /// The lost-room scan: a diagnostic that walks the grid's unmapped space with a game call
        /// per external cell. It runs only while telemetry collects or the room overlay is up,
        /// which makes it the most expensive thing in a dump that nothing outside a dump pays.
        /// </summary>
        public readonly TimingStat LostRooms = new TimingStat("    of which lost-room scan");

        /// <summary>The hottest-node read and the grid health check.</summary>
        public readonly TimingStat Health = new TimingStat("    of which hottest and health");

        /// <summary>Telemetry's own per-step sampling walk.</summary>
        public readonly TimingStat Sampling = new TimingStat("    of which telemetry sampling");

        /// <summary>
        /// Whether a tick is what is driving the stages right now.
        ///
        /// The one-off build runs the same three stages from the entity's own callback, outside any
        /// tick and outside the session frame. Recorded into the same rows it made them larger than
        /// the `grid simulation` row the table indents them under, and added stage milliseconds to
        /// frames that had not spent them. The build is timed as its own root instead — see
        /// <see cref="GridTelemetry.BuildTime"/>.
        /// </summary>
        public bool InTick;

        public void Begin(SimulationPhase phase)
        {
            if (!InTick) return;
            Stat(phase).Begin();
        }

        public void End(SimulationPhase phase)
        {
            if (!InTick) return;

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
