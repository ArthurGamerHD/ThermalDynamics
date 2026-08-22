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
    /// Twenty-six pages under `docs/`, two READMEs and a tools page cross-reference each other
    /// several hundred times, and every one of those references is a claim that a file, a heading
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
    }
}
