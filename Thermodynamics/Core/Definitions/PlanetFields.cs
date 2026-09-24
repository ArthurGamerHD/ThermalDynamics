using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Flags indicating which planet properties are being supplied in a merge operation.
    /// Each flag represents a specific thermal property of a planet's environment.
    /// Uses bit flags pattern to allow multiple properties to be specified simultaneously.
    /// </summary>
    [System.Flags]
    public enum PlanetField
    {
        /// <summary>No properties supplied.</summary>
        None = 0,
        
        /// <summary>Night temperature in Kelvin.</summary>
        NightTemperature = 1,
        
        /// <summary>Day temperature in Kelvin.</summary>
        DayTemperature = 2,
        
        /// <summary>Underground temperature in Kelvin (at depth).</summary>
        UndergroundTemperature = 4,
        
        /// <summary>Planetary core temperature in Kelvin.</summary>
        CoreTemperature = 8,
        
        /// <summary>Depth at which sealevel deadzone begins (meters).</summary>
        SealevelDeadzone = 16,
        
        /// <summary>Temperature drop at poles due to latitude (Kelvin).</summary>
        PoleTemperatureDrop = 32,
        
        /// <summary>Time constant for ambient temperature lag (seconds).</summary>
        AmbientLagSeconds = 64,
        
        /// <summary>Rate of temperature change with altitude (Kelvin per km).</summary>
        AmbientLapseRate = 128,
        
        /// <summary>Depth at which underground temperature stabilizes (meters).</summary>
        UndergroundDampingDepth = 256,
        
        /// <summary>Solar radiation decay factor in atmosphere (0-1).</summary>
        SolarDecay = 512,
        
        /// <summary>Convection coefficient for air heat transfer (W/m²·K).</summary>
        ConvectionCoefficient = 1024,
        
        /// <summary>Convection coefficient for underground heat transfer (W/m²·K).</summary>
        UndergroundConvectionCoefficient = 2048,
        
        /// <summary>Portion of day length used for ambient lag calculation (0-1).</summary>
        AmbientLagShareOfDay = 4096,

        /// <summary>All planet properties combined.</summary>
        All = NightTemperature | DayTemperature | UndergroundTemperature | CoreTemperature
            | SealevelDeadzone | PoleTemperatureDrop | AmbientLagSeconds | AmbientLapseRate
            | UndergroundDampingDepth | SolarDecay | ConvectionCoefficient
            | UndergroundConvectionCoefficient | AmbientLagShareOfDay,
    }

    /// <summary>
    /// Utility class for planet thermal property operations.
    /// Provides merging of properties and vacuum detection.
    /// </summary>
    public static class PlanetProperties
    {
        /// <summary>
        /// Merges properties from 'read' into 'baseline' for properties specified in 'supplied'.
        /// Uses the supplied bit flags to determine which properties to overwrite.
        /// Returns a clamped copy of the baseline with selected properties updated.
        /// </summary>
        /// <param name="baseline">The base configuration to merge into.</param>
        /// <param name="read">The source configuration with new values.</param>
        /// <param name="supplied">Bit flags indicating which properties to merge.</param>
        /// <returns>A new PlanetThermalProperties with merged and clamped values.</returns>
        public static PlanetThermalProperties Merge(
            PlanetThermalProperties baseline, PlanetThermalProperties read, PlanetField supplied)
        {
            if (baseline == null) baseline = new PlanetThermalProperties();

            PlanetThermalProperties merged = baseline.Clone();
            if (read == null || supplied == PlanetField.None) return merged.Clamp();

            if ((supplied & PlanetField.NightTemperature) != 0)
                merged.NightTemperature = read.NightTemperature;

            if ((supplied & PlanetField.DayTemperature) != 0)
                merged.DayTemperature = read.DayTemperature;

            if ((supplied & PlanetField.UndergroundTemperature) != 0)
                merged.UndergroundTemperature = read.UndergroundTemperature;

            if ((supplied & PlanetField.CoreTemperature) != 0)
                merged.CoreTemperature = read.CoreTemperature;

            if ((supplied & PlanetField.SealevelDeadzone) != 0)
                merged.SealevelDeadzone = read.SealevelDeadzone;

            if ((supplied & PlanetField.PoleTemperatureDrop) != 0)
                merged.PoleTemperatureDrop = read.PoleTemperatureDrop;

            if ((supplied & PlanetField.AmbientLagSeconds) != 0)
                merged.AmbientLagSeconds = read.AmbientLagSeconds;

            if ((supplied & PlanetField.AmbientLagShareOfDay) != 0)
                merged.AmbientLagShareOfDay = read.AmbientLagShareOfDay;

            if ((supplied & PlanetField.AmbientLapseRate) != 0)
                merged.AmbientLapseRate = read.AmbientLapseRate;

            if ((supplied & PlanetField.UndergroundDampingDepth) != 0)
                merged.UndergroundDampingDepth = read.UndergroundDampingDepth;

            if ((supplied & PlanetField.SolarDecay) != 0)
                merged.SolarDecay = read.SolarDecay;

            if ((supplied & PlanetField.ConvectionCoefficient) != 0)
                merged.ConvectionCoefficient = read.ConvectionCoefficient;

            if ((supplied & PlanetField.UndergroundConvectionCoefficient) != 0)
                merged.UndergroundConvectionCoefficient = read.UndergroundConvectionCoefficient;

            return merged.Clamp();
        }

        /// <summary>
        /// Determines if planet properties indicate a vacuum environment.
        /// A vacuum is detected when all temperatures (day, night, underground) are zero or negative.
        /// </summary>
        /// <param name="properties">The planet properties to check.</param>
        /// <returns>True if the planet has no meaningful thermal profile (vacuum).</returns>
        public static bool IsVacuum(PlanetThermalProperties properties)
        {
            return properties == null
                || (properties.DayTemperature <= 0f
                    && properties.NightTemperature <= 0f
                    && properties.UndergroundTemperature <= 0f);
        }
    }
}
