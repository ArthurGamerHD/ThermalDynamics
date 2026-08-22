using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The performance report exists to be compared against itself across changes, so the thing
    /// that must not rot is its <em>shape</em>: the sections it produces, the keys it produces
    /// them under, and its ability to read back what it wrote.
    ///
    /// A benchmark suite fails quietly. If a case is renamed, a diff against last month's
    /// baseline silently drops that row and reports no regression; if the CSV round-trip breaks,
    /// every comparison reads as "everything is new". Neither shows up as a failure anywhere
    /// else, which is why these are ordinary tests rather than something a person remembers to
    /// check.
    /// </summary>
    [Trait("speed", "slow")]
    public class PerformanceReportTests
    {
        /// <summary>Small enough to run in the ordinary suite; the shape is the same at any size.</summary>
        private static List<ReportRow> Small()
        {
            PerformanceReport.Repeats = 1;
            return PerformanceReport.Run("ship", 600, 2, new int[] { 600 });
        }

        /// <summary>
        /// The clamp A/B measures two regimes, and the whole point of it is that they are
        /// different regimes.
        ///
        /// Both rows would still be produced, and both would still look plausible, if the resolved
        /// case had drifted stiff or the refused case had been given enough substeps to resolve —
        /// and the pair would then be one measurement printed twice, reporting a saving of nothing
        /// and a worst case of nothing. The <c>clamp live</c> flags are what distinguish them, so
        /// they are asserted rather than merely printed.
        /// </summary>
        [Fact]
        public void TheClampComparisonMeasuresBothRegimes()
        {
            List<ReportRow> rows = Small();

            double resolved = Value(rows, "overshoot clamp", "resolved", "clamp live");
            double refused = Value(rows, "overshoot clamp", "refused", "clamp live");

            Assert.Equal(0.0, resolved);
            Assert.Equal(1.0, refused);

            // And both halves of each A/B are present, or a comparison has nothing to compare.
            Assert.True(Value(rows, "overshoot clamp", "resolved", "step, gated") > 0.0);
            Assert.True(Value(rows, "overshoot clamp", "resolved", "step, always clamped") > 0.0);
            Assert.True(Value(rows, "overshoot clamp", "refused", "step, gated") > 0.0);
            Assert.True(Value(rows, "overshoot clamp", "refused", "step, always clamped") > 0.0);
        }

        /// <summary>
        /// Diagnostics cost something, and the report has claimed otherwise before.
        ///
        /// `bench report --diagnostics` set a flag that this file never read, so the whole report
        /// ran in the cheap configuration under a name that said it had not. Every field dump is
        /// taken with the per-mechanism watts on — taking a dump is what turns them on — so a
        /// report that cannot reach that configuration cannot be compared against one.
        /// </summary>
        [Fact]
        public void TheDiagnosticsRowMeasuresBothConfigurations()
        {
            List<ReportRow> rows = Small();

            Assert.True(Value(rows, "diagnostics", "per-mechanism watts", "step, off") > 0.0);
            Assert.True(Value(rows, "diagnostics", "per-mechanism watts", "step, on") > 0.0);
            Assert.True(Value(rows, "diagnostics", "per-mechanism watts", "step, every substep") > 0.0);

            // What distinguishes the three cases, asserted off the node objects rather than off
            // the timings. An earlier version of this test compared the milliseconds — a two-tick
            // run on a six-hundred-block hull, where the difference is inside the scheduler's
            // noise — and failed on a busy machine while the code was correct.
            Assert.Equal(0.0, Value(rows, "diagnostics", "per-mechanism watts", "written, off"));
            Assert.Equal(1.0, Value(rows, "diagnostics", "per-mechanism watts", "written, on"));
            Assert.Equal(1.0, Value(rows, "diagnostics", "per-mechanism watts", "written, every substep"));
        }

        private static double Value(IList<ReportRow> rows, string section, string name, string metric)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                ReportRow row = rows[i];
                if (row.Section == section && row.Case == name && row.Metric == metric) return row.Value;
            }

            Assert.Fail("no row for " + section + " / " + name + " / " + metric);
            return 0.0;
        }

        [Fact]
        public void TheReportCoversEverySectionAndEveryFeature()
        {
            List<ReportRow> rows = Small();

            HashSet<string> sections = new HashSet<string>();
            HashSet<string> features = new HashSet<string>();

            for (int i = 0; i < rows.Count; i++)
            {
                sections.Add(rows[i].Section);
                if (rows[i].Section == "features") features.Add(rows[i].Case);
            }

            Assert.Contains("machine", sections);
            Assert.Contains("ladder", sections);
            Assert.Contains("features", sections);
            Assert.Contains("substep cap", sections);
            Assert.Contains("overshoot clamp", sections);
            Assert.Contains("diagnostics", sections);

            // Every switch a world can turn off has to be in the breakdown, or a feature can grow
            // expensive without any report noticing.
            foreach (string expected in new string[]
            {
                "conduction", "radiation", "convection", "solar", "self shadow", "waste heat",
                "heat sources", "friction", "damage", "coolant loops", "room air", "heat pumps",
                "conduction clamp", "environment clamp",
            })
            {
                Assert.Contains(expected, features);
            }

        }

        [Fact]
        public void EveryFigureHasAUniqueKey()
        {
            List<ReportRow> rows = Small();
            HashSet<string> keys = new HashSet<string>();

            for (int i = 0; i < rows.Count; i++)
            {
                Assert.True(keys.Add(rows[i].Key),
                    "two figures share the key " + rows[i].Key + ", so a comparison cannot join on it");
            }
        }

        [Fact]
        public void TheCsvRoundTrips()
        {
            List<ReportRow> rows = Small();
            List<ReportRow> read = PerformanceReport.ParseCsv(PerformanceReport.Csv(rows));

            Assert.Equal(rows.Count, read.Count);

            for (int i = 0; i < rows.Count; i++)
            {
                Assert.Equal(rows[i].Key, read[i].Key);
                Assert.Equal(rows[i].Unit, read[i].Unit);
                Assert.Equal(rows[i].LowerIsBetter, read[i].LowerIsBetter);

                // Round-tripped exactly, not approximately: a comparison against a baseline is a
                // subtraction, and a value that loses digits on the way to disk turns into a
                // regression the next time anyone reads it.
                Assert.Equal(rows[i].Value, read[i].Value);
            }
        }

        [Fact]
        public void ComparingAReportAgainstItselfFindsNothing()
        {
            List<ReportRow> rows = Small();
            string diff = PerformanceReport.Compare(rows, rows);

            Assert.Contains("regressions (0)", diff);
            Assert.DoesNotContain("not in the baseline (", diff);
            Assert.DoesNotContain("gone since the baseline (", diff);
        }

        [Fact]
        public void AWorseNumberIsReportedAsARegressionAndABetterOneIsNot()
        {
            List<ReportRow> baseline = new List<ReportRow>
            {
                new ReportRow { Section = "s", Case = "c", Metric = "slower", Value = 1.0, LowerIsBetter = true },
                new ReportRow { Section = "s", Case = "c", Metric = "faster", Value = 1.0, LowerIsBetter = true },
            };

            List<ReportRow> current = new List<ReportRow>
            {
                new ReportRow { Section = "s", Case = "c", Metric = "slower", Value = 1.5, LowerIsBetter = true },
                new ReportRow { Section = "s", Case = "c", Metric = "faster", Value = 0.5, LowerIsBetter = true },
            };

            string diff = PerformanceReport.Compare(baseline, current);

            Assert.Contains("regressions (1)", diff);
            Assert.Contains("s/c/slower", diff);
            Assert.Contains("improvements", diff);
        }

        /// <summary>
        /// A change smaller than the machine's own run-to-run spread is not a finding, and a
        /// report that calls it one trains its reader to ignore the section.
        /// </summary>
        [Fact]
        public void AChangeInsideTheThresholdIsNotReported()
        {
            List<ReportRow> baseline = new List<ReportRow>
            {
                new ReportRow { Section = "s", Case = "c", Metric = "m", Value = 1.00 },
            };

            List<ReportRow> current = new List<ReportRow>
            {
                new ReportRow { Section = "s", Case = "c", Metric = "m", Value = 1.02 },
            };

            Assert.Contains("regressions (0)", PerformanceReport.Compare(baseline, current, 0.05));
            Assert.Contains("regressions (1)", PerformanceReport.Compare(baseline, current, 0.01));
        }

        /// <summary>
        /// The report measures features by switching them off, so it is also a check that they
        /// can be: a toggle that has quietly stopped being wired to anything would show as
        /// costing exactly nothing in both columns.
        /// </summary>
        [Fact]
        public void TheExpensiveFeaturesCostSomething()
        {
            List<ReportRow> rows = Small();

            foreach (string feature in new string[] { "conduction", "radiation", "solar" })
            {
                double isolated = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Section == "features" && rows[i].Case == feature
                        && rows[i].Metric == "isolated")
                    {
                        isolated = rows[i].Value;
                    }
                }

                Assert.True(isolated != 0d,
                    feature + " cost exactly nothing in isolation, which means the switch is no"
                    + " longer wired to anything the solver reads");
            }
        }
    }
}
