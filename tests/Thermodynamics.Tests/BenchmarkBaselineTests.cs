using System;
using System.Collections.Generic;
using System.IO;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    [Trait("speed", "slow")]
    public class BenchmarkBaselineTests
    {
/// <summary>RepoRoot operation.</summary>
        private static string RepoRoot()
        {
            return Thermodynamics.Harness.ShippedBlocks.RepoRoot();
        }

/// <summary>Keys operation.</summary>
        private static HashSet<string> Keys(IEnumerable<ReportRow> rows)
        {
/// <summary>HashSet operation.</summary>
            HashSet<string> keys = new HashSet<string>();
            foreach (ReportRow row in rows) keys.Add(row.Key);
            return keys;
        }

        [Fact]
/// <summary>TheCommittedBaselineCarriesTheKeysTheReportStillProduces operation.</summary>
        public void TheCommittedBaselineCarriesTheKeysTheReportStillProduces()
        {
            string path = Path.Combine(RepoRoot(), "tests", "benchmarks", "performance.csv");
            Assert.True(File.Exists(path), "no committed baseline at " + path);

            PerformanceReport.Repeats = 1;
            List<ReportRow> fresh = PerformanceReport.Run("ship", 600, 2, new int[] { 600 });

/// <summary>Keys operation.</summary>
            HashSet<string> committed = Keys(PerformanceReport.ParseCsv(File.ReadAllText(path)));
/// <summary>Keys operation.</summary>
            HashSet<string> current = Keys(fresh);

/// <summary>List operation.</summary>
            List<string> missing = new List<string>();
            foreach (string key in current)
            {
                if (!key.StartsWith("ladder/") && !committed.Contains(key)) missing.Add(key);
            }

/// <summary>List operation.</summary>
            List<string> orphaned = new List<string>();
            foreach (string key in committed)
            {
                if (!key.StartsWith("ladder/") && !current.Contains(key)) orphaned.Add(key);
            }

            missing.Sort();
            orphaned.Sort();

            Assert.True(missing.Count == 0 && orphaned.Count == 0,
                "tests/benchmarks/performance.csv no longer matches the report. Re-record it with\n"
                + "  dotnet run --project Thermodynamics.Sim -- bench report --size 32000 --max 125000 --csv benchmarks\n"
                + "figures the report now produces and the baseline lacks:\n  "
                + string.Join("\n  ", missing.ToArray())
                + "\nfigures the baseline carries and the report no longer produces:\n  "
                + string.Join("\n  ", orphaned.ToArray()));
        }
    }
}
