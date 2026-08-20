using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Thermal properties worked out from a planet's own generator definition.
    ///
    /// <para>Every planet in this mod used to run on one entry — <c>DefaultThermodynamics</c>, an
    /// earthlike climate — so the Moon, Titan and Pertam were all 294 K by day and 283 K by night.
    /// The game's own definitions already say a great deal about what a world is like; this reads
    /// them and turns them into the mod's figures, so a planet nobody has authored an entry for still
    /// gets a climate that belongs to it.</para>
    ///
    /// <para>What the engine actually supplies, and what each thing is worth:</para>
    ///
    /// <list type="bullet">
    /// <item><c>DefaultSurfaceTemperature</c> — a five-level enum, <c>ExtremeFreeze</c> to
    /// <c>ExtremeHot</c>, which the engine turns into 0, 0.25, 0.5, 0.75 or 1 on a comfort scale.
    /// It is the only statement the game makes about how hot a world is, and it is the anchor
    /// here.</item>
    /// <item><c>SurfaceGravity</c> — real, and the lapse rate is <c>g/c_p</c>, so this is a
    /// derivation rather than a guess.</item>
    /// <item><c>Atmosphere.Density</c> — sets how much of the day-night swing the air damps out, how
    /// much heat gets carried from equator to pole, and what convection is worth.</item>
    /// <item><c>SolarRadiationProtectionFactor</c> — the engine's own figure for how much the
    /// atmosphere stands between the sun and the ground.</item>
    /// <item><c>Breathable</c> — a composition hint, and nothing better exists. Breathable air is
    /// taken as nitrogen and oxygen, and anything else as carbon dioxide, which changes
    /// <c>c_p</c> and therefore the lapse rate.</item>
    /// </list>
    ///
    /// <para><b>What the engine does not supply is orbital distance.</b> Space Engineers has one
    /// sun and no concept of how far a planet sits from it, so a world cannot be cold *because* it is
    /// far away. <c>DefaultSurfaceTemperature</c> is the whole of the game's answer, and everything
    /// here is anchored to it rather than to a solar constant.</para>
    ///
    /// <para>Pure, and free of the game so it can be tested and so the same code can generate the
    /// shipped <c>Planets.xml</c> offline.</para>
    /// </summary>
    public static class PlanetThermalDerivation
    {
        /// <summary>What a planet generator definition says, reduced to what matters thermally.</summary>
        public struct Engine
        {
            /// <summary>
            /// The engine's own comfort figure for <c>DefaultSurfaceTemperature</c>: 0 for
            /// <c>ExtremeFreeze</c>, 0.25 <c>Freeze</c>, 0.5 <c>Cozy</c>, 0.75 <c>Hot</c>, 1
            /// <c>ExtremeHot</c>. Read from <c>MySectorWeatherComponent.LevelToTemperature</c>.
            /// </summary>
            public float SurfaceTemperatureLevel;

            /// <summary>Surface gravity in g. The definition's own field.</summary>
            public float SurfaceGravity;

            public bool HasAtmosphere;

            /// <summary>Sea-level air density. The object builder's default is 1.</summary>
            public float AtmosphereDensity;

            /// <summary>Whether the air is breathable, taken as a hint at its composition.</summary>
            public bool Breathable;

            /// <summary>
            /// The engine's <c>SolarRadiationProtectionFactor</c>. Default 1; an earthlike world
            /// authors 1.8 and Mars 0.2.
            /// </summary>
            public float SolarRadiationProtection;

            /// <summary>Effective density: zero when the world has no air at all.</summary>
            public float Air
            {
                get
                {
                    if (!HasAtmosphere) return 0f;
                    return AtmosphereDensity < 0f ? 0f : AtmosphereDensity;
                }
            }
        }

        // ---- the five levels, in kelvin --------------------------------------------------------

        /// <summary>
        /// Mean surface temperature for each of the engine's five levels, K.
        ///
        /// Authored rather than derived, because the levels are the game's own vocabulary and
        /// nothing in the engine turns them into kelvin. Each is anchored to a real body so the
        /// scale means something:
        ///
        /// <list type="bullet">
        /// <item><b>100 K</b> — Titan is 94 K and Europa 102 K.</item>
        /// <item><b>215 K</b> — Mars's measured mean.</item>
        /// <item><b>288 K</b> — Earth's.</item>
        /// <item><b>325 K</b> — a hot desert world; Earth's hottest surface air is about 330 K.</item>
        /// <item><b>450 K</b> — between Mercury's day side at 440 K and Venus's 737 K: a world you
        /// cannot stand on.</item>
        /// </list>
        ///
        /// They are not evenly spaced, and interpolating between them linearly would put
        /// <c>Cozy</c> at 275 K, which is freezing. The five anchors are the model.
        /// </summary>
        public static readonly float[] LevelTemperatures = { 100f, 215f, 288f, 325f, 450f };

        /// <summary>The mean surface temperature a level asks for, K.</summary>
        public static float MeanTemperature(float level)
        {
            if (level < 0f) level = 0f;
            if (level > 1f) level = 1f;

            // Four steps between five anchors, interpolated so a modded planet that lands between
            // two levels is not snapped to one of them.
            float position = level * (LevelTemperatures.Length - 1);

            int low = (int)position;
            if (low >= LevelTemperatures.Length - 1) return LevelTemperatures[LevelTemperatures.Length - 1];

            float fraction = position - low;
            return LevelTemperatures[low] + ((LevelTemperatures[low + 1] - LevelTemperatures[low]) * fraction);
        }

        // ---- the swing -------------------------------------------------------------------------

        /// <summary>Day-night swing with a full atmosphere, K. Earth's equatorial range.</summary>
        public const float ThickAirSwing = 11f;

        /// <summary>
        /// Day-night swing with no atmosphere at all, K. The Moon runs from about 100 K before dawn
        /// to 390 K at noon, so this is the real figure rather than a chosen one.
        /// </summary>
        public const float AirlessSwing = 220f;

        /// <summary>
        /// How far the day and night temperatures sit either side of the mean, K.
        ///
        /// Air is what damps the swing: it carries heat away from the sunlit ground and gives it back
        /// after dark, and it has a heat capacity of its own. With none of it the ground answers the
        /// sun directly and the range is enormous.
        /// </summary>
        public static float Swing(float air)
        {
            if (air < 0f) air = 0f;
            if (air > 1f) air = 1f;

            // Falls off faster than linearly: the first tenth of an atmosphere does most of the
            // damping, which is why Mars — at less than a hundredth of Earth's pressure — still has
            // a far smaller swing than the Moon.
            float thin = (1f - air) * (1f - air);
            return ThickAirSwing + ((AirlessSwing - ThickAirSwing) * thin);
        }

        // ---- the derivations -------------------------------------------------------------------

        /// <summary>
        /// Specific heat of the air at constant pressure, J/(kg·K), guessed from breathability.
        ///
        /// Nitrogen and oxygen come to about 1005; carbon dioxide, the obvious stand-in for air a
        /// human cannot breathe, about 850. The difference is what makes Mars's lapse rate shallower
        /// than Earth's at similar gravity.
        /// </summary>
        public static float SpecificHeat(bool breathable)
        {
            return breathable ? 1005f : 850f;
        }

        /// <summary>Standard gravity, m/s².</summary>
        public const float StandardGravity = 9.81f;

        /// <summary>
        /// The share of the dry adiabatic rate that a real atmosphere actually runs at.
        ///
        /// Earth's dry adiabatic rate is 9.8 K/km and its environmental rate is about 6.5, because
        /// condensing water releases heat on the way up. Two thirds is that ratio, applied to every
        /// world for want of anything better to apply.
        /// </summary>
        public const float EnvironmentalShare = 0.66f;

        /// <summary>
        /// Lapse rate, K per km — <c>Γ = g/c_p</c>, the one figure here that is a derivation rather
        /// than a judgement. An earthlike world comes out at 6.4 K/km against Earth's measured 6.5.
        /// </summary>
        public static float LapseRate(float gravity, bool breathable, float air)
        {
            if (air <= 0f) return 0f;   // no air, no lapse: there is nothing to cool as it rises

            float metresPerKelvin = (gravity * StandardGravity) / SpecificHeat(breathable);
            return metresPerKelvin * 1000f * EnvironmentalShare;
        }

        /// <summary>
        /// How much colder a pole is than the equator, K.
        ///
        /// Air moves heat polewards; without it every latitude keeps what it is given. Earth, with a
        /// full atmosphere, runs about 40 K from equator to pole. The Moon runs about 120.
        /// </summary>
        public static float PoleDrop(float air)
        {
            if (air < 0f) air = 0f;
            if (air > 1f) air = 1f;

            return 40f + (80f * (1f - air));
        }

        /// <summary>
        /// How long the air takes to answer the sun, in seconds of play.
        ///
        /// A game figure rather than a physical one — it is what puts the day's peak after noon —
        /// but it scales with the air, since a thick atmosphere is what does the remembering. A bare
        /// rock answers the sun almost at once.
        /// </summary>
        public static float LagSeconds(float air)
        {
            if (air < 0f) air = 0f;
            if (air > 1f) air = 1f;

            float lag = 45f * air;
            return lag < 5f ? 5f : lag;
        }

        /// <summary>
        /// Fraction of the sun a full atmosphere absorbs, 0..1.
        ///
        /// The engine's <c>SolarRadiationProtectionFactor</c> scaled to that range. An earthlike
        /// world authors 1.8, which comes out at 0.30 — close to the quarter to a third of incoming
        /// sunlight that Earth's atmosphere really does absorb and scatter before it reaches the
        /// ground. Mars's 0.2 comes out at 0.03.
        /// </summary>
        public static float SolarDecay(float protection, float air)
        {
            if (air <= 0f) return 0f;
            if (protection < 0f) protection = 0f;

            float decay = protection * (0.30f / 1.8f);
            if (decay > 0.9f) decay = 0.9f;
            return decay;
        }

        /// <summary>
        /// Convective coefficient at the surface, W/(m²·K), in proportion to the air there is.
        ///
        /// The solver blends this by atmosphere factor at the point of transfer, so this is the
        /// sea-level figure. Fifty is the mod's earthlike value and the anchor.
        /// </summary>
        public static float ConvectionCoefficient(float air)
        {
            if (air <= 0f) return 0f;
            if (air > 1f) air = 1f;

            return 50f * air;
        }

        /// <summary>
        /// Everything, from one definition.
        ///
        /// The four figures this does not derive — damping depth, core temperature, the sea-level
        /// deadzone, and the underground temperature's relationship to the surface — are carried
        /// through from the defaults, because nothing in a planet generator definition says anything
        /// about a planet's interior.
        /// </summary>
        public static PlanetThermalProperties Derive(Engine engine)
        {
            float air = engine.Air;

            float mean = MeanTemperature(engine.SurfaceTemperatureLevel);
            float half = Swing(air) * 0.5f;

            PlanetThermalProperties properties = new PlanetThermalProperties();

            properties.DayTemperature = mean + half;
            properties.NightTemperature = mean - half;
            if (properties.NightTemperature < 0f) properties.NightTemperature = 0f;

            properties.PoleTemperatureDrop = PoleDrop(air);
            properties.AmbientLagSeconds = LagSeconds(air);
            properties.AmbientLapseRate = LapseRate(engine.SurfaceGravity, engine.Breathable, air);

            // The rock below settles at the mean of what the surface does over a day, which is what
            // the mean temperature already is.
            properties.UndergroundTemperature = mean;

            properties.SolarDecay = SolarDecay(engine.SolarRadiationProtection, air);
            properties.ConvectionCoefficient = ConvectionCoefficient(air);

            return properties;
        }
    }
}
