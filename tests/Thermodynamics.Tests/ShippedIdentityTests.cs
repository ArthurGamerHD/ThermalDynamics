using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Thermodynamics.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    public class ShippedIdentityTests
    {
        private readonly ITestOutputHelper output;


        public ShippedIdentityTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const string WorkshopId = "2985582372";

        private const string SteamIdOwner = "76561198079985653";

        [Fact]

        public void TheWorkshopIdentityIsTheOneTheModIsPublishedUnder()
        {
            string path = Path.Combine(ShippedBlocks.RepoRoot(), "Workshop", "modinfo.sbmi");
            Assert.True(File.Exists(path), "modinfo.sbmi is gone, which is the whole failure");

            XDocument info = XDocument.Load(path);


            List<string> ids = new List<string>();
            foreach (XElement element in info.Descendants())
            {
                if (element.Name.LocalName != "Id") continue;
                ids.Add(element.Value.Trim());
            }

            output.WriteLine("workshop ids in modinfo.sbmi: " + string.Join(", ", ids));

            Assert.True(ids.Contains(WorkshopId),
                "modinfo.sbmi does not carry workshop id " + WorkshopId + " — it carries "
                + (ids.Count == 0 ? "none at all" : string.Join(", ", ids))
                + ". Publishing from this folder would create a NEW workshop item and every"
                + " subscriber would stay on the old one. If the mod is genuinely being republished"
                + " under a new id, change the constant in this test and say why in the commit.");

            XElement owner = null;
            foreach (XElement element in info.Descendants())
            {
                if (element.Name.LocalName == "SteamIDOwner") owner = element;
            }

            Assert.NotNull(owner);
            Assert.Equal(SteamIdOwner, owner.Value.Trim());
        }

        [Fact]

        public void TheSecondIdentityFileIsStillThere()
        {
            string path = Path.Combine(ShippedBlocks.RepoRoot(), "Workshop", "metadata.mod");
            Assert.True(File.Exists(path),
                "metadata.mod is gone; the workshop identity is two files and this is the other one");
        }

        [Fact]

        public void TheModelTreeKeepsItsShape()
        {
            string models = Path.Combine(ShippedBlocks.ContentRoot(), "Models");
            Assert.True(Directory.Exists(models), "Models/ is gone");


            List<string> paths = new List<string>();
            foreach (string file in Directory.GetFiles(models, "*.mwm", SearchOption.AllDirectories))
            {
                paths.Add(file.Substring(ShippedBlocks.ContentRoot().Length)
                    .TrimStart('/', '\\').Replace('\\', '/'));
            }

            paths.Sort(StringComparer.Ordinal);

            Assert.True(paths.Count >= 100,
                "only " + paths.Count + " models were found, so this test is looking in the wrong"
                + " place and would pass on an emptied tree");

            string joined = string.Join("\n", paths.ToArray());
            string digest;
            using (SHA256 sha = SHA256.Create())
            {
                digest = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(joined)))
                    .Replace("-", "").ToLowerInvariant().Substring(0, 16);
            }

            output.WriteLine("{0} models, path-set digest {1}", paths.Count, digest);


            List<string> stray = new List<string>();
            for (int i = 0; i < paths.Count; i++)
            {
                if (!paths[i].StartsWith("Models/", StringComparison.Ordinal)) stray.Add(paths[i]);
            }

            Assert.True(stray.Count == 0,
                "models outside Models/:\n  " + string.Join("\n  ", stray.ToArray()));

            Assert.Equal(ModelPathDigest, digest);
        }

        private const string ModelPathDigest = "fcaac6b833bce623";
    }
}
