using System.Collections.Generic;

namespace Thermodynamics.Harness
{
    public static class LabStats
    {
/// <summary>PercentileOfSorted operation.</summary>
        public static float PercentileOfSorted(List<float> sorted, float fraction)
        {
            if (sorted.Count == 0) return 0f;
            return sorted[(int)(fraction * (sorted.Count - 1))];
        }
    }
}
