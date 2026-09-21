using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    public static class LoopBefore
    {
        public const float FlatKilogramsPerPipe = 50f;

        public const float Coefficient = 160f;

        public const float StagnantFraction = 1f;

/// <summary>For operation.</summary>
        public static LoopThermalProperties For(float gridSize)
        {
            LoopThermalProperties properties = LoopThermalProperties.Default();

            properties.CoolantMassPerPipe = FlatKilogramsPerPipe;
            properties.HeatTransferCoefficient = Coefficient;
            properties.StagnantTransferFraction = StagnantFraction;

            return properties.Clamp();
        }

/// <summary>Applies the .</summary>
        public static void Apply(ShipAssembly assembly)
        {
            if (assembly == null) return;

            for (int i = 0; i < assembly.Simulations.Count; i++)
            {
                ThermalSimulation simulation = assembly.Simulations[i];
/// <summary>For operation.</summary>
                simulation.LoopProperties = For(simulation.Grid.GridSize);
                simulation.RebuildAll();
            }
        }
    }
}
