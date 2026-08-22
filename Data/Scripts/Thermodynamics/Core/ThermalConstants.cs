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
        public const float KilowattsToWatts = 1000f;

        /// <summary>Offset between Kelvin and Celsius.</summary>
        public const float KelvinOffset = 273.15f;

        /// <summary>
        /// Turns a block's real thermal conductivity into this simulation's pace, dimensionless.
        ///
        /// <c>Conductivity</c> in a block definition is the number a materials table gives — mild
        /// steel 50 W/(m K), aluminium 237, copper 400 — so a definition reads as a description of
        /// what the block is made of. This is the one place the game's pace is set against those
        /// real figures, exactly as <c>HeatTimeScale</c> is the one place it is set against real
        /// specific heats.
        ///
        /// 2.4 is chosen so mild steel lands where the old 0..1 quality value put it: the previous
        /// default of 0.6 against a 200 W/(m K) reference gave 120, and 50 x 2.4 is 120. A hull of
        /// ordinary armour therefore conducts exactly as it did; what changed is that copper pipes
        /// and aluminium panels now conduct like copper and aluminium instead of like each other.
        /// </summary>
        public const float ConductionScale = 2.4f;

        /// <summary>
        /// Reference conductivity for the coolant loop's fluid coupling, W/(m K).
        ///
        /// Still a 0..1 quality value times 200, because the loop's <c>Conductivity</c> is not
        /// really a conductivity: fluid-to-wall transfer is convective, and the honest real-world
        /// dial for it is a heat transfer coefficient in W/(m^2 K), which is a change to the loop
        /// equations rather than to a number. Left as it was so this pass changes one thing.
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

        public static float KelvinToFahrenheit(float kelvin)
        {
            return ((kelvin - KelvinOffset) * 9f / 5f) + 32f;
        }
    }
}
