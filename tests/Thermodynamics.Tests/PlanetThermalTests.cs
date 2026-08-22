using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using System.Xml.Linq;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Thermal properties derived from a planet's own generator definition, and the shipped file
    /// they generate.
    ///
    /// <para>Every world in this mod used to run one entry — an earthlike climate — so the Moon,
    /// Titan and Pertam were all 294 K by day. These hold the derivation that replaced it, the
    /// judgement calls layered on top of it, and the fact that <c>Data/Planets.xml</c> is what this
    /// code produces rather than something that has drifted away from it.</para>
    /// </summary>
    public class PlanetThermalTests
    {
        private static PlanetLab.World Find(string subtype)
        {
            List<PlanetLab.World> worlds = PlanetLab.Vanilla();
            for (int i = 0; i < worlds.Count; i++)
            {
                if (worlds[i].Subtype == subtype) return worlds[i];
            }
            throw new ArgumentException("no world " + subtype);
        }

        private static string RepoRoot()
        {
            // Delegates rather than walking up from the assembly, because the build output no
            // longer sits inside the repository — see Directory.Build.props. ShippedBlocks anchors
            // itself to its own compiled-in source path, which survives the move.
            return Thermodynamics.Harness.ShippedBlocks.RepoRoot();
        }

        // ---- the levels ------------------------------------------------------------------------

        [Fact]
        public void TheFiveEngineLevelsMapToTheirAnchoredTemperatures()
        {
            // The engine's DefaultSurfaceTemperature is a five-level enum turned into 0, 0.25, 0.5,
            // 0.75 or 1 by MySectorWeatherComponent.LevelToTemperature. Nothing in the engine turns
            // those into kelvin, so the five anchors are authored — each against a real body.
            Assert.Equal(100f, PlanetThermalDerivation.MeanTemperature(0f), 2);
            Assert.Equal(215f, PlanetThermalDerivation.MeanTemperature(0.25f), 2);
            Assert.Equal(288f, PlanetThermalDerivation.MeanTemperature(0.5f), 2);
            Assert.Equal(325f, PlanetThermalDerivation.MeanTemperature(0.75f), 2);
            Assert.Equal(450f, PlanetThermalDerivation.MeanTemperature(1f), 2);
        }

        [Fact]
        public void TheAnchorsAreNotEvenlySpacedAndInterpolatingThemLinearlyWouldFreezeEarth()
        {
            // Worth stating as a test because it is the reason the anchors are a table rather than a
            // range: spread evenly from 100 K to 450 K, Cozy would land at 275 K — below freezing,
            // for the level the game gives an earthlike world.
            float evenlySpaced = 100f + ((450f - 100f) * 0.5f);

            Assert.True(Math.Abs(evenlySpaced - 288f) > 10f);
            Assert.Equal(288f, PlanetThermalDerivation.MeanTemperature(0.5f), 1);
        }

        [Fact]
        public void ALevelBetweenTwoAnchorsLandsBetweenThem()
        {
            // A modded planet is free to sit between levels. It should not snap to one.
            float between = PlanetThermalDerivation.MeanTemperature(0.375f);
            Assert.InRange(between, 215f, 288f);
        }

        // ---- the derivations -------------------------------------------------------------------

        [Fact]
        public void TheLapseRateIsGravityOverSpecificHeatAndComesOutAtEarthsMeasuredValue()
        {
            // The one figure here that is a derivation rather than a judgement. Earth's dry adiabatic
            // rate is 9.8 K/km and its environmental rate about 6.5; an earthlike world comes out at
            // 6.4, which is the check that the arithmetic means what it says.
            float earthlike = PlanetThermalDerivation.LapseRate(1f, true, 1f);
            Assert.InRange(earthlike, 6.2f, 6.7f);
        }

        [Fact]
        public void LowGravityAndHeavierAirBothFlattenTheLapseRate()
        {
            // Titan: a quarter of Earth's gravity, so a quarter of the rate. This is why a mountain
            // on a low-gravity moon is barely colder at the top than at the bottom.
            float titan = PlanetThermalDerivation.LapseRate(0.25f, true, 1f);
            float earth = PlanetThermalDerivation.LapseRate(1f, true, 1f);

            Assert.InRange(titan / earth, 0.2f, 0.3f);

            // Unbreathable air is taken as carbon dioxide, which has a lower specific heat, so the
            // same gravity gives a steeper rate.
            Assert.True(
                PlanetThermalDerivation.LapseRate(1f, false, 1f)
                > PlanetThermalDerivation.LapseRate(1f, true, 1f));
        }

        [Fact]
        public void AWorldWithNoAirHasNoLapseRateAndNoConvectionAndAlmostNoLag()
        {
            // There is nothing to cool as it rises, nothing to carry heat off a hull, and no air to
            // remember this morning. All three must be zero or near it rather than merely small.
            Assert.Equal(0f, PlanetThermalDerivation.LapseRate(1f, true, 0f), 4);
            Assert.Equal(0f, PlanetThermalDerivation.ConvectionCoefficient(0f), 4);
            Assert.Equal(0f, PlanetThermalDerivation.SolarDecay(1.8f, 0f), 4);
            Assert.InRange(PlanetThermalDerivation.LagSeconds(0f), 0f, 10f);
        }

        [Fact]
        public void TakingTheAirAwayOpensTheDayNightSwingEnormously()
        {
            // Earth's equatorial range is about 11 K. The Moon runs from 100 K before dawn to 390 K
            // at noon. Air is the whole of the difference.
            Assert.Equal(PlanetThermalDerivation.ThickAirSwing, PlanetThermalDerivation.Swing(1f), 2);
            Assert.Equal(PlanetThermalDerivation.AirlessSwing, PlanetThermalDerivation.Swing(0f), 2);

            Assert.True(PlanetThermalDerivation.Swing(0f) > PlanetThermalDerivation.Swing(1f) * 15f);
        }

        [Fact]
        public void TheFirstBreathOfAirDoesMostOfTheDamping()
        {
            // Which is why Mars, at under a hundredth of Earth's pressure, still has a far smaller
            // range than the Moon. Half an atmosphere should already be much closer to Earth's
            // swing than to an airless one.
            float half = PlanetThermalDerivation.Swing(0.5f);
            float airless = PlanetThermalDerivation.Swing(0f);

            Assert.True(half < airless * 0.4f, "half an atmosphere still swung " + half + " K");
        }

        [Fact]
        public void TheEnginesSolarProtectionBecomesAPlausibleShareOfSunlight()
        {
            // An earthlike world authors 1.8, and Earth's atmosphere really does absorb and scatter
            // somewhere around a quarter to a third of incoming sunlight before it reaches the
            // ground. Mars authors 0.2 and gets almost none.
            Assert.InRange(PlanetThermalDerivation.SolarDecay(1.8f, 1f), 0.25f, 0.35f);
            Assert.InRange(PlanetThermalDerivation.SolarDecay(0.2f, 1f), 0f, 0.06f);
            Assert.Equal(0f, PlanetThermalDerivation.SolarDecay(0f, 1f), 4);
        }

        [Fact]
        public void NothingDerivedIsEverNonsense()
        {
            // A sweep over every combination a modded definition could present, including ones no
            // shipped world uses.
            float[] levels = { 0f, 0.25f, 0.5f, 0.75f, 1f, 0.37f };
            float[] gravities = { 0f, 0.05f, 0.25f, 1f, 2f, 10f };
            float[] airs = { 0f, 0.01f, 0.5f, 1f, 2f };

            foreach (float level in levels)
                foreach (float gravity in gravities)
                    foreach (float air in airs)
                        foreach (bool breathable in new[] { true, false })
                        {
                            PlanetThermalProperties p = PlanetThermalDerivation.Derive(
                                new PlanetThermalDerivation.Engine
                                {
                                    SurfaceTemperatureLevel = level,
                                    SurfaceGravity = gravity,
                                    HasAtmosphere = air > 0f,
                                    AtmosphereDensity = air,
                                    Breathable = breathable,
                                    SolarRadiationProtection = 1.8f,
                                });

                            Assert.True(p.NightTemperature >= 0f, "negative night temperature");
                            Assert.True(p.DayTemperature >= p.NightTemperature, "night warmer than day");
                            Assert.False(float.IsNaN(p.AmbientLapseRate));
                            Assert.InRange(p.SolarDecay, 0f, 0.9f);
                            Assert.True(p.ConvectionCoefficient >= 0f);
                            Assert.True(p.AmbientLagSeconds > 0f);
                            Assert.InRange(p.PoleTemperatureDrop, 0f, 200f);
                        }
        }

        // ---- the shipped worlds ------------------------------------------------------------------

        [Fact]
        public void EveryShippedWorldGetsItsOwnClimateRatherThanOneSharedOne()
        {
            // The defect this whole exercise exists to fix: before it, every planet in the game ran
            // DefaultThermodynamics and the Moon was as warm as Earth.
            List<PlanetLab.World> worlds = PlanetLab.Vanilla();
            Assert.Equal(8, worlds.Count);

            HashSet<string> distinct = new HashSet<string>();
            for (int i = 0; i < worlds.Count; i++)
            {
                PlanetThermalProperties p = worlds[i].Shipped;
                distinct.Add(p.DayTemperature.ToString("n1") + "/" + p.NightTemperature.ToString("n1"));
            }

            Assert.True(distinct.Count >= 5,
                "only " + distinct.Count + " distinct climates across eight worlds");
        }

        [Fact]
        public void TheMoonIsAirlessAndBehavesLikeIt()
        {
            PlanetThermalProperties moon = Find("Moon").Shipped;

            Assert.Equal(0f, moon.ConvectionCoefficient, 3);
            Assert.Equal(0f, moon.AmbientLapseRate, 3);
            Assert.Equal(0f, moon.SolarDecay, 3);

            Assert.True(moon.DayTemperature - moon.NightTemperature > 200f,
                "an airless world should swing enormously, got "
                + (moon.DayTemperature - moon.NightTemperature));

            // And the poles keep far less of what the equator gets, with no air to carry it.
            Assert.True(moon.PoleTemperatureDrop > Find("EarthLike").Shipped.PoleTemperatureDrop);
        }

        [Fact]
        public void AnAuthoredTemperatureLevelIsFollowedEvenWhereTheRealBodyDisagrees()
        {
            // SE's Triton is breathable, has full-density air and 1 g. It is nothing like the real
            // Triton at 38 K. But the definition authors ExtremeFreeze, and an authored field is the
            // game making a decision — so it is followed and no override applies.
            PlanetLab.World triton = Find("Triton");

            Assert.Equal("ExtremeFreeze", triton.AuthoredTemperatureLevel);
            Assert.Null(triton.OverrideReason);
            Assert.InRange(triton.Shipped.NightTemperature, 80f, 110f);
        }

        [Fact]
        public void AnUnauthoredLevelOnAWorldNamedForARealPlaceIsNotTreatedAsIntent()
        {
            // Titan, Mars and the Moon do not author DefaultSurfaceTemperature at all. Reading the
            // engine's Cozy default as a decision would put an ice moon at 288 K and have players
            // landing on it in shirtsleeves. Silence is an omission, and the mod's job is to fill it.
            foreach (string name in new[] { "Titan", "Mars", "Moon" })
            {
                PlanetLab.World world = Find(name);

                Assert.Null(world.AuthoredTemperatureLevel);
                Assert.NotNull(world.OverrideReason);

                float derived = (world.Derived.DayTemperature + world.Derived.NightTemperature) * 0.5f;
                float shipped = (world.Shipped.DayTemperature + world.Shipped.NightTemperature) * 0.5f;

                Assert.True(Math.Abs(derived - shipped) > 20f,
                    name + " override changed nothing: " + derived + " vs " + shipped);
            }

            Assert.InRange(Find("Titan").Shipped.DayTemperature, 80f, 110f);
            Assert.InRange(Find("Mars").Shipped.DayTemperature, 230f, 260f);
        }

        [Fact]
        public void AWorldTheGameIsSilentAboutAndThatIsNotARealPlaceKeepsTheDerivation()
        {
            // EarthLike, Alien and Pertam are either authored or not named for anywhere, so nothing
            // is layered on top of what the definition says.
            foreach (string name in new[] { "EarthLike", "Alien", "Pertam" })
            {
                Assert.Null(Find(name).OverrideReason);
            }
        }

        [Fact]
        public void ThePlanetsWithLowGravityHaveTheFlattestLapseRates()
        {
            // Titan and Europa at a quarter g against Pertam at 1.2. This is the derivation showing
            // through into the shipped file rather than being averaged away.
            Assert.True(Find("Titan").Shipped.AmbientLapseRate < 2.5f);
            Assert.True(Find("Europa").Shipped.AmbientLapseRate < 2.5f);
            Assert.True(Find("Pertam").Shipped.AmbientLapseRate > 7f);
        }

        // ---- the generated file --------------------------------------------------------------------

        /// <summary>
        /// The shipped file is byte-for-byte what the code that explains it produces.
        ///
        /// <para>
        /// <c>environment.md</c>, "The file is generated", said this was checked and it was not. The claim is worth
        /// making true rather than retracting: every figure in that file is derived from a world's
        /// own generator definition, the departures from the derivation are written into it beside
        /// the entries they affect, and a number that cannot be regenerated from the reasoning
        /// behind it is a number nobody can check. Without this, a change to
        /// <see cref="PlanetLab"/> that was never written out would leave the reasoning and the
        /// shipped climates describing different worlds.
        /// </para>
        ///
        /// <para>
        /// It is the file the mod reads, so the cases below still read the file rather than the
        /// generator — the point of this one is that there is no difference to read.
        /// </para>
        /// </summary>
        [Fact]
        public void TheShippedPlanetsFileIsWhatThisCodeGenerates()
        {
            string path = Path.Combine(RepoRoot(), "Data", "Planets.xml");
            string onDisk = File.ReadAllText(path);
            string generated = PlanetLab.Xml();

            if (onDisk == generated) return;

            // Name the first line that differs: the file is six hundred lines and a diff of the
            // whole of it in an assertion message is not readable.
            string[] a = onDisk.Replace("\r\n", "\n").Split('\n');
            string[] b = generated.Replace("\r\n", "\n").Split('\n');

            for (int i = 0; i < a.Length && i < b.Length; i++)
            {
                if (a[i] == b[i]) continue;

                Assert.Fail("Data/Planets.xml has drifted from PlanetLab.Xml() at line " + (i + 1)
                    + ".\n  on disk:    " + a[i]
                    + "\n  generated:  " + b[i]
                    + "\nRegenerate with: dotnet run --project Thermodynamics.Sim -- planets"
                    + " --write ../Data/Planets.xml");
            }

            Assert.Fail("Data/Planets.xml has " + a.Length + " lines against the generator's "
                + b.Length + "; regenerate it.");
        }

        /// <summary>
        /// Data/Planets.xml is what the mod reads, so this checks the file itself rather than
        /// the generator — <see cref="TheShippedPlanetsFileIsWhatThisCodeGenerates"/> is what
        /// holds the two together.
        /// </summary>
        [Fact]
        public void TheShippedPlanetsFileIsCompleteAndReadable()
        {
            string path = Path.Combine(RepoRoot(), "Data", "Planets.xml");
            string onDisk = File.ReadAllText(path);

            Assert.Contains("<SubtypeId>DefaultThermodynamics</SubtypeId>", onDisk);

            List<PlanetLab.World> worlds = PlanetLab.Vanilla();
            for (int i = 0; i < worlds.Count; i++)
            {
                Assert.Contains("<SubtypeId>" + worlds[i].Subtype + "</SubtypeId>", onDisk);
            }

            // Every entry has to carry every property the game reads, for the reason Cubes.xml does:
            // an omitted value arrives as zero rather than as the fallback's number.
            foreach (XElement definition in XDocument.Parse(onDisk).Descendants("Definition"))
            {
                string subtype = (string)definition.Element("Id").Element("SubtypeId");
                HashSet<string> declared = new HashSet<string>();
                foreach (XElement value in definition.Descendants("Decimal"))
                {
                    XAttribute key = value.Attribute("Name");
                    if (key != null) declared.Add(key.Value);
                }

                foreach (string required in PlanetProperties)
                {
                    Assert.True(declared.Contains(required), subtype + " omits " + required);
                }
            }
        }

        /// <summary>Every property a planet entry must carry.</summary>
        private static readonly string[] PlanetProperties =
        {
            "DayTemperature",
            "NightTemperature",
            "PoleTemperatureDrop",
            "AmbientLagSeconds",
            "AmbientLapseRate",
            "UndergroundTemperature",
            "UndergroundDampingDepth",
            "CoreTemperature",
            "SealevelDeadzone",
            "SolarDecay",
            "ConvectionCoefficient",
        };

        [Fact]
        public void TheFileCarriesAnEntryForEveryShippedWorldPlusTheFallback()
        {
            string xml = PlanetLab.Xml();

            Assert.Contains("<SubtypeId>DefaultThermodynamics</SubtypeId>", xml);

            List<PlanetLab.World> worlds = PlanetLab.Vanilla();
            for (int i = 0; i < worlds.Count; i++)
            {
                Assert.Contains("<SubtypeId>" + worlds[i].Subtype + "</SubtypeId>", xml);
            }

            // Nine definitions: the fallback plus eight worlds.
            Assert.Equal(9, Regex.Matches(xml, "<SubtypeId>").Count);
        }

        [Fact]
        public void TheFallbackEntryIsUnchangedFromWhatTheModAlwaysShipped()
        {
            // A world running a planet this file does not name must behave exactly as it did before
            // the per-planet entries existed.
            PlanetThermalProperties defaults = new PlanetThermalProperties();
            string xml = PlanetLab.Xml();

            int start = xml.IndexOf("DefaultThermodynamics", StringComparison.Ordinal);
            int end = xml.IndexOf("</Definition>", start, StringComparison.Ordinal);
            string entry = xml.Substring(start, end - start);

            Assert.Contains("\"DayTemperature\" Value=\"" + defaults.DayTemperature.ToString("0.####"), entry);
            Assert.Contains("\"NightTemperature\" Value=\"" + defaults.NightTemperature.ToString("0.####"), entry);
            Assert.Contains("\"ConvectionCoefficient\" Value=\"" + defaults.ConvectionCoefficient.ToString("0.####"), entry);
        }

        [Fact]
        public void EveryEntryCarriesEveryPropertyTheModReads()
        {
            // A property missing from an entry silently takes the reader's default, which is how a
            // planet ends up half-configured with nothing saying so.
            string[] required =
            {
                "NightTemperature", "DayTemperature", "PoleTemperatureDrop", "AmbientLagSeconds",
                "AmbientLapseRate", "UndergroundTemperature", "UndergroundDampingDepth",
                "CoreTemperature", "SealevelDeadzone", "SolarDecay", "ConvectionCoefficient",
            };

            string xml = PlanetLab.Xml();

            for (int i = 0; i < required.Length; i++)
            {
                int count = Regex.Matches(xml, "Name=\"" + required[i] + "\"").Count;
                Assert.Equal(9, count);
            }
        }

        [Fact]
        public void EveryDepartureFromTheDerivationSaysWhyInTheFileItself()
        {
            string xml = PlanetLab.Xml();
            List<PlanetLab.World> worlds = PlanetLab.Vanilla();

            int departures = 0;
            for (int i = 0; i < worlds.Count; i++)
            {
                if (worlds[i].OverrideReason == null) continue;
                departures++;

                Assert.Contains(worlds[i].OverrideReason, xml);
            }

            Assert.True(departures >= 3, "expected the unauthored worlds to carry a reason each");
            Assert.Contains("DEPARTS FROM THE DERIVATION", xml);
        }
    }
}
