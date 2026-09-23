using System;
using System.Collections.Generic;

namespace Thermodynamics.Harness
{
    public sealed class FractionCache<TRow>
    {
        private readonly Dictionary<float, List<TRow>> cache = new Dictionary<float, List<TRow>>();

        private readonly Func<float, List<TRow>> produce;


        public FractionCache(Func<float, List<TRow>> produce)
        {
            this.produce = produce;
        }


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
