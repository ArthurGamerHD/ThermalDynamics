using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The mod's source against the game's script whitelist.
    ///
    /// <para>
    /// **The check the mod project cannot make.** `Generic.csproj` builds `Data/Scripts` against
    /// the installed game's own assemblies, so it catches a member that does not exist; the game
    /// additionally runs a Roslyn analyzer over a positive whitelist, so a type can resolve here
    /// and be refused there. That happened: `Units.Watts` took an `IFormatProvider`, the build was
    /// green, and the mod would not load. See
    /// known-issues.md.
    /// </para>
    /// </summary>
    [Trait("speed", "slow")]
    public class ScriptWhitelistTests
    {
        /// <summary>
        /// **The check.** No file the game would compile names a framework type the game refuses.
        /// </summary>
        [Fact]
        public void NoModSourceNamesAFrameworkTypeTheGameRefuses()
        {
            if (!GameBlocks.IsInstalled) return;

            Dictionary<string, string> prohibited = ScriptWhitelist.ProhibitedNames();
            Assert.True(prohibited.Count > 100,
                "the framework surface came back with " + prohibited.Count + " prohibited names, "
                + "which is too few to be the real one — netstandard.dll was probably not read");

            List<string> sources = ScriptWhitelist.Sources();
            Assert.True(sources.Count > 50,
                "found " + sources.Count + " mod source files, which is not the mod");

            List<ScriptWhitelist.Finding> findings = new List<ScriptWhitelist.Finding>();
            foreach (string path in sources)
            {
                findings.AddRange(ScriptWhitelist.Scan(Short(path), File.ReadAllText(path), prohibited));
            }

            if (findings.Count == 0) return;

            StringBuilder message = new StringBuilder();
            message.Append(findings.Count)
                .Append(" name(s) the game's script whitelist refuses. The mod project builds them "
                    + "because the assemblies have them; a session will not compile the mod at all. ")
                .Append(ScriptWhitelist.TranscribedFrom).AppendLine();

            for (int i = 0; i < findings.Count && i < 40; i++)
            {
                message.AppendLine("  " + findings[i]);
            }

            Assert.Fail(message.ToString());
        }

        /// <summary>
        /// **The check judges something.** Fed the exact declaration that broke a session, it
        /// reports it — spelled short and spelled out — and it stays quiet about the fix that
        /// replaced it.
        ///
        /// Without this the test above passes on an empty prohibited set, on a source list that
        /// found nothing, or on a scanner that looks in the wrong places (`E8`).
        /// </summary>
        [Fact]
        public void TheCheckFindsTheDeclarationThatBrokeASession()
        {
            if (!GameBlocks.IsInstalled) return;

            Dictionary<string, string> prohibited = ScriptWhitelist.ProhibitedNames();

            Assert.True(prohibited.ContainsKey("IFormatProvider"),
                "IFormatProvider is not in the prohibited set, so the case this file exists for "
                + "would not be caught");

            const string broken =
                "namespace T { public static class U {\n"
                + "  public static string W(float w, IFormatProvider culture) { return null; }\n"
                + "  public static string X(float w, System.IFormatProvider culture) { return null; }\n"
                + "} }";

            List<ScriptWhitelist.Finding> found = ScriptWhitelist.Scan("broken.cs", broken, prohibited);
            Assert.Equal(2, found.Count);
            Assert.All(found, f => Assert.Equal("IFormatProvider", f.Name));

            const string fixedUp =
                "using System.Globalization;\n"
                + "namespace T { public static class U {\n"
                + "  public static string W(float w, CultureInfo culture) { return null; }\n"
                + "} }";

            Assert.Empty(ScriptWhitelist.Scan("fixed.cs", fixedUp, prohibited));
        }

        /// <summary>
        /// **And it does not cry wolf on a member.** A field called `Uri` and a property called
        /// `Lazy` are not types, and the framework has a type of each of those names — a check that
        /// could not tell would report a hundred of them and be switched off within a week.
        /// </summary>
        [Fact]
        public void TheCheckDoesNotReportMembersThatShareAFrameworkTypesName()
        {
            if (!GameBlocks.IsInstalled) return;

            Dictionary<string, string> prohibited = ScriptWhitelist.ProhibitedNames();

            // The premise: both really are framework type names the whitelist refuses, and the
            // game does not declare a type of either name.
            Assert.True(prohibited.ContainsKey("Uri"), "Uri is not a prohibited framework name");
            Assert.True(prohibited.ContainsKey("Lazy"), "Lazy is not a prohibited framework name");

            const string innocent =
                "namespace T { public class B {\n"
                + "  public int Uri;\n"
                + "  public string Lazy { get { return null; } }\n"
                + "  public void Use(B other) { int s = other.Uri; string c = other.Lazy; }\n"
                + "} }";

            Assert.Empty(ScriptWhitelist.Scan("innocent.cs", innocent, prohibited));
        }

        /// <summary>
        /// **A name the game also declares is not judged**, which is the deliberate blind spot: the
        /// mod writes `Color` several hundred times and means `VRageMath.Color` every time, while
        /// the framework has a `System.Drawing.Color` the whitelist would refuse.
        /// </summary>
        [Fact]
        public void ANameTheGameAlsoDeclaresIsLeftAlone()
        {
            if (!GameBlocks.IsInstalled) return;

            Assert.Contains("Color", ScriptWhitelist.GameTypeNames());
            Assert.False(ScriptWhitelist.ProhibitedNames().ContainsKey("Color"),
                "Color was reported, which would bury every real finding under it");
        }

        /// <summary>
        /// The transcription is a copy of something in the installed game, so it says which build
        /// it was copied from — a whitelist that has quietly moved on is the one failure mode this
        /// file cannot detect for itself.
        /// </summary>
        [Fact]
        public void TheTranscriptionSaysWhereItCameFrom()
        {
            Assert.Contains("Bin64", ScriptWhitelist.TranscribedFrom);
            Assert.NotEmpty(ScriptWhitelist.AllowedNamespaces);
            Assert.True(ScriptWhitelist.AllowedTypes.Count > 50);

            // System itself must not be in the namespace list: the whole reason the check exists is
            // that it is not allowed whole.
            Assert.DoesNotContain("System", ScriptWhitelist.AllowedNamespaces);
        }

        /// <summary>The path, relative to the repository, so a failure is clickable.</summary>
        private static string Short(string path)
        {
            int index = path.IndexOf("Data" + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            return index < 0 ? path : path.Substring(index);
        }
    }
}
