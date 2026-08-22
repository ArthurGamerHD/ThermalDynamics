using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Every command the harness answers to is one a reader can find.
    ///
    /// <para>
    /// The front end had grown thirty-nine commands and six of them appeared in no usage text:
    /// the wind day, the planet climate table and its file generator, the sealed-block scan, and
    /// three benchmarks. Each of those is a measurement somebody built and then had no way of
    /// discovering again, which is how `CoolingLadder` ended up written, wired to nothing and
    /// unread for the life of the repository.
    /// </para>
    ///
    /// <para>
    /// Read from the source rather than by running the program, because the point is the dispatch
    /// table and the help text agreeing, and both are text. It cannot check that a description is
    /// *accurate* — only that a command cannot be added silently.
    /// </para>
    /// </summary>
    public class SimCommandTests
    {
        private static string Source()
        {
            return File.ReadAllText(Path.Combine(
                Thermodynamics.Harness.ShippedBlocks.RepoRoot(),
                "tests", "Thermodynamics.Sim", "Program.cs"));
        }

        /// <summary>
        /// The cases of one switch statement, found by matching braces from its opening one.
        /// </summary>
        private static List<string> CasesOf(string source, string switchHeader)
        {
            int start = source.IndexOf(switchHeader, StringComparison.Ordinal);
            Assert.True(start >= 0, "no switch matching \"" + switchHeader
                + "\" — the front end has been restructured and this test is reading nothing");

            int open = source.IndexOf('{', start);
            int depth = 0;
            int i = open;

            while (i < source.Length)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0) break;
                }
                i++;
            }

            List<string> cases = new List<string>();
            foreach (Match match in Regex.Matches(source.Substring(open, i - open),
                "case \"([a-z0-9\\-]+)\":"))
            {
                cases.Add(match.Groups[1].Value);
            }

            return cases;
        }

        [Fact]
        public void EveryTopLevelCommandIsInTheUsageText()
        {
            string source = Source();
            string usage = source.Substring(source.IndexOf("private static void PrintUsage", StringComparison.Ordinal));

            List<string> commands = CasesOf(source, "switch (args[0])");
            Assert.True(commands.Count > 15,
                "only " + commands.Count + " commands were found, so the dispatch has changed shape");

            List<string> hidden = new List<string>();
            foreach (string command in commands)
            {
                if (!Regex.IsMatch(usage, "\"  " + Regex.Escape(command) + "[ \"]")) hidden.Add(command);
            }

            hidden.Sort(StringComparer.Ordinal);
            Assert.True(hidden.Count == 0,
                "commands the harness answers to and its usage text does not mention:\n  "
                + string.Join("\n  ", hidden.ToArray()));
        }

        [Fact]
        public void EveryBenchmarkIsInTheUsageText()
        {
            string source = Source();
            string usage = source.Substring(source.IndexOf("private static void PrintUsage", StringComparison.Ordinal));

            List<string> benchmarks = CasesOf(source, "private static int BenchCommand");
            Assert.True(benchmarks.Count > 10,
                "only " + benchmarks.Count + " benchmarks were found, so the dispatch has changed shape");

            List<string> hidden = new List<string>();
            foreach (string benchmark in benchmarks)
            {
                if (usage.IndexOf("bench " + benchmark, StringComparison.Ordinal) < 0) hidden.Add(benchmark);
            }

            hidden.Sort(StringComparer.Ordinal);
            Assert.True(hidden.Count == 0,
                "benchmarks the harness answers to and its usage text does not mention:\n  "
                + string.Join("\n  ", hidden.ToArray()));
        }

        /// <summary>
        /// Every lab that produces a report is reachable. A lab nobody can run is a measurement
        /// nobody takes, and the repository had one — 297 lines answering an open balance
        /// criterion, wired to nothing.
        /// </summary>
        [Fact]
        public void EveryLabThatProducesAReportIsReachable()
        {
            string harness = Path.Combine(Thermodynamics.Harness.ShippedBlocks.RepoRoot(),
                "tests", "Thermodynamics.Harness");

            string front = Source();
            string tests = "";
            foreach (string file in Directory.GetFiles(Path.Combine(
                Thermodynamics.Harness.ShippedBlocks.RepoRoot(), "tests", "Thermodynamics.Tests"), "*.cs"))
            {
                tests += File.ReadAllText(file);
            }

            List<string> orphans = new List<string>();
            int labs = 0;

            foreach (string file in Directory.GetFiles(harness, "*.cs"))
            {
                string source = File.ReadAllText(file);

                // A lab is a class with a public, argument-light Report() — the shape every
                // command in the front end calls.
                Match declaration = Regex.Match(source,
                    @"(?m)^[ \t]*public\s+static\s+class\s+(\w+)");
                if (!declaration.Success) continue;
                if (!Regex.IsMatch(source, @"public\s+static\s+string\s+Report\s*\(")) continue;

                string name = declaration.Groups[1].Value;
                labs++;

                if (front.IndexOf(name + ".", StringComparison.Ordinal) >= 0) continue;
                if (tests.IndexOf(name + ".", StringComparison.Ordinal) >= 0) continue;

                orphans.Add(name);
            }

            Assert.True(labs > 5,
                "only " + labs + " labs were recognised, so the pattern has changed and this test"
                + " is no longer reading anything");

            orphans.Sort(StringComparer.Ordinal);
            Assert.True(orphans.Count == 0,
                "labs that produce a report no command runs and no test reads:\n  "
                + string.Join("\n  ", orphans.ToArray()));
        }
    }
}
