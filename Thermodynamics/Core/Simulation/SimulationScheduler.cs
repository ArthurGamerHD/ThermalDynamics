using System;

namespace Thermodynamics.Core
{
    public class SimulationScheduler
    {
/// <summary>SimulationScheduler operation.</summary>
        public SimulationScheduler(ThermalSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
        }

        public long StepsRun { get; private set; }

/// <summary>RoomMappingBudget operation.</summary>
        public static int RoomMappingBudget(int gridCellVolume)
        {
            int budget = gridCellVolume / 60;
            if (budget < 64) budget = 64;
            if (budget > 4096) budget = 4096;
            return budget;
        }

/// <summary>ExposureBudget operation.</summary>
        public static int ExposureBudget(int nodeCount)
        {
            int budget = nodeCount / 40;
            if (budget < 256) budget = 256;
            if (budget > 4096) budget = 4096;
            return budget;
        }

/// <summary>ShapeNormalBudget operation.</summary>
        public static int ShapeNormalBudget(int nodeCount)
        {
            int budget = nodeCount / 280;
            if (budget < 32) budget = 32;
            if (budget > 512) budget = 512;
            return budget;
        }

/// <summary>SweepSlice operation.</summary>
        public static int SweepSlice(int count, int steps, int interval, int cap)
        {
            if (count <= 0 || steps <= 0 || cap <= 0) return 0;
            if (interval < 1) interval = 1;

            long share = ((long)count * steps) / interval;

            if (share < 1) share = 1;
            if (share > cap) share = cap;
            if (share > count) share = count;

            return (int)share;
        }

/// <summary>CountStep operation.</summary>
        public void CountStep()
        {
            StepsRun++;
        }

/// <summary>Reset operation.</summary>
        public void Reset()
        {
            StepsRun = 0;
        }
    }
}
