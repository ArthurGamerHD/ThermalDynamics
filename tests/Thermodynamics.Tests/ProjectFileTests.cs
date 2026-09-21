using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    public class ProjectFileTests
    {
/// <summary>MsBuildFiles operation.</summary>
        public static IEnumerable<object[]> MsBuildFiles()
        {
            string root = ShippedBlocks.RepoRoot();
            string[] patterns = { "*.csproj", "*.props", "*.targets", "*.slnx"};

            foreach (string pattern in patterns)
            {
                foreach (string path in Directory.GetFiles(root, pattern, SearchOption.AllDirectories))
                {
                    string relative = path.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/');
                    if (relative.Contains("/obj/") || relative.Contains("/bin/")) continue;
                    yield return new object[] { relative };
                }
            }
        }

        [Theory]
        [MemberData(nameof(MsBuildFiles))]
/// <summary>EveryMsBuildFileParses operation.</summary>
        public void EveryMsBuildFileParses(string relative)
        {
            string path = Path.Combine(ShippedBlocks.RepoRoot(), relative);
/// <summary>XmlDocument operation.</summary>
            XmlDocument document = new XmlDocument();

            try
            {
                document.Load(path);
            }
            catch (XmlException failure)
            {
                Assert.Fail(relative + " is not readable by MSBuild, so every project that"
                    + " references it silently leaves the build: " + failure.Message);
            }

            Assert.NotNull(document.DocumentElement);
        }

        [Fact]
/// <summary>TheModProjectIsAMemberOfTheTestSolution operation.</summary>
        public void TheModProjectIsAMemberOfTheTestSolution()
        {
            string solution = File.ReadAllText(
                Path.Combine(ShippedBlocks.RepoRoot(), "tests", "Thermodynamics.slnx"));

            Assert.Contains("../Thermodynamics/Thermodynamics.csproj", solution);
            Assert.True(File.Exists(Path.Combine(ShippedBlocks.RepoRoot(),
                "Thermodynamics", "Thermodynamics.csproj")));
        }
    }
}
