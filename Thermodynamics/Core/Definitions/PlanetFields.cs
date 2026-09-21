namespace Thermodynamics.Core
{
    [System.Flags]
    public enum PlanetField
    {
        None = 0,
        NightTemperature = 1,
        DayTemperature = 2,
        UndergroundTemperature = 4,
        CoreTemperature = 8,
        SealevelDeadzone = 16,
        PoleTemperatureDrop = 32,
        AmbientLagSeconds = 64,
        AmbientLapseRate = 128,
        UndergroundDampingDepth = 256,
        SolarDecay = 512,
        ConvectionCoefficient = 1024,
        UndergroundConvectionCoefficient = 2048,
        AmbientLagShareOfDay = 4096,

        All = NightTemperature | DayTemperature | UndergroundTemperature | CoreTemperature
            | SealevelDeadzone | PoleTemperatureDrop | AmbientLagSeconds | AmbientLapseRate
            | UndergroundDampingDepth | SolarDecay | ConvectionCoefficient
            | UndergroundConvectionCoefficient | AmbientLagShareOfDay,
    }

    public static class PlanetProperties
    {
/// <summary>Merge operation.</summary>
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

/// <summary>IsVacuum operation.</summary>
        public static bool IsVacuum(PlanetThermalProperties properties)
        {
            return properties == null
                || (properties.DayTemperature <= 0f
                    && properties.NightTemperature <= 0f
                    && properties.UndergroundTemperature <= 0f);
        }
    }
}
