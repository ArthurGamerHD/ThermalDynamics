using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// What a step costs through the path the host actually takes, against the path the ladder
    /// measures.
    ///
    /// <para>
    /// Every benchmark in this repository that reports a step time calls
    /// <see cref="ThermalSolver.Step(float, EnvironmentState)"/> directly. The game does not: it
    /// asks how long a step it can afford before starting one, and that question is answered by a
    /// walk over every node — mirroring the node objects into the flat rows, re-summing the
    /// conductance totals, applying the mass floor, then cubing a temperature per node for the
    /// stability estimate. A benchmark that skips it measures a step with half its fixed cost
    /// missing, and the fixed cost is roughly half a step on a grid taking three substeps.
    /// </para>
    ///
    /// <para>
    /// The two columns are the same physics over the same grid. Their difference is the price of
    /// the host's question, and it is the figure this lab exists to keep visible.
    /// </para>
    /// </summary>
    public static class StepPathLab
    {
        public class Row
        {
            public int Blocks;
            public int Links;

            /// <summary>The <c>MaxSubsteps</c> this row ran under, and what it was granted.</summary>
            public int Cap;
            public int Substeps;

            /// <summary>Milliseconds a step takes when driven straight at the solver.</summary>
            public double SolverMs;

            /// <summary>Milliseconds the same step takes through the host's entry point.</summary>
            public double HostMs;

            /// <summary>Node walks the host path ran per step, sync and stability estimate alike.</summary>
            public double SyncsPerStep;
            public double EstimatesPerStep;

            public double OverheadMs { get { return HostMs - SolverMs; } }

            public double OverheadShare
            {
                get { return HostMs <= 0d ? 0d : OverheadMs / HostMs; }
            }
        }

        /// <summary>
        /// Substep caps the sweep runs each rung under.
        ///
        /// The overhead is a fixed cost per step and the substeps are what it is amortised over,
        /// so a single substep count would answer the question for one grid and mislead about
        /// every other. The field runs at about three; a grid granted everything it demands runs
        /// at a dozen or more.
        /// </summary>
        public static readonly int[] DefaultCaps = { 1, 3, 64 };

        public static List<Row> Run(string shape, IList<int> ladder, IList<int> caps, int steps,
            Action<string> log)
        {
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

        private static Row Measure(string shape, int blocks, int cap, int steps)
        {
            Row row = new Row();
            row.Cap = cap;

            ThermalSimulation simulation = LoadBenchmarks.BuildSettled(shape, blocks);
            LoadBenchmarks.SeedSpread(simulation);

            // The visit budget is left active but out of reach. Switching it off would take the
            // host down a branch that skips the estimate entirely, and leaving it at its default
            // would shorten the step on the upper rungs — either way the two columns would no
            // longer be the same step, and the difference would stop being the overhead.
            simulation.Settings.MaxElementVisitsPerStep = int.MaxValue;
            simulation.Settings.MaxSubsteps = cap;
            simulation.Settings.Derive();

            row.Blocks = simulation.Solver.Nodes.Count;
            row.Links = simulation.Solver.Links.Count;

            EnvironmentSample sample = Worlds.Space(new VRageMath.Vector3(0f, 1f, 0f));
            EnvironmentState state = EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, sample);

            float step = simulation.Settings.StepSeconds;

            // Warmed on both paths before either is timed: the first step of a grid's life touches
            // every flat array for the first time, and whichever path ran first would carry it.
            for (int i = 0; i < 3; i++) simulation.Solver.Step(step, state);
            simulation.StepExact(3, sample);

            Stopwatch watch = new Stopwatch();
            row.SolverMs = double.MaxValue;
            row.HostMs = double.MaxValue;

            // Each timed run starts from the same spread. Conduction skips a link whose ends
            // agree, so a run left to inherit the previous run's grid measures a flatter one and
            // reads cheaper for no reason but its place in the order. That alone made the host
            // path look 26 % faster than the solver path it is a superset of.
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

        public static string Table(IList<Row> rows)
        {
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

        public static string Csv(IList<Row> rows)
        {
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
