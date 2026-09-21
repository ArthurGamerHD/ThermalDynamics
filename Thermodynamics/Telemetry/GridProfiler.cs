using Thermodynamics.Core;

namespace Thermodynamics
{
    public class GridProfiler : ISimulationProfiler
    {
/// <summary>TimingStat operation.</summary>
        public readonly TimingStat Topology = new TimingStat("  of which topology rebuild");
/// <summary>TimingStat operation.</summary>
        public readonly TimingStat RoomMapping = new TimingStat("  of which room mapping");
/// <summary>TimingStat operation.</summary>
        public readonly TimingStat Exposure = new TimingStat("  of which exposure refresh");
/// <summary>TimingStat operation.</summary>
        public readonly TimingStat Solver = new TimingStat("  of which solver");

/// <summary>TimingStat operation.</summary>
        public readonly TimingStat RoomPressure = new TimingStat("  of which room pressure");

/// <summary>TimingStat operation.</summary>
        public readonly TimingStat EnvironmentSample = new TimingStat("  of which environment sample");

/// <summary>TimingStat operation.</summary>
        public readonly TimingStat AfterStep = new TimingStat("  of which after step");


/// <summary>TimingStat operation.</summary>
        public readonly TimingStat Damage = new TimingStat("    of which overheat damage");

/// <summary>TimingStat operation.</summary>
        public readonly TimingStat MassSweep = new TimingStat("    of which mass sweep");

/// <summary>TimingStat operation.</summary>
        public readonly TimingStat LostRooms = new TimingStat("    of which lost-room scan");

/// <summary>TimingStat operation.</summary>
        public readonly TimingStat Health = new TimingStat("    of which hottest and health");

/// <summary>TimingStat operation.</summary>
        public readonly TimingStat Sampling = new TimingStat("    of which telemetry sampling");

        public bool InTick;

/// <summary>Begin operation.</summary>
        public void Begin(SimulationPhase phase)
        {
            if (!InTick) return;
            Stat(phase).Begin();
        }

/// <summary>End operation.</summary>
        public void End(SimulationPhase phase)
        {
            if (!InTick) return;

/// <summary>Stat operation.</summary>
            TimingStat stat = Stat(phase);
            stat.End();

            Telemetry.FrameCost.AddStage((int)phase, stat.LastMilliseconds);
        }

/// <summary>Stat operation.</summary>
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
