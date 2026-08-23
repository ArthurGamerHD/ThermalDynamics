using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// The eight worlds Space Engineers ships, their thermal properties, and the file that carries
    /// them.
    ///
    /// <para>Every figure in <see cref="Vanilla"/> is read out of the game's own
    /// <c>PlanetGeneratorDefinitions.sbc</c> — nothing here is remembered or estimated. What the
    /// derivation then does with them is in <see cref="PlanetThermalDerivation"/>, and
    /// <see cref="Xml"/> writes the result out as the mod's <c>Planets.xml</c>, so the shipped file
    /// is generated rather than typed and can be regenerated when a definition changes.</para>
    ///
    /// <para><b>The one thing worth knowing before reading the table:</b> Space Engineers has no
    /// orbital distance. One sun, and every planet gets the same light from it, so a world cannot be
    /// cold because it is far away. <c>DefaultSurfaceTemperature</c> — a five-level enum — is the
    /// entire statement the game makes about how hot a planet is, and four of the eight worlds do
    /// not even author it and take the <c>Cozy</c> default.</para>
    /// </summary>
    public static class PlanetLab
    {
        /// <summary>A world as its definition describes it, plus what that definition leaves out.</summary>
        public class World
        {
            public string Subtype;

            /// <summary>What the definition authors, or null where it takes the engine default.</summary>
            public string AuthoredTemperatureLevel;

            public PlanetThermalDerivation.Engine Engine;

            /// <summary>The real body the name evokes, and its measured mean surface temperature.</summary>
            public string RealAnalogue;
            public float RealMeanKelvin;

            /// <summary>
            /// Day-night swing the real body has, K, where that is known and worth using. NaN to let
            /// the derivation decide.
            /// </summary>
            public float RealSwingKelvin = float.NaN;

            /// <summary>Why this entry departs from the pure derivation, or null where it does not.</summary>
            public string OverrideReason;

            /// <summary>What the derivation alone makes of the definition.</summary>
            public PlanetThermalProperties Derived
            {
                get { return PlanetThermalDerivation.Derive(Engine); }
            }

            /// <summary>
            /// What actually ships.
            ///
            /// <para>The same derivation, except where the game is <b>silent</b> and the world is
            /// named after a real place. Four of the eight do not author
            /// <c>DefaultSurfaceTemperature</c> at all, and an unauthored field is an omission rather
            /// than a statement — reading <c>Cozy</c> out of it as intent would put Titan at 288 K
            /// and have players landing on an ice moon in shirtsleeves.</para>
            ///
            /// <para>Where the game <b>does</b> author a level it is followed, whatever the real body
            /// does. SE's Triton is breathable with full-density air and nothing like the real one;
            /// the definition says <c>ExtremeFreeze</c> and that is the game's call to make.</para>
            /// </summary>
            public PlanetThermalProperties Shipped
            {
                get
                {
                    PlanetThermalProperties p = Derived;
                    if (OverrideReason == null) return p;

                    float mean = RealMeanKelvin;
                    float swing = float.IsNaN(RealSwingKelvin)
                        ? p.DayTemperature - p.NightTemperature
                        : RealSwingKelvin;

                    p.DayTemperature = mean + (swing * 0.5f);
                    p.NightTemperature = mean - (swing * 0.5f);
                    if (p.NightTemperature < 0f) p.NightTemperature = 0f;
                    p.UndergroundTemperature = mean;

                    return p;
                }
            }
        }

        /// <summary>
        /// Every shipped world.
        ///
        /// Read from the definition file, with the object builder's defaults filled in where a field
        /// is not authored: <c>Density 1</c>, <c>OxygenDensity 1</c>, <c>LimitAltitude 2</c>,
        /// <c>SolarRadiationProtectionFactor 1</c> and <c>DefaultSurfaceTemperature Cozy</c>. Four
        /// of the eight take that last default, which is worth noticing: the game does not say the
        /// Moon or Titan are cold.
        /// </summary>
        public static List<World> Vanilla()
        {
            List<World> worlds = new List<World>();

            worlds.Add(new World
            {
                Subtype = "EarthLike",
                AuthoredTemperatureLevel = null,
                RealAnalogue = "Earth",
                RealMeanKelvin = 288f,
                Engine = new PlanetThermalDerivation.Engine
                {
                    DeepestGroundMetres = 600f,
                    SurfaceTemperatureLevel = 0.5f,     // Cozy, by default
                    SurfaceGravity = 1.0f,
                    HasAtmosphere = true,
                    AtmosphereDensity = 1.0f,
                    Breathable = true,
                    SolarRadiationProtection = 1.8f,
                },
            });

            worlds.Add(new World
            {
                Subtype = "Alien",
                AuthoredTemperatureLevel = null,
                RealAnalogue = "none — a thick-aired earthlike",
                RealMeanKelvin = 288f,
                Engine = new PlanetThermalDerivation.Engine
                {
                    DeepestGroundMetres = 600f,
                    SurfaceTemperatureLevel = 0.5f,
                    SurfaceGravity = 1.1f,
                    HasAtmosphere = true,
                    AtmosphereDensity = 1.2f,
                    Breathable = true,
                    SolarRadiationProtection = 1.8f,
                },
            });

            worlds.Add(new World
            {
                Subtype = "Mars",
                AuthoredTemperatureLevel = null,
                RealAnalogue = "Mars",
                RealMeanKelvin = 215f,
                RealSwingKelvin = 60f,
                OverrideReason =
                    "the definition does not author DefaultSurfaceTemperature, so the engine's Cozy "
                    + "default would put Mars at 288 K. Anchored to the real planet's 215 K mean and "
                    + "its ~60 K daily range instead, which its thin real air cannot damp.",
                Engine = new PlanetThermalDerivation.Engine
                {
                    DeepestGroundMetres = 600f,
                    SurfaceTemperatureLevel = 0.5f,     // Cozy, by default — see the report
                    SurfaceGravity = 0.9f,
                    HasAtmosphere = true,
                    AtmosphereDensity = 1.0f,
                    Breathable = false,
                    SolarRadiationProtection = 0.2f,
                },
            });

            worlds.Add(new World
            {
                Subtype = "Pertam",
                AuthoredTemperatureLevel = "Hot",
                RealAnalogue = "none — a desert world",
                RealMeanKelvin = 325f,
                Engine = new PlanetThermalDerivation.Engine
                {
                    DeepestGroundMetres = 750f,
                    SurfaceTemperatureLevel = 0.75f,
                    SurfaceGravity = 1.2f,
                    HasAtmosphere = true,
                    AtmosphereDensity = 1.0f,
                    Breathable = true,
                    SolarRadiationProtection = 0.75f,
                },
            });

            worlds.Add(new World
            {
                Subtype = "Triton",
                AuthoredTemperatureLevel = "ExtremeFreeze",
                RealAnalogue = "Triton (38 K) — but SE's is breathable",
                RealMeanKelvin = 38f,
                Engine = new PlanetThermalDerivation.Engine
                {
                    DeepestGroundMetres = 2000f,
                    SurfaceTemperatureLevel = 0f,
                    SurfaceGravity = 1.0f,
                    HasAtmosphere = true,
                    AtmosphereDensity = 1.0f,
                    Breathable = true,
                    SolarRadiationProtection = 1.8f,
                },
            });

            worlds.Add(new World
            {
                Subtype = "Europa",
                AuthoredTemperatureLevel = "ExtremeFreeze",
                RealAnalogue = "Europa",
                RealMeanKelvin = 102f,
                Engine = new PlanetThermalDerivation.Engine
                {
                    DeepestGroundMetres = 285f,
                    SurfaceTemperatureLevel = 0f,
                    SurfaceGravity = 0.25f,
                    HasAtmosphere = true,
                    AtmosphereDensity = 1.0f,
                    Breathable = false,
                    SolarRadiationProtection = 0f,
                },
            });

            worlds.Add(new World
            {
                Subtype = "Titan",
                AuthoredTemperatureLevel = null,
                RealAnalogue = "Titan",
                RealMeanKelvin = 94f,
                RealSwingKelvin = 3f,
                OverrideReason =
                    "unauthored, so the engine default would make an ice moon 288 K. Anchored to "
                    + "Titan's measured 94 K, with the very small daily range its thick cold "
                    + "nitrogen atmosphere gives it.",
                Engine = new PlanetThermalDerivation.Engine
                {
                    DeepestGroundMetres = 285f,
                    SurfaceTemperatureLevel = 0.5f,     // Cozy, by default — see the report
                    SurfaceGravity = 0.25f,
                    HasAtmosphere = true,
                    AtmosphereDensity = 1.0f,
                    Breathable = true,
                    SolarRadiationProtection = 1.8f,
                },
            });

            worlds.Add(new World
            {
                Subtype = "Moon",
                AuthoredTemperatureLevel = null,
                RealAnalogue = "the Moon",
                RealMeanKelvin = 245f,
                RealSwingKelvin = 290f,
                OverrideReason =
                    "unauthored. The derivation already gets an airless world's enormous swing right "
                    + "from having no atmosphere; this pins it to the Moon's measured 100 K before "
                    + "dawn and 390 K at noon rather than leaving the mean at the Cozy default.",
                Engine = new PlanetThermalDerivation.Engine
                {
                    DeepestGroundMetres = 285f,
                    SurfaceTemperatureLevel = 0.5f,
                    SurfaceGravity = 0.25f,
                    HasAtmosphere = false,
                    AtmosphereDensity = 0f,
                    Breathable = false,
                    SolarRadiationProtection = 1f,
                },
            });

            return worlds;
        }

        // ---- the file --------------------------------------------------------------------------

        /// <summary>
        /// The whole of <c>Data/Planets.xml</c>: the default entry the mod falls back to, and one
        /// entry per shipped world.
        ///
        /// Generated rather than typed, so that when a definition changes the answer changes with
        /// it, and so the reasoning behind each figure lives in code beside a test rather than in a
        /// comment nobody can check.
        /// </summary>
        public static string Xml()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
            sb.Append("<!--\n");
            sb.Append("  GENERATED FILE — do not edit by hand.\n\n");
            // No double hyphen anywhere in this comment: a "--" inside an XML comment is illegal
            // XML, and Definition Extensions reads this file with a strict serialiser. The shipped
            // file carried the regen command verbatim, so every planet entry in it was silently
            // unread by every world that ever loaded it. The command lives in the README instead.
            sb.Append("  Regenerate with the sim's planets command; see tests/README.md.\n\n");
            sb.Append("  Every figure below is derived from the world's own generator definition by\n");
            sb.Append("  Thermodynamics.Core.PlanetThermalDerivation. What each derivation is and why is\n");
            sb.Append("  documented there and in docs/environment.md; the per-entry comments here say\n");
            sb.Append("  what the engine supplied and what came out.\n");
            sb.Append("-->\n");
            sb.Append("<Definitions xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">\n");
            sb.Append("\t<CubeBlocks>\n");

            // The fallback entry keeps the shipped earthlike defaults exactly as they were, so a
            // world with an unrecognised planet is unchanged by this file existing.
            Entry(sb, "DefaultThermodynamics", new PlanetThermalProperties(),
                "The fallback for any planet without an entry of its own. Earthlike, and unchanged\n"
                + "\t\t\tfrom what this mod shipped before the per-planet entries existed.");

            List<World> worlds = Vanilla();
            for (int i = 0; i < worlds.Count; i++)
            {
                World world = worlds[i];

                string note =
                    "From the definition: gravity " + world.Engine.SurfaceGravity.ToString("0.##", CultureInfo.InvariantCulture)
                    + " g, " + (world.Engine.HasAtmosphere
                        ? "air density " + world.Engine.AtmosphereDensity.ToString("0.##", CultureInfo.InvariantCulture)
                            + (world.Engine.Breathable ? " breathable" : " unbreathable")
                        : "no atmosphere")
                    + ", solar protection " + world.Engine.SolarRadiationProtection.ToString("0.##", CultureInfo.InvariantCulture)
                    + ".\n\t\t\tDefaultSurfaceTemperature "
                    + (world.AuthoredTemperatureLevel ?? "not authored, so Cozy by engine default")
                    + ".\n\t\t\tReal analogue: " + world.RealAnalogue + ", mean "
                    + world.RealMeanKelvin.ToString("n0", CultureInfo.InvariantCulture) + " K.";

                if (world.OverrideReason != null)
                {
                    note += "\n\n\t\t\tDEPARTS FROM THE DERIVATION: " + world.OverrideReason;
                }

                Entry(sb, world.Subtype, world.Shipped, note);
            }

            sb.Append("\t</CubeBlocks>\n");
            sb.Append("</Definitions>\n");

            return sb.ToString();
        }

        private static void Entry(
            StringBuilder sb, string subtype, PlanetThermalProperties p, string note)
        {
            sb.Append("\n\t\t<Definition>\n");
            sb.Append("\t\t\t<Id>\n");
            sb.Append("\t\t\t\t<TypeId>PlanetGeneratorDefinition</TypeId>\n");
            sb.Append("\t\t\t\t<SubtypeId>").Append(subtype).Append("</SubtypeId>\n");
            sb.Append("\t\t\t</Id>\n\n");
            sb.Append("\t\t\t<!--\n\t\t\t").Append(note).Append("\n\t\t\t-->\n");
            sb.Append("\t\t\t<ModExtensions>\n");
            sb.Append("\t\t\t\t<Group Name=\"ThermalPlanetProperties\">\n");

            Value(sb, "NightTemperature", p.NightTemperature,
                "Air temperature at the equator at midnight, K.");
            Value(sb, "DayTemperature", p.DayTemperature,
                "Air temperature at the equator at noon, K.");
            Value(sb, "PoleTemperatureDrop", p.PoleTemperatureDrop,
                "How much colder a pole is than the equator, K. Air carries heat polewards, so a\n"
                + "\t\t\t\t\tthin-aired world keeps more of what each latitude is given.");
            Value(sb, "AmbientLagSeconds", p.AmbientLagSeconds,
                "Seconds the air takes to answer the sun. What puts the day's peak after noon,\n"
                + "\t\t\t\t\tand the fallback for a session that has not yet measured a day.");
            Value(sb, "AmbientLagShareOfDay", p.AmbientLagShareOfDay,
                "The same lag as a share of this world's own day, which is what it should be.\n"
                + "\t\t\t\t\tEarth peaks about two hours after noon out of twenty-four.");
            Value(sb, "AmbientLapseRate", p.AmbientLapseRate,
                "How much colder a kilometre up is, K/km. Derived: g/cp, two thirds for moisture.");
            Value(sb, "UndergroundTemperature", p.UndergroundTemperature,
                "Rock temperature below the damping depth, K — the surface's own daily mean.");
            Value(sb, "UndergroundDampingDepth", p.UndergroundDampingDepth,
                "Metres of rock over which the day-night swing dies out.");
            Value(sb, "CoreTemperature", p.CoreTemperature,
                "Rock temperature the model warms toward below the deadzone, K.");
            Value(sb, "SealevelDeadzone", p.SealevelDeadzone,
                "Metres below sea level before the rock starts warming toward the core.");
            Value(sb, "SolarDecay", p.SolarDecay,
                "Fraction of the sun a full atmosphere absorbs, 0..1. From the engine's own\n"
                + "\t\t\t\t\tSolarRadiationProtectionFactor.");
            Value(sb, "ConvectionCoefficient", p.ConvectionCoefficient,
                "Convective coefficient at the surface, W/(m^2 K), in proportion to the air.");
            Value(sb, "UndergroundConvectionCoefficient", p.UndergroundConvectionCoefficient,
                "The same for a grid buried in rock, W/(m^2 K). Rock is a far worse heat sink\n"
                + "\t\t\t\t\tthan moving air: 2k/D for k 2.5 W/(m K) over a 2.5 m block.");

            sb.Append("\t\t\t\t</Group>\n");
            sb.Append("\t\t\t</ModExtensions>\n");
            sb.Append("\t\t</Definition>\n");
        }

        private static void Value(StringBuilder sb, string name, float value, string note)
        {
            sb.Append("\n\t\t\t\t\t<!--").Append(note).Append("-->\n");
            sb.Append("\t\t\t\t\t<Decimal Name=\"").Append(name).Append("\" Value=\"")
              .Append(value.ToString("0.####", CultureInfo.InvariantCulture)).Append("\" />\n");
        }

        // ---- the report ------------------------------------------------------------------------

        public static string Report()
        {
            List<World> worlds = Vanilla();
            StringBuilder sb = new StringBuilder();

            sb.Append("Planet thermals, derived from each world's own generator definition\n\n");

            sb.Append("What the engine says\n");
            sb.Append("  world        level          gravity   air        breathable  solarProt\n");
            for (int i = 0; i < worlds.Count; i++)
            {
                World w = worlds[i];
                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-12} {1,-14} {2,5:n2} g   {3,-10} {4,-11} {5,5:n2}\n",
                    w.Subtype,
                    w.AuthoredTemperatureLevel ?? "Cozy (default)",
                    w.Engine.SurfaceGravity,
                    w.Engine.HasAtmosphere
                        ? w.Engine.AtmosphereDensity.ToString("n2", CultureInfo.InvariantCulture)
                        : "none",
                    w.Engine.HasAtmosphere ? (w.Engine.Breathable ? "yes" : "no") : "-",
                    w.Engine.SolarRadiationProtection));
            }

            sb.Append("\nWhat ships\n");
            sb.Append("  world          night K   day K   swing   pole drop   lapse K/km   lag s   decay   convection\n");
            for (int i = 0; i < worlds.Count; i++)
            {
                World w = worlds[i];
                PlanetThermalProperties p = w.Shipped;

                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-12} {1,8:n1} {2,7:n1} {3,7:n1} {4,11:n0} {5,12:n2} {6,7:n0} {7,7:n2} {8,12:n1}\n",
                    w.Subtype, p.NightTemperature, p.DayTemperature,
                    p.DayTemperature - p.NightTemperature, p.PoleTemperatureDrop,
                    p.AmbientLapseRate, p.AmbientLagSeconds, p.SolarDecay, p.ConvectionCoefficient));
            }

            sb.Append("\nWhere the shipped figures depart from the pure derivation, and why\n");
            int departures = 0;
            for (int i = 0; i < worlds.Count; i++)
            {
                World w = worlds[i];
                if (w.OverrideReason == null) continue;
                departures++;

                PlanetThermalProperties d = w.Derived;
                PlanetThermalProperties p = w.Shipped;

                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0}: derived {1:n0}..{2:n0} K, shipped {3:n0}..{4:n0} K\n",
                    w.Subtype, d.NightTemperature, d.DayTemperature,
                    p.NightTemperature, p.DayTemperature));
                sb.Append("    ").Append(w.OverrideReason).Append('\n');
            }

            if (departures == 0) sb.Append("  none\n");

            sb.Append("\n  The rule: an authored DefaultSurfaceTemperature is followed whatever the real\n");
            sb.Append("  body does — SE's Triton is breathable with full-density air and the definition\n");
            sb.Append("  calls it ExtremeFreeze, so it is. An *unauthored* one is an omission rather than\n");
            sb.Append("  a statement, and reading Cozy out of it as intent would put Titan at 288 K.\n");

            return sb.ToString();
        }
    }
}
