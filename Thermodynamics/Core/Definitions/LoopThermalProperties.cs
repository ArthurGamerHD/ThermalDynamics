using System;

namespace Thermodynamics.Core
{
    public class LoopThermalProperties
    {
        public float CoolantKilogramsPerCubicMetre = 33f;

        public float CoolantMassPerPipe = 0f;

/// <summary>MassPerPipe operation.</summary>
        public float MassPerPipe(float cellSizeMetres)
        {
            if (CoolantMassPerPipe > 0f) return CoolantMassPerPipe;

            if (cellSizeMetres <= 0f) cellSizeMetres = LargeGridCellMetres;

            return Math.Max(ThermalConstants.MinimumThermalMass,
                CoolantKilogramsPerCubicMetre * cellSizeMetres * cellSizeMetres * cellSizeMetres);
        }

        public float HeatTransferCoefficient = 1000f;

        public float SpecificHeat = 3400f;

        public float PipeContactMultiplier = 1f;

        public float RefillEquivalentKelvin = 100f;

/// <summary>RefillWattsAt operation.</summary>
        public float RefillWattsAt(float heatTimeScale)
        {
            if (heatTimeScale <= 0f) heatTimeScale = 1f;
            return RefillKilogramsPerSecond * SpecificHeat * RefillEquivalentKelvin / heatTimeScale;
        }

        public float RefillKilogramsPerSecond = 5f;

        public float SinkContactMultiplier = 1f;

        public float LargeGridFlowRate = 10f;

        public float SmallGridFlowRate = 10f;

/// <summary>FlowRateFor operation.</summary>
        public float FlowRateFor(float cellSizeMetres)
        {
            return cellSizeMetres < LargeGridCellThresholdMetres ? SmallGridFlowRate : LargeGridFlowRate;
        }

        public const float LargeGridCellThresholdMetres = 1f;

        public const float LargeGridCellMetres = 2.5f;

        public const float SmallGridCellMetres = 0.5f;

        public float StagnantTransferFraction = 0.16f;

/// <summary>Default operation.</summary>
        public static LoopThermalProperties Default()
        {
            return new LoopThermalProperties();
        }

/// <summary>Clamp operation.</summary>
        public LoopThermalProperties Clamp()
        {
            CoolantMassPerPipe = Math.Max(0f, CoolantMassPerPipe);
            CoolantKilogramsPerCubicMetre = Math.Max(0f, CoolantKilogramsPerCubicMetre);
            RefillEquivalentKelvin = Math.Max(0f, RefillEquivalentKelvin);
            RefillKilogramsPerSecond = Math.Max(0f, RefillKilogramsPerSecond);
            LargeGridFlowRate = Math.Max(0f, LargeGridFlowRate);
            SmallGridFlowRate = Math.Max(0f, SmallGridFlowRate);
            StagnantTransferFraction = Math.Max(0f, Math.Min(1f, StagnantTransferFraction));
            HeatTransferCoefficient = Math.Max(0f, HeatTransferCoefficient);
            SpecificHeat = Math.Max(ThermalConstants.MinimumThermalMass, SpecificHeat);
            PipeContactMultiplier = Math.Max(0f, PipeContactMultiplier);
            SinkContactMultiplier = Math.Max(0f, SinkContactMultiplier);
            return this;
        }

/// <summary>Clone operation.</summary>
        public LoopThermalProperties Clone()
        {
            return (LoopThermalProperties)MemberwiseClone();
        }
    }
}
