using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class HeatSourceWalkLab
    {
        public class Row
        {
            public int Sources;
            public double StepMs;
            public int Substeps;
        }

        public static readonly int[] SourceCounts = { 0, 1, 8, 32 };

/// <summary>Run operation.</summary>
        public static List<Row> Run(string shape, int blocks, int repeats = 12, Action<string> log = null)
        {
/// <summary>List operation.</summary>
            List<Row> rows = new List<Row>();
            foreach (int sources in SourceCounts)
            {
                if (log != null) log(sources + " sources");
                rows.Add(Measure(shape, blocks, sources, repeats));
            }
            return rows;
        }

/// <summary>Measure operation.</summary>
        private static Row Measure(string shape, int blocks, int sources, int repeats)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build(shape, blocks));
            ThermalSimulation simulation = builder.BuildSimulation(Hulls.Uncapped(), 293.15f);
            LoadBenchmarks.SeedSpread(simulation);

            EnvironmentSample sample = Worlds.DarkVacuumWithSources(sources);

            simulation.StepExact(1, sample);

/// <summary>Stopwatch operation.</summary>
            Stopwatch watch = new Stopwatch();
            double best = double.MaxValue;
            for (int r = 0; r < repeats; r++)
            {
                watch.Restart();
                simulation.StepExact(1, sample);
                watch.Stop();
                if (watch.Elapsed.TotalMilliseconds < best) best = watch.Elapsed.TotalMilliseconds;
            }

/// <summary>Row operation.</summary>
            Row row = new Row();
            row.Sources = sources;
            row.StepMs = best;
            row.Substeps = simulation.Solver.LastSubsteps;
            return row;
        }

/// <summary>Report operation.</summary>
        public static string Report(string shape, int blocks, Action<string> log = null)
        {
            return Table(Run(shape, blocks, 12, log));
        }

/// <summary>Table operation.</summary>
        public static string Table(List<Row> rows)
        {
/// <summary>StringBuilder operation.</summary>
            StringBuilder text = new StringBuilder();
            text.Append("sources".PadLeft(9))
                .Append("step ms".PadLeft(11))
                .Append("substeps".PadLeft(10))
                .Append("over 0-source".PadLeft(15))
                .Append('\n');

            double baseMs = 0d;
            foreach (Row row in rows) { if (row.Sources == 0) baseMs = row.StepMs; }

            foreach (Row row in rows)
            {
                text.Append(row.Sources.ToString(CultureInfo.InvariantCulture).PadLeft(9))
                    .Append(row.StepMs.ToString("n3", CultureInfo.InvariantCulture).PadLeft(11))
                    .Append(row.Substeps.ToString(CultureInfo.InvariantCulture).PadLeft(10))
                    .Append((row.Sources == 0 ? "-" : "+" + (row.StepMs - baseMs).ToString("n3", CultureInfo.InvariantCulture)).PadLeft(15))
                    .Append('\n');
            }

            Row high = null;
            int substeps = 0;
            foreach (Row row in rows) { if (row.Sources == 32) { high = row; substeps = row.Substeps; } }
            if (high != null && baseMs > 0)
            {
                double perSource = (high.StepMs - baseMs) / high.Sources;
                double share = perSource / baseMs;
                double foldFactor = substeps > 0 ? (substeps - 1d) / substeps : 0d;
                text.Append('\n')
                    .Append("per source: ").Append(perSource.ToString("n4", CultureInfo.InvariantCulture))
                    .Append(" ms = ").Append(share.ToString("p2", CultureInfo.InvariantCulture))
                    .Append(" of a 0-source step; a fold removes ")
                    .Append(foldFactor.ToString("p0", CultureInfo.InvariantCulture))
                    .Append(" of it (").Append(substeps.ToString(CultureInfo.InvariantCulture))
                    .Append(" substeps)\n");
            }

            return text.ToString();
        }
    }
}
