using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    public static class StepPathLab
    {
        public class Row
        {
            public int Blocks;
            public int Links;

            public int Cap;
            public int Substeps;

            public double SolverMs;

            public double HostMs;

            public double SyncsPerStep;
            public double EstimatesPerStep;

            public double OverheadMs { get { return HostMs - SolverMs; } }

            public double OverheadShare
            {
                get { return HostMs <= 0d ? 0d : OverheadMs / HostMs; }
            }
        }

        public static readonly int[] DefaultCaps = { 1, 3, 64 };

/// <summary>Run operation.</summary>
        public static List<Row> Run(string shape, IList<int> ladder, IList<int> caps, int steps,
            Action<string> log)
        {
/// <summary>List operation.</summary>
            List<Row> rows = new List<Row>();

            for (int i = 0; i < ladder.Count; i++)
            {
                for (int c = 0; c < caps.Count; c++)
                {
                    if (log != null)
                    {
                        log(shape + " " + ladder[i].ToString("n0") + ", cap " + caps[c]);
                    }
                    rows.Add(Measure(shape, ladder[i], caps[c], steps));
                }
            }

            return rows;
        }

/// <summary>Measure operation.</summary>
        private static Row Measure(string shape, int blocks, int cap, int steps)
        {
/// <summary>Row operation.</summary>
            Row row = new Row();
            row.Cap = cap;

            ThermalSimulation simulation = LoadBenchmarks.BuildSettled(shape, blocks);
            LoadBenchmarks.SeedSpread(simulation);

            simulation.Settings.MaxElementVisitsPerStep = int.MaxValue;
            simulation.Settings.MaxSubsteps = cap;
            simulation.Settings.Derive();

            row.Blocks = simulation.Solver.Nodes.Count;
            row.Links = simulation.Solver.Links.Count;

            EnvironmentSample sample = Worlds.Space(new VRageMath.Vector3(0f, 1f, 0f));
            EnvironmentState state = EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, sample);

            float step = simulation.Settings.StepSeconds;

            for (int i = 0; i < 3; i++) simulation.Solver.Step(step, state);
            simulation.StepExact(3, sample);

/// <summary>Stopwatch operation.</summary>
            Stopwatch watch = new Stopwatch();
            row.SolverMs = double.MaxValue;
            row.HostMs = double.MaxValue;

            for (int round = 0; round < 2; round++)
            {
                LoadBenchmarks.SeedSpread(simulation);
                watch.Restart();
                for (int i = 0; i < steps; i++) simulation.Solver.Step(step, state);
                watch.Stop();

                double solver = watch.Elapsed.TotalMilliseconds / steps;
                if (solver < row.SolverMs) row.SolverMs = solver;
                row.Substeps = simulation.Solver.LastSubsteps;

                LoadBenchmarks.SeedSpread(simulation);
                simulation.Work.Reset();
                watch.Restart();
                simulation.StepExact(steps, sample);
                watch.Stop();

                double host = watch.Elapsed.TotalMilliseconds / steps;
                if (host < row.HostMs) row.HostMs = host;

                long ran = simulation.Work.SolverSteps;
                if (ran > 0)
                {
                    row.SyncsPerStep = simulation.Work.NodeStateSyncs / (double)ran;
                    row.EstimatesPerStep = simulation.Work.StabilityEstimates / (double)ran;
                }
            }

            return row;
        }

/// <summary>Table operation.</summary>
        public static string Table(IList<Row> rows)
        {
/// <summary>StringBuilder operation.</summary>
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("     blocks     links   cap  substeps   solver ms     host ms"
                + "   overhead   syncs   estimates");

            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                sb.Append(r.Blocks.ToString("n0").PadLeft(11));
                sb.Append(r.Links.ToString("n0").PadLeft(10));
                sb.Append(r.Cap.ToString().PadLeft(6));
                sb.Append(r.Substeps.ToString().PadLeft(10));
                sb.Append(r.SolverMs.ToString("n3").PadLeft(12));
                sb.Append(r.HostMs.ToString("n3").PadLeft(12));
                sb.Append((r.OverheadShare * 100d).ToString("n1").PadLeft(10) + "%");
                sb.Append(r.SyncsPerStep.ToString("n2").PadLeft(8));
                sb.Append(r.EstimatesPerStep.ToString("n2").PadLeft(12));
                sb.AppendLine();
            }

            return sb.ToString();
        }

/// <summary>Csv operation.</summary>
        public static string Csv(IList<Row> rows)
        {
/// <summary>StringBuilder operation.</summary>
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("blocks,links,cap,substeps,solver_ms,host_ms,overhead_ms,overhead_share,"
                + "syncs_per_step,estimates_per_step");

            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                sb.Append(r.Blocks).Append(',');
                sb.Append(r.Links).Append(',');
                sb.Append(r.Cap).Append(',');
                sb.Append(r.Substeps).Append(',');
                sb.Append(r.SolverMs.ToString("r")).Append(',');
                sb.Append(r.HostMs.ToString("r")).Append(',');
                sb.Append(r.OverheadMs.ToString("r")).Append(',');
                sb.Append(r.OverheadShare.ToString("r")).Append(',');
                sb.Append(r.SyncsPerStep.ToString("r")).Append(',');
                sb.Append(r.EstimatesPerStep.ToString("r"));
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
