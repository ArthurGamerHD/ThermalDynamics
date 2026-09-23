using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    public static class FirstStepLab
    {
        public class Row
        {
            public int Step;
            public double Ms;
            public long MinorFaults;
            public long AllocatedKb;
        }


        public static List<Row> Run(string shape, int blocks, int steps, EnvironmentSample sample,
            bool warmProcess = false)
        {
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
