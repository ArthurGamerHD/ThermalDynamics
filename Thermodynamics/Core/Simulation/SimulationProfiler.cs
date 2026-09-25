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

        void Begin(SimulationPhase phase);

        void End(SimulationPhase phase);
    }
}
