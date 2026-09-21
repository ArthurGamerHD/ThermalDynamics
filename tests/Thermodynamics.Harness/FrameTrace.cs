using System;
using System.Collections.Generic;
using System.Text;

namespace Thermodynamics.Harness
{
    public class FrameTrace
    {
        public const double FrameBudgetMs = 1000d / 60d;

/// <summary>List operation.</summary>
        private readonly List<double> samples = new List<double>();

        public double WorstMs { get; private set; }
        public string WorstLabel { get; private set; }
        public int WorstIndex { get; private set; }

        public string Label;

/// <summary>FrameTrace operation.</summary>
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

/// <summary>Adds a .</summary>
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

/// <summary>Percentile operation.</summary>
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

        public int OverBudget
        {
/// <summary>CountOver operation.</summary>
            get { return CountOver(FrameBudgetMs); }
        }

/// <summary>CountOver operation.</summary>
        public int CountOver(double milliseconds)
        {
            int over = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i] > milliseconds) over++;
            }
            return over;
        }

        public double SpikeRatio
        {
            get
            {
/// <summary>Percentile operation.</summary>
                double median = Percentile(0.5);
                return median <= 0d ? 0d : WorstMs / median;
            }
        }

/// <summary>Describe operation.</summary>
        public string Describe()
        {
            if (samples.Count == 0) return Label + ": no ticks";

/// <summary>StringBuilder operation.</summary>
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

/// <summary>Row operation.</summary>
        public string Row()
        {
            return Percentile(0.5).ToString("n3") + "\t"
/// <summary>Percentile operation.</summary>
                + Percentile(0.95).ToString("n3") + "\t"
/// <summary>Percentile operation.</summary>
                + Percentile(0.99).ToString("n3") + "\t"
                + WorstMs.ToString("n3") + "\t"
                + SpikeRatio.ToString("n1") + "\t"
                + OverBudget;
        }
    }
}
