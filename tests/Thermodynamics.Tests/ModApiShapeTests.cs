using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The mod API's delegate table has the shape the API page says it has.
    ///
    /// <para>
    /// A consumer reaches this mod by casting: `api["GetBlockTemperature"] as
    /// Func&lt;IMySlimBlock, float&gt;`. A cast that does not match returns **null**, not an error —
    /// so a signature that changes on one side and not the other produces a second mod whose
    /// feature silently does nothing, in a session, with no message anywhere. `R9` calls the API
    /// page part of the contract, and until now the only thing checked was that every entry is
    /// *named* there.
    /// </para>
    ///
    /// <para>
    /// Textual, because the shape is text on both sides and neither can be reached without a
    /// session. It cannot check that a signature is a good one; it checks that there is one answer
    /// rather than two (`D3`).
    /// </para>
    /// </summary>
    public class ModApiShapeTests
    {
        private static string RepoRoot()
        {
            return Thermodynamics.Harness.ShippedBlocks.RepoRoot();
        }

        /// <summary>
        /// Every `methods["Name"] = new Func&lt;…&gt;` in the table, as name against signature.
        ///
        /// The generic argument list is taken by matching angle brackets rather than by regex,
        /// because `MyTuple&lt;float, float, float, int&gt;` nests and a lazy pattern stops at the
        /// first `&gt;` it meets — which is the middle of the tuple.
        /// </summary>
        private static Dictionary<string, string> Declared()
        {
            string source = File.ReadAllText(Path.Combine(RepoRoot(),
                "Data", "Scripts", "Thermodynamics", "ThermalApi.cs"));

            Dictionary<string, string> shapes = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (Match match in Regex.Matches(source, @"methods\[""(\w+)""\]"))
            {
                int from = source.IndexOf("new Func<", match.Index, StringComparison.Ordinal);
                if (from < 0) continue;

                int open = source.IndexOf('<', from);
                int depth = 0;
                int i = open;

                while (i < source.Length)
                {
                    if (source[i] == '<') depth++;
                    else if (source[i] == '>')
                    {
                        depth--;
                        if (depth == 0) break;
                    }
                    i++;
                }

                if (i >= source.Length) continue;
                shapes[match.Groups[1].Value] = Normalise("Func" + source.Substring(open, i - open + 1));
            }

            return shapes;
        }

        /// <summary>Every `| `Name` | `Func<…>` |` row of the API page's tables.</summary>
        private static Dictionary<string, string> Documented()
        {
            string page = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "api.md"));

            Dictionary<string, string> shapes = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (Match match in Regex.Matches(page,
                @"\|\s*`(\w+)`\s*\|\s*`(Func<[^`]*)`\s*\|"))
            {
                shapes[match.Groups[1].Value] = Normalise(match.Groups[2].Value);
            }

            return shapes;
        }

        /// <summary>Whitespace is not part of a signature, and the two sides space them differently.</summary>
        private static string Normalise(string signature)
        {
            return Regex.Replace(signature, @"\s+", "");
        }

        [Fact]
        public void EveryEntryHasTheSignatureTheApiPageGivesIt()
        {
            Dictionary<string, string> declared = Declared();
            Dictionary<string, string> documented = Documented();

            Assert.True(declared.Count > 10,
                "only " + declared.Count + " entries were read out of the delegate table, so it has"
                + " changed shape and this test is reading nothing");
            Assert.True(documented.Count > 10,
                "only " + documented.Count + " signatures were read out of the API page");

            List<string> wrong = new List<string>();

            foreach (KeyValuePair<string, string> entry in declared)
            {
                string page;
                if (!documented.TryGetValue(entry.Key, out page))
                {
                    wrong.Add(entry.Key + ": the page gives it no signature");
                    continue;
                }

                if (page == entry.Value) continue;

                wrong.Add(entry.Key + ":\n      code " + entry.Value + "\n      page " + page);
            }

            // And the other direction: a signature on the page for something the table no longer
            // holds is an entry a consumer will cast and get null from.
            foreach (KeyValuePair<string, string> entry in documented)
            {
                if (!declared.ContainsKey(entry.Key))
                {
                    wrong.Add(entry.Key + ": the page documents it and the table does not hold it");
                }
            }

            wrong.Sort(StringComparer.Ordinal);
            Assert.True(wrong.Count == 0,
                "the delegate table and the API page disagree about " + wrong.Count
                + " entries:\n  " + string.Join("\n  ", wrong.ToArray()));
        }

        /// <summary>
        /// The worked examples on the page cast with the same signatures its table gives, because a
        /// reader copies the example rather than the table — and an example that casts wrongly
        /// hands them a null they will not understand.
        /// </summary>
        [Fact]
        public void TheWorkedExamplesCastWithTheSignaturesTheTableGives()
        {
            string page = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "api.md"));
            Dictionary<string, string> documented = Documented();

            List<string> wrong = new List<string>();
            int checked_ = 0;

            foreach (Match match in Regex.Matches(page,
                @"api\[""(\w+)""\]\s*as\s+(Func<[^;>]*(?:<[^>]*>)?[^;]*?)>\s*;"))
            {
                string name = match.Groups[1].Value;
                string cast = Normalise(match.Groups[2].Value + ">");

                string table;
                if (!documented.TryGetValue(name, out table)) continue;

                checked_++;
                if (cast == table) continue;

                wrong.Add(name + ":\n      example " + cast + "\n      table   " + table);
            }

            Assert.True(checked_ > 3,
                "only " + checked_ + " worked casts were read, so this test is not reading the"
                + " examples");

            wrong.Sort(StringComparer.Ordinal);
            Assert.True(wrong.Count == 0,
                "worked examples that cast differently from the table:\n  "
                + string.Join("\n  ", wrong.ToArray()));
        }
    }
}
