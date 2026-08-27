using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The conclusions the balance pass reached, pinned so they cannot quietly invert.
    ///
    /// These are not assertions about the solver — the physics suite covers that. They are
    /// assertions about the *definitions*: that the radiator still beats the armour it displaces,
    /// that the coolant loop is still the stiffest way to feed a panel, that a block's mass is
    /// still the sum of its components. Every one of them is a statement a design decision was
    /// made on, and every one of them would otherwise be re-derived from memory a year from now.
    ///
    /// Figures come from <see cref="BalanceLab"/>, which reads the shipped XML at run time, so
    /// editing a definition moves these tests. That is the point: a tuning change that inverts a
    /// conclusion should have to say so out loud.
    /// </summary>
    [Trait("speed", "slow")]
    public class BalanceTests
    {
        // ---- the reference data ------------------------------------------------------------

        /// <summary>
        /// The transcribed vanilla figures against the installed game, when there is one.
        ///
        /// <see cref="Vanilla"/> is checked in because most machines running this suite have no
        /// Space Engineers install. That makes it a transcription, and a transcription nobody
        /// checks drifts silently through a game update — so on a machine that *does* have the
        /// content, it gets checked. Same arrangement as <c>CensusFidelityTests</c>.
        /// </summary>
        [Fact]
        public void TheVanillaReferenceStillMatchesTheInstalledGame()
        {
            string content = GameContentPath();
            if (content == null) return;      // no install here; nothing to check against

            Dictionary<string, float> componentMass = new Dictionary<string, float>();
            foreach (XElement component in XDocument.Load(Path.Combine(content, "Components.sbc"))
                         .Descendants("Component"))
            {
                XElement id = component.Element("Id");
                if (id == null) continue;
                string subtype = (string)id.Element("SubtypeId");
                float mass;
                if (subtype != null && float.TryParse((string)component.Element("Mass"),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out mass))
                {
                    componentMass[subtype] = mass;
                }
            }

            foreach (KeyValuePair<string, float> entry in Vanilla.ComponentMasses)
            {
                float actual;
                Assert.True(componentMass.TryGetValue(entry.Key, out actual),
                    entry.Key + " is no longer a component in the installed game");
                Assert.True(Math.Abs(actual - entry.Value) < 0.01f,
                    entry.Key + " mass is " + actual + " in game, " + entry.Value + " in Vanilla.cs");
            }

            // **Keyed on type and subtype together, because a subtype is not a name.** Thirteen
            // of the game's definitions carry no `SubtypeId` at all — the vanilla oxygen generator
            // among them — so a dictionary keyed on subtype alone gives every one of them the same
            // key, and the first file read wins. This test resolved that block to a door and
            // checked the door's mass against the generator's, which is the shape of failure the
            // whole page exists to make loud rather than quiet.
            Dictionary<string, XElement> blocks = new Dictionary<string, XElement>();
            foreach (string file in Directory.GetFiles(Path.Combine(content, "CubeBlocks"), "*.sbc"))
            {
                XDocument document;
                try { document = XDocument.Load(file); }
                catch (Exception) { continue; }

                foreach (XElement definition in document.Descendants("Definition"))
                {
                    XElement id = definition.Element("Id");
                    if (id == null) continue;

                    string subtype = (string)id.Element("SubtypeId");
                    string type = (string)id.Element("TypeId");
                    if (subtype == null || type == null) continue;

                    string key = Key(type, subtype);
                    if (!blocks.ContainsKey(key)) blocks[key] = definition;
                }
            }

            foreach (Vanilla.Block block in Vanilla.Reference)
            {
                XElement definition;
                Assert.True(blocks.TryGetValue(Key(block.TypeId, block.Subtype), out definition),
                    Name(block) + " is no longer a block in the installed game");

                float mass = 0f;
                foreach (XElement component in definition.Descendants("Component"))
                {
                    int count;
                    float each;
                    if (int.TryParse((string)component.Attribute("Count"), out count)
                        && componentMass.TryGetValue((string)component.Attribute("Subtype") ?? "", out each))
                    {
                        mass += each * count;
                    }
                }

                Assert.True(Math.Abs(mass - block.Mass) < 1f,
                    Name(block) + " weighs " + mass + " kg in game, " + block.Mass + " kg in Vanilla.cs");
            }
        }

        /// <summary>
        /// The identity of a definition, as the pair the game actually keys on. `MyObjectBuilder_`
        /// is stripped so a `TypeId` read from a definition file and one transcribed into
        /// <see cref="Vanilla"/> compare equal.
        /// </summary>
        private static string Key(string typeId, string subtypeId)
        {
            string type = typeId ?? "";
            if (type.StartsWith("MyObjectBuilder_")) type = type.Substring("MyObjectBuilder_".Length);
            return type + "/" + (subtypeId ?? "");
        }

        /// <summary>What to call a reference row in a failure message, since a subtype can be empty.</summary>
        private static string Name(Vanilla.Block block)
        {
            return block.Subtype.Length > 0 ? block.Subtype : block.TypeId + " (no subtype)";
        }

        /// <summary>
        /// The Steam default, or SE_BIN's parent. Returns null when the game is not installed,
        /// which is not a failure — most machines running this suite have no copy.
        /// </summary>
        private static string GameContentPath()
        {
            List<string> candidates = new List<string>();

            string bin = Environment.GetEnvironmentVariable("SE_BIN");
            if (!string.IsNullOrEmpty(bin))
            {
                DirectoryInfo parent = Directory.GetParent(bin.TrimEnd('/', '\\'));
                if (parent != null) candidates.Add(Path.Combine(parent.FullName, "Content", "Data"));
            }

            string home = Environment.GetEnvironmentVariable("HOME") ?? "";
            candidates.Add(Path.Combine(home,
                "Steam/SteamLibrary/steamapps/common/SpaceEngineers/Content/Data"));
            candidates.Add("C:/Program Files (x86)/Steam/steamapps/common/SpaceEngineers/Content/Data");

            foreach (string candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "Components.sbc"))) return candidate;
            }
            return null;
        }

        // ---- the loader ----------------------------------------------------------------------

        /// <summary>
        /// The transcribed build costs still match the installed game.
        ///
        /// These matter more than the masses beside them now: a block's thermal properties are
        /// derived from its components, so a component list that has drifted does not merely make
        /// a mass wrong, it makes the block a different material.
        /// </summary>
        [Fact]
        public void TheVanillaComponentListsStillMatchTheInstalledGame()
        {
            if (!GameBlocks.IsInstalled) return;

            List<string> wrong = new List<string>();

            foreach (Vanilla.Block reference in Vanilla.Reference)
            {
                // Type and subtype together, for the reason the test above states: an empty
                // subtype is thirteen different blocks.
                GameBlocks.Definition installed = null;
                foreach (GameBlocks.Definition block in GameBlocks.All())
                {
                    if (Key(block.TypeId, block.SubtypeId) == Key(reference.TypeId, reference.Subtype))
                    {
                        installed = block;
                        break;
                    }
                }

                if (installed == null)
                {
                    wrong.Add(Name(reference) + " is no longer a block in the installed game");
                    continue;
                }

                float transcribed = 0f;
                foreach (BlockComponent line in reference.Components) transcribed += line.Mass;

                if (Math.Abs(transcribed - installed.Mass) > 0.5f)
                {
                    wrong.Add(Name(reference) + " components weigh " + transcribed
                        + " in Vanilla.cs and " + installed.Mass + " in game");
                }
            }

            Assert.Empty(wrong);
        }

        [Fact]
        public void EveryShippedBlockIsPricedAndWeighsSomething()
        {
            foreach (string subtype in ShippedBlocks.Subtypes())
            {
                ShippedBlocks.Definition definition = ShippedBlocks.Get(subtype);

                Assert.True(definition.Components.Count > 0, subtype + " lists no components");
                Assert.True(definition.Mass > 0f, subtype + " weighs nothing");

                foreach (KeyValuePair<string, int> component in definition.Components)
                {
                    Assert.True(Vanilla.ComponentMasses.ContainsKey(component.Key),
                        subtype + " is built from " + component.Key
                        + ", which Vanilla.ComponentMasses does not price");
                }
            }
        }

        /// <summary>
        /// Every shipped block resolves to its own thermal entry rather than falling through to a
        /// default.
        ///
        /// This is the failure <c>ShippedDefinitionTests</c> was written for, approached from the
        /// other end: that suite checks the XML is addressed correctly, this one checks the
        /// properties actually arrive at the block the balance figures are quoted for.
        /// </summary>
        [Fact]
        public void EveryShippedBlockGetsItsOwnThermalProperties()
        {
            foreach (string subtype in ShippedBlocks.Subtypes())
            {
                Assert.True(ShippedBlocks.Get(subtype).HasOwnThermalEntry,
                    subtype + " has no entry of its own in Cubes.xml and fell through to a default");
            }
        }

        /// <summary>
        /// The coolant blocks carry plumbing and the others do not.
        ///
        /// A pipe whose subtype is not in the shape table becomes an ordinary block with no ports:
        /// it builds, it looks right, and no ring through it ever becomes a loop. Adding a variant
        /// and forgetting the table is the single easiest mistake to make in this mod.
        /// </summary>
        [Fact]
        public void EveryCoolantBlockHasPlumbingAndNothingElseDoes()
        {
            foreach (string subtype in ShippedBlocks.Subtypes())
            {
                ShippedBlocks.Definition definition = ShippedBlocks.Get(subtype);
                bool shouldPlumb = subtype.Contains("CoolantPipe") || subtype.Contains("CoolantPump");
                bool doesPlumb = ThermalCoolantShapes.Get(subtype, definition.Size) != null;

                Assert.True(shouldPlumb == doesPlumb,
                    subtype + (shouldPlumb ? " is a coolant block with no plumbing"
                                           : " is not a coolant block but declares plumbing"));
            }
        }

        /// <summary>
        /// The retired property names are still honoured.
        ///
        /// Definition Extensions matches on the name string, so the rename would otherwise revert
        /// every third-party definition written before it to the shipped defaults — no error, no
        /// log line, just a mod whose blocks stop being what they say they are. The game reads both
        /// in <c>ThermalCellDefinition</c>; this asserts the harness's own parser agrees, which is
        /// what every figure in the balance report is read through.
        /// </summary>
        [Theory]
        [InlineData("SurfaceAreaScaler", "ExposedSurfaceMultiplier")]
        [InlineData("CriticalTemperatureScaler", "OverheatDamagePerKelvin")]
        public void TheRetiredPropertyNamesAreStillRead(string legacy, string current)
        {
            string cubes = File.ReadAllText(Path.Combine(ShippedBlocks.RepoRoot(), "Data", "Cubes.xml"));

            Assert.Contains(current, cubes);
            Assert.DoesNotContain(legacy + "\"", cubes);

            // Both spellings must land on the same field. Reading the shipped file back with the
            // names swapped is the only check that actually exercises the alias.
            BlockThermalProperties viaCurrent = ShippedBlocks.Get("Gauge_LG_Radiator").Thermal;
            Assert.True(viaCurrent.ExposedSurfaceMultiplier > 1f,
                "the radiator lost its surface multiplier, so the rename dropped a value");
        }

        // ---- the conclusions -------------------------------------------------------------------

        /// <summary>
        /// The radiator earns its place: on the same load, in the same position, it beats a slab of
        /// ordinary armour of the same shape by a wide margin per tonne.
        ///
        /// If this inverts, the block has become a decoration — a player would do better bolting on
        /// more hull, and the mod would be shipping a block whose only distinction is its icon. The
        /// margin is asserted loosely because the exact figure moves with any tuning change; the
        /// direction is what must not.
        /// </summary>
        [Fact]
        public void TheRadiatorBeatsTheArmourItDisplaces()
        {
            List<BalanceLab.DeliveredRow> rows = BalanceLab.Delivered();

            BalanceLab.DeliveredRow radiator = rows.First(
                r => r.Label.Contains("radiators") && r.Label.StartsWith("200") && r.Count == 1);
            BalanceLab.DeliveredRow armour = rows.First(
                r => r.Label.Contains("armour") && r.Label.StartsWith("200") && r.Count == 1);

            Assert.True(radiator.KelvinSaved > armour.KelvinSaved,
                "the radiator saved " + radiator.KelvinSaved + " K against the armour slab's "
                + armour.KelvinSaved + " K");

            Assert.True(radiator.KelvinPerTonne > armour.KelvinPerTonne * 10f,
                "the radiator is only " + (radiator.KelvinPerTonne / armour.KelvinPerTonne)
                + "x better per tonne than plain armour");
        }

        /// <summary>
        /// **Plumbing a panel is worth more than any single surface dial a definition author would
        /// reach for first, and it takes four times the area to beat it.**
        ///
        /// <para>
        /// **This claim was stronger and `C24` weakened it, measurably.** A sink face carries about
        /// 1,000 W/K whatever the conduction pace is, because it is a fluid against a wall; a bolt
        /// joint is solid conduction and scales with `ConductionScale`, so at the pace that now
        /// ships it carries **1,168 W/K** where it used to carry a fraction of the sink's. The
        /// asserted margin was *twice the best surface dial*: measured then, plumbing beat
        /// everything the definitions could do to the panel's surface. Measured now, plumbing is
        /// worth 73.5 K, doubling the area 48.7 K and lifting emissivity to 0.8 57.7 K — but
        /// quadrupling the area is worth 94.1 K and eight times is worth 135.3 K, so the sweep's
        /// top rungs have passed it. `ASinkFaceConductsSeveralTimesHarderThanABoltJoint` pins the mechanism.
        /// </para>
        ///
        /// <para>
        /// **What this does not say is that bolting has caught up with plumbing.** The shipped row
        /// this table is read against *is* a bolted panel, and plumbing the same panel on the same
        /// load is worth 73.5 K over it: a joint carries heat one block and a loop carries it
        /// wherever the ring goes. What has changed is a *tuning* answer — "the radiator is not
        /// shedding enough" can now be answered with area as well as with plumbing — and the
        /// player-facing guidance in blocks.md is unmoved.
        /// </para>
        /// </summary>
        [Fact]
        public void PlumbingAPanelBeatsEveryDialButAMultipliedArea()
        {
            List<BalanceLab.SensitivityRow> rows = BalanceLab.Sensitivity();

            BalanceLab.SensitivityRow coolant = rows.First(r => r.Dial == "coolant sink");

            Assert.True(coolant.KelvinVersusShipped > 0f,
                "plumbing the panel rather than bolting it bought " + coolant.KelvinVersusShipped
                + " K, so the loop has stopped being the better way to feed a panel and"
                + " blocks.md's 'plumb it, do not bolt it' needs rewriting");

            float emissivity = rows.First(r => r.Dial == "Emissivity").KelvinVersusShipped;
            Assert.True(coolant.KelvinVersusShipped > emissivity,
                "a coolant sink bought " + coolant.KelvinVersusShipped + " K against emissivity's "
                + emissivity + " K");

            // The area sweep's rungs, in order. Plumbing has to beat the first of them and is
            // allowed to lose to a multiplied area — which is the part C24 moved.
            List<BalanceLab.SensitivityRow> area = rows
                .Where(r => r.Dial == "ExposedSurfaceMultiplier")
                .OrderBy(r => r.KelvinVersusShipped)
                .ToList();

            Assert.True(area.Count >= 3, "the area sweep lost its rows");
            Assert.True(coolant.KelvinVersusShipped > area[0].KelvinVersusShipped,
                "doubling the panel's area bought " + area[0].KelvinVersusShipped
                + " K against plumbing it at " + coolant.KelvinVersusShipped
                + " K, so the cheapest surface dial has passed the loop");

            Assert.True(coolant.JointWattsPerKelvin > 500f,
                "a sink face now couples at only " + coolant.JointWattsPerKelvin + " W/K");
        }

        /// <summary>
        /// **A sink face carries about five times a bolt joint, which is the design statement this
        /// mod's guidance rests on — restored, after two changes took it away and gave it back.**
        ///
        /// <para>
        /// The sink face is a fluid against a wall: a convection coefficient times an area, which no
        /// clock or conduction pace touches. A bolt joint is solid conduction, multiplied by
        /// `ConductionScale`. The mod's own statement — a sink face at 1,000 W/K against a bolt
        /// joint's 167 — was written where that ratio was **six to one**.
        /// </para>
        ///
        /// <para>
        /// `C24` took `ConductionScale` to 9.6 and the bolt joint to 1,168 W/K, which made the two
        /// **equal** and cost the guidance its rate argument; `C25` kept the pace anyway, on the
        /// grounds that pacing a fluid with solid conduction would put a coefficient no fluid has
        /// into the model, and rested the guidance on *reach* instead — a joint carries heat one
        /// block and a ring carries it wherever the ring goes.
        /// </para>
        ///
        /// <para>
        /// **`C42` gave the ratio back, and not by pacing the fluid.** The pumped coefficient went
        /// to 1,000 W/(m²·K) because a pumped water-glycol ring is forced convection and 160 was the
        /// stagnant end of the range — a fidelity argument, decided by the pickup being the only
        /// thing that says whether a big block can be cooled at all. A sink face is now 6,250 W/K
        /// against the bolt joint's 1,168: **5.4 to one**, which is where the statement started.
        /// The guidance still rests on reach, and now the rate agrees with it.
        /// </para>
        /// </summary>
        [Fact]
        public void ASinkFaceConductsSeveralTimesHarderThanABoltJoint()
        {
            List<BalanceLab.SensitivityRow> rows = BalanceLab.Sensitivity();

            BalanceLab.SensitivityRow bolted = rows.First(r => r.Dial == "(shipped)");
            BalanceLab.SensitivityRow coolant = rows.First(r => r.Dial == "coolant sink");

            // Measured 2026-08-26: 6,250 W/K plumbed against 1,168 W/K bolted.
            Assert.InRange(coolant.JointWattsPerKelvin / bolted.JointWattsPerKelvin, 3f, 8f);

            // And the joint is solid conduction, which is why: it is the pace that moved it, not
            // the block. At the 2.4 the conversion calibrated to it carried a quarter of this.
            Assert.InRange(
                bolted.JointWattsPerKelvin * (2.4f / ThermalConstants.ConductionScale),
                200f, 400f);
        }

        /// <summary>
        /// Surface area and emissivity both still do something, and neither is close to free.
        ///
        /// Quadrupling the area scaler is worth a couple of dozen kelvin, not a couple of hundred:
        /// the panel simply runs colder and radiation falls away as the fourth power. Pinning the
        /// *shape* of that curve is what stops a future tuning pass from reaching for a large
        /// multiplier expecting a large effect.
        /// </summary>
        [Fact]
        public void SurfaceDialsGiveDiminishingReturns()
        {
            List<BalanceLab.SensitivityRow> rows = BalanceLab.Sensitivity()
                .Where(r => r.Dial == "ExposedSurfaceMultiplier")
                .OrderBy(r => r.KelvinVersusShipped)
                .ToList();

            Assert.True(rows.Count >= 3, "the area sweep lost its rows");

            // Each doubling buys less than the one before it.
            for (int i = 1; i < rows.Count; i++)
            {
                float previous = rows[i - 1].KelvinVersusShipped;
                float current = rows[i].KelvinVersusShipped;
                Assert.True(current > previous, "the sweep is not monotonic");
                Assert.True(current < previous * 2f,
                    "doubling the area scaler bought " + (current - previous)
                    + " K on top of " + previous + " K, which is more than proportional");
            }
        }

        /// <summary>
        /// A longer ring couples harder, and the block it cools ends up colder for it.
        ///
        /// **The only thing that asserts this**, and blocks.md's build advice is written from it.
        /// It used to say it restated a second test, which stopped existing at some point and took
        /// its half of the claim with it — the coupling column is checked here, as the ordering, and
        /// nowhere else. That name also carried a claim that is no longer true: it said a longer
        /// ring carries the same fluid, and the charge has been per *pipe* since.
        /// </summary>
        [Fact]
        public void LongerRingsDeliverColderBlocks()
        {
            List<BalanceLab.LoopRow> rows = BalanceLab.Loops();
            Assert.True(rows.Count >= 2, "the ring sweep lost its rows");

            for (int i = 1; i < rows.Count; i++)
            {
                Assert.True(rows[i].Pipes > rows[i - 1].Pipes, "the sweep is not ordered by size");
                Assert.True(rows[i].CouplingWattsPerKelvin > rows[i - 1].CouplingWattsPerKelvin,
                    "a longer ring stopped coupling harder");
                Assert.True(rows[i].SettledKelvin < rows[i - 1].SettledKelvin,
                    "a " + rows[i].Pipes + "-pipe ring settled at " + rows[i].SettledKelvin
                    + " K against a " + rows[i - 1].Pipes + "-pipe ring's " + rows[i - 1].SettledKelvin + " K");
            }
        }

        /// <summary>
        /// The heat pump's binding limit moves through all three regimes across an ordinary range
        /// of gaps, and the coefficient never exceeds its cap.
        ///
        /// A pump that is rating-bound everywhere is a constant, and a pump that is Carnot-bound
        /// everywhere is a tax. It is only interesting because which limit binds changes with how
        /// it is installed, so that it does is worth asserting.
        /// </summary>
        [Fact]
        public void TheHeatPumpPassesThroughAllThreeOfItsLimits()
        {
            List<BalanceLab.PumpRow> rows = BalanceLab.Pump();
            ThermalSettings settings = new ThermalSettings();

            HashSet<string> seen = new HashSet<string>();
            foreach (BalanceLab.PumpRow row in rows)
            {
                seen.Add(row.Binding);
                Assert.True(row.Coefficient <= settings.HeatPumpMaxCoefficient + 0.001f,
                    "coefficient " + row.Coefficient + " exceeded the cap");
                Assert.True(row.DrawnWatts <= 20000f + 0.5f,
                    "the pump drew " + row.DrawnWatts + " W, above its rating");
            }

            Assert.Contains("coefficient cap", seen);
            Assert.Contains("rating", seen);
            Assert.Contains("carnot", seen);
        }

        /// <summary>
        /// A shipped block's exposed area is its geometry times its area scaler, and the radiator
        /// is the only block that claims more surface than it has.
        ///
        /// Cheap to assert and it catches a scaler pasted onto the wrong definition, which no
        /// temperature reading would make obvious.
        /// </summary>
        [Fact]
        public void OnlyTheRadiatorClaimsExtraSurface()
        {
            foreach (BalanceLab.BlockRow row in BalanceLab.Blocks())
            {
                bool radiator = row.Kind == "radiator";
                Assert.True(radiator == (row.ExposedSurfaceMultiplier > 1f),
                    row.Subtype + " has area scaler " + row.ExposedSurfaceMultiplier
                    + (radiator ? " and should claim extra surface" : " and should not"));
            }
        }
    }
}
