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
            public readonly List<double> Samples = new List<double>();
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
