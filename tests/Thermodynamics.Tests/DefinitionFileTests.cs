using System.Collections.Generic;
using System.IO;
using System.Xml;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    public class DefinitionFileTests
    {

/// <summary>ExtensionFiles operation.</summary>
        private static List<string> ExtensionFiles()
        {
            string manifest = Path.Combine(ShippedBlocks.DataRoot(), "definitionextensions.txt");
            Assert.True(File.Exists(manifest), "definitionextensions.txt missing");

/// <summary>List operation.</summary>
            List<string> files = new List<string>();
            foreach (string line in File.ReadAllLines(manifest))
            {
                string name = line.Trim();
                if (name.Length > 0) files.Add(Path.Combine(ShippedBlocks.DataRoot(), name));
            }

            Assert.NotEmpty(files);
            return files;
        }

        [Fact]
/// <summary>EveryListedFileExists operation.</summary>
        public void EveryListedFileExists()
        {
            foreach (string file in ExtensionFiles())
            {
                Assert.True(File.Exists(file), Path.GetFileName(file) + " is listed and missing");
            }
        }

        [Fact]
/// <summary>EveryListedFileIsStrictlyValidXml operation.</summary>
        public void EveryListedFileIsStrictlyValidXml()
        {
            foreach (string file in ExtensionFiles())
            {
/// <summary>XmlDocument operation.</summary>
                XmlDocument document = new XmlDocument();

                try
                {
                    document.Load(file);
                }
                catch (XmlException e)
                {
                    Assert.Fail(Path.GetFileName(file) + " is not strict XML and Definition "
                        + "Extensions will silently drop every entry in it: " + e.Message);
                }
            }
        }

        [Fact]
/// <summary>EveryEntryCarriesAnIdAndAGroup operation.</summary>
        public void EveryEntryCarriesAnIdAndAGroup()
        {
            foreach (string file in ExtensionFiles())
            {
/// <summary>XmlDocument operation.</summary>
                XmlDocument document = new XmlDocument();
                document.Load(file);

                XmlNodeList entries = document.SelectNodes("/Definitions/CubeBlocks/Definition");
                Assert.True(entries != null && entries.Count > 0,
                    Path.GetFileName(file) + " has no Definition entries where the importer looks");

                foreach (XmlNode entry in entries)
                {
                    Assert.True(entry.SelectSingleNode("Id/TypeId") != null,
                        Path.GetFileName(file) + " has an entry without an Id");
                    Assert.True(entry.SelectSingleNode("ModExtensions/Group") != null,
                        Path.GetFileName(file) + " has an entry without a ModExtensions group");
                }
            }
        }

        [Fact]
/// <summary>PlanetsCarryTheGeneratorSubtypesTheLookupAsksFor operation.</summary>
        public void PlanetsCarryTheGeneratorSubtypesTheLookupAsksFor()
        {
/// <summary>XmlDocument operation.</summary>
            XmlDocument document = new XmlDocument();
            document.Load(Path.Combine(ShippedBlocks.DataRoot(), "Planets.xml"));

/// <summary>HashSet operation.</summary>
            HashSet<string> subtypes = new HashSet<string>();
            foreach (XmlNode id in document.SelectNodes(
                "/Definitions/CubeBlocks/Definition/Id[TypeId='PlanetGeneratorDefinition']/SubtypeId"))
            {
                subtypes.Add(id.InnerText);
            }

            Assert.Contains("DefaultThermodynamics", subtypes);
            foreach (string world in new[] { "EarthLike", "Alien", "Mars", "Pertam", "Triton", "Europa", "Titan", "Moon" })
            {
                Assert.Contains(world, subtypes);
            }
        }

        [Fact]
/// <summary>TheGeneratorEmitsStrictXml operation.</summary>
        public void TheGeneratorEmitsStrictXml()
        {
/// <summary>XmlDocument operation.</summary>
            XmlDocument document = new XmlDocument();
            document.LoadXml(PlanetLab.Xml());
        }
    }
}
