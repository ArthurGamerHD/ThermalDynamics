using System;
using System.Collections.Generic;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **The pay-for-one-run cache the waste-fraction sweeps share.** Every row those labs
    /// produce is a pure function of the shipped XML and the tables above it, so a suite that
    /// asserts on five fractions should pay for one run of each — and the policy was stated
    /// twice, once per lab, where one copy losing its lock would be a silent thread-safety bug
    /// in a suite that runs eight-wide.
    ///
    /// A miss is computed outside the lock on purpose: two workers may both build the same
    /// fraction and the second write wins, which is benign for a pure function and cheaper than
    /// holding every other fraction's reader behind a multi-minute run.
    /// </summary>
    public sealed class FractionCache<TRow>
    {
        private readonly Dictionary<float, List<TRow>> cache = new Dictionary<float, List<TRow>>();

        /// <summary>Builds every row of one fraction. Must be pure: results are kept for the run.</summary>
        private readonly Func<float, List<TRow>> produce;

        public FractionCache(Func<float, List<TRow>> produce)
        {
            this.produce = produce;
        }

        /// <summary>The rows of every listed fraction, each computed at most once per process.</summary>
        public List<TRow> SweepAt(float[] fractions)
        {
            List<TRow> rows = new List<TRow>();

            foreach (float fraction in fractions)
            {
                List<TRow> cached;
                lock (cache)
                {
                    if (cache.TryGetValue(fraction, out cached))
                    {
                        rows.AddRange(cached);
                        continue;
                    }
                }

                cached = produce(fraction);
                lock (cache) cache[fraction] = cached;
                rows.AddRange(cached);
            }

            return rows;
        }
    }
}
