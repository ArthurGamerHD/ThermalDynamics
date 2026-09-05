using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// The first steps of a grid's life, each on its own clock, with the process's minor page
    /// faults and the step's allocation beside the milliseconds.
    ///
    /// <para>
    /// This is `D4`'s instrument. The hitch benchmark says the first step is the largest tick in
    /// the distribution — 60.83 ms against an 11.28 median at 126,731 blocks — and attributes it
    /// to "first touch of every flat array", which is a hypothesis, not a measurement: a freshly
    /// built grid also runs its first sun-visibility pass and its first row fills on that step,
    /// and a fix aimed at page faults does nothing for arithmetic. The fault column is what
    /// separates them — a step whose extra time arrives with tens of thousands of minor faults is
    /// paying the memory system, and one whose faults match a later step's is paying real work.
    /// </para>
    ///
    /// <para>
    /// Faults are read from <c>/proc/self/stat</c> and the column is −1 where that file does not
    /// exist, rather than 0: a platform this cannot see must not print the number that means
    /// "no faults happened" (`P2`).
    /// </para>
    /// </summary>
    public static class FirstStepLab
    {
        public class Row
        {
            public int Step;
            public double Ms;
            public long MinorFaults;
            public long AllocatedKb;
        }

        /// <summary>
        /// Builds a settled, temperature-spread grid the way the hitch benchmark does, then times
        /// each of the first <paramref name="steps"/> full steps individually.
        /// </summary>
        public static List<Row> Run(string shape, int blocks, int steps, EnvironmentSample sample,
            bool warmProcess = false)
        {
            // A fresh process pays the JIT on its first walk of the step path, and a fresh grid
            // does not: stepping a small throwaway grid first separates the two, which is what
            // decides whether `D4` is a per-grid cost or mostly a per-process one this
            // instrument was conflating with it (`P4` — the harness fault looks like physics).
            if (warmProcess)
            {
                ThermalSimulation throwaway = LoadBenchmarks.BuildSettled(shape, 2000);
                LoadBenchmarks.SeedSpread(throwaway);
                throwaway.StepExact(3, sample);
            }

            ThermalSimulation simulation = LoadBenchmarks.BuildSettled(shape, blocks);
            LoadBenchmarks.SeedSpread(simulation);

            List<Row> rows = new List<Row>();
            Stopwatch watch = new Stopwatch();

            for (int step = 1; step <= steps; step++)
            {
                long faults = MinorFaults();
                long allocated = GC.GetAllocatedBytesForCurrentThread();

                watch.Restart();
                simulation.StepExact(1, sample);
                watch.Stop();

                Row row = new Row();
                row.Step = step;
                row.Ms = watch.Elapsed.TotalMilliseconds;
                row.MinorFaults = faults < 0 ? -1 : MinorFaults() - faults;
                row.AllocatedKb = (GC.GetAllocatedBytesForCurrentThread() - allocated) / 1024;
                rows.Add(row);
            }

            return rows;
        }

        public static string Table(List<Row> rows)
        {
            StringBuilder text = new StringBuilder();
            text.Append("step".PadLeft(6))
                .Append("ms".PadLeft(12))
                .Append("minor faults".PadLeft(15))
                .Append("alloc KB".PadLeft(12))
                .Append('\n');

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                text.Append(row.Step.ToString(CultureInfo.InvariantCulture).PadLeft(6))
                    .Append(row.Ms.ToString("n3", CultureInfo.InvariantCulture).PadLeft(12))
                    .Append((row.MinorFaults < 0 ? "?" : row.MinorFaults.ToString("n0", CultureInfo.InvariantCulture)).PadLeft(15))
                    .Append(row.AllocatedKb.ToString("n0", CultureInfo.InvariantCulture).PadLeft(12))
                    .Append('\n');
            }

            return text.ToString();
        }

        public static string Csv(List<Row> rows)
        {
            StringBuilder text = new StringBuilder("step,ms,minor_faults,allocated_kb\n");
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                text.Append(row.Step.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.Ms.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.MinorFaults.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.AllocatedKb.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            return text.ToString();
        }

        /// <summary>
        /// The process's cumulative minor page faults — field 10 of <c>/proc/self/stat</c> — or −1
        /// where the file cannot be read. Minor faults are the soft kind a first touch of a fresh
        /// zero page takes; they cost microseconds each and tens of thousands of them are
        /// milliseconds.
        /// </summary>
        public static long MinorFaults()
        {
            try
            {
                return ParseMinorFaults(File.ReadAllText("/proc/self/stat"));
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>
        /// The minflt field of one <c>/proc/[pid]/stat</c> line, or −1 where the line cannot be
        /// read — never 0, which is a measurement (`P2`). The command name (field 2) is in
        /// parentheses and may itself hold spaces and parentheses, so fields are counted from the
        /// last ')': state is field 3, and minflt, field 10, is index 7 of the tokens after it.
        /// </summary>
        public static long ParseMinorFaults(string stat)
        {
            if (stat == null) return -1;

            int close = stat.LastIndexOf(')');
            if (close < 0) return -1;
            string[] fields = stat.Substring(close + 1)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 8) return -1;

            long value;
            if (!long.TryParse(fields[7], NumberStyles.None, CultureInfo.InvariantCulture, out value)) return -1;
            return value;
        }
    }
}
