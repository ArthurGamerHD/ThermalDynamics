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
        /// the retired loop quality can be migrated onto the coefficient that replaced it, and it is spent as it is
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

        /// <summary>
        /// **Every mechanism switch is classified in the ladder inventory** (`C7`, and the intent
        /// it grew a dimension into).
        ///
        /// <para>
        /// The intent is that a feature is configured as a list of options from `off` to
        /// `realistic`, and the settings surface is not that shape yet — so
        /// [configuration.md](../../docs/configuration.md) carries an inventory of how far each
        /// mechanism is from it. **An inventory that a new mechanism can be added behind is a
        /// document that says the gap is smaller than it is**, which is the drift this catches:
        /// every `Enable*` switch has to appear in the inventory section, by name.
        /// </para>
        ///
        /// <para>
        /// Scoped to `Enable*` because those are the mechanisms. A physical constant, a balance
        /// dial and a debug overlay are not features with a fidelity ladder, and demanding a rung
        /// for each of them would make the inventory a copy of the reference table.
        /// </para>
        /// </summary>
        [Fact]
        public void EveryMechanismSwitchIsClassifiedInTheLadderInventory()
        {
            string doc = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "configuration.md"));

            const string Heading = "## Every mechanism, and the rungs it has";
            int start = doc.IndexOf(Heading, StringComparison.Ordinal);
            Assert.True(start >= 0, "docs/configuration.md has no ladder inventory section");

            int end = doc.IndexOf("\n## ", start + Heading.Length, StringComparison.Ordinal);
            string inventory = end < 0 ? doc.Substring(start) : doc.Substring(start, end - start);

            // A diagnostic is not a feature with a fidelity ladder. Telemetry's ladder is the
            // *Light* goal — off unless something reads it — and giving it a rung would make this
            // inventory a copy of the reference table.
            HashSet<string> diagnostics = new HashSet<string> { "EnableTelemetry" };

            List<string> mechanisms = new List<string>();
            foreach (string name in Declared())
            {
                if (!name.StartsWith("Enable", StringComparison.Ordinal)) continue;
                if (diagnostics.Contains(name)) continue;
                mechanisms.Add(name);
            }

            Assert.True(mechanisms.Count > 10, "only " + mechanisms.Count + " mechanism switches"
                + " were found, so this test is not reading Settings.cs");

            List<string> unclassified = new List<string>();
            foreach (string name in mechanisms)
            {
                if (!inventory.Contains("`" + name + "`")) unclassified.Add(name);
            }

            unclassified.Sort(StringComparer.Ordinal);
            Assert.True(unclassified.Count == 0,
                "mechanisms with no row in configuration.md's ladder inventory, so nothing says"
                + " what rungs they have:\n  " + string.Join("\n  ", unclassified.ToArray()));
        }

        /// <summary>
        /// **And the same question asked the other way round, which is the direction that finds a
        /// mechanism with no switch at all.**
        ///
        /// <para>
        /// The test above walks the `Enable*` settings and asks whether each is classified. That
        /// can only ever find a switch nobody documented; a *mechanism* nobody gave a switch is
        /// invisible to it, because it has no setting to enumerate. Wind was exactly that until
        /// 2026-08-24 — a whole model with ends and no `off`, listed in this very inventory with
        /// its own row saying so, and passing every check the suite had (`C7`,
        /// [backlog.md](../../docs/backlog.md) `B31`).
        /// </para>
        ///
        /// <para>
        /// So every row of the inventory must name an `Enable*` that exists, **or** declare in its
        /// rung column that it has no ladder to switch: `1` for a mechanism whose only rung is the
        /// one it has, `scalars` for the integration dials, which are not a feature, and an em dash
        /// for a row that names no setting at all. A row that claims a ladder and offers no switch
        /// is the failure this catches, and the wording it used to get away with was `continuous`.
        /// </para>
        /// </summary>
        [Fact]
        public void EveryMechanismInTheLadderInventoryHasASwitchOrSaysItHasNoLadder()
        {
            string doc = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "configuration.md"));

            const string Heading = "## Every mechanism, and the rungs it has";
            int start = doc.IndexOf(Heading, StringComparison.Ordinal);
            Assert.True(start >= 0, "docs/configuration.md has no ladder inventory section");

            int end = doc.IndexOf("\n## ", start + Heading.Length, StringComparison.Ordinal);
            string inventory = end < 0 ? doc.Substring(start) : doc.Substring(start, end - start);

            HashSet<string> switches = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in Declared())
            {
                if (name.StartsWith("Enable", StringComparison.Ordinal)) switches.Add(name);
            }

            // A row saying its ladder has one rung, that it is not a feature, or that it is
            // configured by nothing at all, is a row with nothing to switch.
            HashSet<string> noLadder = new HashSet<string>(StringComparer.Ordinal)
                { "1", "scalars", "—", "-" };

            List<string> switchless = new List<string>();
            int rows = 0;

            foreach (string line in inventory.Split('\n'))
            {
                string row = line.Trim();
                if (!row.StartsWith("|", StringComparison.Ordinal)) continue;

                string[] cells = row.Trim('|').Split('|');
                if (cells.Length < 3) continue;

                string mechanism = cells[0].Trim();
                if (mechanism == "Mechanism" || mechanism.StartsWith("---", StringComparison.Ordinal))
                {
                    continue;
                }

                rows++;
                if (noLadder.Contains(cells[2].Trim())) continue;

                bool named = false;
                foreach (string name in switches)
                {
                    if (cells[1].Contains("`" + name + "`")) { named = true; break; }
                }

                if (!named) switchless.Add(mechanism + " — configured by " + cells[1].Trim());
            }

            Assert.True(rows > 10,
                "only " + rows + " inventory rows were read, so this test is not reading the table");

            Assert.True(switchless.Count == 0,
                "mechanisms in configuration.md's ladder inventory with a ladder and no switch to"
                + " turn them off, which is `C7`:\n  " + string.Join("\n  ", switchless.ToArray()));
        }

        /// <summary>
        /// **Nothing in the model is a rung a world cannot reach.**
        ///
        /// <para>
        /// `WellMixedCoolant` was one for as long as it existed: declared on `ThermalSettings`, read
        /// by the solver, exercised by the suite, documented in two pages as a choice a world
        /// makes — and with no field in `Settings.cs` and no line in `Apply`, so no player could
        /// ever set it. A cheap rung that reaches no world is the same as a rung that does not
        /// exist, and it is worse, because the documentation says it is there.
        /// </para>
        ///
        /// <para>
        /// Textual, for the reason the rest of this class is: `Settings.cs` reads `Sandbox.*` and
        /// cannot be linked into this project. Four core fields are excluded by name and each says
        /// why.
        /// </para>
        /// </summary>
        [Fact]
        public void EveryCoreSettingIsReachableFromAWorldsConfiguration()
        {
            string core = File.ReadAllText(Path.Combine(RepoRoot(), "Data", "Scripts",
                "Thermodynamics", "Core", "Settings", "ThermalSettings.cs"));
            string world = File.ReadAllText(Path.Combine(RepoRoot(), "Data", "Scripts",
                "Thermodynamics", "Settings.cs"));

            // Not tunable: `Version` is the schema number, `Revision` counts changes so a grid can
            // notice one, and the two step figures are derived from `Frequency` by `Derive`.
            HashSet<string> derived = new HashSet<string>
            {
                "Version", "Revision", "StepSeconds", "StepsPerSecond",
            };

            List<string> unreachable = new List<string>();
            int fields = 0;

            foreach (Match match in Regex.Matches(core,
                @"(?m)^\s*public\s+(?:bool|float|int)\s+([A-Za-z0-9_]+)\s*(?:=|\{)"))
            {
                string name = match.Groups[1].Value;
                if (derived.Contains(name)) continue;

                fields++;
                if (!Regex.IsMatch(world, @"core\." + Regex.Escape(name) + @"\s*=")) unreachable.Add(name);
            }

            Assert.True(fields > 30, "only " + fields + " core settings were found, so this test is"
                + " not reading ThermalSettings.cs and would pass on a rung wired to nothing");

            unreachable.Sort(StringComparer.Ordinal);
            Assert.True(unreachable.Count == 0,
                "core settings the solver reads that no world configuration can set, so they are"
                + " rungs reachable only by a test:\n  " + string.Join("\n  ", unreachable.ToArray()));
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
