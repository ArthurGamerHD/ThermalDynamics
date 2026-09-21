using System;

namespace Thermodynamics.Core
{
    public static class ThermalConstants
    {
        public const float StefanBoltzmann = 0.00000005670374419f;

        public const float MegawattsToWatts = 1000000f;
        public const float WattsToMegawatts = 1f / MegawattsToWatts;

        public const float KelvinOffset = 273.15f;

        public const float ConductionScale = 9.6f;

        public const float AirSpecificHeat = 1005f;

        public const float MinimumTemperature = 0f;

        public const float MinimumThermalMass = 0.001f;

        public const float UndergroundContactDepth = 5f;

/// <summary>KelvinToCelsius operation.</summary>
        public static float KelvinToCelsius(float kelvin)
        {
            return kelvin - KelvinOffset;
        }

/// <summary>CelsiusToKelvin operation.</summary>
        public static float CelsiusToKelvin(float celsius)
        {
            return celsius + KelvinOffset;
        }
    }
}
