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
        /// <see cref="DayTemperature"/> and <see cref="NightTemperature"/> are the world's
        /// equatorial figures; this is the span from there to its poles. Earth's is about 40 K.
        /// Zero gives the old behaviour, which was one climate for a whole planet.
        /// </summary>
        public float PoleTemperatureDrop = 40f;

        /// <summary>
        /// How long the air takes to answer the sun, in seconds of play.
        ///
        /// Without it the hottest moment of the day is exactly noon and the coldest is midnight,
        /// which is true nowhere: air lags by hours, so the peak lands mid-afternoon and the low
        /// just before dawn. Zero switches the lag off.
        /// </summary>
        public float AmbientLagSeconds = 45f;

        /// <summary>
        /// How much colder a kilometre above sea level is, K.
        ///
        /// Earth's is about 6.5. This defaults lower because the ground table already carries some
        /// of what altitude does — a mountain reads as snow, and snow is already worth -14 K — so
        /// the two at full strength put a 5.6 km peak near -50 C. Zero switches altitude off and
        /// gives one temperature from the sea to the stratosphere, which is what the model did
        /// before it was asked.
        /// </summary>
        public float AmbientLapseRate = 4f;

        /// <summary>Ambient below the surface, K.</summary>
        public float UndergroundTemperature = 280f;

        /// <summary>
        /// Metres of rock that blunt the surface's day-night swing to nothing.
        ///
        /// Rock is slow. A hand's depth of soil still feels the afternoon; a cellar does not, and
        /// nothing below a cellar has ever felt one. This is where that ends — above it a buried
        /// block still sees a fraction of the day, below it only
        /// <see cref="UndergroundTemperature"/> and whatever the core is sending up.
        /// </summary>
        public float UndergroundDampingDepth = 20f;

        /// <summary>Temperature at the planet's centre, K.</summary>
        public float CoreTemperature = 3000f;

        /// <summary>
        /// Depth below sea level that stays at <see cref="UndergroundTemperature"/>, m.
        ///
        /// Below it the rock warms toward <see cref="CoreTemperature"/>. Measured from sea level
        /// and not from the surface, so a tunnel driven into a mountain stays cold however far in
        /// it goes, and a shaft sunk from a beach does not.
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
