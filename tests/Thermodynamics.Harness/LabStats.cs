using System.Collections.Generic;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Statistics the labs print, stated once. Two labs carried an identical nearest-rank
    /// percentile; consolidating them is not a change of definition, and the definition is worth
    /// naming: this is the nearest rank at `fraction · (n − 1)`, **not** the interpolated
    /// percentile `tools/corpus/scoring.py` publishes population figures with. These feed lab
    /// console diagnostics; a figure that crosses into a published page goes through the corpus
    /// tooling and its definition instead.
    /// </summary>
    public static class LabStats
    {
        /// <summary>The value at `fraction` through an already-sorted list, or 0 for an empty one.</summary>
        public static float PercentileOfSorted(List<float> sorted, float fraction)
        {
            if (sorted.Count == 0) return 0f;
            return sorted[(int)(fraction * (sorted.Count - 1))];
        }
    }
}
