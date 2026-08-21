using System;
using System.Diagnostics;
using System.Text;

namespace Thermodynamics
{
    /// <summary>
    /// A streaming min/max/mean/stddev accumulator.
    ///
    /// Every figure the telemetry module records uses one of these rather than a sample buffer. A
    /// session can run for hours, over which arrays of samples would exceed the simulation's own
    /// memory without adding anything a count and two sums do not provide.
    /// </summary>
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
            // NaN would silently poison every derived figure. Telemetry.Anomaly records the
            // occurrence and the accumulator rejects the sample.
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

    /// <summary>
    /// A fixed-edge histogram. Used for temperature distributions and frame-cost distributions,
    /// where the shape of the tail matters more than the mean.
    /// </summary>
    public class Histogram
    {
        public readonly float[] Edges;
        public readonly long[] Counts;

        /// <param name="edges">Ascending upper bounds. One extra overflow bucket is appended.</param>
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

        /// <summary>
        /// Renders as "&lt;100: 12 (3.1%)" lines, skipping empty buckets so a wide range of edges
        /// stays readable.
        /// </summary>
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

    /// <summary>
    /// Wall-clock cost of one code path: call count, total, mean, worst and distribution.
    ///
    /// Not reentrant: Begin/End pairs must not nest on the same instance. Every call site in this mod
    /// is either a leaf or has its own instance.
    /// </summary>
    public class TimingStat
    {
        public readonly string Name;
        public long Calls;
        public double TotalMilliseconds;
        public double MaxMilliseconds;

        /// <summary>The most recent call, so a caller can forward it somewhere else too.</summary>
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

    /// <summary>
    /// What the mod cost against the session clock, from timings that nest inside one another.
    ///
    /// <para>
    /// The cost table measures the same work at several depths. <c>session frame</c> wraps the
    /// session component's whole per-frame call, which is what drives every grid, so
    /// <c>grid simulation</c> is contained by it, and the <c>of which</c> rows are contained by
    /// that in turn. Only paths the engine enters independently are roots: the frame itself, and
    /// the save and load callbacks, which the engine raises outside the frame.
    /// </para>
    ///
    /// <para>
    /// Summing a row and the row it sits inside charges the same milliseconds twice. Doing that to
    /// <c>grid simulation</c>, which accounts for nearly all of a frame, reported a mod costing a
    /// third of real time as costing two thirds of it — and would report one costing 60 % as
    /// costing more than all the time there was.
    /// </para>
    /// </summary>
    public static class CostRollup
    {
        /// <summary>
        /// Total wall clock the mod is responsible for, over the roots only.
        ///
        /// It takes no grid-simulation argument by construction: the figure is nested inside
        /// <paramref name="sessionFrame"/> and there is no correct way to add it.
        ///
        /// <para><paramref name="build"/> is a root for the opposite reason: a grid's one-off build
        /// runs from the entity's own callback, so nothing else here contains it. It was left out
        /// of the total entirely until it was given a row.</para>
        /// </summary>
        public static double MeasuredMilliseconds(
            double sessionFrame, double save, double load, double build)
        {
            return sessionFrame + save + load + build;
        }

        /// <summary>
        /// What a parent row cost that none of its children claimed.
        ///
        /// Reported rather than clamped. A large positive figure means work nobody has instrumented
        /// — which is what a field dump showed for the observation that runs around a step — and a
        /// negative one means two children timed the same milliseconds, which is a defect in the
        /// instrumentation and is not made to disappear by taking a maximum with zero.
        /// </summary>
        public static double Unattributed(double parent, double children)
        {
            return parent - children;
        }

        /// <summary>Share of the session clock the roots account for, or -1 when the clock is unset.</summary>
        public static double ShareOfRealTime(double measuredMilliseconds, double sessionSeconds)
        {
            if (sessionSeconds <= 0.0) return -1.0;
            return 100.0 * measuredMilliseconds / (sessionSeconds * 1000.0);
        }
    }
}
