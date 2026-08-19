using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Climate description of a planet, from the <c>ThermalPlanetProperties</c> group.
    /// </summary>
    public class PlanetThermalProperties
    {
        /// <summary>Ambient with the sun on the far side of the planet, K.</summary>
        public float NightTemperature = 283.15f;

        /// <summary>Ambient with the sun directly overhead, K.</summary>
        public float DayTemperature = 294.261f;

        /// <summary>
        /// How much colder a pole is than the equator, K.
        ///
        /// <see cref="DayTemperature"/> and <see cref="NightTemperature"/> are the world's equatorial
        /// figures; this is the span from there to its poles. Earth's is about 40 K. Zero gives one
        /// climate for the whole planet.
        /// </summary>
        public float PoleTemperatureDrop = 40f;

        /// <summary>
        /// Time constant for the air's response to the sun, in seconds of play.
        ///
        /// Without it the hottest moment of the day falls exactly at noon and the coldest at
        /// midnight. Real air lags by hours, placing the peak mid-afternoon and the low just before
        /// dawn. Zero disables the lag.
        /// </summary>
        public float AmbientLagSeconds = 45f;

        /// <summary>
        /// Lapse rate: how much colder a kilometre above sea level is, K.
        ///
        /// Earth's is about 6.5. The default here is lower because the ground table already accounts
        /// for part of the same effect — a mountain reads as snow, itself worth -14 K — and both at
        /// full strength put a 5.6 km peak near -50 C. Zero disables the lapse and gives one
        /// temperature from sea level to the stratosphere.
        /// </summary>
        public float AmbientLapseRate = 4f;

        /// <summary>Ambient below the surface, K.</summary>
        public float UndergroundTemperature = 280f;

        /// <summary>
        /// Depth of rock over which the surface's day-night swing is damped to nothing, m.
        ///
        /// Above this depth a buried block still sees a fraction of the day; below it only
        /// <see cref="UndergroundTemperature"/> and the heat rising from the core.
        /// </summary>
        public float UndergroundDampingDepth = 20f;

        /// <summary>Temperature at the planet's centre, K.</summary>
        public float CoreTemperature = 3000f;

        /// <summary>
        /// Depth below sea level that stays at <see cref="UndergroundTemperature"/>, m.
        ///
        /// Below it the rock warms towards <see cref="CoreTemperature"/>. Measured from sea level
        /// rather than from the surface, so a tunnel driven into a mountain stays cold however far it
        /// goes while a shaft sunk from a beach does not.
        /// </summary>
        public float SealevelDeadzone = 2000f;

        /// <summary>Fraction of solar energy absorbed by a full-density atmosphere, 0..1.</summary>
        public float SolarDecay = 0.5f;

        /// <summary>Convective heat transfer coefficient at rest, W/(m^2 K).</summary>
        public float ConvectionCoefficient = 50f;

        public static PlanetThermalProperties Default()
        {
            return new PlanetThermalProperties();
        }

        /// <summary>Vacuum: no atmosphere, no climate.</summary>
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

        public PlanetThermalProperties Clamp()
        {
            NightTemperature = Math.Max(0f, NightTemperature);
            DayTemperature = Math.Max(0f, DayTemperature);
            UndergroundTemperature = Math.Max(0f, UndergroundTemperature);
            PoleTemperatureDrop = Math.Max(0f, PoleTemperatureDrop);
            UndergroundDampingDepth = Math.Max(0f, UndergroundDampingDepth);
            AmbientLapseRate = Math.Max(0f, AmbientLapseRate);
            AmbientLagSeconds = Math.Max(0f, AmbientLagSeconds);
            CoreTemperature = Math.Max(0f, CoreTemperature);
            SealevelDeadzone = Math.Max(0f, SealevelDeadzone);
            SolarDecay = Math.Max(0f, Math.Min(1f, SolarDecay));
            ConvectionCoefficient = Math.Max(0f, ConvectionCoefficient);
            return this;
        }

        public PlanetThermalProperties Clone()
        {
            return (PlanetThermalProperties)MemberwiseClone();
        }
    }
}
