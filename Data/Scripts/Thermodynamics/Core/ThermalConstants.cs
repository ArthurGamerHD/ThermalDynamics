using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Physical constants and unit conversions. Nothing here is tunable at runtime; tunable values
    /// live in <see cref="ThermalSettings"/> or in the definition property classes.
    /// </summary>
    public static class ThermalConstants
    {
        /// <summary>Stefan-Boltzmann constant, W/(m^2 K^4).</summary>
        public const float StefanBoltzmann = 0.00000005670374419f;

        public const float MegawattsToWatts = 1000000f;
        public const float WattsToMegawatts = 1f / MegawattsToWatts;

        /// <summary>Offset between Kelvin and Celsius.</summary>
        public const float KelvinOffset = 273.15f;

        /// <summary>
        /// Turns a block's real thermal conductivity into this simulation's pace, dimensionless — the
        /// one place the game's conduction pace is set, as <c>HeatTimeScale</c> is for capacity.
        /// 2.4 puts mild steel exactly where the old 0..1 quality value put it.
        /// See definitions.md, Conductivity is in real W/(m·K).
        /// </summary>
        public const float ConductionScale = 2.4f;

        /// <summary>
        /// Reference conductivity for the coolant loop's fluid coupling, W/(m K). Still a 0..1 quality
        /// value times 200, because fluid-to-wall transfer is convective and its honest dial is a heat
        /// transfer coefficient — a change to the loop equations rather than to a number.
        /// </summary>
        public const float ReferenceConductivity = 200f;

        /// <summary>
        /// Specific heat of air at constant pressure, J/(kg K). Used for room air, which is the
        /// one mass in the simulation the block definitions do not describe.
        /// </summary>
        public const float AirSpecificHeat = 1005f;

        /// <summary>Absolute floor for any node temperature.</summary>
        public const float MinimumTemperature = 0f;

        /// <summary>
        /// Minimum heat capacity, guarding against division by zero when a definition declares zero
        /// specific heat or a block reports zero mass.
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
    }
}
