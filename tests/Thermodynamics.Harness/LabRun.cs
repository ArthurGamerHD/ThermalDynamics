using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// How the lab's work is spread over the machine, and why there are two answers.
    ///
    /// <para>
    /// **Balance runs go wide.** Collecting the matrix is thousands of independent settling
    /// problems, each a pure function of a ship and a scenario, and the answer does not depend on
    /// how long it took to get. Running them concurrently is free accuracy: nothing about a
    /// temperature changes because another core was busy.
    /// </para>
    ///
    /// <para>
    /// **Performance runs go single file.** The moment a figure is a *duration*, every other core
    /// is noise — cache pressure, memory bandwidth, turbo headroom and the scheduler all move it,
    /// and none of that is a property of the code being measured. A benchmark run alongside fifteen
    /// others measures the machine's contention, not the solver.
    /// </para>
    ///
    /// <para>
    /// The distinction is not a preference and it is not a switch to leave on the fast setting. A
    /// balance figure taken linearly is the same figure; a cost figure taken in parallel is a
    /// different one, and there is no way to tell from the number itself.
    /// </para>
    /// </summary>
    public enum LabMode
    {
        /// <summary>
        /// Concurrent. For collecting balance data, where every result is independent of timing.
        /// </summary>
        Parallel,

        /// <summary>
        /// One at a time. For anything whose result is a duration, where a busy machine is a
        /// measurement error rather than a faster run.
        /// </summary>
        Linear,
    }

    public static class LabRun
    {
        /// <summary>
        /// Workers to use in <see cref="LabMode.Parallel"/>.
        ///
        /// One below the core count, so the machine keeps enough to stay responsive and the run
        /// does not spend its time in the scheduler. A ship's settling problem is compute-bound
        /// with almost no allocation, so there is nothing to gain from oversubscribing.
        /// </summary>
        public static int Workers
        {
            get
            {
                int cores = Environment.ProcessorCount - 1;
                return cores < 1 ? 1 : cores;
            }
        }

        /// <summary>
        /// Maps <paramref name="work"/> over <paramref name="items"/> and collects what comes back,
        /// dropping whatever throws.
        ///
        /// **Order is preserved regardless of mode**, because results are written into a slot per
        /// item rather than appended. A report that reorders itself depending on how the machine
        /// was loaded is a report two runs of which cannot be diffed, and diffing two runs is most
        /// of what the lab is for.
        /// </summary>
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

                // One item per chunk. The default range partitioner hands a worker a contiguous
                // block of indices on the assumption that items cost about the same, and corpus
                // ships do not: the largest blueprint here is some eighteen hundred times the
                // median. A worker dealt a run of big hulls finishes long after the rest, and the
                // pass waits for it.
                Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, items.Count, 1),
                    options,
                    range =>
                    {
                        for (int i = range.Item1; i < range.Item2; i++)
                        {
                            slots[i] = Attempt(items[i], work);
                        }
                    });
            }

            List<TResult> results = new List<TResult>(items.Count);
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null) results.Add(slots[i]);
            }

            return results;
        }

        /// <summary>
        /// One item's work, or null.
        ///
        /// A corpus of ten thousand will contain ships this model cannot build, and losing one is
        /// not a reason to lose the pass. In parallel it is also the difference between a dropped
        /// row and an exception surfacing from inside <c>Parallel.For</c> as something unrelated.
        /// </summary>
        private static TResult Attempt<TItem, TResult>(TItem item, Func<TItem, TResult> work)
            where TResult : class
        {
            try
            {
                return work(item);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Maps over items that each produce several results, flattening in order.
        ///
        /// The battery's shape: one ship yields one outcome per scenario, and the ship is the unit
        /// of parallelism rather than the scenario. That is not a tuning choice — every simulation
        /// built from one ship shares that ship's <c>BlockInstance</c> objects, and
        /// <see cref="ShipLoad"/> writes the load onto them, so two scenarios on the same ship at
        /// the same time would overwrite each other's load. Different ships share nothing.
        /// </summary>
        public static List<TResult> MapMany<TItem, TResult>(IList<TItem> items,
            Func<TItem, List<TResult>> work, LabMode mode) where TResult : class
        {
            List<List<TResult>> grouped = Map(items, work, mode);

            List<TResult> flat = new List<TResult>();
            for (int i = 0; i < grouped.Count; i++) flat.AddRange(grouped[i]);
            return flat;
        }

        /// <summary>The mode a command line asked for. Parallel unless told otherwise.</summary>
        public static LabMode ModeOf(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--linear") return LabMode.Linear;
            }
            return LabMode.Parallel;
        }

        public static string Describe(LabMode mode)
        {
            return mode == LabMode.Linear
                ? "linear (one at a time; use this when a figure is a duration)"
                : "parallel on " + Workers + " workers";
        }
    }
}
