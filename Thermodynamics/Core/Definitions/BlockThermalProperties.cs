using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    public class BlockThermalProperties
    {
        public bool ExcludeFromSimulation;

        public float Conductivity = 50f;

        public float SpecificHeat = 2f;

        public float Emissivity = 0.125f;

        public float SolarAbsorptivity = -1f;

        public float EffectiveSolarAbsorptivity
        {
            get { return SolarAbsorptivity < 0f ? Emissivity : SolarAbsorptivity; }
        }

        public float ExposedSurfaceMultiplier = 1f;

        public float ProducerWasteEnergy = 0.05f;

        public float ConsumerWasteEnergy = 0.05f;

        public float HeatSourceWatts;

        public float CriticalTemperature = 900f;

        public float OverheatDamagePerKelvin = 1f;


        public static BlockThermalProperties Default()
        {
            return new BlockThermalProperties();
        }


        public BlockThermalProperties Clamp()
        {
            Conductivity = Math.Max(0f, Conductivity);
            Emissivity = ThermalMath.Clamp01(Emissivity);

            if (SolarAbsorptivity > 1f) SolarAbsorptivity = 1f;
            SpecificHeat = Math.Max(ThermalConstants.MinimumThermalMass, SpecificHeat);
            ExposedSurfaceMultiplier = Math.Max(0f, ExposedSurfaceMultiplier);
            ProducerWasteEnergy = Math.Max(0f, ProducerWasteEnergy);
            ConsumerWasteEnergy = Math.Max(0f, ConsumerWasteEnergy);
            CriticalTemperature = Math.Max(0f, CriticalTemperature);
            OverheatDamagePerKelvin = Math.Max(0f, OverheatDamagePerKelvin);
            HeatSourceWatts = Math.Max(0f, HeatSourceWatts);
            return this;
        }


        public List<string> Validate()
        {

            List<string> problems = new List<string>();
            if (SpecificHeat <= 0f) problems.Add("SpecificHeat must be greater than zero.");
            if (Emissivity > 1f) problems.Add("Emissivity above 1 is not physical.");
            if (SolarAbsorptivity > 1f) problems.Add("SolarAbsorptivity above 1 is not physical.");
            if (ProducerWasteEnergy > 1f) problems.Add("ProducerWasteEnergy above 1 creates energy from nothing.");
            if (ConsumerWasteEnergy > 1f) problems.Add("ConsumerWasteEnergy above 1 creates energy from nothing.");
            return problems;
        }


        public BlockThermalProperties Clone()
        {
            return (BlockThermalProperties)MemberwiseClone();
        }
    }
}
