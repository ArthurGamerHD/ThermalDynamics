using System;

namespace Thermodynamics.Presentation
{
    public static class ThermalVisionFleetBudget
    {
/// <summary>BackOff operation.</summary>
        public static int BackOff(int current, int grids)
        {
            if (current < 1 || grids < 1) throw new ArgumentException("Positive fleet capacity required");
            return Math.Max(grids, current / 2);
        }

/// <summary>ForViewport operation.</summary>
        public static int ForViewport(int capacity, int previousCount, int currentCount)
        {
            return previousCount > 0 && currentCount > 0 && currentCount <= previousCount / 2
                ? 1400 : capacity;
        }

/// <summary>Allocate operation.</summary>
        public static int[] Allocate(double[] weights, int[] demands, int total)
        {
            if (weights == null || demands == null || weights.Length != demands.Length || total < weights.Length)
                throw new ArgumentException("Budget and demands must cover every grid");
            var result = new int[weights.Length];
            if (result.Length == 0) return result;
            int floor = Math.Min(64, total / result.Length), remaining = total;
            for (int i = 0; i < result.Length; i++)
            {
                if (demands[i] < 1) throw new ArgumentException("Positive demand required");
                result[i] = Math.Min(floor, demands[i]); remaining -= result[i];
            }
            while (remaining > 0)
            {
                double sum = 0;
                for (int i = 0; i < result.Length; i++) if (result[i] < demands[i]) sum += Weight(weights[i]);
                if (sum == 0) break;
                int available = remaining;
                for (int i = 0; i < result.Length; i++)
                {
                    if (result[i] == demands[i]) continue;
                    int share = Math.Min(demands[i] - result[i], (int)Math.Floor(available * Weight(weights[i]) / sum));
                    result[i] += share; remaining -= share;
                }
                if (remaining == available)
                    for (int i = 0; i < result.Length && remaining > 0; i++)
                        if (result[i] < demands[i]) { result[i]++; remaining--; }
            }
            return result;
        }

/// <summary>Allocate operation.</summary>
        public static int[] Allocate(double[] weights, int total)
        {
            if (weights == null || total < weights.Length) throw new ArgumentException("Budget must cover every grid");
            int[] result = new int[weights.Length];
            if (weights.Length == 0) return result;
            int floor = Math.Min(64, total / weights.Length);
            int left = total - floor * weights.Length;
            double sum = 0;
            for (int i = 0; i < weights.Length; i++) sum += Weight(weights[i]);
            int used = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                int share = (int)Math.Floor(left * Weight(weights[i]) / sum);
                result[i] = floor + share; used += result[i];
            }
            for (int i = 0; used < total; i = (i + 1) % result.Length, used++) result[i]++;
            return result;
        }
/// <summary>Weight operation.</summary>
        private static double Weight(double value)
        { return double.IsNaN(value) || double.IsInfinity(value) || value <= 0 ? .000001 : Math.Min(value, 1000000); }
    }
}
