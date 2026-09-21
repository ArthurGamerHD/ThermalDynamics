namespace Thermodynamics.Core
{
    public enum SimulationPhase
    {
        Topology = 0,

        RoomMapping = 1,

        Exposure = 2,

        Solver = 3,

        Count = 4
    }

    public interface ISimulationProfiler
    {
/// <summary>Begin operation.</summary>
        void Begin(SimulationPhase phase);
/// <summary>End operation.</summary>
        void End(SimulationPhase phase);
    }
}
