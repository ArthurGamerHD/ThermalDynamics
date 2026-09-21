using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Every MSBuild file in the repository is a file MSBuild can read.
    ///
    /// <para>
    /// **A project file that does not parse does not fail loudly — it leaves the build.** `C11` puts
    /// `Generic.csproj` in `tests/Thermodynamics.slnx` so the suite's own build compiles every file
    /// the game compiles, and that is the right check for a *rename*. It is not the check for a
    /// malformed project: `dotnet build Thermodynamics.Tests.csproj` and `dotnet test --no-build`
    /// both succeed while the mod project is unreadable, so the only command that says so is the
    /// one command a session may not have run. Pass 9, Iteration 4 wrote a `--` into an XML comment
    /// in `Generic.csproj` and shipped it; the mod project did not build for three commits and the
    /// suite passed throughout.
    /// </para>
    ///
    /// <para>
    /// This is the same shape as <see cref="OptimisedBuildTests"/>: a build property is not covered
    /// by the thing it configures, so something has to read the file itself. Parsing is all this
    /// asserts — whether the project *means* the right thing is what the solution build is for —
    /// because parsing is the failure that hides.
    /// </para>
    /// </summary>
    public class ProjectFileTests
    {
        /// <summary>
        /// The MSBuild files, found rather than listed: a new project this test does not know about
        /// is exactly the one nobody has built yet. `obj` and `bin` hold generated `.props` files
        /// that are MSBuild's own business and are not in the tree.
        /// </summary>
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
        public void EveryMsBuildFileParses(string relative)
        {
            string path = Path.Combine(ShippedBlocks.RepoRoot(), relative);
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

        /// <summary>
        /// The mod project is in the test solution, which is `C11` and the reason a rename in
        /// `Core` cannot pass the suite while leaving the mod uncompilable. It is asserted here
        /// rather than trusted because the membership is one line of a file nothing else reads.
        /// </summary>
        [Fact]
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
