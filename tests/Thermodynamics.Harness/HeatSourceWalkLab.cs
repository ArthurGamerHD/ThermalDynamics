using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// What a registered heat source costs a step, and how much of that a fold into the
    /// precomputed source row would remove.
    ///
    /// <para>
    /// This is the fourth-sweep instrument on redesign.md. A point source resolves to one
    /// direction and one irradiance per grid per step (`ThermalHeatSources.Sample`), so its
    /// per-node contribution — <c>irradiance · absorptivity · faceWeight · area</c> — carries no
    /// temperature and cannot change across a step's substeps. Solar, its directional-irradiance
    /// twin, is already folded into <c>nodeSourceRow</c> and read once; registered sources go
    /// through a separate <c>AccumulateHeatSources</c> pass that reruns every substep. This lab
    /// measures the per-source per-step cost of the shipped (per-substep) path by differencing
    /// stepped hulls at 0, 1, 8 and 32 sources, so the fold's saving is that cost times
    /// <c>(substeps − 1) / substeps</c> — the substeps it need not repeat.
    /// </para>
    ///
    /// <para>
    /// **The criterion, fixed before the first run** (`E1`): the fold earns a design row if one
    /// source costs **0.3 % or more of a settled step** at 126,731 blocks — a source cheap enough
    /// to lose in the noise is not worth a reorder that (like every reorder here, `P4`) must be
    /// proven bit-identical through `SolverAb` rather than a prototype. The shipped default has no
    /// sources, so this is a conditional design and the row says so: worth it to a world that uses
    /// them, invisible to one that does not (`P8` — off already costs nothing, this is the on
    /// cost).
    /// </para>
    /// </summary>
    public static class HeatSourceWalkLab
    {
        public class Row
        {
            public int Sources;
            public double StepMs;
            public int Substeps;
        }

        public static readonly int[] SourceCounts = { 0, 1, 8, 32 };

        public static List<Row> Run(string shape, int blocks, int repeats = 12, Action<string> log = null)
        {
            List<Row> rows = new List<Row>();
            foreach (int sources in SourceCounts)
            {
                if (log != null) log(sources + " sources");
                rows.Add(Measure(shape, blocks, sources, repeats));
            }
            return rows;
        }

        private static Row Measure(string shape, int blocks, int sources, int repeats)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build(shape, blocks));
            ThermalSimulation simulation = builder.BuildSimulation(Hulls.Uncapped(), 293.15f);
            LoadBenchmarks.SeedSpread(simulation);

            EnvironmentSample sample = Worlds.DarkVacuumWithSources(sources);

            // Settle onto this sample so the substep count and the mirrored rows are the ones a
            // steady step of it pays, then time the settled step.
            simulation.StepExact(1, sample);

            Stopwatch watch = new Stopwatch();
            double best = double.MaxValue;
            for (int r = 0; r < repeats; r++)
            {
                watch.Restart();
                simulation.StepExact(1, sample);
                watch.Stop();
                if (watch.Elapsed.TotalMilliseconds < best) best = watch.Elapsed.TotalMilliseconds;
            }

            Row row = new Row();
            row.Sources = sources;
            row.StepMs = best;
            row.Substeps = simulation.Solver.LastSubsteps;
            return row;
        }

        public static string Report(string shape, int blocks, Action<string> log = null)
        {
            return Table(Run(shape, blocks, 12, log));
        }

        public static string Table(List<Row> rows)
        {
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

            // Per-source per-step cost and its share, taken off the 32-source row where the
            // signal is largest against the noise. The fold removes (substeps-1)/substeps of it.
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
