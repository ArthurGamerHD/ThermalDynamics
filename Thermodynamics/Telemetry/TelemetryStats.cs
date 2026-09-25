using System;
using System.Diagnostics;
using System.Text;

namespace Thermodynamics
{
    public class RunningStat
    {
        public long Count;
        public double Sum;
        public double SumSquares;
        public float Min = float.MaxValue;
        public float Max = float.MinValue;
        public float Last;


        public void Add(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;

            Count++;
            Sum += value;
            SumSquares += (double)value * value;
            if (value < Min) Min = value;
            if (value > Max) Max = value;
            Last = value;
        }

        public double Mean
        {
            get { return Count == 0 ? 0 : Sum / Count; }
        }

        public double StdDev
        {
            get
            {
                if (Count < 2) return 0;
                double mean = Sum / Count;
                double variance = (SumSquares / Count) - (mean * mean);
                return variance <= 0 ? 0 : Math.Sqrt(variance);
            }
        }

        public float SafeMin
        {
            get { return Count == 0 ? 0 : Min; }
        }

        public float SafeMax
        {
            get { return Count == 0 ? 0 : Max; }
        }


        public void Merge(RunningStat other)
        {
            if (other == null || other.Count == 0) return;

            Count += other.Count;
            Sum += other.Sum;
            SumSquares += other.SumSquares;
            if (other.Min < Min) Min = other.Min;
            if (other.Max > Max) Max = other.Max;
            Last = other.Last;
        }


        public string Format(string format)
        {
            if (Count == 0) return "-";
            return SafeMin.ToString(format) + " / " + Mean.ToString(format) + " / " + SafeMax.ToString(format)
                + " (sd " + StdDev.ToString(format) + ", n " + Count + ")";
        }


        public override string ToString()
        {

            return Format("n3");
        }
    }

    public class Histogram
    {
        public readonly float[] Edges;
        public readonly long[] Counts;


        public Histogram(float[] edges)
        {
            Edges = edges;
            Counts = new long[edges.Length + 1];
        }


        public void Add(float value)
        {
            if (float.IsNaN(value)) return;

            for (int i = 0; i < Edges.Length; i++)
            {
                if (value < Edges[i])
                {
                    Counts[i]++;
                    return;
                }
            }

            Counts[Edges.Length]++;
        }

        public long Total
        {
            get
            {
                long total = 0;
                for (int i = 0; i < Counts.Length; i++) total += Counts[i];
                return total;
            }
        }


        public void Merge(Histogram other)
        {
            if (other == null || other.Counts.Length != Counts.Length) return;
            for (int i = 0; i < Counts.Length; i++) Counts[i] += other.Counts[i];
        }


        public void Clear()
        {
            for (int i = 0; i < Counts.Length; i++) Counts[i] = 0;
        }


        public void Write(StringBuilder sb, string indent, string unit)
        {
            long total = Total;
            if (total == 0)
            {
                sb.Append(indent).Append("(no samples)\n");
                return;
            }

            for (int i = 0; i < Counts.Length; i++)
            {
                if (Counts[i] == 0) continue;

                string label = TelemetryFormat.BucketLabel(Edges, i, unit);

                double percent = 100.0 * Counts[i] / total;
                sb.Append(indent)
                  .Append(label.PadRight(24))
                  .Append(Counts[i].ToString().PadLeft(10))
                  .Append("  ")
                  .Append(percent.ToString("n2"))
                  .Append("%\n");
            }
        }


        public static float[] TemperatureEdges()
        {
            return new float[] { 2.8f, 50, 100, 200, 273.15f, 300, 350, 400, 500, 700, 1000, 1500, 2000, 5000 };
        }


        public static float[] MillisecondEdges()
        {
            return new float[] { 0.01f, 0.05f, 0.1f, 0.25f, 0.5f, 1, 2, 5, 10, 25, 50, 100 };
        }
    }

    public class TimingStat
    {
        public readonly string Name;
        public long Calls;
        public double TotalMilliseconds;
        public double MaxMilliseconds;

        public double LastMilliseconds;


        private readonly Stopwatch _watch = new Stopwatch();

        private readonly Histogram _distribution = new Histogram(Histogram.MillisecondEdges());


        public TimingStat(string name)
        {
            Name = name;
        }


        public void Begin()
        {
            _watch.Reset();
            _watch.Start();
        }


        public void End()
        {
            _watch.Stop();
            Record(_watch.Elapsed.TotalMilliseconds);
        }


        public void Record(double milliseconds)
        {
            LastMilliseconds = milliseconds;
            Calls++;
            TotalMilliseconds += milliseconds;
            if (milliseconds > MaxMilliseconds) MaxMilliseconds = milliseconds;
            _distribution.Add((float)milliseconds);
        }

        public double MeanMilliseconds
        {
            get { return Calls == 0 ? 0 : TotalMilliseconds / Calls; }
        }


        public void Merge(TimingStat other)
        {
            if (other == null || other.Calls == 0) return;

            Calls += other.Calls;
            TotalMilliseconds += other.TotalMilliseconds;
            if (other.MaxMilliseconds > MaxMilliseconds) MaxMilliseconds = other.MaxMilliseconds;
            _distribution.Merge(other._distribution);
        }


        public void WriteDistribution(StringBuilder sb, string indent)
        {
            _distribution.Write(sb, indent, "ms");
        }


        public void WriteRow(StringBuilder sb)
        {
            sb.Append("  ")
              .Append(Name.PadRight(32))
              .Append(Calls.ToString().PadLeft(12))
              .Append(TotalMilliseconds.ToString("n2").PadLeft(14))
              .Append(MeanMilliseconds.ToString("n5").PadLeft(14))
              .Append(MaxMilliseconds.ToString("n3").PadLeft(12))
              .Append('\n');
        }


        public static void WriteHeader(StringBuilder sb, string title)
        {
            sb.Append("  ")
              .Append(title.PadRight(32))
              .Append("calls".PadLeft(12))
              .Append("total ms".PadLeft(14))
              .Append("mean ms".PadLeft(14))
              .Append("max ms".PadLeft(12))
              .Append('\n');
        }
    }

    public static class CostRollup
    {

        public static double MeasuredMilliseconds(
            double sessionFrame, double save, double load, double build)
        {
            return sessionFrame + save + load + build;
        }


        public static double MeasuredMilliseconds(
            double sessionFrame, double save, double load, double build, double blockEvents)
        {
            return MeasuredMilliseconds(sessionFrame, save, load, build) + blockEvents;
        }


        public static double Unattributed(double parent, double children)
        {
            return parent - children;
        }


        public static double ShareOfRealTime(double measuredMilliseconds, double sessionSeconds)
        {
            if (sessionSeconds <= 0.0) return -1.0;
            return 100.0 * measuredMilliseconds / (sessionSeconds * 1000.0);
        }
    }
}
