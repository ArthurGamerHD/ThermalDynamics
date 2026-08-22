using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The documentation is checked the way the code is.
    ///
    /// <para>
    /// Every page under `docs/`, two READMEs and a tools page cross-reference each other several
    /// hundred times, and every one of those references is a claim that a file, a heading, a rule
    /// or a scenario exists. Nothing was checking them. A rename of `sim/` to `tests/` left nine
    /// dead links behind, three pages had fallen out of the README's index entirely, and three
    /// more pointed at helper scripts in a parent folder that stopped existing when this mod
    /// became its own repository.
    /// </para>
    ///
    /// <para>
    /// A dead link is worse than a missing one: it reads as though somebody checked. These tests
    /// are textual and cheap, and they fail on exactly the drift that a reader discovers by
    /// clicking.
    /// </para>
    /// </summary>
    public class DocumentationTests
    {
        private static string RepoRoot()
        {
            return Thermodynamics.Harness.ShippedBlocks.RepoRoot();
        }

        /// <summary>Every markdown file in the repository, excluding build output.</summary>
        private static List<string> MarkdownFiles()
        {
            List<string> files = new List<string>();
            foreach (string file in Directory.GetFiles(RepoRoot(), "*.md", SearchOption.AllDirectories))
            {
                string relative = Relative(file);
                if (relative.StartsWith("out/", StringComparison.Ordinal)) continue;
                if (relative.Contains("/bin/") || relative.Contains("/obj/")) continue;
                files.Add(file);
            }

            files.Sort(StringComparer.Ordinal);
            return files;
        }

        private static string Relative(string file)
        {
            return file.Substring(RepoRoot().Length).TrimStart('/', '\\').Replace('\\', '/');
        }

        /// <summary>
        /// GitHub's heading slug: lowercase, punctuation dropped, spaces to hyphens. Applied to the
        /// heading text after any inline markdown has been stripped, which is what a link written
        /// by hand from a rendered page is written against.
        /// </summary>
        private static string Slug(string heading)
        {
            string text = Regex.Replace(heading, @"\[([^\]]*)\]\([^)]*\)", "$1");   // links to their text
            text = text.Replace("`", "").Replace("*", "").Replace("_", "");
            text = text.ToLowerInvariant().Trim();

            StringBuilder slug = new StringBuilder();
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c)) slug.Append(c);
                else if (c == ' ' || c == '-') slug.Append('-');
                // everything else — punctuation, em dashes, quotes — is dropped
            }

            return slug.ToString();
        }

        private static HashSet<string> Anchors(string file)
        {
            HashSet<string> anchors = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.Ordinal);

            bool inFence = false;
            foreach (string line in File.ReadAllLines(file))
            {
                if (line.StartsWith("```", StringComparison.Ordinal)) { inFence = !inFence; continue; }
                if (inFence) continue;

                Match heading = Regex.Match(line, @"^#{1,6}\s+(.*?)\s*$");
                if (!heading.Success) continue;

                string slug = Slug(heading.Groups[1].Value);
                if (slug.Length == 0) continue;

                int count;
                if (seen.TryGetValue(slug, out count))
                {
                    seen[slug] = count + 1;
                    anchors.Add(slug + "-" + count);      // GitHub disambiguates repeats with -1, -2
                }
                else
                {
                    seen[slug] = 1;
                    anchors.Add(slug);
                }
            }

            return anchors;
        }

        /// <summary>
        /// Every relative link points at a file that exists.
        /// </summary>
        [Fact]
        public void EveryRelativeLinkResolves()
        {
            List<string> files = MarkdownFiles();
            Assert.True(files.Count > 25,
                "only " + files.Count + " markdown files were found, so this test is looking in the"
                + " wrong place and would pass whatever the documentation said");

            List<string> broken = new List<string>();
            int checked_ = 0;

            foreach (string file in files)
            {
                string folder = Path.GetDirectoryName(file);
                foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\[[^\]]*\]\(([^)\s]+)\)"))
                {
                    string target = match.Groups[1].Value;
                    if (target.StartsWith("http", StringComparison.Ordinal)) continue;
                    if (target.StartsWith("#", StringComparison.Ordinal)) continue;

                    string path = target.Split('#')[0];
                    if (path.Length == 0) continue;

                    checked_++;
                    string full = Path.GetFullPath(Path.Combine(folder, path));
                    if (!File.Exists(full) && !Directory.Exists(full))
                    {
                        broken.Add(Relative(file) + " -> " + target);
                    }
                }
            }

            Assert.True(checked_ > 200,
                "only " + checked_ + " relative links were checked, so the link pattern has changed");

            broken.Sort(StringComparer.Ordinal);
            Assert.True(broken.Count == 0,
                "documentation links pointing at files that do not exist:\n  "
                + string.Join("\n  ", broken.ToArray()));
        }

        /// <summary>
        /// Every `#anchor` in a link to a markdown file names a heading in that file.
        ///
        /// <para>
        /// This is the half that rots quietly: a heading gets reworded, every link to it still
        /// resolves to the file, and the reader lands at the top of a long page instead of at the
        /// section that was cited as evidence.
        /// </para>
        /// </summary>
        [Fact]
        public void EveryAnchorNamesAHeading()
        {
            Dictionary<string, HashSet<string>> anchors = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            List<string> broken = new List<string>();
            int checked_ = 0;

            foreach (string file in MarkdownFiles())
            {
                string folder = Path.GetDirectoryName(file);
                foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\[[^\]]*\]\(([^)\s]+)\)"))
                {
                    string target = match.Groups[1].Value;
                    if (target.StartsWith("http", StringComparison.Ordinal)) continue;

                    int hash = target.IndexOf('#');
                    if (hash < 0) continue;

                    string anchor = target.Substring(hash + 1);
                    if (anchor.Length == 0) continue;

                    string path = target.Substring(0, hash);
                    string full = path.Length == 0 ? file : Path.GetFullPath(Path.Combine(folder, path));
                    if (!full.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!File.Exists(full)) continue;         // the other test reports that

                    HashSet<string> headings;
                    if (!anchors.TryGetValue(full, out headings))
                    {
                        headings = Anchors(full);
                        anchors[full] = headings;
                    }

                    checked_++;
                    if (!headings.Contains(anchor))
                    {
                        broken.Add(Relative(file) + " -> " + target);
                    }
                }
            }

            Assert.True(checked_ > 20,
                "only " + checked_ + " anchored links were checked, so the link pattern has changed");

            broken.Sort(StringComparer.Ordinal);
            Assert.True(broken.Count == 0,
                "documentation links pointing at headings that do not exist:\n  "
                + string.Join("\n  ", broken.ToArray()));
        }

        /// <summary>
        /// Counts the test cases in this assembly the way the runner does: one per `[Fact]`, and
        /// one per data row for a `[Theory]`.
        /// </summary>
        private static int TestCaseCount()
        {
            int count = 0;

            foreach (Type type in typeof(DocumentationTests).Assembly.GetTypes())
            {
                if (!type.IsPublic || type.IsAbstract) continue;

                foreach (System.Reflection.MethodInfo method in type.GetMethods(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    object[] facts = method.GetCustomAttributes(typeof(FactAttribute), true);
                    if (facts.Length == 0) continue;

                    object[] data = method.GetCustomAttributes(typeof(Xunit.Sdk.DataAttribute), true);
                    if (data.Length == 0) { count++; continue; }

                    foreach (Xunit.Sdk.DataAttribute source in data)
                    {
                        foreach (object[] row in source.GetData(method)) { count++; }
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// The suite's size, wherever a page quotes it, is the size the suite actually is.
        ///
        /// <para>
        /// Three pages advertised "1,026 tests" and one advertised 1,179 while the runner was
        /// reporting 1,467. A count in prose is the first thing to rot, because nothing fails when
        /// it does, and a reader has no way to tell a figure that is one commit old from one that
        /// is four hundred commits old.
        /// </para>
        ///
        /// <para>
        /// The tolerance is deliberately one-sided and loose: a page may quote a round figure
        /// slightly below the true one — "over 1,450" is a fair thing to write — but it may never
        /// quote more tests than exist, and it may not fall more than a tenth behind. That is wide
        /// enough that ordinary commits do not have to edit prose, and tight enough that a figure
        /// cannot silently become a historical curiosity.
        /// </para>
        /// </summary>
        [Fact]
        public void EveryQuotedSuiteSizeIsCurrent()
        {
            int actual = TestCaseCount();
            Assert.True(actual > 1000,
                "only " + actual + " test cases were counted by reflection, so this test is not"
                + " seeing the suite and would pass whatever a page claimed");

            List<string> wrong = new List<string>();

            foreach (string file in MarkdownFiles())
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(file),
                    @"([\d][\d,]*)\s+tests\b"))
                {
                    int quoted = int.Parse(match.Groups[1].Value.Replace(",", ""));
                    if (quoted < 500) continue;          // not a suite size — a count of something else

                    if (quoted > actual || quoted < actual - actual / 10)
                    {
                        wrong.Add(Relative(file) + ": quoted " + match.Groups[1].Value
                            + " tests against " + actual + " actual");
                    }
                }
            }

            wrong.Sort(StringComparer.Ordinal);
            Assert.True(wrong.Count == 0,
                "pages quoting a suite size that is no longer true (the current count is "
                + actual + "):\n  " + string.Join("\n  ", wrong.ToArray()));
        }

        /// <summary>
        /// A class declaration at file scope inside the test project.
        /// </summary>
        private const string TestClassPattern =
            @"(?m)^[ \t]*public\s+(?:sealed\s+|static\s+|partial\s+)*class\s+(\w+)";

        /// <summary>
        /// Whether a class in the test project holds cases, as opposed to being a fixture, a
        /// shared rig or a reference implementation. Those are documented where they are used
        /// from and belong to no subject of their own.
        /// </summary>
        private static bool HoldsCases(string name)
        {
            return name.EndsWith("Tests", StringComparison.Ordinal)
                || name.EndsWith("Walk", StringComparison.Ordinal)
                || name.EndsWith("Survey", StringComparison.Ordinal)
                || name.EndsWith("Sweep", StringComparison.Ordinal)
                || name.EndsWith("Census", StringComparison.Ordinal);
        }

        /// <summary>
        /// Every class of tests is filed under a subject in the suite's index.
        ///
        /// <para>
        /// The index replaced a bullet list of everything the suite covered, written in prose and
        /// maintained by whoever remembered to. It had drifted: suites written in the last hundred
        /// commits were absent, and two of the bullets described a coverage the suite had moved
        /// elsewhere. An index of names against subjects is the part worth keeping current, and it
        /// is the part a check can keep current — what each suite is *for* lives in its own
        /// summary, where it cannot drift away from the code it describes.
        /// </para>
        /// </summary>
        [Fact]
        public void EveryTestClassIsInTheIndex()
        {
            string index = File.ReadAllText(Path.Combine(RepoRoot(), "tests", "README.md"));
            string folder = Path.Combine(RepoRoot(), "tests", "Thermodynamics.Tests");

            List<string> missing = new List<string>();
            List<string> names = new List<string>();

            foreach (string file in Directory.GetFiles(folder, "*.cs"))
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(file), TestClassPattern))
                {
                    string name = match.Groups[1].Value;
                    if (!HoldsCases(name)) continue;

                    names.Add(name);
                    if (index.IndexOf("`" + name + "`", StringComparison.Ordinal) < 0) missing.Add(name);
                }
            }

            Assert.True(names.Count > 100,
                "only " + names.Count + " test classes were recognised, so the declaration pattern"
                + " has changed and this test is no longer reading anything");

            missing.Sort(StringComparer.Ordinal);
            Assert.True(missing.Count == 0,
                "test classes filed under no subject in tests/README.md:\n  "
                + string.Join("\n  ", missing.ToArray()));
        }

        /// <summary>
        /// The first three words of a camel-case name — "TheShippedPlanetsFile" from
        /// <c>TheShippedPlanetsFileIsCompleteAndReadable</c>.
        /// </summary>
        private static string CamelPrefix(string name, int words)
        {
            MatchCollection parts = Regex.Matches(name, @"[A-Z][a-z0-9]*");
            if (parts.Count < words) return null;

            System.Text.StringBuilder prefix = new System.Text.StringBuilder();
            for (int i = 0; i < words; i++) prefix.Append(parts[i].Value);
            return prefix.ToString();
        }

        /// <summary>
        /// A test the documentation names by name has not been renamed out from under it.
        ///
        /// <para>
        /// Citing a test is how a page turns a claim into evidence, and it is the citation rather
        /// than the claim that rots: the test gets renamed, the page keeps the old name, and a
        /// reader who goes looking finds nothing and cannot tell whether the check was removed or
        /// merely moved. Two had gone that way and both were worse than a dead link. One named a
        /// test that had been renamed <em>and</em> had reversed its conclusion — the page said the
        /// shipped budget bound on a mid-size grid where the test says it fits. The other promised
        /// that the shipped <c>Planets.xml</c> was checked against the code that generates it, and
        /// no such check existed; the file and the generator happened to agree.
        /// </para>
        ///
        /// <para>
        /// Flagged only when a real test shares the name's first three words, which is what a
        /// rename looks like. A looser rule cannot work: the pages are full of backticked
        /// camel-case names that are settings, engine API members and exception types, and none of
        /// those is declared in this repository either.
        /// </para>
        /// </summary>
        [Fact]
        public void NoPageNamesATestThatHasBeenRenamed()
        {
            HashSet<string> declared = new HashSet<string>(StringComparer.Ordinal);
            List<string> testNames = new List<string>();

            foreach (string file in Directory.GetFiles(
                Path.Combine(RepoRoot(), "tests", "Thermodynamics.Tests"), "*.cs"))
            {
                string source = File.ReadAllText(file);

                foreach (Match match in Regex.Matches(source,
                    @"public\s+(?:async\s+)?(?:void|Task)\s+(\w+)\s*\("))
                {
                    declared.Add(match.Groups[1].Value);
                    testNames.Add(match.Groups[1].Value);
                }

                foreach (Match match in Regex.Matches(source, TestClassPattern))
                {
                    declared.Add(match.Groups[1].Value);
                }
            }

            Assert.True(testNames.Count > 1000,
                "only " + testNames.Count + " test names were found, so this test is looking in the"
                + " wrong place and would pass whatever a page claimed");

            Dictionary<string, string> byPrefix = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in testNames)
            {
                string prefix = CamelPrefix(name, 3);
                if (prefix != null && !byPrefix.ContainsKey(prefix)) byPrefix[prefix] = name;
            }

            List<string> renamed = new List<string>();

            foreach (string file in MarkdownFiles())
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(file),
                    @"`([A-Z][a-z0-9]*(?:[A-Z][a-z0-9]*){3,})`"))
                {
                    string cited = match.Groups[1].Value;
                    if (declared.Contains(cited)) continue;

                    string prefix = CamelPrefix(cited, 3);
                    string actual;
                    if (prefix == null || !byPrefix.TryGetValue(prefix, out actual)) continue;

                    renamed.Add(Relative(file) + ": " + cited + " — did the page mean " + actual + "?");
                }
            }

            renamed.Sort(StringComparer.Ordinal);
            Assert.True(renamed.Count == 0,
                "documentation naming tests that have been renamed:\n  "
                + string.Join("\n  ", renamed.ToArray()));
        }

        /// <summary>
        /// Every class that holds tests says what it is for.
        ///
        /// <para>
        /// A test method's name states what it asserts; nothing states why the group of them
        /// exists, what fixture they share, or which of them is the one with a defect behind it.
        /// Twenty-nine classes — the oldest and most foundational, conduction and orientation and
        /// the save format among them — carried no summary at all, so the only way to learn what a
        /// suite was protecting was to read every case in it and guess.
        /// </para>
        ///
        /// <para>
        /// Checked from the source rather than by reflection, because an XML doc comment is not
        /// compiled into the assembly unless documentation generation is switched on, and a check
        /// that silently stops looking is worse than no check.
        /// </para>
        /// </summary>
        [Fact]
        public void EveryTestClassSaysWhatItIsFor()
        {
            string folder = Path.Combine(RepoRoot(), "tests", "Thermodynamics.Tests");
            string[] files = Directory.GetFiles(folder, "*.cs");

            Assert.True(files.Length > 100,
                "only " + files.Length + " test sources were found, so this test is looking in the"
                + " wrong place and would pass whatever the suite did");

            List<string> undocumented = new List<string>();
            int classes = 0;

            foreach (string file in files)
            {
                string source = File.ReadAllText(file);

                foreach (Match match in Regex.Matches(source, TestClassPattern))
                {
                    string name = match.Groups[1].Value;
                    if (!HoldsCases(name)) continue;

                    classes++;

                    // Attributes sit between the summary and the declaration — [Trait("speed",
                    // "slow")] on the batteries — so they are stepped back over before looking.
                    string before = source.Substring(0, match.Index).TrimEnd();
                    while (before.EndsWith("]", StringComparison.Ordinal))
                    {
                        int open = before.LastIndexOf('[');
                        if (open < 0) break;
                        before = before.Substring(0, open).TrimEnd();
                    }

                    // A one-line summary closes on the same line it opens, so the tag is what is
                    // looked for rather than a line consisting only of it.
                    if (!before.EndsWith("</summary>", StringComparison.Ordinal))
                    {
                        undocumented.Add(Path.GetFileName(file) + ": " + name);
                    }
                }
            }

            Assert.True(classes > 100,
                "only " + classes + " test classes were recognised, so the declaration pattern has"
                + " changed and this test is no longer reading anything");

            undocumented.Sort(StringComparer.Ordinal);
            Assert.True(undocumented.Count == 0,
                "test classes with no summary saying what they are for:\n  "
                + string.Join("\n  ", undocumented.ToArray()));
        }

        /// <summary>
        /// Every block property the game reads is in the definitions reference, and the reference
        /// names none that it does not.
        ///
        /// <para>
        /// `definitions.md` is what a third-party mod author writes a definition from, so a
        /// property missing from it is a feature nobody outside this repository can use, and one
        /// listed that does not exist is an afternoon spent wondering why a number does nothing.
        /// `HeatSourceWatts` was the first: implemented, tested, and in neither the reference nor
        /// the in-game reader.
        /// </para>
        ///
        /// <para>
        /// Both name lists are read as text — the reader for the same reason
        /// `ConfigurationDocTests` reads `Settings.cs` that way, and the table because a markdown
        /// table has no other form.
        /// </para>
        /// </summary>
        [Theory]
        [InlineData("ThermalCellDefinition.cs", "ThermalBlockProperties", 8)]
        [InlineData("PlanetDefinition.cs", "ThermalPlanetProperties", 8)]
        [InlineData("ThermalLoopDefinition.cs", "ThermalLoopProperties", 8)]
        public void EveryPropertyTheGameReadsIsInTheReference(string file, string group, int least)
        {
            string reader = File.ReadAllText(Path.Combine(RepoRoot(),
                "Data", "Scripts", "Thermodynamics", "Definitions", file));

            HashSet<string> read = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(reader, @"GetOrCompute\(""(\w+)""\)"))
            {
                read.Add(match.Groups[1].Value);
            }

            // The group the properties live in, not a property.
            read.Remove(group);

            Assert.True(read.Count >= least,
                "only " + read.Count + " property names were found in " + file + ", so this test is"
                + " no longer looking at it");

            string doc = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "definitions.md"));

            HashSet<string> documented = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(doc, @"(?m)^\|\s*`(\w+)`"))
            {
                documented.Add(match.Groups[1].Value);
            }

            List<string> missing = new List<string>();
            foreach (string name in read)
            {
                if (!documented.Contains(name)) missing.Add(name);
            }

            missing.Sort(StringComparer.Ordinal);
            Assert.True(missing.Count == 0,
                "properties " + file + " reads and docs/definitions.md does not list:\n  "
                + string.Join("\n  ", missing.ToArray()));
        }

        /// <summary>
        /// Every entry in the mod API's delegate table is documented.
        ///
        /// <para>
        /// The table is the mod's contract with every other mod, and it is a dictionary of strings
        /// to delegates — so a caller finds out that a name is wrong at run time, in someone
        /// else's session, with a cast that fails. `api.md` is the only place the names and their
        /// signatures are written for a reader, which makes it part of the contract rather than a
        /// description of it.
        /// </para>
        /// </summary>
        [Fact]
        public void EveryModApiEntryIsDocumented()
        {
            string api = File.ReadAllText(Path.Combine(RepoRoot(),
                "Data", "Scripts", "Thermodynamics", "ThermalApi.cs"));

            List<string> keys = new List<string>();
            foreach (Match match in Regex.Matches(api, @"methods\[""(\w+)""\]"))
            {
                keys.Add(match.Groups[1].Value);
            }

            Assert.True(keys.Count > 10,
                "only " + keys.Count + " API entries were found, so the table has changed shape and"
                + " this test is no longer reading it");

            string doc = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "api.md"));

            List<string> missing = new List<string>();
            foreach (string key in keys)
            {
                if (doc.IndexOf("`" + key + "`", StringComparison.Ordinal) < 0) missing.Add(key);
            }

            missing.Sort(StringComparer.Ordinal);
            Assert.True(missing.Count == 0,
                "entries in the mod API's delegate table that docs/api.md does not name:\n  "
                + string.Join("\n  ", missing.ToArray()));
        }

        /// <summary>
        /// Every page carries a change log, and history lives in it rather than in the prose.
        ///
        /// <para>
        /// The documentation standard is in `development.md`: a page describes what the code does
        /// now, in the present tense, and every revision — including a correction to something this
        /// repository previously published — is a dated row at the bottom. Without the log there is
        /// nowhere for that history to go, so it stays in the body and the page slowly stops being
        /// a description of the code and becomes a record of how it got here. Two pages had reached
        /// that state before this check existed, and one of them said so in its own opening
        /// sentence.
        /// </para>
        ///
        /// <para>
        /// Vendored third-party documentation is exempt: it is replaced wholesale rather than
        /// edited (`R6`), so imposing this repository's shape on it would guarantee a conflict on
        /// the next update.
        /// </para>
        /// </summary>
        [Fact]
        public void EveryPageHasAChangeLog()
        {
            List<string> missing = new List<string>();
            List<string> undated = new List<string>();
            int checked_ = 0;

            foreach (string file in MarkdownFiles())
            {
                string relative = Relative(file);

                // Vendored: replaced, never edited.
                if (relative.Contains("RichHudFramework")) continue;
                if (relative.Contains("NetworkAPI")) continue;

                checked_++;
                string text = File.ReadAllText(file);

                if (!Regex.IsMatch(text, @"(?m)^##\s+Change log\s*$"))
                {
                    missing.Add(relative);
                    continue;
                }

                // The log is the last section, and it holds at least one dated row.
                string log = text.Substring(text.LastIndexOf("## Change log", StringComparison.Ordinal));
                if (!Regex.IsMatch(log, @"(?m)^\|\s*\d{4}-\d{2}-\d{2}\s*\|"))
                {
                    undated.Add(relative);
                }
            }

            Assert.True(checked_ > 20,
                "only " + checked_ + " pages were checked, so this test is looking in the wrong"
                + " place and would pass whatever the documentation said");

            missing.Sort(StringComparer.Ordinal);
            undated.Sort(StringComparer.Ordinal);

            Assert.True(missing.Count == 0,
                "pages with no \"## Change log\" section (see docs/development.md, Documentation"
                + " conventions):\n  " + string.Join("\n  ", missing.ToArray()));

            Assert.True(undated.Count == 0,
                "pages whose change log holds no dated `| YYYY-MM-DD |` row:\n  "
                + string.Join("\n  ", undated.ToArray()));
        }

        /// <summary>
        /// Every page under `docs/` is reachable from the README's index.
        ///
        /// <para>
        /// The index is the only place a reader who does not already know a page exists can find
        /// it. Three pages — the block balance argument, the corpus reading and the field-tuning
        /// log — had been written, cited from their siblings and never listed, so the only route to
        /// them was a link inside a page you had to already be reading.
        /// </para>
        /// </summary>
        [Fact]
        public void EveryDocumentIsInTheIndex()
        {
            string index = File.ReadAllText(Path.Combine(RepoRoot(), "README.md"));

            List<string> missing = new List<string>();
            foreach (string file in Directory.GetFiles(Path.Combine(RepoRoot(), "docs"), "*.md"))
            {
                string name = "docs/" + Path.GetFileName(file);
                if (index.IndexOf("(" + name + ")", StringComparison.Ordinal) < 0) missing.Add(name);
            }

            missing.Sort(StringComparer.Ordinal);
            Assert.True(missing.Count == 0,
                "pages under docs/ that the README's documentation table does not list:\n  "
                + string.Join("\n  ", missing.ToArray()));
        }
        // --- The rules page ------------------------------------------------------------------
        //
        // The rules this repository is bound by are stated in one place, `docs/rules.md`, and
        // argued in the page that holds the evidence for each (`R13`). Three things can rot in
        // that arrangement without anything looking wrong: a page can cite a rule that no longer
        // exists, the page's own index can drift from the rules it states, and a *Checked by*
        // field can name a check that has stopped running. The third is the one that already
        // happened — three citations named a file rather than a class, the wrong class, and a
        // case that had been demoted to an uncalled helper — and an unchecked rule that reads as
        // checked is worse than one marked unchecked, because it ends the search (`R11`).

        private static string RulesPage()
        {
            return File.ReadAllText(Path.Combine(RepoRoot(), "docs", "rules.md"));
        }

        /// <summary>Every rule identifier the rules page states, taken from its own headings.</summary>
        private static HashSet<string> StatedRules()
        {
            HashSet<string> rules = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(RulesPage(), @"(?m)^#{3,4}\s+([EMDCROJ]\d{1,2})\s+—"))
            {
                rules.Add(m.Groups[1].Value);
            }

            return rules;
        }

        /// <summary>
        /// Every rule a page cites in its banner is a rule that exists.
        ///
        /// <para>
        /// A page that argues a standing rule opens with "The rules argued here are stated
        /// canonically in rules.md", naming each by identifier. That banner is the join between
        /// the page with the evidence and the page with the sentence, and a citation to a rule
        /// that has been renamed, absorbed or dropped reads exactly like one that resolves.
        /// </para>
        /// </summary>
        [Fact]
        public void EveryRuleCitedByAPageExists()
        {
            HashSet<string> stated = StatedRules();
            Assert.True(stated.Count > 40,
                "only " + stated.Count + " rules were read out of docs/rules.md, so this test is"
                + " parsing the page wrongly and would pass whatever any page cited");

            List<string> dangling = new List<string>();
            int banners = 0;

            foreach (string file in MarkdownFiles())
            {
                string relative = Relative(file);
                if (relative == "docs/rules.md") continue;

                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].IndexOf("stated canonically in", StringComparison.Ordinal) < 0) continue;

                    // The banner is a blockquote and may wrap over several lines.
                    StringBuilder banner = new StringBuilder(lines[i]);
                    for (int j = i + 1; j < lines.Length && lines[j].StartsWith(">", StringComparison.Ordinal); j++)
                    {
                        banner.Append(' ').Append(lines[j]);
                    }

                    banners++;
                    int cited = 0;
                    foreach (Match m in Regex.Matches(banner.ToString(), @"`([EMDCROJ]\d{1,2})`"))
                    {
                        cited++;
                        if (!stated.Contains(m.Groups[1].Value))
                        {
                            dangling.Add(relative + " cites `" + m.Groups[1].Value + "`");
                        }
                    }

                    Assert.True(cited > 0,
                        relative + " says its rules are stated canonically in rules.md and then"
                        + " names none of them by identifier");
                    break;
                }
            }

            Assert.True(banners > 10,
                "only " + banners + " pages carry a rules banner, so this test found almost"
                + " nothing to check and would pass on a page citing a rule that never existed");

            dangling.Sort(StringComparer.Ordinal);
            Assert.True(dangling.Count == 0,
                "rules cited by a page that docs/rules.md does not state:\n  "
                + string.Join("\n  ", dangling.ToArray()));
        }

        /// <summary>
        /// The rules page indexes every rule it states, and states every rule it indexes.
        ///
        /// <para>
        /// The index is what a reader scans and the body is what they then read, so a rule present
        /// in one and not the other is either invisible or a dangling row — and both look like an
        /// ordinary page. Every rule also belongs to exactly one principle, except the four filed
        /// as low value, which name their disposition instead.
        /// </para>
        /// </summary>
        [Fact]
        public void TheRulesPageIndexesEveryRuleItStates()
        {
            string page = RulesPage();

            HashSet<string> stated = StatedRules();
            HashSet<string> indexed = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> lowValue = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match m in Regex.Matches(page, @"(?m)^\|\s*\*\*([EMDCROJ]\d{1,2})\*\*\s*\|([^|]*)\|([^|]*)\|"))
            {
                indexed.Add(m.Groups[1].Value);
                if (m.Groups[3].Value.IndexOf("low value", StringComparison.Ordinal) >= 0)
                {
                    lowValue.Add(m.Groups[1].Value);
                }
            }

            Assert.True(indexed.Count > 40,
                "only " + indexed.Count + " rules were read out of the index, so this test is"
                + " parsing the page wrongly");

            List<string> unstated = new List<string>(indexed);
            unstated.RemoveAll(stated.Contains);
            unstated.Sort(StringComparer.Ordinal);
            Assert.True(unstated.Count == 0,
                "rules the index lists that the page never states:\n  "
                + string.Join("\n  ", unstated.ToArray()));

            List<string> unindexed = new List<string>(stated);
            unindexed.RemoveAll(indexed.Contains);
            unindexed.Sort(StringComparer.Ordinal);
            Assert.True(unindexed.Count == 0,
                "rules the page states that the index does not list:\n  "
                + string.Join("\n  ", unindexed.ToArray()));

            // Every rule that is still a rule follows from one of the principles.
            HashSet<string> underAPrinciple = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match row in Regex.Matches(page, @"(?m)^\|\s*\*\*(P\d{1,2})\*\*\s*\|(.*)$"))
            {
                foreach (Match cited in Regex.Matches(row.Groups[2].Value, @"`([EMDCROJ]\d{1,2})`"))
                {
                    underAPrinciple.Add(cited.Groups[1].Value);
                }
            }

            List<string> orphaned = new List<string>();
            foreach (string rule in stated)
            {
                if (lowValue.Contains(rule)) continue;
                if (!underAPrinciple.Contains(rule)) orphaned.Add(rule);
            }

            orphaned.Sort(StringComparer.Ordinal);
            Assert.True(orphaned.Count == 0,
                "rules that follow from no principle in the table:\n  "
                + string.Join("\n  ", orphaned.ToArray()));
        }

        /// <summary>Every source file a citation could name: the mod, the tests, the tools, the build.</summary>
        private static string CodeText()
        {
            if (_codeText != null) return _codeText;

            StringBuilder all = new StringBuilder();
            string[] patterns = { "*.cs", "*.csproj", "*.props", "*.json", "*.py", "*.sh" };
            foreach (string pattern in patterns)
            {
                foreach (string file in Directory.GetFiles(RepoRoot(), pattern, SearchOption.AllDirectories))
                {
                    string relative = Relative(file);
                    if (relative.Contains("/bin/") || relative.Contains("/obj/")) continue;
                    if (relative.StartsWith("out/", StringComparison.Ordinal)) continue;
                    all.Append(File.ReadAllText(file)).Append('\n');
                }
            }

            _codeText = all.ToString();
            return _codeText;
        }

        private static string _codeText;

        /// <summary>Test methods that actually run: a name carrying [Fact] or [Theory].</summary>
        private static HashSet<string> LiveCases()
        {
            HashSet<string> cases = new HashSet<string>(StringComparer.Ordinal);
            string tests = Path.Combine(RepoRoot(), "tests");

            foreach (string file in Directory.GetFiles(tests, "*.cs", SearchOption.AllDirectories))
            {
                string relative = Relative(file);
                if (relative.Contains("/bin/") || relative.Contains("/obj/")) continue;

                string text = File.ReadAllText(file);
                string current = null;

                foreach (Match m in Regex.Matches(text,
                    @"(?m)^\s*(?:public|internal)\s+(?:sealed\s+|static\s+|partial\s+|abstract\s+)*class\s+([A-Za-z0-9_]+)"
                    + @"|\[(?:Fact|Theory)[^\]]*\][\s\S]{0,400}?\bpublic\s+(?:async\s+)?[A-Za-z0-9_<>,\[\]\. ]+?\s([A-Za-z0-9_]+)\s*\("))
                {
                    if (m.Groups[1].Success)
                    {
                        current = m.Groups[1].Value;
                    }
                    else if (m.Groups[2].Success)
                    {
                        cases.Add(m.Groups[2].Value);
                        if (current != null) cases.Add(current);       // the class runs too
                    }
                }
            }

            return cases;
        }

        /// <summary>
        /// Every check the rules page cites resolves to something that runs.
        ///
        /// <para>
        /// This is `R11`. A name resolves when it is a live test case or a class holding one; when
        /// it is a type or member the code refers to somewhere other than its own declaration; or
        /// when it is a file that exists. The middle test is what catches the failure that produced
        /// the rule: `SealedBlocksAreRare` was cited as the check on a defect bound months after it
        /// had been demoted from a test case to an `internal static` helper nothing calls. It still
        /// compiled, it still read as enforcement, and it never ran again.
        /// </para>
        ///
        /// <para>
        /// The limit of the test is that "referred to somewhere" is not "reached at run time": a
        /// helper called only by another dead helper resolves here. It catches a name with no
        /// caller at all, which is the shape every stale citation on the page has had.
        /// </para>
        /// </summary>
        [Fact]
        public void EveryCheckCitedByTheRulesPageResolves()
        {
            string[] lines = File.ReadAllLines(Path.Combine(RepoRoot(), "docs", "rules.md"));
            HashSet<string> live = LiveCases();
            string code = CodeText();

            Assert.True(live.Count > 500,
                "only " + live.Count + " live test names were found, so this test is not reading"
                + " the suite and would pass on a citation to nothing");

            List<string> unresolved = new List<string>();
            int cited = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("*Checked by:*", StringComparison.Ordinal)) continue;

                StringBuilder field = new StringBuilder(lines[i]);
                for (int j = i + 1; j < lines.Length; j++)
                {
                    if (lines[j].Length == 0 || lines[j].StartsWith("*", StringComparison.Ordinal)) break;
                    field.Append(' ').Append(lines[j]);
                }

                foreach (Match m in Regex.Matches(field.ToString(), @"`([^`]+)`"))
                {
                    string name = m.Groups[1].Value;

                    // A file, by path or by name.
                    if (name.Contains("/") || Regex.IsMatch(name, @"\.[a-z]{1,6}$"))
                    {
                        cited++;
                        string path = Path.Combine(RepoRoot(), name.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(path)) continue;
                        if (Directory.GetFiles(RepoRoot(), Path.GetFileName(name), SearchOption.AllDirectories).Length > 0) continue;
                        unresolved.Add(name + " — no such file");
                        continue;
                    }

                    // Anything else is a citation only if it is written like an identifier.
                    if (!Regex.IsMatch(name, @"^[A-Z][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")) continue;

                    cited++;
                    string leaf = name.Substring(name.LastIndexOf('.') + 1);
                    if (live.Contains(leaf)) continue;

                    // A type or member the code refers to somewhere other than where it is declared.
                    if (Regex.Matches(code, @"\b" + Regex.Escape(leaf) + @"\b").Count > 1) continue;

                    unresolved.Add(name + " — declared nowhere, or declared and never used");
                }

                i++;
            }

            Assert.True(cited > 30,
                "only " + cited + " checks were read out of the rules page's *Checked by* fields,"
                + " so this test is parsing them wrongly");

            unresolved.Sort(StringComparer.Ordinal);
            Assert.True(unresolved.Count == 0,
                "checks docs/rules.md cites that do not resolve to anything that runs (R11):\n  "
                + string.Join("\n  ", unresolved.ToArray()));
        }
    }
}
