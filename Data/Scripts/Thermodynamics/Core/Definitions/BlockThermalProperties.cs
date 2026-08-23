using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The thermal description of a block type. Populated from the
    /// <c>ThermalBlockProperties</c> ModExtensions group, or built directly in tests.
    /// </summary>
    public class BlockThermalProperties
    {
        /// <summary>Exclude the block from the simulation entirely.</summary>
        public bool ExcludeFromSimulation;

        /// <summary>
        /// Thermal conductivity in real W/(m K) — mild steel 50, aluminium 237, copper 400.
        /// Multiplied by <see cref="ThermalConstants.ConductionScale"/> to set the game's pace,
        /// the same way <c>HeatTimeScale</c> paces real specific heats.
        /// </summary>
        public float Conductivity = 50f;

        /// <summary>Specific heat capacity in J/(kg K) game units.</summary>
        public float SpecificHeat = 2f;

        /// <summary>Grey-body emissivity, 0..1. What leaves the block as thermal radiation.</summary>
        public float Emissivity = 0.125f;

        /// <summary>
        /// Solar absorptivity, 0..1: the share of arriving radiation the surface takes in.
        /// **Negative means follow <see cref="Emissivity"/>**, which is what every block does until
        /// somebody authors otherwise, so nothing declared before this property existed changes.
        ///
        /// <para>
        /// It is a separate number because a surface is not obliged to absorb what it emits. A real
        /// spacecraft radiator is a *selective* surface — high emissivity in the thermal infrared,
        /// low absorptivity in the visible, so it sheds its own heat and takes little from the sun —
        /// and that is exactly the combination one number cannot express. Making it two turns
        /// surface finish into a thing a builder chooses rather than a constant.
        /// See definitions.md, Emissivity and absorptivity are two numbers.
        /// </para>
        ///
        /// <para>
        /// Read through <see cref="EffectiveSolarAbsorptivity"/> rather than directly: the sentinel
        /// is resolved in one place so a properties object built in a test without going through
        /// <see cref="Clamp"/> cannot absorb a negative amount of sunlight.
        /// </para>
        /// </summary>
        public float SolarAbsorptivity = -1f;

        /// <summary>
        /// What the solver actually absorbs with: the authored absorptivity, or the emissivity
        /// where none was authored.
        /// </summary>
        public float EffectiveSolarAbsorptivity
        {
            get { return SolarAbsorptivity < 0f ? Emissivity : SolarAbsorptivity; }
        }

        /// <summary>Multiplier on the geometric face area, for finned or folded surfaces.</summary>
        public float ExposedSurfaceMultiplier = 1f;

        /// <summary>Fraction of generated power that becomes heat.</summary>
        public float ProducerWasteEnergy = 0.05f;

        /// <summary>Fraction of consumed power that becomes heat.</summary>
        public float ConsumerWasteEnergy = 0.05f;

        /// <summary>
        /// Watts this block emits regardless of power, thrust or anything else it is doing — decay
        /// heat, a forge, a wreck still burning. Watts rather than a fraction, because there is nothing
        /// to take a fraction of, and added to the waste-heat terms rather than replacing them, so a
        /// block may both draw power and smoulder. See definitions.md.
        /// </summary>
        public float HeatSourceWatts;

        /// <summary>Kelvin above which the block takes damage.</summary>
        public float CriticalTemperature = 900f;

        /// <summary>Damage per Kelvin of overshoot, per second.</summary>
        public float OverheatDamagePerKelvin = 1f;

        public static BlockThermalProperties Default()
        {
            return new BlockThermalProperties();
        }

        /// <summary>
        /// Applies the documented ranges. Called after reading from a definition so that a
        /// malformed value cannot destabilise the solver.
        /// </summary>
        public BlockThermalProperties Clamp()
        {
            Conductivity = Math.Max(0f, Conductivity);
            Emissivity = Clamp01(Emissivity);

            // Only the upper bound, because a negative value is the sentinel for "follow the
            // emissivity" rather than a mistake to be corrected to zero.
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
        /// Problems a mod author would want to hear about. Unlike <see cref="Clamp"/> this does
        /// not modify anything.
        /// </summary>
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

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
