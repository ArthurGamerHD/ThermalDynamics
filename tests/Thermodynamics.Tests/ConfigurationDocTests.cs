using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Every setting a world can carry is documented, and every documented setting exists.
    ///
    /// <para>
    /// `configuration.md` is the reference an administrator reads before changing anything, and it
    /// drifts in one direction: settings get added to `Settings.cs` and the menu, and the reference
    /// is written later or not at all. Twenty-one had accumulated that way — the whole `Loop*` and
    /// `Planet*` families, added when the loop and planet definitions became world settings, plus
    /// `MaxSubsteps` and `ClampEnvironmentOvershoot`, which the prose discussed at length and the
    /// tables never listed.
    /// </para>
    ///
    /// <para>
    /// The check is textual because `Settings.cs` reads `Sandbox.*` and cannot be linked into this
    /// project. That is enough: a setting is declared on one line with a `ProtoMember` attribute,
    /// and documented as the first cell of a table row. Both forms are stable, and the failure this
    /// guards against is a setting that appears in one and not the other.
    /// </para>
    /// </summary>
    public class ConfigurationDocTests
    {
        /// <summary>
        /// Settings by shape and not by nature: nobody tunes them, so the reference does not list
        /// them and nothing outside `Settings.cs` reads them.
        ///
        /// `Version` is the config file's schema number, which a player never sets and a migration
        /// rewrites. `LegacyLoopConductivity` is a retired dial kept only so a world saved before
        /// `C20` can be migrated onto the coefficient that replaced it, and it is spent as it is
        /// read.
        /// </summary>
        private static readonly HashSet<string> NotTunable =
            new HashSet<string> { "Version", "LegacyLoopConductivity" };

        private static string RepoRoot()
        {
            // Delegates rather than walking up from the assembly, because the build output no
            // longer sits inside the repository — see Directory.Build.props. ShippedBlocks anchors
            // itself to its own compiled-in source path, which survives the move.
            return Thermodynamics.Harness.ShippedBlocks.RepoRoot();
        }

        private static HashSet<string> Declared()
        {
            string source = File.ReadAllText(Path.Combine(
                RepoRoot(), "Data", "Scripts", "Thermodynamics", "Settings.cs"));

            HashSet<string> names = new HashSet<string>();
            foreach (Match match in Regex.Matches(source,
                @"\[ProtoMember\(\d+\)\]\s*public\s+[A-Za-z0-9_<>\[\]]+\s+([A-Za-z0-9_]+)"))
            {
                string name = match.Groups[1].Value;
                if (!NotTunable.Contains(name)) names.Add(name);
            }

            return names;
        }

        private static HashSet<string> Documented()
        {
            string doc = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "configuration.md"));

            HashSet<string> names = new HashSet<string>();
            foreach (Match match in Regex.Matches(doc, @"(?m)^\|\s*`([A-Za-z0-9_]+)`"))
            {
                names.Add(match.Groups[1].Value);
            }

            return names;
        }

        [Fact]
        public void EverySettingIsInTheReference()
        {
            HashSet<string> declared = Declared();
            Assert.True(declared.Count > 40, "only " + declared.Count
                + " settings were found in Settings.cs, so the declaration pattern has changed and"
                + " this test is no longer reading anything");

            HashSet<string> documented = Documented();

            List<string> missing = new List<string>();
            foreach (string name in declared)
            {
                if (!documented.Contains(name)) missing.Add(name);
            }

            missing.Sort();

            Assert.True(missing.Count == 0,
                "settings that exist and are not in docs/configuration.md's tables:\n  "
                + string.Join("\n  ", missing.ToArray()));
        }

        [Fact]
        public void TheReferenceDocumentsNothingThatHasBeenRemoved()
        {
            HashSet<string> declared = Declared();
            HashSet<string> documented = Documented();

            // Profile names share the tables' shape and are not settings.
            HashSet<string> profiles = new HashSet<string>
            {
                "simulation", "optimized", "simlite", "responsive", "arcade",
            };

            List<string> stale = new List<string>();
            foreach (string name in documented)
            {
                if (profiles.Contains(name)) continue;
                if (!declared.Contains(name) && !NotTunable.Contains(name)) stale.Add(name);
            }

            stale.Sort();

            Assert.True(stale.Count == 0,
                "docs/configuration.md documents settings that no longer exist:\n  "
                + string.Join("\n  ", stale.ToArray()));
        }

        /// <summary>
        /// Files that carry a setting's name without being a reader of it: the declaration itself,
        /// the menu that lists what can be edited, and the two halves of the replication, which move
        /// every setting by name and would therefore vouch for all of them.
        /// </summary>
        private static readonly HashSet<string> NotReaders = new HashSet<string>
        {
            "Settings.cs", "ThermalSettingsMenu.cs", "SettingsSync.cs", "SettingsRequests.cs",
        };

        /// <summary>
        /// Every setting is read by something.
        ///
        /// <para>
        /// This is the check that `DebugWindRaycast` would have failed for as long as it existed. It
        /// had a field with a `ProtoMember`, a place in `Names()`, a place in `ClientOwned`, a row on
        /// the Debug page and a label reading "Draw wind vector" — and no reader anywhere. Switching
        /// it on drew nothing, and nothing could tell you that but reading the whole codebase for the
        /// absence of a mention. The coolant pump's on/off switch was the same failure wearing a
        /// terminal control instead of a config field.
        /// </para>
        ///
        /// <para>
        /// A setting with no reader is worse than a missing feature, because it is a promise on
        /// screen. Textual, and deliberately generous: any mention of the name outside the files that
        /// merely enumerate settings counts as a reader. It cannot catch a setting that is read into
        /// a variable nothing then uses, but it catches the whole class of setting that was never
        /// wired to anything at all.
        /// </para>
        /// </summary>
        [Fact]
        public void EverySettingIsReadBySomething()
        {
            HashSet<string> declared = Declared();

            string scripts = Path.Combine(RepoRoot(), "Data", "Scripts", "Thermodynamics");
            System.Text.StringBuilder body = new System.Text.StringBuilder();

            foreach (string file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                // Bundled third-party sources. They cannot read this mod's settings, and sweeping
                // them in would only slow the test down.
                if (file.Contains("RichHudFramework") || file.Contains("NetworkAPI")) continue;
                if (NotReaders.Contains(Path.GetFileName(file))) continue;

                body.Append(File.ReadAllText(file)).Append('\n');
            }

            string source = body.ToString();
            Assert.True(source.Length > 100000,
                "only " + source.Length + " characters of source were read, so this test is looking"
                + " in the wrong place and would pass whatever the code did");

            List<string> unread = new List<string>();
            foreach (string name in declared)
            {
                if (source.IndexOf(name, StringComparison.Ordinal) < 0) unread.Add(name);
            }

            unread.Sort();

            Assert.True(unread.Count == 0,
                "settings that exist, appear in the menu and are read by nothing:\n  "
                + string.Join("\n  ", unread.ToArray()));
        }
    }
}
