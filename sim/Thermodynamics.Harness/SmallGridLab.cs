using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// What one grid costs before any of its blocks do.
    ///
    /// <para>
    /// A step has a fixed part — the environment solve, the node mirror, the stability estimate,
    /// the stage machine, the loop and pump prologues — and a part proportional to the grid. The
    /// fleet rows in the performance report found the two in balance at eight hundred blocks a
    /// grid, and concluded that grid count does not matter. A real world is not made of
    /// eight-hundred-block grids: the 2026-08-20 field dump holds 97 grids of five cells or fewer
    /// and 198 of five hundred or fewer, and they cost 10 % of the mod's time for 5 % of its
    /// blocks.
    /// </para>
    ///
    /// <para>
    /// This sweeps grid size down to one block at a fixed fleet size, so the fixed part is read
    /// where it dominates rather than where it is already amortised. The environment is a planet
    /// surface, because a vacuum sample skips most of what the environment solve does and every
    /// grid in that dump was in air.
    /// </para>
    /// </summary>
    public static class SmallGridLab
    {
        /// <summary>Blocks per grid, spanning the sizes a real world is mostly made of.</summary>
        public static readonly int[] DefaultSizes = { 1, 3, 8, 32, 128, 512, 2048 };

        public class Row
        {
            public int Grids;
            public int BlocksPerGrid;
            public int Blocks;

            /// <summary>Milliseconds one step of the whole fleet takes, run whole.</summary>
            public double FleetStepMs;

            /// <summary>
            /// Milliseconds the same simulated second costs when the host paces it: every grid
            /// visited every frame, each frame doing its share of a step.
            /// </summary>
            public double PacedStepMs;

            /// <summary>Calls into the resumable stage machine per step, per grid.</summary>
            public double AdvancesPerStep;

            public double PacingOverhead
            {
                get { return FleetStepMs <= 0d ? 0d : (PacedStepMs - FleetStepMs) / FleetStepMs; }
            }

            public double MicrosecondsPerGridStep
            {
                get { return Grids <= 0 ? 0d : FleetStepMs * 1000d / Grids; }
            }

            public double NanosecondsPerBlockStep
            {
                get { return Blocks <= 0 ? 0d : FleetStepMs * 1000000d / Blocks; }
            }

            public double Substeps;
        }

        public static List<Row> Run(int grids, IList<int> sizes, int steps, Action<string> log)
        {
            List<Row> rows = new List<Row>();

            for (int i = 0; i < sizes.Count; i++)
            {
                if (log != null) log(grids + " grids of " + sizes[i]);
                rows.Add(Measure(grids, sizes[i], steps));
            }

            return rows;
        }

        private static Row Measure(int grids, int blocksPerGrid, int steps)
        {
            Row row = new Row();
            row.Grids = grids;
            row.BlocksPerGrid = blocksPerGrid;

            List<ThermalSimulation> fleet = new List<ThermalSimulation>(grids);
            for (int i = 0; i < grids; i++)
            {
                ThermalSimulation simulation = Build(blocksPerGrid);
                fleet.Add(simulation);
                row.Blocks += simulation.Solver.Nodes.Count;
            }

            EnvironmentSample sample = Worlds.PlanetSurface(1f, 0.5f);

            for (int i = 0; i < fleet.Count; i++) fleet[i].StepExact(3, sample);
            row.Substeps = fleet[0].Solver.LastSubsteps;

            double best = double.MaxValue;
            Stopwatch watch = new Stopwatch();

            for (int repeat = 0; repeat < 3; repeat++)
            {
                watch.Restart();
                for (int s = 0; s < steps; s++)
                {
                    for (int i = 0; i < fleet.Count; i++) fleet[i].StepExact(1, sample);
                }
                watch.Stop();

                double ms = watch.Elapsed.TotalMilliseconds / steps;
                if (ms < best) best = ms;
            }

            row.FleetStepMs = best;

            // And again the way the host drives it: every grid visited every frame, each frame
            // doing the fraction of a step its own length represents. Same simulated time, same
            // substeps, same arithmetic — the difference is how many times the stage machine is
            // entered to do it.
            float frame = 1f / 60f;
            int framesPerStep = (int)Math.Round(60f / fleet[0].Settings.StepsPerSecond);
            int frames = steps * framesPerStep;

            for (int i = 0; i < fleet.Count; i++) fleet[i].Update(frame, sample);

            double pacedBest = double.MaxValue;
            long advances = 0;
            long completed = 0;

            for (int repeat = 0; repeat < 3; repeat++)
            {
                for (int i = 0; i < fleet.Count; i++) fleet[i].Work.Reset();

                watch.Restart();
                for (int f = 0; f < frames; f++)
                {
                    for (int i = 0; i < fleet.Count; i++) fleet[i].Update(frame, sample);
                }
                watch.Stop();

                double ms = watch.Elapsed.TotalMilliseconds / steps;
                if (ms < pacedBest)
                {
                    pacedBest = ms;
                    advances = 0;
                    completed = 0;
                    for (int i = 0; i < fleet.Count; i++)
                    {
                        advances += fleet[i].Work.StepAdvances;
                        completed += fleet[i].Work.SolverSteps;
                    }
                }
            }

            row.PacedStepMs = pacedBest;
            row.AdvancesPerStep = completed <= 0 ? 0d : advances / (double)completed;
            return row;
        }

        /// <summary>
        /// A compact hull of exactly this many cells, from the block census so the stiffness
        /// spread is a ship's rather than one material's.
        /// </summary>
        private static ThermalSimulation Build(int blocks)
        {
            if (blocks < 1) blocks = 1;

            int side = 1;
            while (side * side * side < blocks) side++;

            List<Vector3I> cells = new List<Vector3I>(blocks);
            for (int x = 0; x < side && cells.Count < blocks; x++)
            {
                for (int y = 0; y < side && cells.Count < blocks; y++)
                {
                    for (int z = 0; z < side && cells.Count < blocks; z++)
                    {
                        cells.Add(new Vector3I(x, y, z));
                    }
                }
            }

            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(cells);

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings());
            simulation.RebuildAll();
            return simulation;
        }

        public static string Table(IList<Row> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("  grids  blocks/grid    blocks  substeps    whole ms"
                + "    paced ms   pacing   advances   us/grid/step");

            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                sb.Append(r.Grids.ToString("n0").PadLeft(7));
                sb.Append(r.BlocksPerGrid.ToString("n0").PadLeft(13));
                sb.Append(r.Blocks.ToString("n0").PadLeft(10));
                sb.Append(r.Substeps.ToString("n0").PadLeft(10));
                sb.Append(r.FleetStepMs.ToString("n3").PadLeft(12));
                sb.Append(r.PacedStepMs.ToString("n3").PadLeft(12));
                sb.Append((r.PacingOverhead * 100d).ToString("n0").PadLeft(8) + "%");
                sb.Append(r.AdvancesPerStep.ToString("n1").PadLeft(11));
                sb.Append(r.MicrosecondsPerGridStep.ToString("n3").PadLeft(15));
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public static string Csv(IList<Row> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("grids,blocks_per_grid,blocks,substeps,fleet_step_ms,paced_step_ms,"
                + "pacing_overhead,advances_per_step,us_per_grid_step,ns_per_block_step");

            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                sb.Append(r.Grids).Append(',');
                sb.Append(r.BlocksPerGrid).Append(',');
                sb.Append(r.Blocks).Append(',');
                sb.Append(r.Substeps.ToString("r")).Append(',');
                sb.Append(r.FleetStepMs.ToString("r")).Append(',');
                sb.Append(r.PacedStepMs.ToString("r")).Append(',');
                sb.Append(r.PacingOverhead.ToString("r")).Append(',');
                sb.Append(r.AdvancesPerStep.ToString("r")).Append(',');
                sb.Append(r.MicrosecondsPerGridStep.ToString("r")).Append(',');
                sb.Append(r.NanosecondsPerBlockStep.ToString("r"));
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
