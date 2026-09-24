using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Physical constants and conversion factors for thermal simulation.
    /// Provides fundamental constants, unit conversions, and simulation parameters.
    /// </summary>
    public static class ThermalConstants
    {
        /// <summary>
        /// Stefan-Boltzmann constant in W/m²·K⁴.
        /// Used for radiative heat transfer calculations: Power = εσAT⁴
        /// Value: 5.670374419 × 10⁻⁸ W/m²·K⁴
        /// </summary>
        public const float StefanBoltzmann = 0.00000005670374419f;

        /// <summary>
        /// Conversion factor from megawatts to watts (1 MW = 1,000,000 W).
        /// Used for power unit conversions in simulation output.
        /// </summary>
        public const float MegawattsToWatts = 1000000f;

        /// <summary>
        /// Conversion factor from watts to megawatts (1 W = 0.000001 MW).
        /// Inverse of MegawattsToWatts for unit conversion.
        /// </summary>
        public const float WattsToMegawatts = 1f / MegawattsToWatts;

        /// <summary>
        /// Offset in Kelvin to convert to Celsius (273.15 K = 0 °C).
        /// Used for display temperatures in Celsius instead of Kelvin.
        /// </summary>
        public const float KelvinOffset = 273.15f;

        /// <summary>
        /// Scaling factor for conduction calculations.
        /// Adjusts heat transfer rate for simulation stability and performance.
        /// Default: 9.6 (empirically tuned for Space Engineers block sizes).
        /// </summary>
        public const float ConductionScale = 9.6f;

        /// <summary>
        /// Specific heat capacity of air in J/kg·K at constant pressure.
        /// Value for dry air at room temperature: ~1005 J/kg·K
        /// Used for room air thermal mass calculations.
        /// </summary>
        public const float AirSpecificHeat = 1005f;

        /// <summary>
        /// Absolute zero temperature in Kelvin.
        /// Lower bound for all temperature calculations.
        /// </summary>
        public const float MinimumTemperature = 0f;

        /// <summary>
        /// Minimum allowable thermal mass in J/K.
        /// Prevents division by zero and numerical instability in calculations.
        /// Small non-zero value ensures stable simulation even for tiny masses.
        /// </summary>
        public const float MinimumThermalMass = 0.001f;

        /// <summary>
        /// Depth in meters at which underground contact becomes significant.
        /// Used to interpolate between surface and underground convection coefficients.
        /// Below this depth, blocks are considered "in rock" for heat transfer purposes.
        /// </summary>
        public const float UndergroundContactDepth = 5f;


        /// <summary>
        /// Converts temperature from Kelvin to Celsius.
        /// Formula: Celsius = Kelvin - 273.15
        /// </summary>
        /// <param name="kelvin">Temperature in Kelvin.</param>
        /// <returns>Temperature in degrees Celsius.</returns>
        public static float KelvinToCelsius(float kelvin)
        {
            return kelvin - KelvinOffset;
        }


        /// <summary>
        /// Converts temperature from Celsius to Kelvin.
        /// Formula: Kelvin = Celsius + 273.15
        /// </summary>
        /// <param name="celsius">Temperature in degrees Celsius.</param>
        /// <returns>Temperature in Kelvin.</returns>
        public static float CelsiusToKelvin(float celsius)
        {
            return celsius + KelvinOffset;
        }
    }
}
