using System;

namespace Thermodynamics.Core
{
    public struct SuitStepResult
    {
        public float InteriorKelvin;

        public float RegulatedWatts;

        public float Damage;

        public bool Overwhelmed;
    }

    public static class SuitThermal
    {
        public const float ComfortKelvin = 310.15f;

        public const float OpenHelmetConductanceFactor = 10f;


        public static SuitStepResult Step(ThermalSettings settings, float interiorKelvin,
            float environmentKelvin, bool helmetOpen, bool energyAvailable, float seconds)
        {

            SuitStepResult result = new SuitStepResult();
            result.InteriorKelvin = interiorKelvin;

            if (settings == null || seconds <= 0f) return result;

            if (!settings.EnableSuitDamage) return result;

            float scale = settings.HeatTimeScale > 0f ? settings.HeatTimeScale : 1f;
            float capacity = settings.SuitHeatCapacity / scale;
            if (capacity <= 0f) capacity = ThermalConstants.MinimumThermalMass;

            float conductance = settings.SuitConductance;
            if (helmetOpen) conductance *= OpenHelmetConductanceFactor;

            float load = conductance * (environmentKelvin - interiorKelvin);

            float regulated = 0f;

            if (energyAvailable && settings.SuitCoolingWatts > 0f)
            {
                float wanted = load + (capacity * (interiorKelvin - ComfortKelvin) / seconds);
                float limit = settings.SuitCoolingWatts;
                regulated = wanted > limit ? limit : (wanted < -limit ? -limit : wanted);
            }

            result.Overwhelmed = load > 0f && regulated < load;

            float updated = interiorKelvin + ((load - regulated) * seconds / capacity);
            if (updated < ThermalConstants.MinimumTemperature)
            {
                updated = ThermalConstants.MinimumTemperature;
            }

            result.InteriorKelvin = updated;
            result.RegulatedWatts = regulated;

            float over = updated - settings.SuitCriticalTemperature;
            if (over > 0f) result.Damage = over * settings.SuitDamagePerKelvin * seconds;

            return result;
        }


        public static float SurvivableKelvin(ThermalSettings settings, bool helmetOpen = false)
        {
            if (settings == null || settings.SuitConductance <= 0f) return float.PositiveInfinity;

            float conductance = settings.SuitConductance;
            if (helmetOpen) conductance *= OpenHelmetConductanceFactor;

            return ComfortKelvin + (settings.SuitCoolingWatts / conductance);
        }
    }
}
