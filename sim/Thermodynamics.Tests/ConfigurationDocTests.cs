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
        /// Bookkeeping rather than a tunable: the config file's schema number, which a player never
        /// sets and a migration rewrites.
        /// </summary>
        private static readonly HashSet<string> NotTunable = new HashSet<string> { "Version" };

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
    }
}
