using System;

namespace Thermodynamics.Core
{
    public class PlanetThermalProperties
    {
        public float NightTemperature = 283.15f;

        public float DayTemperature = 294.261f;

        public float PoleTemperatureDrop = 40f;

        public float AmbientLagSeconds = 45f;

        public float AmbientLagShareOfDay = 0.083f;

        public float AmbientLapseRate = 4f;

        public float UndergroundTemperature = 280f;

        public float UndergroundDampingDepth = 20f;

        public float CoreTemperature = 3000f;

        public float SealevelDeadzone = 2000f;

        public float SolarDecay = 0.5f;

        public float ConvectionCoefficient = 50f;

        public float UndergroundConvectionCoefficient = 2f;

/// <summary>Default operation.</summary>
        public static PlanetThermalProperties Default()
        {
            return new PlanetThermalProperties();
        }

/// <summary>None operation.</summary>
        public static PlanetThermalProperties None()
        {
/// <summary>PlanetThermalProperties operation.</summary>
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

/// <summary>LagSecondsFor operation.</summary>
        public float LagSecondsFor(float dayLengthSeconds)
        {
            if (AmbientLagShareOfDay <= 0f || dayLengthSeconds <= 0f) return AmbientLagSeconds;
            return AmbientLagShareOfDay * dayLengthSeconds;
        }

/// <summary>Clamp operation.</summary>
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

/// <summary>Clone operation.</summary>
        public PlanetThermalProperties Clone()
        {
            return (PlanetThermalProperties)MemberwiseClone();
        }
    }
}
