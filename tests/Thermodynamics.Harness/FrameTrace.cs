using System;
using System.Collections.Generic;
using System.Text;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Every host tick's wall-clock cost, kept as a full sample buffer.
    ///
    /// The in-game telemetry deliberately keeps no sample buffer — a world open for hours would
    /// spend more memory on samples than on the simulation — and reports a mean, a max and a
    /// histogram instead. A benchmark runs for seconds and can afford the buffer, and it needs
    /// it: a mean says nothing about hitching, and a max says nothing about how often. What
    /// separates "slower but smooth" from "hitching" is the shape between the two, so the
    /// percentiles and the over-budget count are the headline figures here.
    /// </summary>
    public class FrameTrace
    {
        /// <summary>
        /// One rendered frame at 60 fps. The grid tick is every tenth frame, so a tick that
        /// costs more than this has certainly dropped one.
        /// </summary>
        public const double FrameBudgetMs = 1000d / 60d;

        private readonly List<double> samples = new List<double>();

        /// <summary>Cost of the worst tick, and what was happening on it.</summary>
        public double WorstMs { get; private set; }
        public string WorstLabel { get; private set; }
        public int WorstIndex { get; private set; }

        public string Label;

        public FrameTrace(string label)
        {
            Label = label;
            WorstLabel = "";
            WorstIndex = -1;
        }

        public int Count
        {
            get { return samples.Count; }
        }

        public IList<double> Samples
        {
            get { return samples; }
        }

        public void Add(double milliseconds, string what = null)
        {
            samples.Add(milliseconds);

            if (milliseconds <= WorstMs) return;
            WorstMs = milliseconds;
            WorstLabel = what ?? "";
            WorstIndex = samples.Count - 1;
        }

        public double TotalMs
        {
            get
            {
                double total = 0d;
                for (int i = 0; i < samples.Count; i++) total += samples[i];
                return total;
            }
        }

        /// <summary>
        /// The value below which <paramref name="fraction"/> of ticks fall, by nearest rank on a
        /// sorted copy. Sorting a few thousand doubles per benchmark is free next to the
        /// simulation being measured, and it keeps this exact rather than approximated.
        /// </summary>
        public double Percentile(double fraction)
        {
            if (samples.Count == 0) return 0d;

            double[] sorted = samples.ToArray();
            Array.Sort(sorted);

            int rank = (int)Math.Ceiling(fraction * sorted.Length) - 1;
            if (rank < 0) rank = 0;
            if (rank >= sorted.Length) rank = sorted.Length - 1;
            return sorted[rank];
        }

        /// <summary>Ticks that cost more than one 60 fps frame.</summary>
        public int OverBudget
        {
            get { return CountOver(FrameBudgetMs); }
        }

        public int CountOver(double milliseconds)
        {
            int over = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i] > milliseconds) over++;
            }
            return over;
        }

        /// <summary>
        /// How spiky the trace is: the worst tick against the median one.
        ///
        /// This is the number the whole exercise is about. A simulation that is uniformly slow
        /// has a ratio near 1 and costs the player frame rate; one that is fast on average and
        /// occasionally enormous has a ratio in the hundreds and costs them a stutter, which is
        /// far more noticeable and far worse. Driving this down is worth accepting a higher
        /// median for.
        /// </summary>
        public double SpikeRatio
        {
            get
            {
                double median = Percentile(0.5);
                return median <= 0d ? 0d : WorstMs / median;
            }
        }

        public string Describe()
        {
            if (samples.Count == 0) return Label + ": no ticks";

            StringBuilder sb = new StringBuilder();
            sb.Append(Label).Append(": ").Append(samples.Count).Append(" ticks, ")
              .Append("median ").Append(Percentile(0.5).ToString("n3"))
              .Append(" / p95 ").Append(Percentile(0.95).ToString("n3"))
              .Append(" / p99 ").Append(Percentile(0.99).ToString("n3"))
              .Append(" / max ").Append(WorstMs.ToString("n3")).Append(" ms")
              .Append(", spike x").Append(SpikeRatio.ToString("n1"))
              .Append(", ").Append(OverBudget).Append(" over frame budget");

            if (WorstLabel.Length > 0)
            {
                sb.Append(" (worst at tick ").Append(WorstIndex).Append(": ").Append(WorstLabel).Append(')');
            }

            return sb.ToString();
        }

        /// <summary>The columns a report table uses, without the label.</summary>
        public string Row()
        {
            return Percentile(0.5).ToString("n3") + "\t"
                + Percentile(0.95).ToString("n3") + "\t"
                + Percentile(0.99).ToString("n3") + "\t"
                + WorstMs.ToString("n3") + "\t"
                + SpikeRatio.ToString("n1") + "\t"
                + OverBudget;
        }
    }
}
