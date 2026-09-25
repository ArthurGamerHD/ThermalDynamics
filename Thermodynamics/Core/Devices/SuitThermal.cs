using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Result of a single suit thermal simulation step.
    /// Contains the updated interior temperature, regulated heat flow, and damage calculations.
    /// </summary>
    public struct SuitStepResult
    {
        /// <summary>
        /// Interior temperature after this step in Kelvin.
        /// Updated based on heat load, regulation, and suit thermal mass.
        /// </summary>
        public float InteriorKelvin;

        /// <summary>
        /// Regulated heat flow in watts applied during this step.
        /// Positive = cooling (heat removed), Negative = heating (heat added).
        /// </summary>
        public float RegulatedWatts;

        /// <summary>
        /// Damage accumulated during this step in suit integrity units.
        /// Calculated based on time above critical temperature threshold.
        /// </summary>
        public float Damage;

        /// <summary>
        /// True if the suit regulation was overwhelmed during this step.
        /// Occurs when the heat load exceeds the suit's cooling/heating capacity.
        /// </summary>
        public bool Overwhelmed;
    }

    /// <summary>
    /// Suit thermal system simulation.
    /// Models heat transfer through the suit, regulation via cooling systems,
    /// and damage accumulation when temperatures exceed safe limits.
    /// </summary>
    public static class SuitThermal
    {
        /// <summary>
        /// Comfort temperature in Kelvin (37°C = 310.15K).
        /// The target interior temperature that the suit regulation maintains.
        /// </summary>
        public const float ComfortKelvin = 310.15f;

        /// <summary>
        /// Conductance multiplier when helmet is open.
        /// Open helmets allow much more heat transfer (10x default) because air can circulate freely.
        /// </summary>
        public const float OpenHelmetConductanceFactor = 10f;


        /// <summary>
        /// Simulates one suit thermal step.
        /// Calculates heat load from environment, applies regulation if power is available,
        /// and updates interior temperature and damage.
        /// </summary>
        /// <param name="settings">Thermal settings including suit properties.</param>
        /// <param name="interiorKelvin">Current interior suit temperature in Kelvin.</param>
        /// <param name="environmentKelvin">External environment temperature in Kelvin.</param>
        /// <param name="helmetOpen">True if helmet is open to environment.</param>
        /// <param name="energyAvailable">True if suit has power for active regulation.</param>
        /// <param name="seconds">Duration of this step in seconds.</param>
        /// <returns>SuitStepResult with updated interior temperature and other metrics.</returns>
        /// <remarks>
        /// The simulation follows these steps:
        /// 1. Calculate heat load: conductance * (environment - interior)
        /// 2. If regulation is available, calculate desired cooling/heating to return to ComfortKelvin
        /// 3. Clamp regulation to SuitCoolingWatts limit
        /// 4. Update interior temperature: interior + (load - regulation) * seconds / capacity
        /// 5. Calculate damage if above SuitCriticalTemperature
        /// 
        /// Note: Regulation power is used to actively cool/heating, but heat also transfers
        /// passively through the suit's conductance regardless of regulation status.
        /// </remarks>
        public static SuitStepResult Step(ThermalSettings settings, float interiorKelvin,
            float environmentKelvin, bool helmetOpen, bool energyAvailable, float seconds)
        {
            SuitStepResult result = new SuitStepResult();
            result.InteriorKelvin = interiorKelvin;

            if (settings == null || seconds <= 0f) return result;
            if (!settings.EnableSuitDamage) return result;

            // Scale heat capacity by heat time scale (slower heat transfer = more effective cooling)
            float scale = settings.HeatTimeScale > 0f ? settings.HeatTimeScale : 1f;
            float capacity = settings.SuitHeatCapacity / scale;
            if (capacity <= 0f) capacity = ThermalConstants.MinimumThermalMass;

            // Calculate conductance (heat transfer rate per degree temperature difference)
            float conductance = settings.SuitConductance;
            if (helmetOpen) conductance *= OpenHelmetConductanceFactor;

            // Passive heat load from environment
            float load = conductance * (environmentKelvin - interiorKelvin);

            float regulated = 0f;

            // Apply active regulation if power is available
            if (energyAvailable && settings.SuitCoolingWatts > 0f)
            {
                // Desired regulation to return to comfort temperature
                // This accounts for both passive load and active cooling to reach target
                float wanted = load + (capacity * (interiorKelvin - ComfortKelvin) / seconds);
                float limit = settings.SuitCoolingWatts;

                // Clamp to maximum regulation capacity
                regulated = wanted > limit ? limit : (wanted < -limit ? -limit : wanted);
            }

            // Check if regulation was insufficient
            result.Overwhelmed = load > 0f && regulated < load;

            // Update interior temperature
            // New temp = current + (net heat flow) * seconds / capacity
            // Net flow = passive load - active regulation
            float updated = interiorKelvin + ((load - regulated) * seconds / capacity);
            if (updated < ThermalConstants.MinimumTemperature)
            {
                updated = ThermalConstants.MinimumTemperature;
            }

            result.InteriorKelvin = updated;
            result.RegulatedWatts = regulated;

            // Calculate damage if above critical threshold
            float over = updated - settings.SuitCriticalTemperature;
            if (over > 0f) result.Damage = over * settings.SuitDamagePerKelvin * seconds;

            return result;
        }


        /// <summary>
        /// Calculates the highest environment temperature that can be safely survived.
        /// Based on suit conductance, cooling capacity, and comfort temperature.
        /// </summary>
        /// <param name="settings">Thermal settings including suit properties.</param>
        /// <param name="helmetOpen">True if helmet is open (increases conductance).</param>
        /// <returns>Safe environment temperature in Kelvin, or infinity if suit cannot fail.</returns>
        /// <remarks>
        /// The calculation assumes equilibrium where:
        ///   - Heat load from environment = Cooling capacity
        ///   - Interior temperature = Comfort temperature
        /// 
        /// Formula: Survivable = Comfort + (CoolingWatts / Conductance)
        /// With open helmet, conductance is multiplied by OpenHelmetConductanceFactor.
        /// 
        /// This represents the maximum external temperature the suit can handle
        /// while maintaining the occupant at comfort temperature with full cooling capacity.
        /// </remarks>
        public static float SurvivableKelvin(ThermalSettings settings, bool helmetOpen = false)
        {
            if (settings == null || settings.SuitConductance <= 0f) return float.PositiveInfinity;

            float conductance = settings.SuitConductance;
            if (helmetOpen) conductance *= OpenHelmetConductanceFactor;

            // Maximum environment temperature where cooling can maintain comfort
            return ComfortKelvin + (settings.SuitCoolingWatts / conductance);
        }
    }
}
