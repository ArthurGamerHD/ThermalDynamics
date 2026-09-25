using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Thermal properties defining a planet's environmental characteristics.
    /// Used by ClimateModel and EnvironmentSolver to calculate temperature profiles,
    /// atmospheric effects, and heat transfer coefficients based on location.
    /// </summary>
    public class PlanetThermalProperties
    {
        /// <summary>
        /// Temperature at night in Kelvin (no sunlight).
        /// Cold temperatures for airless or thin-atmosphere bodies.
        /// </summary>
        public float NightTemperature = 283.15f;

        /// <summary>
        /// Temperature during the day in Kelvin (full sunlight).
        /// Hot temperatures for airless bodies with no atmospheric heat distribution.
        /// </summary>
        public float DayTemperature = 294.261f;

        /// <summary>
        /// Temperature drop from equator to pole in Kelvin.
        /// Larger values indicate less efficient atmospheric heat transport.
        /// Earth: ~40K, Airless bodies: ~80-100K.
        /// </summary>
        public float PoleTemperatureDrop = 40f;

        /// <summary>
        /// Time constant for ambient temperature lag in seconds.
        /// How quickly the environment approaches the target temperature.
        /// Atmospheres with high thermal mass have longer lag times.
        /// </summary>
        public float AmbientLagSeconds = 45f;

        /// <summary>
        /// Fraction of day length used for ambient lag calculation.
        /// Alternative way to specify lag time based on planetary rotation.
        /// Typical value: 0.083 (about 2 hours for a 24-hour day).
        /// </summary>
        public float AmbientLagShareOfDay = 0.083f;

        /// <summary>
        /// Atmospheric lapse rate in Kelvin per kilometer.
        /// Temperature decrease with altitude (positive = cools as you go up).
        /// Earth: ~6.5 K/km, Airless: 0 K/km.
        /// </summary>
        public float AmbientLapseRate = 4f;

        /// <summary>
        /// Temperature underground (at damping depth) in Kelvin.
        /// Represents the stable temperature below the surface layer.
        /// </summary>
        public float UndergroundTemperature = 280f;

        /// <summary>
        /// Depth in meters at which underground temperature changes damp out.
        /// Below this depth, temperature variations from surface are minimal.
        /// Earth: ~20m, Airless: ~1-2m.
        /// </summary>
        public float UndergroundDampingDepth = 20f;

        /// <summary>
        /// Planetary core temperature in Kelvin.
        /// Used for deep underground calculations.
        /// Earth: ~5700K, Mars: ~1500K.
        /// </summary>
        public float CoreTemperature = 3000f;

        /// <summary>
        /// Depth in meters where sealevel deadzone begins.
        /// Below this depth, planetary radius effects on temperature become negligible.
        /// </summary>
        public float SealevelDeadzone = 2000f;

        /// <summary>
        /// Factor reducing solar radiation through the atmosphere (0-1).
        /// Higher values = more atmosphere blocking solar energy.
        /// Earth: ~0.3 (clouds/atmosphere), Airless: 0 (no blocking).
        /// </summary>
        public float SolarDecay = 0.5f;

        /// <summary>
        /// Convection coefficient for air heat transfer in W/m²·K.
        /// Higher = more efficient heat transfer via air movement.
        /// Earth: ~10-25 W/m²·K, High wind: up to ~100 W/m²·K.
        /// </summary>
        public float ConvectionCoefficient = 50f;

        /// <summary>
        /// Convection coefficient for underground/rock heat transfer in W/m²·K.
        /// Typically much lower than air convection due to poorer thermal contact.
        /// Earth rock: ~1-3 W/m²·K.
        /// </summary>
        public float UndergroundConvectionCoefficient = 2f;


        /// <summary>
        /// Creates a new PlanetThermalProperties with default values.
        /// Default values approximate Earth-like conditions.
        /// </summary>
        public static PlanetThermalProperties Default()
        {
            return new PlanetThermalProperties();
        }


        /// <summary>
        /// Creates a PlanetThermalProperties representing a vacuum/airless body.
        /// All temperature-related properties are set to zero.
        /// Used for space environments or airless asteroids/moons.
        /// </summary>
        public static PlanetThermalProperties None()
        {
            PlanetThermalProperties p = new PlanetThermalProperties();
            p.NightTemperature = 0f;
            p.DayTemperature = 0f;
            p.PoleTemperatureDrop = 0f;
            p.AmbientLapseRate = 0f;
            p.UndergroundTemperature = 0f;
            p.CoreTemperature = 0f;
            p.SolarDecay = 0f;
            p.ConvectionCoefficient = 0f;
            return p;
        }


        /// <summary>
        /// Calculates lag time based on day length.
        /// Uses AmbientLagShareOfDay multiplied by actual day length.
        /// Falls back to AmbientLagSeconds if share is zero.
        /// </summary>
        /// <param name="dayLengthSeconds">Length of planetary day in seconds.</param>
        /// <returns>Lag time in seconds for temperature changes.</returns>
        public float LagSecondsFor(float dayLengthSeconds)
        {
            if (AmbientLagShareOfDay <= 0f || dayLengthSeconds <= 0f) return AmbientLagSeconds;
            return AmbientLagShareOfDay * dayLengthSeconds;
        }


        /// <summary>
        /// Clamps all properties to physically valid ranges.
        /// Ensures no negative values for temperatures, depths, and coefficients.
        /// </summary>
        /// <returns>The same instance with clamped values.</returns>
        public PlanetThermalProperties Clamp()
        {
            NightTemperature = Math.Max(0f, NightTemperature);
            DayTemperature = Math.Max(0f, DayTemperature);
            UndergroundTemperature = Math.Max(0f, UndergroundTemperature);
            PoleTemperatureDrop = Math.Max(0f, PoleTemperatureDrop);
            UndergroundDampingDepth = Math.Max(0f, UndergroundDampingDepth);
            AmbientLapseRate = Math.Max(0f, AmbientLapseRate);
            AmbientLagSeconds = Math.Max(0f, AmbientLagSeconds);
            AmbientLagShareOfDay = Math.Max(0f, AmbientLagShareOfDay);
            CoreTemperature = Math.Max(0f, CoreTemperature);
            SealevelDeadzone = Math.Max(0f, SealevelDeadzone);
            SolarDecay = Math.Max(0f, Math.Min(1f, SolarDecay));
            ConvectionCoefficient = Math.Max(0f, ConvectionCoefficient);
            UndergroundConvectionCoefficient = Math.Max(0f, UndergroundConvectionCoefficient);
            return this;
        }


        /// <summary>
        /// Creates a shallow copy of this PlanetThermalProperties instance.
        /// </summary>
        /// <returns>A new instance with the same property values.</returns>
        public PlanetThermalProperties Clone()
        {
            return (PlanetThermalProperties)MemberwiseClone();
        }
    }
}
