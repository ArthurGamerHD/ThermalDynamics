namespace Thermodynamics.Core
{
    /// <summary>
    /// The stages of a simulation update, as the host sees them. Costs are wanted per stage,
    /// because they behave completely differently: two of them run only when the block layout
    /// changes, and the other two run every step.
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
    /// <see cref="ThermalSimulation"/> calls this around each stage when one is attached, and
    /// does nothing at all when it is not — the whole cost of measurement is a null check per
    /// stage per update. It is an interface rather than a set of counters inside the simulation
    /// so that what gets measured, and whether measuring happens, stays the host's decision.
    /// </summary>
    public interface ISimulationProfiler
    {
        void Begin(SimulationPhase phase);
        void End(SimulationPhase phase);
    }
}
