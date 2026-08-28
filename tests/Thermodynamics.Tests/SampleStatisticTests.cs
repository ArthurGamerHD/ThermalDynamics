using System;
using System.Collections.Generic;
using System.IO;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The lab that asks which summary of a stage's repeats is reproducible.**
    ///
    /// <para>
    /// `SampleStatisticLab` decides a rule the whole performance page rests on, so its arithmetic is
    /// checked against hand-computed answers rather than against itself (`E7`), and the two ways it
    /// can quietly compare the wrong thing — a stage present in some runs and not others, a run file
    /// that parsed to nothing — are checked to be loud (`P2`).
    /// </para>
    /// </summary>
    public class SampleStatisticTests
    {
        /// <summary>
        /// The quantile is by nearest rank, so every value it returns is a reading the instrument
        /// actually took. Interpolating would invent a time that was never measured, which is a poor
        /// property for a figure a comparison divides.
        /// </summary>
        [Fact]
        public void AQuantileIsAReadingThatWasActuallyTaken()
        {
            // A hundred and one sorted readings valued 0..100, so index = floor(q * 100) is the
            // value: every candidate lands somewhere different and the test can tell them apart.
            double[] sorted = new double[101];
            for (int i = 0; i < sorted.Length; i++) sorted[i] = i;

            Assert.Equal(0d, SampleStatisticLab.Quantile(sorted, 0d));
            Assert.Equal(1d, SampleStatisticLab.Quantile(sorted, 0.01d));
            Assert.Equal(5d, SampleStatisticLab.Quantile(sorted, 0.05d));
            Assert.Equal(10d, SampleStatisticLab.Quantile(sorted, 0.10d));
            Assert.Equal(25d, SampleStatisticLab.Quantile(sorted, 0.25d));
            Assert.Equal(50d, SampleStatisticLab.Quantile(sorted, 0.50d));

            // Every answer is one of the inputs.
            foreach (double q in SampleStatisticLab.Quantiles)
            {
                Assert.Contains(SampleStatisticLab.Quantile(sorted, q), sorted);
            }

            // **On a short sample the low quantiles collapse onto the minimum**, by nearest rank
            // and correctly: there is no fifth-percentile reading in ten. It is asserted rather
            // than left to surprise a reader of a `--repeats 10` run, and it is why the floor the
            // lab runs at matters to the choice of statistic as much as to the statistic's value.
            double[] ten = { 1d, 2d, 3d, 4d, 5d, 6d, 7d, 8d, 9d, 10d };
            Assert.Equal(1d, SampleStatisticLab.Quantile(ten, 0.05d));
            Assert.Equal(1d, SampleStatisticLab.Quantile(ten, 0.10d));
            Assert.Equal(3d, SampleStatisticLab.Quantile(ten, 0.25d));

            // An empty sample answers zero rather than throwing out of a reporting path.
            Assert.Equal(0d, SampleStatisticLab.Quantile(new double[0], 0.5d));
        }

        /// <summary>
        /// The spread is between runs, per statistic — the figure that decides which statistic to
        /// keep, so it is computed here by hand and compared.
        /// </summary>
        [Fact]
        public void TheSpreadIsBetweenRunsForOneStatistic()
        {
            // Two runs of one stage. The minima are 1 and 3 — 200 % apart. The medians are 10 and
            // 11 — 10 % apart. A statistic can be far more reproducible than the minimum, and that
            // is the whole question this lab exists to ask.
            List<SampleStatisticLab.Series> series = new List<SampleStatisticLab.Series>
            {
                Series("run1", "links", new double[] { 1d, 10d, 10d, 10d, 12d }),
                Series("run2", "links", new double[] { 3d, 11d, 11d, 11d, 12d }),
            };

            List<string> dropped;
            List<SampleStatisticLab.Row> rows = SampleStatisticLab.Compare(series, out dropped);

            Assert.Empty(dropped);

            SampleStatisticLab.Row min = Find(rows, "links", 0d);
            Assert.Equal(2, min.Runs);
            Assert.Equal(1d, min.Lowest);
            Assert.Equal(3d, min.Highest);
            Assert.Equal(200d, min.SpreadPercent, 6);

            SampleStatisticLab.Row median = Find(rows, "links", 0.50d);
            Assert.Equal(10d, median.Lowest);
            Assert.Equal(11d, median.Highest);
            Assert.Equal(10d, median.SpreadPercent, 6);
        }

        /// <summary>
        /// A stage that is not in every run is named and dropped, never averaged over the runs that
        /// happen to hold it. Comparing a stage across two runs and calling it three is the
        /// partial-sweep failure in miniature (`E4`).
        /// </summary>
        [Fact]
        public void AStageMissingFromARunIsDroppedAndSaidSo()
        {
            List<SampleStatisticLab.Series> series = new List<SampleStatisticLab.Series>
            {
                Series("run1", "links", new double[] { 1d, 2d }),
                Series("run2", "links", new double[] { 1d, 2d }),
                Series("run1", "rooms", new double[] { 5d, 6d }),
            };

            List<string> dropped;
            List<SampleStatisticLab.Row> rows = SampleStatisticLab.Compare(series, out dropped);

            Assert.Single(dropped);
            Assert.Contains("rooms", dropped[0]);
            Assert.Contains("1 of 2", dropped[0]);

            foreach (SampleStatisticLab.Row row in rows) Assert.Equal("links", row.Stage);
        }

        /// <summary>
        /// A run file that parsed to no repeats throws rather than contributing nothing quietly:
        /// a comparison silently short of a run reads exactly like one that had it (`E8`).
        /// </summary>
        [Fact]
        public void ARunThatParsedToNothingIsLoud()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "stage,blocks,repeat,ms\n");
                Assert.Throws<InvalidOperationException>(() => SampleStatisticLab.Read(path, "run1"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>The file the stage lab writes is the file this lab reads, round trip.</summary>
        [Fact]
        public void TheStageLabsSamplesFileIsWhatThisLabReads()
        {
            int repeats = StageLab.Repeats;
            int confirming = StageLab.ConfirmingRepeats;

            StageLab.Repeats = 3;
            StageLab.ConfirmingRepeats = 2;
            try
            {
                List<StageLab.Row> stageRows = StageLab.Run("ship", 2000, new[] { "surfaces", "links" });

                string path = Path.Combine(Path.GetTempPath(),
                    "samples-" + Guid.NewGuid().ToString("n") + ".csv");
                File.WriteAllText(path, StageLab.SamplesCsv(stageRows));
                try
                {
                    List<SampleStatisticLab.Series> read = SampleStatisticLab.Read(path, "run1");

                    Assert.Equal(stageRows.Count, read.Count);
                    for (int i = 0; i < stageRows.Count; i++)
                    {
                        Assert.Equal(stageRows[i].Stage, read[i].Stage);
                        Assert.Equal(stageRows[i].Repeats, read[i].Samples.Count);
                        Assert.Equal(stageRows[i].Samples.Count, read[i].Samples.Count);

                        // The fastest the stage reported is the fastest in the file, or the
                        // artefact is not of the run that produced the row.
                        double[] sorted = read[i].Samples.ToArray();
                        Array.Sort(sorted);
                        Assert.Equal(stageRows[i].BestMs, sorted[0], 9);
                    }
                }
                finally
                {
                    File.Delete(path);
                }
            }
            finally
            {
                StageLab.Repeats = repeats;
                StageLab.ConfirmingRepeats = confirming;
            }
        }

        private static SampleStatisticLab.Series Series(string run, string stage, double[] samples)
        {
            SampleStatisticLab.Series series = new SampleStatisticLab.Series();
            series.Run = run;
            series.Stage = stage;
            series.Samples.AddRange(samples);
            return series;
        }

        private static SampleStatisticLab.Row Find(IList<SampleStatisticLab.Row> rows, string stage, double quantile)
        {
            foreach (SampleStatisticLab.Row row in rows)
            {
                if (row.Stage == stage && row.Quantile == quantile) return row;
            }

            throw new InvalidOperationException("no row for " + stage + " at " + quantile);
        }
    }
}
