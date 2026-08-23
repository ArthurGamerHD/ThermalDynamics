using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Cross-checks the shipped XML against itself. These are the failures a green solver suite
    /// cannot see: the simulation is correct, and the numbers it is handed are not the ones the
    /// definitions author.
    ///
    /// The defect that prompted this: every one of the mod's own blocks declared its thermal
    /// properties under <c>&lt;TypeId&gt;CubeBlocks&lt;/TypeId&gt;</c>, which is not an object
    /// builder type. Definition Extensions therefore matched none of them, every block fell
    /// through to <c>DefaultThermodynamics</c>, and a live dump showed the radiator running at
    /// emissivity 0.125 with no area bonus instead of the authored 0.35 and 1.25 — the two
    /// properties the block exists for.
    /// </summary>
    public class ShippedDefinitionTests
    {
        /// <summary>
        /// Walks up from the test assembly for the repository root, identified by the data files
        /// themselves rather than by a fixed depth, so the test survives a change of target
        /// framework or output layout.
        /// </summary>
        private static string RepoRoot()
        {
            // Delegates rather than walking up from the assembly, because the build output no
            // longer sits inside the repository — see Directory.Build.props. ShippedBlocks anchors
            // itself to its own compiled-in source path, which survives the move.
            return Thermodynamics.Harness.ShippedBlocks.RepoRoot();
        }

        private static XDocument Load(params string[] parts)
        {
            string[] segments = new string[parts.Length + 1];
            segments[0] = RepoRoot();
            Array.Copy(parts, 0, segments, 1, parts.Length);
            return XDocument.Load(Path.Combine(segments));
        }

        /// <summary>Subtype to object-builder TypeId, read from each shipped block definition.</summary>
        private static Dictionary<string, string> ShippedBlockTypes()
        {
            Dictionary<string, string> types = new Dictionary<string, string>();
            string folder = Path.Combine(RepoRoot(), "Data", "CubeBlocks");

            foreach (string file in Directory.GetFiles(folder, "*.sbc"))
            {
                XDocument document = XDocument.Load(file);
                foreach (XElement definition in document.Descendants("Definition"))
                {
                    XElement id = definition.Element("Id");
                    if (id == null) continue;

                    XElement type = id.Element("TypeId");
                    XElement subtype = id.Element("SubtypeId");
                    if (type == null || subtype == null) continue;

                    types[subtype.Value.Trim()] = type.Value.Trim();
                }
            }

            return types;
        }

        /// <summary>Every &lt;Id&gt; in Cubes.xml, as (TypeId, SubtypeId).</summary>
        private static List<KeyValuePair<string, string>> CubesEntries()
        {
            List<KeyValuePair<string, string>> entries = new List<KeyValuePair<string, string>>();

            foreach (XElement definition in Load("Data", "Cubes.xml").Descendants("Definition"))
            {
                XElement id = definition.Element("Id");
                if (id == null) continue;

                XElement type = id.Element("TypeId");
                XElement subtype = id.Element("SubtypeId");
                if (type == null || subtype == null) continue;

                entries.Add(new KeyValuePair<string, string>(type.Value.Trim(), subtype.Value.Trim()));
            }

            return entries;
        }

        [Fact]
        public void EveryShippedBlockDeclaresItsThermalPropertiesUnderItsOwnTypeId()
        {
            Dictionary<string, string> shipped = ShippedBlockTypes();
            List<string> wrong = new List<string>();

            foreach (KeyValuePair<string, string> entry in CubesEntries())
            {
                string expected;
                if (!shipped.TryGetValue(entry.Value, out expected)) continue;   // vanilla or fallback entry

                if (entry.Key != expected)
                {
                    wrong.Add(entry.Value + " authored as <TypeId>" + entry.Key +
                        "</TypeId> but the block is a " + expected);
                }
            }

            Assert.Empty(wrong);
        }

        [Fact]
        public void EveryShippedBlockHasThermalPropertiesOfItsOwn()
        {
            Dictionary<string, string> shipped = ShippedBlockTypes();
            HashSet<string> authored = new HashSet<string>();

            foreach (KeyValuePair<string, string> entry in CubesEntries())
            {
                authored.Add(entry.Value);
            }

            List<string> missing = new List<string>();
            foreach (KeyValuePair<string, string> block in shipped)
            {
                if (!authored.Contains(block.Key)) missing.Add(block.Key);
            }

            Assert.Empty(missing);
        }

        /// <summary>
        /// The radiator's whole purpose is to shed faster than the armour around it, which is two
        /// numbers: emissivity and the area multiplier. Pinned against the default entry rather
        /// than against literals, because the failure being guarded is silently *becoming* the
        /// default.
        /// </summary>
        [Fact]
        public void TheRadiatorShedsFasterThanTheDefaultBlock()
        {
            Dictionary<string, double> fallback = PropertiesOf("DefaultThermodynamics");

            foreach (string subtype in new string[] { "Gauge_LG_Radiator", "Gauge_SG_Radiator" })
            {
                Dictionary<string, double> radiator = PropertiesOf(subtype);

                Assert.True(radiator["Emissivity"] > fallback["Emissivity"],
                    subtype + " emissivity " + radiator["Emissivity"] + " does not beat the default");
                Assert.True(radiator["ExposedSurfaceMultiplier"] > fallback["ExposedSurfaceMultiplier"],
                    subtype + " area scaler " + radiator["ExposedSurfaceMultiplier"] + " does not beat the default");
            }
        }

        /// <summary>
        /// The solver already puts every watt a heat pump draws into its hot side. A waste-energy
        /// fraction on top would charge that same energy to the grid a second time, so both
        /// fractions must be zero. A live dump caught this reading 0.05: the block was falling
        /// through to the default entry and generating 1 kW out of nothing.
        /// </summary>
        [Fact]
        public void TheHeatPumpChargesItsWorkOnlyOnce()
        {
            foreach (string subtype in new string[] { "Gauge_LG_HeatPump", "Gauge_SG_HeatPump" })
            {
                Dictionary<string, double> pump = PropertiesOf(subtype);

                Assert.Equal(0d, pump["ProducerWasteEnergy"]);
                Assert.Equal(0d, pump["ConsumerWasteEnergy"]);
            }
        }

        /// <summary>What a type entry declares: the properties a block's function decides.</summary>
        private static readonly string[] FunctionProperties =
        {
            "ProducerWasteEnergy",
            "ConsumerWasteEnergy",
            "ExposedSurfaceMultiplier",
            "OverheatDamagePerKelvin",
        };

        private static readonly string[] RequiredProperties =
        {
            "Conductivity",
            "SpecificHeat",
            "Emissivity",
            "ExposedSurfaceMultiplier",
            "ProducerWasteEnergy",
            "ConsumerWasteEnergy",
            "CriticalTemperature",
            "OverheatDamagePerKelvin",

            // HeatSourceWatts is deliberately absent. The rule this list encodes is "the game reads
            // an omitted property as 0, and 0 is catastrophic" — a block with no SpecificHeat
            // reaches any temperature instantly, one with no CriticalTemperature is damaged from
            // placement. Zero watts of intrinsic heat is exactly what a block that is not a heat
            // source should have, so omission is the correct default rather than a trap.
        };

        /// <summary>
        /// **An entry in Cubes.xml must be complete, because a property it omits reads as zero and
        /// not as the value of the entry it is standing in for.**
        ///
        /// <c>ThermalCellDefinition</c>'s fields have no initialisers, so a property an entry does
        /// not declare arrives as zero — a block with no SpecificHeat reaches any temperature
        /// instantly, one with no CriticalTemperature is above critical the moment it is placed.
        ///
        /// <c>ToThermalProperties</c> guards every field with <c>WasDeclared</c>, so an omission
        /// leaves the derived value standing rather than zeroing it. That is what lets a type entry
        /// declare only the four properties a block's function decides. A subtype entry is a
        /// complete description of one block and still has to carry everything.
        /// </summary>
        [Fact]
        public void EveryEntryInCubesDeclaresEveryPropertyTheGameReads()
        {
            List<string> incomplete = new List<string>();

            foreach (XElement definition in Load("Data", "Cubes.xml").Descendants("Definition"))
            {
                XElement id = definition.Element("Id");
                if (id == null) continue;

                string subtype = (string)id.Element("SubtypeId");
                string type = (string)id.Element("TypeId");
                string name = type + "/" + subtype;

                // A type entry sets what a block's function decides and leaves the rest derived.
                bool typeEntry = subtype == "DefaultThermodynamics" && type != "EnvironmentDefinition";
                string[] required = typeEntry ? FunctionProperties : RequiredProperties;

                foreach (XElement group in definition.Descendants("Group"))
                {
                    XAttribute groupName = group.Attribute("Name");
                    if (groupName == null || groupName.Value != "ThermalBlockProperties") continue;

                    HashSet<string> declared = new HashSet<string>();
                    foreach (XElement value in group.Elements("Decimal"))
                    {
                        XAttribute key = value.Attribute("Name");
                        if (key != null) declared.Add(key.Value);
                    }

                    foreach (string property in required)
                    {
                        if (!declared.Contains(property))
                        {
                            incomplete.Add(name + " omits " + property + ", which the game reads as 0");
                        }
                    }

                    // A type entry may also correct its family's materials, which is what a light
                    // fitting needs: its build cost is mostly the steel plate it mounts on, so the
                    // mass-weighted derivation calls a plastic housing a metal one.
                }
            }

            Assert.Empty(incomplete);
        }

        /// <summary>
        /// Cubes.xml is now the only place a block's function is written, so the file is
        /// load-bearing: a missing or malformed type entry would silently return every block in
        /// the game to the ordinary 0.05 trickle.
        /// </summary>
        [Fact]
        public void TheTypeEntriesStillCarryTheNumbersTheCodeTableHeld()
        {
            Dictionary<string, float[]> expected = new Dictionary<string, float[]>
            {
                // producer, consumer, area, damage
                { "Reactor", new[] { 0.01f, 0.01f, 1f, 0.25f } },
                { "HydrogenEngine", new[] { 0.60f, 0.05f, 1f, 0.5f } },
                { "BatteryBlock", new[] { 0.03f, 0.03f, 1f, 1f } },
                // 0.2 is 1 - the PowerEfficiency 0.8 the game states for the vanilla drive; the
                // prototech pair state 0.9 and get 0.1 from the per-block derivation, which a type
                // entry cannot express. Was 0.15 by assertion and wrong for all four.
                { "JumpDrive", new[] { 0f, 0.2f, 1f, 2f } },
                { "Thrust", new[] { 0f, 0.25f, 1.5f, 1f } },
                { "InteriorLight", new[] { 0f, 0.9f, 1f, 1f } },
                { "Gyro", new[] { 0f, 0.15f, 1f, 1f } },
                { "Warhead", new[] { 0f, 0.05f, 1f, 4f } },
                { "HeatVentBlock", new[] { 0f, 0.3f, 3f, 1f } },
            };

            foreach (KeyValuePair<string, float[]> entry in expected)
            {
                ShippedBlocks.Function f = ShippedBlocks.FunctionOf(entry.Key);
                Assert.Equal(entry.Value[0], f.ProducerWasteEnergy, 4);
                Assert.Equal(entry.Value[1], f.ConsumerWasteEnergy, 4);
                Assert.Equal(entry.Value[2], f.ExposedSurfaceMultiplier, 4);
                Assert.Equal(entry.Value[3], f.OverheatDamagePerKelvin, 4);
            }

            // The whole table, not just the sample above.
            Assert.Equal(95, ShippedBlocks.FunctionTypes().Count);
        }

        /// <summary>
        /// **An entry without <c>ExcludeFromSimulation</c> is not an entry at all.**
        ///
        /// <c>GetDefinition</c> decides whether to use a subtype's own entry by asking Definition
        /// Extensions for that one bool. The lookup failing is how it detects "this block has no
        /// entry", so an otherwise complete and correct block that omits the bool falls straight
        /// through to its type's default and every authored number is silently ignored — the exact
        /// failure the rest of this class exists because of, one property further in.
        /// </summary>
        [Fact]
        public void EveryEntryInCubesDeclaresTheBoolThatMakesItVisible()
        {
            List<string> invisible = new List<string>();

            foreach (XElement definition in Load("Data", "Cubes.xml").Descendants("Definition"))
            {
                XElement id = definition.Element("Id");
                if (id == null) continue;

                foreach (XElement group in definition.Descendants("Group"))
                {
                    XAttribute groupName = group.Attribute("Name");
                    if (groupName == null || groupName.Value != "ThermalBlockProperties") continue;

                    bool found = false;
                    foreach (XElement value in group.Elements("Bool"))
                    {
                        XAttribute key = value.Attribute("Name");
                        if (key == null) continue;
                        if (key.Value == "ExcludeFromSimulation" || key.Value == "IgnoreThermals") found = true;
                    }

                    if (!found)
                    {
                        invisible.Add((string)id.Element("TypeId") + "/" + (string)id.Element("SubtypeId")
                            + " omits ExcludeFromSimulation, so the whole entry is skipped");
                    }
                }
            }

            Assert.Empty(invisible);
        }

        /// <summary>The ThermalBlockProperties group of one subtype in Cubes.xml.</summary>
        private static Dictionary<string, double> PropertiesOf(string subtype)
        {
            foreach (XElement definition in Load("Data", "Cubes.xml").Descendants("Definition"))
            {
                XElement id = definition.Element("Id");
                if (id == null) continue;

                XElement name = id.Element("SubtypeId");
                if (name == null || name.Value.Trim() != subtype) continue;

                Dictionary<string, double> values = new Dictionary<string, double>();
                foreach (XElement group in definition.Descendants("Group"))
                {
                    XAttribute groupName = group.Attribute("Name");
                    if (groupName == null || groupName.Value != "ThermalBlockProperties") continue;

                    foreach (XElement value in group.Elements("Decimal"))
                    {
                        XAttribute key = value.Attribute("Name");
                        XAttribute number = value.Attribute("Value");
                        if (key == null || number == null) continue;

                        values[key.Value] = double.Parse(number.Value, CultureInfo.InvariantCulture);
                    }
                }

                if (values.Count > 0) return values;
            }

            throw new InvalidOperationException("No ThermalBlockProperties for " + subtype);
        }
    }
}
