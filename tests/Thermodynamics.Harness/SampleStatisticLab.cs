using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **Which summary of a stage's repeats reproduces between runs.**
    ///
    /// <para>
    /// The stage lab has reported the fastest repeat since pass 1, on the argument that timing noise
    /// is one-sided so averaging it in measures the operating system. That argument is sound and it
    /// is not the whole question: it says the minimum is the least *biased* summary, not that the
    /// minimum of a sample of this size is *reproducible*. Pass 8 found fifteen repeats nowhere near
    /// enough for it and raised the floor to a hundred; pass 9's iteration 2 then found five stages
    /// of eight failing to reproduce their own best inside four hundred.
    /// </para>
    ///
    /// <para>
    /// So this compares the candidates on the same evidence. Given the raw repeats from several
    /// runs of one binary — <see cref="StageLab.SamplesCsv"/>, one file per process — it reports,
    /// per stage and per candidate statistic, the spread between runs. **The statistic to keep is
    /// the one whose value two runs of the same code agree on**, because that is the only property
    /// a comparison uses. See performance.md, Pass 9, Iteration 3.
    /// </para>
    ///
    /// <para>
    /// The candidates are the minimum and four low quantiles. Nothing above the median is offered:
    /// the one-sided-noise argument rules the mean and the upper tail out on evidence that has not
    /// changed, and this lab is asking a narrower question than *which statistic is best in general*.
    /// </para>
    /// </summary>
    public static class SampleStatisticLab
    {
        /// <summary>
        /// The summaries compared, as a quantile of the sorted repeats. Zero is the minimum, which
        /// is what the lab reports today and so is the row every other row is judged against.
        /// </summary>
        public static readonly double[] Quantiles = { 0d, 0.01d, 0.05d, 0.10d, 0.25d, 0.50d };

        public static string NameOf(double quantile)
        {
            if (quantile <= 0d) return "min";
            if (quantile >= 0.5d) return "median";
            return "p" + ((int)Math.Round(quantile * 100d)).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>One run's repeats for one stage.</summary>
        public class Series
        {
            public string Run;
            public string Stage;

            /// <summary>
            /// When the process that took these repeats started, from the artefact's `taken_utc`
            /// column; empty for a file written before <see cref="StageLab.TakenUtc"/> existed.
            /// See <see cref="WindowSpan"/> for what it is for.
            /// </summary>
            public string TakenUtc = string.Empty;

            public readonly List<double> Samples = new List<double>();
        }

        /// <summary>
        /// How far apart the runs being compared were taken, and whether that is one window.
        ///
        /// <para>
        /// **Every spread this lab reports is a spread between runs, and a spread between runs of
        /// two sessions is not the same measurement as a spread between runs of one.** Pass 9,
        /// Iteration 6 measured bit-identical exposure code at a 4.75 ms median across twelve
        /// processes of one held window — agreeing to 1.3 % — against the 11.40 ms of a session two
        /// days earlier. Nothing in the artefacts said they were different sessions, and no
        /// summary of the repeats could have: what moved was the whole distribution.
        /// </para>
        /// </summary>
        public class WindowSpan
        {
            public string Earliest = string.Empty;
            public string Latest = string.Empty;

            /// <summary>Runs whose artefact carried no stamp, which cannot be placed in a window at all.</summary>
            public int Unstamped;

            public TimeSpan Elapsed;

            /// <summary>
            /// Whether the runs are close enough together to be read as one window.
            ///
            /// <para>
            /// **An hour, and it is a heuristic rather than a law.** `heavy` windows are capped in
            /// tens of minutes and a pairing's legs are taken inside one, so runs an hour apart were
            /// not taken together whatever else is true; runs inside an hour usually were. An
            /// unstamped run is not one window with anything, because nothing says where it sits.
            /// </para>
            /// </summary>
            public static readonly TimeSpan OneWindow = TimeSpan.FromHours(1d);

            public bool IsOneWindow
            {
                get { return Unstamped == 0 && Elapsed <= OneWindow; }
            }
        }

        /// <summary>
        /// The window the given runs were taken in. Stamps that do not parse are counted as
        /// unstamped rather than skipped, because a stamp nobody can read is not evidence that two
        /// runs were taken together (`E8`).
        /// </summary>
        public static WindowSpan Window(IList<Series> series)
        {
            WindowSpan span = new WindowSpan();
            List<DateTime> stamps = new List<DateTime>();
            HashSet<string> seenRuns = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> unstampedRuns = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < series.Count; i++)
            {
                Series one = series[i];
                if (!seenRuns.Add(one.Run + "\u0000" + one.TakenUtc)) continue;

                DateTime stamp;
                if (string.IsNullOrEmpty(one.TakenUtc)
                    || !DateTime.TryParse(one.TakenUtc, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out stamp))
                {
                    unstampedRuns.Add(one.Run);
                    continue;
                }

                stamps.Add(stamp);
            }

            span.Unstamped = unstampedRuns.Count;
            if (stamps.Count == 0) return span;

            stamps.Sort();
            span.Earliest = stamps[0].ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            span.Latest = stamps[stamps.Count - 1].ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            span.Elapsed = stamps[stamps.Count - 1] - stamps[0];
            return span;
        }

        /// <summary>What one stage's runs say about one candidate statistic.</summary>
        public class Row
        {
            public string Stage;
            public double Quantile;
            public int Runs;

            /// <summary>The statistic's value in each run, in the order the runs were given.</summary>
            public readonly List<double> Values = new List<double>();

            public double Lowest;
            public double Highest;

            /// <summary>
            /// The spread between the runs as a share of the lowest — the figure that decides this,
            /// because it is what a pairing's ratio inherits when the two legs are two runs.
            /// </summary>
            public double SpreadPercent
            {
                get { return Lowest <= 0d ? 0d : 100d * (Highest - Lowest) / Lowest; }
            }
        }

        /// <summary>
        /// The quantile of a sample, by nearest rank on the sorted values, which needs no
        /// interpolation and returns a reading the instrument actually took. A quantile of zero is
        /// the minimum exactly.
        /// </summary>
        public static double Quantile(IList<double> sorted, double quantile)
        {
            if (sorted == null || sorted.Count == 0) return 0d;
            if (quantile <= 0d) return sorted[0];

            int index = (int)Math.Floor(quantile * (sorted.Count - 1));
            if (index < 0) index = 0;
            if (index >= sorted.Count) index = sorted.Count - 1;
            return sorted[index];
        }

        /// <summary>Reads one `samples.csv`, tagging every series with the run's name.</summary>
        public static List<Series> Read(string path, string run)
        {
            List<Series> series = new List<Series>();
            Dictionary<string, Series> byStage = new Dictionary<string, Series>(StringComparer.Ordinal);

            string[] lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;

                string[] parts = line.Split(',');
                if (parts.Length < 4) continue;

                Series stage;
                if (!byStage.TryGetValue(parts[0], out stage))
                {
                    stage = new Series();
                    stage.Run = run;
                    stage.Stage = parts[0];
                    // Absent in artefacts written before the stamp existed, which is why its
                    // absence is reported rather than treated as agreement.
                    stage.TakenUtc = parts.Length > 4 ? parts[4].Trim() : string.Empty;
                    byStage[parts[0]] = stage;
                    series.Add(stage);
                }

                stage.Samples.Add(double.Parse(parts[3], CultureInfo.InvariantCulture));
            }

            if (series.Count == 0)
            {
                throw new InvalidOperationException(path + " holds no repeats, so this run would"
                    + " contribute nothing and the comparison would silently be of fewer runs");
            }

            return series;
        }

        /// <summary>
        /// One row per stage per candidate, over the runs given. A stage missing from any run is
        /// dropped with its name, rather than compared over the runs that happen to have it.
        /// </summary>
        public static List<Row> Compare(IList<Series> series, out List<string> dropped)
        {
            Dictionary<string, List<Series>> byStage = new Dictionary<string, List<Series>>(StringComparer.Ordinal);
            HashSet<string> runs = new HashSet<string>(StringComparer.Ordinal);
            List<string> order = new List<string>();

            for (int i = 0; i < series.Count; i++)
            {
                runs.Add(series[i].Run);

                List<Series> list;
                if (!byStage.TryGetValue(series[i].Stage, out list))
                {
                    list = new List<Series>();
                    byStage[series[i].Stage] = list;
                    order.Add(series[i].Stage);
                }
                list.Add(series[i]);
            }

            dropped = new List<string>();
            List<Row> rows = new List<Row>();

            for (int s = 0; s < order.Count; s++)
            {
                List<Series> list = byStage[order[s]];
                if (list.Count != runs.Count)
                {
                    dropped.Add(order[s] + " (in " + list.Count + " of " + runs.Count + " runs)");
                    continue;
                }

                List<double[]> sorted = new List<double[]>();
                for (int i = 0; i < list.Count; i++)
                {
                    double[] values = list[i].Samples.ToArray();
                    Array.Sort(values);
                    sorted.Add(values);
                }

                for (int q = 0; q < Quantiles.Length; q++)
                {
                    Row row = new Row();
                    row.Stage = order[s];
                    row.Quantile = Quantiles[q];
                    row.Runs = list.Count;
                    row.Lowest = double.MaxValue;

                    for (int i = 0; i < sorted.Count; i++)
                    {
                        double value = Quantile(sorted[i], Quantiles[q]);
                        row.Values.Add(value);
                        if (value < row.Lowest) row.Lowest = value;
                        if (value > row.Highest) row.Highest = value;
                    }

                    rows.Add(row);
                }
            }

            return rows;
        }

        public static string Table(IList<Row> rows)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("  stage        statistic   runs      lowest     highest   spread");
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-10}  {1,-9}  {2,5}  {3,10:n3}  {4,10:n3}  {5,6:n1}%",
                    row.Stage, NameOf(row.Quantile), row.Runs, row.Lowest, row.Highest,
                    row.SpreadPercent));
            }
            return text.ToString();
        }

        public static string Csv(IList<Row> rows)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("stage,statistic,quantile,runs,lowest,highest,spread_percent,values");
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                StringBuilder values = new StringBuilder();
                for (int v = 0; v < row.Values.Count; v++)
                {
                    if (v > 0) values.Append(' ');
                    values.Append(row.Values[v].ToString("r", CultureInfo.InvariantCulture));
                }

                text.AppendLine(string.Join(",",
                    row.Stage,
                    NameOf(row.Quantile),
                    row.Quantile.ToString("r", CultureInfo.InvariantCulture),
                    row.Runs.ToString(CultureInfo.InvariantCulture),
                    row.Lowest.ToString("r", CultureInfo.InvariantCulture),
                    row.Highest.ToString("r", CultureInfo.InvariantCulture),
                    row.SpreadPercent.ToString("r", CultureInfo.InvariantCulture),
                    values.ToString()));
            }
            return text.ToString();
        }
    }
}
