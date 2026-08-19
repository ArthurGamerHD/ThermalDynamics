using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
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
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Data", "Cubes.xml")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            throw new InvalidOperationException("Could not find the repository root from " + AppContext.BaseDirectory);
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
                Assert.True(radiator["SurfaceAreaScaler"] > fallback["SurfaceAreaScaler"],
                    subtype + " area scaler " + radiator["SurfaceAreaScaler"] + " does not beat the default");
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
