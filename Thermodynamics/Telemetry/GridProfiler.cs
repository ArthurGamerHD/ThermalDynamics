using Thermodynamics.Core;

namespace Thermodynamics
{
    public class GridProfiler : ISimulationProfiler
    {

        public readonly TimingStat Topology = new TimingStat("  of which topology rebuild");

        public readonly TimingStat RoomMapping = new TimingStat("  of which room mapping");

        public readonly TimingStat Exposure = new TimingStat("  of which exposure refresh");

        public readonly TimingStat Solver = new TimingStat("  of which solver");


        public readonly TimingStat RoomPressure = new TimingStat("  of which room pressure");


        public readonly TimingStat EnvironmentSample = new TimingStat("  of which environment sample");


        public readonly TimingStat AfterStep = new TimingStat("  of which after step");



        public readonly TimingStat Damage = new TimingStat("    of which overheat damage");


        public readonly TimingStat MassSweep = new TimingStat("    of which mass sweep");


        public readonly TimingStat LostRooms = new TimingStat("    of which lost-room scan");


        public readonly TimingStat Health = new TimingStat("    of which hottest and health");


        public readonly TimingStat Sampling = new TimingStat("    of which telemetry sampling");

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
