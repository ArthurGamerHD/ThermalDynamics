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
        public const float ConductionScale = 9.6f;

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

        /// <summary>
        /// Metres of burial over which a grid stops being in air and starts being in rock.
        ///
        /// A hull's own depth, near enough: a grid one metre under is mostly still in the open and
        /// one five metres under is not. It exists so the environment coefficient crosses over
        /// rather than stepping — a step would put a ship's cooling on a knife edge at the moment
        /// it broke the surface. See environment.md, Underground.
        /// </summary>
        public const float UndergroundContactDepth = 5f;

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
