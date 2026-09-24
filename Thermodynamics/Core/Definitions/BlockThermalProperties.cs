using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Thermal properties for a block that affect how it exchanges heat with its environment.
    /// These properties determine conduction, radiation, solar absorption, and thermal limits.
    /// </summary>
    public class BlockThermalProperties
    {
        /// <summary>
        /// If true, this block is excluded from all thermal simulation calculations.
        /// Used for blocks that should not participate in heat transfer.
        /// </summary>
        public bool ExcludeFromSimulation;

        /// <summary>
        /// Thermal conductivity in W/m·K (Watts per meter per Kelvin).
        /// Higher values mean the block conducts heat more easily to adjacent blocks.
        /// Default: 50 W/m·K (similar to steel).
        /// </summary>
        public float Conductivity = 50f;

        /// <summary>
        /// Specific heat capacity in J/kg·K (Joules per kilogram per Kelvin).
        /// Determines how much energy is required to change the block's temperature.
        /// Higher values mean the block heats up and cools down more slowly.
        /// Default: 2 J/kg·K.
        /// </summary>
        public float SpecificHeat = 2f;

        /// <summary>
        /// Thermal emissivity (dimensionless, 0.0 to 1.0).
        /// Efficiency of emitting thermal radiation. High emissivity = better radiator.
        /// Also used as fallback for solar absorptivity when SolarAbsorptivity is negative.
        /// Default: 0.125 (low, typical for polished metals).
        /// </summary>
        public float Emissivity = 0.125f;

        /// <summary>
        /// Solar absorptivity (dimensionless, 0.0 to 1.0).
        /// Fraction of incident solar radiation that the block absorbs.
        /// When set to -1 (default), EffectiveSolarAbsorptivity returns Emissivity instead.
        /// </summary>
        public float SolarAbsorptivity = -1f;

        /// <summary>
        /// Gets the effective solar absorptivity for this block.
        /// If SolarAbsorptivity is negative, returns Emissivity (Kirchhoff's law approximation).
        /// Otherwise returns SolarAbsorptivity directly.
        /// </summary>
        public float EffectiveSolarAbsorptivity
        {
            get { return SolarAbsorptivity < 0f ? Emissivity : SolarAbsorptivity; }
        }

        /// <summary>
        /// Multiplier for exposed surface area calculations.
        /// Used to scale heat transfer based on how much of the block's surface is exposed.
        /// Default: 1.0 (full exposure).
        /// </summary>
        public float ExposedSurfaceMultiplier = 1f;

        /// <summary>
        /// Fraction of producer (generator) energy that becomes waste heat.
        /// For example, a solar cell producing 100W with 0.05 waste fraction generates 5W waste heat.
        /// Default: 0.05 (5%).
        /// </summary>
        public float ProducerWasteEnergy = 0.05f;

        /// <summary>
        /// Fraction of consumer (powered device) energy that becomes waste heat.
        /// Energy not used for the device's primary function is dissipated as heat.
        /// Default: 0.05 (5%).
        /// </summary>
        public float ConsumerWasteEnergy = 0.05f;

        /// <summary>
        /// Fixed heat generation in watts from this block.
        /// Used for heat sources like reactors or heaters.
        /// Default: 0 (no heat generation).
        /// </summary>
        public float HeatSourceWatts;

        /// <summary>
        /// Critical temperature in Kelvin at which the block begins to take damage.
        /// This is derived from the ServiceLimit of the cladding material.
        /// Default: 900K (626.85°C).
        /// </summary>
        public float CriticalTemperature = 900f;

        /// <summary>
        /// Damage per Kelvin above CriticalTemperature per simulation second.
        /// Determines how quickly the block degrades when overheated.
        /// Default: 1.0 (1 unit of damage per Kelvin per second).
        /// </summary>
        public float OverheatDamagePerKelvin = 1f;


        /// <summary>
        /// Creates a new BlockThermalProperties with default values.
        /// All values are initialized to their defaults in the field declarations.
        /// </summary>
        public static BlockThermalProperties Default()
        {
            return new BlockThermalProperties();
        }


        /// <summary>
        /// Clamps all property values to valid physical ranges.
        /// Ensures no negative values for physical properties and values within expected bounds.
        /// </summary>
        /// <returns>The same instance with clamped values.</returns>
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


        /// <summary>
        /// Validates this BlockThermalProperties for physical consistency.
        /// Checks for impossible values that would violate thermodynamic principles.
        /// </summary>
        /// <returns>A list of validation problems, or empty if all values are valid.</returns>
        public List<string> Validate()
        {
            List<string> problems = new List<string>();
            // Specific heat must be positive for physical materials
            if (SpecificHeat <= 0f) problems.Add("SpecificHeat must be greater than zero.");
            // Emissivity above 1 violates conservation of energy
            if (Emissivity > 1f) problems.Add("Emissivity above 1 is not physical.");
            // Solar absorptivity above 1 would absorb more energy than incident
            if (SolarAbsorptivity > 1f) problems.Add("SolarAbsorptivity above 1 is not physical.");
            // Waste energy above 1 would create more waste than input energy
            if (ProducerWasteEnergy > 1f) problems.Add("ProducerWasteEnergy above 1 creates energy from nothing.");
            if (ConsumerWasteEnergy > 1f) problems.Add("ConsumerWasteEnergy above 1 creates energy from nothing.");
            return problems;
        }


        /// <summary>
        /// Creates a shallow copy of this BlockThermalProperties instance.
        /// </summary>
        /// <returns>A new instance with the same property values.</returns>
        public BlockThermalProperties Clone()
        {
            return (BlockThermalProperties)MemberwiseClone();
        }
    }
}
