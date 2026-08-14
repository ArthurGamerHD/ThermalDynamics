using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Physical constants and unit conversions. Nothing here is tunable at runtime — tunable
    /// values live in <see cref="ThermalSettings"/> or in the definition property classes.
    /// </summary>
    public static class ThermalConstants
    {
        /// <summary>Stefan-Boltzmann constant, W/(m^2 K^4).</summary>
        public const float StefanBoltzmann = 0.00000005670374419f;

        public const float MegawattsToWatts = 1000000f;
        public const float KilowattsToWatts = 1000f;

        /// <summary>Offset between Kelvin and Celsius.</summary>
        public const float KelvinOffset = 273.15f;

        /// <summary>
        /// Conductivity in the block definitions is a unitless 0..1 quality value. This is the
        /// W/(m K) that a value of 1.0 maps to, so that conduction can be computed in real units.
        /// 200 sits between steel (~50) and aluminium (~235); it is a game-feel choice, but it is
        /// applied consistently instead of cancelling out of the equations.
        /// </summary>
        public const float ReferenceConductivity = 200f;

        /// <summary>
        /// Specific heat of air at constant pressure, J/(kg K). Used for room air, which is the
        /// one mass in the simulation the block definitions do not describe.
        /// </summary>
        public const float AirSpecificHeat = 1005f;

        /// <summary>
        /// Absolute floor for any node temperature. Nothing may go below this.
        /// </summary>
        public const float MinimumTemperature = 0f;

        /// <summary>
        /// Guards against divide-by-zero when a definition declares zero specific heat or a block
        /// reports zero mass.
        /// </summary>
        public const float MinimumThermalMass = 0.001f;

        public static float KelvinToCelsius(float kelvin)
        {
            return kelvin - KelvinOffset;
        }

        public static float CelsiusToKelvin(float celsius)
        {
            return celsius + KelvinOffset;
        }

        public static float KelvinToFahrenheit(float kelvin)
        {
            return ((kelvin - KelvinOffset) * 9f / 5f) + 32f;
        }
    }
}
