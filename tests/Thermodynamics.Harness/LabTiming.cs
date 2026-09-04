using System;
using System.Diagnostics;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **The fastest-of-N stopwatch the duration labs share.** Runs an action a stated number of
    /// times and keeps the fastest and slowest readings — the fastest because timing noise is
    /// one-sided, the slowest so the caller can publish the spread beside the figure (`M4`, stated
    /// in rules.md). One statement rather than one per lab, so two labs cannot quietly measure the
    /// same statistic two different ways.
    ///
    /// The repeat count is the caller's: how many repeats a figure needs is part of a lab's own
    /// design, the same way a corpus tool's parse default belongs at its call site.
    /// </summary>
    public static class LabTiming
    {
        /// <summary>Fastest and slowest of <paramref name="repeats"/> runs, in milliseconds.</summary>
        public static void FastestOf(int repeats, Action action, out double fastest, out double slowest)
        {
            fastest = double.MaxValue;
            slowest = 0d;

            for (int r = 0; r < repeats; r++)
            {
                Stopwatch watch = Stopwatch.StartNew();
                action();
                watch.Stop();

                double ms = watch.Elapsed.TotalMilliseconds;
                if (ms < fastest) fastest = ms;
                if (ms > slowest) slowest = ms;
            }
        }
    }
}
