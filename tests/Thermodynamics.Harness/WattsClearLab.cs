using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// What the watts row costs to clear. **A ladder rather than a figure**, because the clear is a
    /// second walk of the same array and what it costs depends on where that array sits in the cache:
    /// a saving that appears only at the top is bandwidth, one flat across it is instructions.
    ///
    /// <para>
    /// Every rung compares the two paths bit for bit, since this is one hull driven the same way and
    /// anything but exact agreement is a defect (`D8`).
    /// See benchmarks.md, The watts row is written.
    /// </para>
    /// </summary>
    public static class WattsClearLab
    {
        /// <summary>Hull sizes, chosen so the watts row crosses each level of the cache.</summary>
        public static readonly int[] DefaultSizes = { 2000, 8000, 32000, 125000, 500000 };

        /// <summary>Timed repeats; the fastest is reported, as everywhere else in this harness.</summary>
        public static int Repeats = 5;

        public class Row
        {
            public int Blocks;
            public int Nodes;
            public int Links;
            public int Substeps;

            /// <summary>Bytes the watts row occupies, which is what the clear walks.</summary>
            public long RowBytes { get { return (long)Nodes * sizeof(float); } }

            /// <summary>Milliseconds a step takes with the redundant clear still in place.</summary>
            public double ClearedMs;

            /// <summary>Milliseconds a step takes with the environment pass writing the row.</summary>
            public double FusedMs;

            public double SavedMs { get { return ClearedMs - FusedMs; } }

            public double SavedPercent
            {
                get { return ClearedMs <= 0d ? 0d : 100d * SavedMs / ClearedMs; }
            }

            /// <summary>Nanoseconds saved per node per substep — flat if the saving is instructions.</summary>
            public double SavedNsPerNodeSubstep
            {
                get
                {
                    long visits = (long)Nodes * (Substeps < 1 ? 1 : Substeps);
                    return visits <= 0 ? 0d : SavedMs * 1e6d / visits;
                }
            }

            /// <summary>True when the two paths produced the same temperatures, bit for bit.</summary>
            public bool Identical;

            /// <summary>The worst disagreement in kelvin, which is zero when Identical.</summary>
            public double WorstDelta;
        }

        public static List<Row> Run(IList<int> sizes, int ticks, Action<string> log = null)
        {
            List<Row> rows = new List<Row>();

            for (int s = 0; s < sizes.Count; s++)
            {
                int size = sizes[s];
                if (log != null) log("watts clear: " + size.ToString("n0") + " blocks");
                rows.Add(Measure(size, ticks));
            }

            return rows;
        }

        private static Row Measure(int blocks, int ticks)
        {
            Row row = new Row();
            row.Blocks = blocks;

            // ---- equivalence, on two hulls -------------------------------------------------
            //
            // Two separate simulations, seeded and driven identically, stepped the same number of
            // times. Anything but exact agreement is a defect in the change rather than a cost of
            // it, so this runs before the clock does.
            ThermalSimulation cleared = Build(blocks, fused: false);
            ThermalSimulation fused = Build(blocks, fused: true);

            row.Nodes = cleared.Solver.Nodes.Count;
            row.Links = cleared.Solver.Links.Count;

            EnvironmentState state = EnvironmentSolver.Solve(
                cleared.Settings, cleared.Planet, Worlds.Flight(1f, 300f));

            float step = cleared.Settings.StepSeconds;
            for (int i = 0; i < EquivalenceSteps; i++)
            {
                cleared.Solver.Step(step, state);
                fused.Solver.Step(step, state);
            }

            row.WorstDelta = WorstDelta(cleared, fused);
            row.Identical = row.WorstDelta == 0d;

            // ---- timing, on one hull -------------------------------------------------------
            //
            // The equivalence phase above proves the flag does not change the answer, which is
            // what makes it safe to flip mid-run — and flipping it on one grid is the only way to
            // measure it honestly. Two grids are two allocations at two addresses with two cache
            // colourings, and at half a million nodes the watts row is 2 MB: whichever hull
            // happened to land better would carry a difference larger than the one being looked
            // for. One grid, one layout, one warm cache, the flag the only thing that moves.
            ThermalSimulation timed = fused;

            // Settle the substep count and warm every array before the clock starts.
            for (int i = 0; i < 3; i++) timed.Solver.Step(step, state);
            row.Substeps = timed.Solver.LastSubsteps;

            double bestCleared = double.MaxValue;
            double bestFused = double.MaxValue;

            int repeats = Repeats < 1 ? 1 : Repeats;
            for (int r = 0; r < repeats; r++)
            {
                // Alternate the order within each repeat as well as across them, so neither path
                // is systematically the one that runs on a colder cache.
                if ((r & 1) == 0)
                {
                    bestCleared = Math.Min(bestCleared, Time(timed, state, ticks, false));
                    bestFused = Math.Min(bestFused, Time(timed, state, ticks, true));
                }
                else
                {
                    bestFused = Math.Min(bestFused, Time(timed, state, ticks, true));
                    bestCleared = Math.Min(bestCleared, Time(timed, state, ticks, false));
                }
            }

            row.ClearedMs = bestCleared;
            row.FusedMs = bestFused;

            return row;
        }

        /// <summary>Steps each hull takes before the two are compared. Enough to leave the seed behind.</summary>
        private const int EquivalenceSteps = 20;

        private static ThermalSimulation Build(int blocks, bool fused)
        {
            ThermalSimulation simulation = Hulls.Driven(Hulls.Uncapped(), blocks);
            simulation.Solver.FuseWattsClear = fused;
            return simulation;
        }

        private static double Time(ThermalSimulation simulation, EnvironmentState state, int ticks,
            bool fused)
        {
            simulation.Solver.FuseWattsClear = fused;
            float step = simulation.Settings.StepSeconds;

            Stopwatch watch = Stopwatch.StartNew();
            for (int i = 0; i < ticks; i++) simulation.Solver.Step(step, state);
            watch.Stop();

            return watch.Elapsed.TotalMilliseconds / ticks;
        }

        private static double WorstDelta(ThermalSimulation a, ThermalSimulation b)
        {
            IList<ThermalNode> left = a.Solver.Nodes;
            IList<ThermalNode> right = b.Solver.Nodes;

            if (left.Count != right.Count) return double.MaxValue;

            double worst = 0d;
            for (int i = 0; i < left.Count; i++)
            {
                double delta = Math.Abs((double)left[i].Temperature - right[i].Temperature);
                if (delta > worst) worst = delta;
            }

            return worst;
        }

        public static string Table(IList<Row> rows)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine(
                "  blocks     nodes     links  substeps    row KB     cleared       fused"
                + "     saved   saved   ns/node/sub  identical");

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,8:n0}  {1,8:n0}  {2,8:n0}  {3,8:n0}  {4,8:n0}  {5,8:n3}ms  {6,8:n3}ms"
                    + "  {7,7:n3}ms  {8,5:n1}%  {9,12:n4}  {10}",
                    row.Blocks, row.Nodes, row.Links, row.Substeps, row.RowBytes / 1024,
                    row.ClearedMs, row.FusedMs, row.SavedMs, row.SavedPercent,
                    row.SavedNsPerNodeSubstep,
                    row.Identical ? "yes" : "NO (" + row.WorstDelta.ToString("g4") + " K)"));
            }

            return text.ToString();
        }

        public static string Csv(IList<Row> rows)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("blocks,nodes,links,substeps,row_bytes,cleared_ms,fused_ms,"
                + "saved_ms,saved_percent,saved_ns_per_node_substep,identical,worst_delta_k");

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                text.AppendLine(string.Join(",",
                    row.Blocks.ToString(CultureInfo.InvariantCulture),
                    row.Nodes.ToString(CultureInfo.InvariantCulture),
                    row.Links.ToString(CultureInfo.InvariantCulture),
                    row.Substeps.ToString(CultureInfo.InvariantCulture),
                    row.RowBytes.ToString(CultureInfo.InvariantCulture),
                    row.ClearedMs.ToString("r", CultureInfo.InvariantCulture),
                    row.FusedMs.ToString("r", CultureInfo.InvariantCulture),
                    row.SavedMs.ToString("r", CultureInfo.InvariantCulture),
                    row.SavedPercent.ToString("r", CultureInfo.InvariantCulture),
                    row.SavedNsPerNodeSubstep.ToString("r", CultureInfo.InvariantCulture),
                    row.Identical ? "yes" : "no",
                    row.WorstDelta.ToString("r", CultureInfo.InvariantCulture)));
            }

            return text.ToString();
        }
    }
}
