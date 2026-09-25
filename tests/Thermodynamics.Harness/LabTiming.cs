using System;
using System.Diagnostics;

namespace Thermodynamics.Harness
{
    public static class LabTiming
    {

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
