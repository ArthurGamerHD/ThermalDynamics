using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **Whether a fleet is cheaper stepped grid by grid or slice by slice**, which is the trade
    /// [backlog.md](../../docs/backlog.md) `D14` asks to re-decide now that the lump is measurable.
    ///
    /// <para>
    /// A step is spread across the frames of its own window so no single frame carries a whole
    /// grid's work. The cost of that is cache: between two of a grid's frames the other grids in
    /// the world evict its arrays, so every resume is a cold start. Stepping whole grids in turn —
    /// staggering — keeps each grid's data hot and pays for it with a lump on the frame that does
    /// one.
    /// </para>
    ///
    /// <para>
    /// **Both schedules do identical arithmetic**, the same substeps over the same nodes in the
    /// same order; only the interleaving differs. So the difference between them is locality and
    /// nothing else, which is what makes this a measurement rather than a comparison of two models
    /// (`D8`'s question, asked of a schedule instead of a rewrite).
    /// </para>
    /// </summary>
    public static class StaggerLab
    {
        /// <summary>Repeats per figure; the fastest is kept and the spread reported (`M4`).</summary>
        public const int Repeats = 5;

        /// <summary>Full steps each grid takes per timed repeat.</summary>
        public const int RoundsPerRepeat = 8;

        public class Row
        {
            public int Grids;
            public int NodesEach;

            /// <summary>Frames one step is spread over, which is what the host's budget decides.</summary>
            public int Slices;

            /// <summary>Milliseconds for one round — every grid advanced one full step.</summary>
            public double StaggeredMs;
            public double SpreadMs;

            /// <summary>Slowest repeat over fastest, per side.</summary>
            public double StaggeredSpread;
            public double SpreadSpread;

            /// <summary>What spreading costs: spread over staggered.</summary>
            public double Penalty
            {
                get { return StaggeredMs <= 0d ? 0d : SpreadMs / StaggeredMs; }
            }

            /// <summary>The lump staggering puts on one frame: a whole grid's step.</summary>
            public double LumpMs
            {
                get { return Grids <= 0 ? 0d : StaggeredMs / Grids; }
            }
        }

        public static List<Row> Run(IList<int> fleetSizes, int nodesEach, int slices,
            Action<string> log = null)
        {
            List<Row> rows = new List<Row>();

            ThermalSettings settings = new ThermalSettings();
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            for (int f = 0; f < fleetSizes.Count; f++)
            {
                int grids = fleetSizes[f];
                if (log != null) log(grids + " grids of " + nodesEach);

                List<ThermalSimulation> fleet = FleetParallelLab.Fleet(grids, nodesEach, settings);
                EnvironmentState state =
                    EnvironmentSolver.Solve(settings, fleet[0].Planet, Worlds.Shadow());

                Row row = new Row();
                row.Grids = grids;
                row.NodesEach = fleet[0].Solver.Nodes.Count;
                row.Slices = slices;

                Staggered(fleet, settings, state, 2);
                Spread(fleet, settings, state, 2, slices);

                double fastest, slowest;
                Time(delegate { Staggered(fleet, settings, state, RoundsPerRepeat); },
                    out fastest, out slowest);
                row.StaggeredMs = fastest / RoundsPerRepeat;
                row.StaggeredSpread = fastest <= 0d ? 0d : slowest / fastest;

                Time(delegate { Spread(fleet, settings, state, RoundsPerRepeat, slices); },
                    out fastest, out slowest);
                row.SpreadMs = fastest / RoundsPerRepeat;
                row.SpreadSpread = fastest <= 0d ? 0d : slowest / fastest;

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>Whole steps, one grid at a time: the staggered schedule.</summary>
        public static void Staggered(IList<ThermalSimulation> fleet, ThermalSettings settings,
            EnvironmentState state, int rounds)
        {
            for (int r = 0; r < rounds; r++)
            {
                for (int i = 0; i < fleet.Count; i++)
                {
                    fleet[i].Solver.Step(settings.StepSeconds, state);
                }
            }
        }

        /// <summary>
        /// One step per grid, cut into <paramref name="slices"/> and interleaved: the spread
        /// schedule, which is what the host does today.
        /// </summary>
        public static void Spread(IList<ThermalSimulation> fleet, ThermalSettings settings,
            EnvironmentState state, int rounds, int slices)
        {
            for (int r = 0; r < rounds; r++)
            {
                for (int i = 0; i < fleet.Count; i++)
                {
                    fleet[i].Solver.BeginStep(settings.StepSeconds, state);
                }

                // A budget per slice rather than a fixed count, because a step's work is known
                // only once it has begun and the host budgets in element visits too.
                for (int s = 0; s < slices; s++)
                {
                    for (int i = 0; i < fleet.Count; i++)
                    {
                        ThermalSolver solver = fleet[i].Solver;
                        long budget = (solver.StepWorkUnits / slices) + 1;
                        solver.AdvanceStep(budget);
                    }
                }

                // Anything the budget did not finish is finished, so both schedules do the same
                // number of whole steps and the comparison is of schedules rather than of work.
                for (int i = 0; i < fleet.Count; i++)
                {
                    while (!fleet[i].Solver.AdvanceStep(long.MaxValue)) { }
                }
            }
        }

        private static void Time(Action action, out double fastest, out double slowest)
        {
            fastest = double.MaxValue;
            slowest = 0d;

            for (int r = 0; r < Repeats; r++)
            {
                Stopwatch watch = Stopwatch.StartNew();
                action();
                watch.Stop();

                double ms = watch.Elapsed.TotalMilliseconds;
                if (ms < fastest) fastest = ms;
                if (ms > slowest) slowest = ms;
            }
        }

        public static string Table(IList<Row> rows)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("grids".PadLeft(7))
              .Append("nodes ea".PadLeft(10))
              .Append("slices".PadLeft(8))
              .Append("staggered".PadLeft(11))
              .Append("spread".PadLeft(10))
              .Append("spreading costs".PadLeft(17))
              .Append("lump".PadLeft(9))
              .Append("noise".PadLeft(14))
              .Append('\n');

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];

                sb.Append(row.Grids.ToString("n0").PadLeft(7))
                  .Append(row.NodesEach.ToString("n0").PadLeft(10))
                  .Append(row.Slices.ToString("n0").PadLeft(8))
                  .Append((row.StaggeredMs.ToString("n3") + " ms").PadLeft(11))
                  .Append((row.SpreadMs.ToString("n3") + " ms").PadLeft(10))
                  .Append(((row.Penalty - 1d) * 100d).ToString("n1").PadLeft(16))
                  .Append("%")
                  .Append((row.LumpMs.ToString("n3") + " ms").PadLeft(9))
                  .Append((row.StaggeredSpread.ToString("n2") + "/"
                      + row.SpreadSpread.ToString("n2") + "x").PadLeft(14))
                  .Append('\n');
            }

            return sb.ToString();
        }
    }
}
