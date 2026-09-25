using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Thermodynamics.Harness
{
    public static class SampleStatisticLab
    {
        public static readonly double[] Quantiles = { 0d, 0.01d, 0.05d, 0.10d, 0.25d, 0.50d };


        public static string NameOf(double quantile)
        {
            if (quantile <= 0d) return "min";
            if (quantile >= 0.5d) return "median";
            return "p" + ((int)Math.Round(quantile * 100d)).ToString(CultureInfo.InvariantCulture);
        }

        public class Series
        {
            public string Run;
            public string Stage;

            public string TakenUtc = string.Empty;


            public readonly List<double> Samples = new List<double>();
        }

        public class WindowSpan
        {
            public string Earliest = string.Empty;
            public string Latest = string.Empty;

            public int Unstamped;

            public TimeSpan Elapsed;

            public static readonly TimeSpan OneWindow = TimeSpan.FromHours(1d);

            public bool IsOneWindow
            {
                get { return Unstamped == 0 && Elapsed <= OneWindow; }
            }
        }


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

        public class Row
        {
            public string Stage;
            public double Quantile;
            public int Runs;


            public readonly List<double> Values = new List<double>();

            public double Lowest;
            public double Highest;

            public double SpreadPercent
            {
                get { return Lowest <= 0d ? 0d : 100d * (Highest - Lowest) / Lowest; }
            }
        }


        public static double Quantile(IList<double> sorted, double quantile)
        {
            if (sorted == null || sorted.Count == 0) return 0d;
            if (quantile <= 0d) return sorted[0];

            int index = (int)Math.Floor(quantile * (sorted.Count - 1));
            if (index < 0) index = 0;
            if (index >= sorted.Count) index = sorted.Count - 1;
            return sorted[index];
        }


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
