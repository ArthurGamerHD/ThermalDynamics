namespace Thermodynamics.Core
{
    /// <summary>
    /// The stages of a simulation update, as the host sees them. Timed separately because their cost
    /// profiles differ: two run only when the block layout changes and two run every step.
    /// </summary>
    public enum SimulationPhase
    {
        /// <summary>Conduction graph and coolant loops, after a block change.</summary>
        Topology = 0,

        /// <summary>One budgeted slice of the room flood fill.</summary>
        RoomMapping = 1,

        /// <summary>Recomputing every node's exposed faces after a mapping pass.</summary>
        Exposure = 2,

        /// <summary>The solver itself.</summary>
        Solver = 3,

        Count = 4
    }

    /// <summary>
    /// Optional instrumentation hook.
    ///
    /// <see cref="ThermalSimulation"/> calls this around each stage when one is attached, and does
    /// nothing when it is not, so uninstrumented measurement costs one null check per stage per
    /// update. An interface rather than counters inside the simulation, so what is measured and
    /// whether measurement happens are the host's decisions.
    /// </summary>
    public interface ISimulationProfiler
    {
        void Begin(SimulationPhase phase);
        void End(SimulationPhase phase);
    }
}
