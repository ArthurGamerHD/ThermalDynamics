using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The plumbing every setting has to pass through, checked as text.
    ///
    /// <para><c>Settings.cs</c> reads <c>Sandbox.*</c> and cannot be linked into this project, so
    /// none of this can be done by calling it. That is fine: every fault this guards against is
    /// visible in the source, and each of them is silent at runtime.</para>
    ///
    /// <para><b>Why these exist.</b> A setting is wired by hand in five places — a field with a
    /// <c>ProtoMember</c> number, an entry in <c>Names()</c>, a case in <c>GetValue</c>, a case in
    /// <c>SetValue</c>, and a row in the reference documentation. Miss one and nothing fails; the
    /// setting simply does not work, or works in one direction, or quietly overwrites another
    /// setting on the wire. All four of those have happened in this codebase.</para>
    /// </summary>
    public class SettingsWiringTests
    {
        private static string RepoRoot()
        {
            // Delegates rather than walking up from the assembly, because the build output no
            // longer sits inside the repository — see Directory.Build.props. ShippedBlocks anchors
            // itself to its own compiled-in source path, which survives the move.
            return Thermodynamics.Harness.ShippedBlocks.RepoRoot();
        }

        private static string Source()
        {
            return File.ReadAllText(Path.Combine(
                RepoRoot(), "Data", "Scripts", "Thermodynamics", "Settings.cs"));
        }

        private static readonly Regex Declaration = new Regex(
            @"\[ProtoMember\((\d+)\)\]\s*public\s+[A-Za-z0-9_<>\[\]]+\s+([A-Za-z0-9_]+)");

        [Fact]
        public void NoTwoSettingsShareAProtoMemberNumber()
        {
            // The one that is genuinely dangerous, and the one that caught a real fault: two fields
            // on the same number serialise onto each other. A world's config would load one
            // setting's value into the other, and the settings replication would push it to every
            // client — silently, because protobuf has no reason to complain.
            //
            // It happened because the numbers are picked by eye and the list is long enough that
            // reading the last dozen is not the same as reading all of them.
            Dictionary<int, List<string>> byNumber = new Dictionary<int, List<string>>();

            foreach (Match match in Declaration.Matches(Source()))
            {
                int number = int.Parse(match.Groups[1].Value);
                string field = match.Groups[2].Value;

                if (!byNumber.ContainsKey(number)) byNumber[number] = new List<string>();
                byNumber[number].Add(field);
            }

            Assert.True(byNumber.Count > 60,
                "only " + byNumber.Count + " settings were found, so the pattern has changed and this"
                + " test is no longer reading anything");

            List<string> clashes = new List<string>();
            foreach (KeyValuePair<int, List<string>> pair in byNumber)
            {
                if (pair.Value.Count > 1)
                    clashes.Add(pair.Key + ": " + string.Join(", ", pair.Value.ToArray()));
            }

            clashes.Sort();

            Assert.True(clashes.Count == 0,
                "settings sharing a ProtoMember number:\n  " + string.Join("\n  ", clashes.ToArray()));
        }

        [Fact]
        public void NoSettingReusesANumberThatWasDeliberatelyRetired()
        {
            // Settings.cs records the numbers of removed settings so an older config file or a peer
            // on an older build cannot land a stale value on a new field. Reusing one silently
            // undoes that.
            string source = Source();

            int[] retired = { 53, 54, 55, 56, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 72, 76 };
            HashSet<int> banned = new HashSet<int>(retired);

            List<string> reused = new List<string>();
            foreach (Match match in Declaration.Matches(source))
            {
                int number = int.Parse(match.Groups[1].Value);
                if (banned.Contains(number)) reused.Add(number + " on " + match.Groups[2].Value);
            }

            Assert.True(reused.Count == 0,
                "retired ProtoMember numbers reused:\n  " + string.Join("\n  ", reused.ToArray()));
        }

        // ---- the by-name table -----------------------------------------------------------------

        private static List<string> Names(string source)
        {
            Match block = Regex.Match(source,
                @"public static List<string> Names\(\)\s*\{\s*return new List<string>\s*\{(.*?)\};",
                RegexOptions.Singleline);

            Assert.True(block.Success, "Names() no longer has the shape this test reads");

            List<string> names = new List<string>();
            foreach (Match match in Regex.Matches(block.Groups[1].Value, "\"([A-Za-z0-9_]+)\""))
            {
                names.Add(match.Groups[1].Value);
            }
            return names;
        }

        /// <summary>The case labels inside one named method.</summary>
        private static HashSet<string> Cases(string source, string signature, string endSignature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, "could not find " + signature);

            int end = endSignature == null
                ? source.Length
                : source.IndexOf(endSignature, start, StringComparison.Ordinal);

            if (end < 0) end = source.Length;

            HashSet<string> cases = new HashSet<string>();
            foreach (Match match in Regex.Matches(source.Substring(start, end - start),
                "case \"([A-Za-z0-9_]+)\""))
            {
                cases.Add(match.Groups[1].Value);
            }
            return cases;
        }

        [Fact]
        public void EveryNamedSettingCanBeReadAndWritten()
        {
            // A setting listed in Names() appears in the menu, in `/thermal set`, and in the
            // replication. Missing a case in GetValue makes it read as NaN; missing one in SetValue
            // makes it silently refuse to change. Neither raises anything.
            string source = Source();
            List<string> names = Names(source);

            Assert.True(names.Count > 60, "only " + names.Count + " names found");

            HashSet<string> readable = Cases(source, "public float GetValue", "public bool SetValue");
            HashSet<string> writable = Cases(source, "public bool SetValue", null);

            List<string> unreadable = new List<string>();
            List<string> unwritable = new List<string>();

            for (int i = 0; i < names.Count; i++)
            {
                if (!readable.Contains(names[i])) unreadable.Add(names[i]);
                if (!writable.Contains(names[i])) unwritable.Add(names[i]);
            }

            Assert.True(unreadable.Count == 0,
                "named settings GetValue cannot read:\n  " + string.Join("\n  ", unreadable.ToArray()));
            Assert.True(unwritable.Count == 0,
                "named settings SetValue cannot write:\n  " + string.Join("\n  ", unwritable.ToArray()));
        }

        [Fact]
        public void NothingIsReadableOrWritableWithoutBeingNamed()
        {
            // The other direction. A case with no entry in Names() is unreachable — nothing
            // enumerates it — so it is either a typo or a leftover.
            string source = Source();
            HashSet<string> names = new HashSet<string>(Names(source));

            HashSet<string> readable = Cases(source, "public float GetValue", "public bool SetValue");
            HashSet<string> writable = Cases(source, "public bool SetValue", null);

            List<string> orphans = new List<string>();
            foreach (string name in readable) if (!names.Contains(name)) orphans.Add("get " + name);
            foreach (string name in writable) if (!names.Contains(name)) orphans.Add("set " + name);

            orphans.Sort();

            Assert.True(orphans.Count == 0,
                "cases that Names() does not list:\n  " + string.Join("\n  ", orphans.ToArray()));
        }

        [Fact]
        public void EverySettingThatIsAModeRatherThanASwitchIsExcludedFromIsFlag()
        {
            // IsFlag decides whether the menu draws a checkbox or a slider, and it works by prefix —
            // anything starting with "Debug" is assumed to be a switch. The overlay settings are
            // multi-valued modes and have to be excluded by name, which is easy to forget when a
            // new one is added.
            string source = Source();

            int start = source.IndexOf("public static bool IsFlag", StringComparison.Ordinal);
            Assert.True(start >= 0);

            string body = source.Substring(start, Math.Min(1200, source.Length - start));

            foreach (string mode in new[] { "DebugBlockOverlay", "DebugWindOverlay" })
            {
                Assert.True(source.Contains(mode),
                    mode + " no longer exists, so this test should be updated");

                Assert.Contains("!= \"" + mode + "\"", body);
            }
        }

        [Fact]
        public void EverySettingIsClampedOrDeliberatelyNot()
        {
            // Not every setting needs a clamp, but a *newly added* one that can be set to nonsense
            // from chat and has no guard is how a world ends up with a negative radius or a zero
            // divisor. This holds the ones where that has bitten or would.
            string source = Source();

            string[] mustBeGuarded =
            {
                "WindRoughnessLength", "WindGradientHeight", "WindDiurnalAmplitude",
                "WindDiurnalCrossover", "WindTerrainInfluence", "WindTerrainRadius",
                "WindSlopeStrength", "DebugWindOverlay", "TelemetryPlanetProbes",
            };

            // The declaration, not a call site — anchoring on "Clamp()" finds the first *call* and
            // then searches the whole rest of the file, which makes this pass for any setting that
            // is merely mentioned again later.
            int start = source.IndexOf("private void Clamp()", StringComparison.Ordinal);
            Assert.True(start >= 0, "could not find the clamping pass");

            int open = source.IndexOf('{', start);
            Assert.True(open > 0);

            int depth = 0, end = -1;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0) { end = i; break; }
                }
            }

            Assert.True(end > open, "Clamp() has no closing brace this test can find");
            string body = source.Substring(open, end - open);

            for (int i = 0; i < mustBeGuarded.Length; i++)
            {
                Assert.Contains(mustBeGuarded[i], body);
            }
        }
    }
}
