namespace Thermodynamics.Core
{
    /// <summary>One value of a planet's climate, as a definition may or may not supply it.</summary>
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

        All = NightTemperature | DayTemperature | UndergroundTemperature | CoreTemperature
            | SealevelDeadzone | PoleTemperatureDrop | AmbientLagSeconds | AmbientLapseRate
            | UndergroundDampingDepth | SolarDecay | ConvectionCoefficient,
    }

    /// <summary>
    /// Merging what a planet's definition said into what the model already believed: **only the fields
    /// a read actually supplied**, since a definition that did not load would otherwise write zeros
    /// over the defaults and make an earthlike world a vacuum. Pure, so what a partial definition does
    /// is a test rather than a session. See environment.md, When the file does not reach the mod.
    /// </summary>
    public static class PlanetProperties
    {
        /// <summary>
        /// <paramref name="baseline"/> with each field <paramref name="supplied"/> names taken from
        /// <paramref name="read"/>. Neither argument is modified.
        /// </summary>
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

            if ((supplied & PlanetField.AmbientLapseRate) != 0)
                merged.AmbientLapseRate = read.AmbientLapseRate;

            if ((supplied & PlanetField.UndergroundDampingDepth) != 0)
                merged.UndergroundDampingDepth = read.UndergroundDampingDepth;

            if ((supplied & PlanetField.SolarDecay) != 0)
                merged.SolarDecay = read.SolarDecay;

            if ((supplied & PlanetField.ConvectionCoefficient) != 0)
                merged.ConvectionCoefficient = read.ConvectionCoefficient;

            return merged.Clamp();
        }

        /// <summary>
        /// True where a climate cannot be simulated as one: no day, no night and no rock. What a
        /// definition read before its lookup was up used to produce, and what nothing should.
        /// </summary>
        public static bool IsVacuum(PlanetThermalProperties properties)
        {
            return properties == null
                || (properties.DayTemperature <= 0f
                    && properties.NightTemperature <= 0f
                    && properties.UndergroundTemperature <= 0f);
        }
    }
}
