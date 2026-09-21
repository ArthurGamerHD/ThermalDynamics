using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Thermodynamics.Harness
{
    public enum LabMode
    {
        Parallel,

        Linear,
    }

    public static class LabRun
    {
        public static int Workers
        {
            get
            {
                int cores = Environment.ProcessorCount - 1;
                return cores < 1 ? 1 : cores;
            }
        }

        public static List<TResult> Map<TItem, TResult>(IList<TItem> items,
            Func<TItem, TResult> work, LabMode mode) where TResult : class
        {
            TResult[] slots = new TResult[items.Count];

            if (mode == LabMode.Linear)
            {
                for (int i = 0; i < items.Count; i++) slots[i] = Attempt(items[i], work);
            }
            else
            {
                ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = Workers };

                Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, items.Count, 1),
                    options,
                    range =>
                    {
                        for (int i = range.Item1; i < range.Item2; i++)
                        {
/// <summary>Attempt operation.</summary>
                            slots[i] = Attempt(items[i], work);
                        }
                    });
            }

/// <summary>List operation.</summary>
            List<TResult> results = new List<TResult>(items.Count);
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null) results.Add(slots[i]);
            }

            return results;
        }

        private static TResult Attempt<TItem, TResult>(TItem item, Func<TItem, TResult> work)
            where TResult : class
        {
            try
            {
/// <summary>work operation.</summary>
                return work(item);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static List<TResult> MapMany<TItem, TResult>(IList<TItem> items,
            Func<TItem, List<TResult>> work, LabMode mode) where TResult : class
        {
/// <summary>Map operation.</summary>
            List<List<TResult>> grouped = Map(items, work, mode);

/// <summary>List operation.</summary>
            List<TResult> flat = new List<TResult>();
            for (int i = 0; i < grouped.Count; i++) flat.AddRange(grouped[i]);
            return flat;
        }

/// <summary>ModeOf operation.</summary>
        public static LabMode ModeOf(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--linear") return LabMode.Linear;
            }
            return LabMode.Parallel;
        }

/// <summary>Describe operation.</summary>
        public static string Describe(LabMode mode)
        {
            return mode == LabMode.Linear
                ? "linear (one at a time; use this when a figure is a duration)"
                : "parallel on " + Workers + " workers";
        }
    }
}
