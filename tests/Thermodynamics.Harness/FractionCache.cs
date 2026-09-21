using System;
using System.Collections.Generic;

namespace Thermodynamics.Harness
{
    public sealed class FractionCache<TRow>
    {
        private readonly Dictionary<float, List<TRow>> cache = new Dictionary<float, List<TRow>>();

        private readonly Func<float, List<TRow>> produce;

/// <summary>FractionCache operation.</summary>
        public FractionCache(Func<float, List<TRow>> produce)
        {
            this.produce = produce;
        }

/// <summary>SweepAt operation.</summary>
        public List<TRow> SweepAt(float[] fractions)
        {
/// <summary>List operation.</summary>
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

/// <summary>produce operation.</summary>
                cached = produce(fraction);
                lock (cache) cache[fraction] = cached;
                rows.AddRange(cached);
            }

            return rows;
        }
    }
}
