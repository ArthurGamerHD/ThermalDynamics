using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **Whether a fleet is cheaper stepped grid by grid or slice by slice**, which is the trade
    /// backlog.md `D14` asks to re-decide now that the lump is measurable.
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

        /// <summary>
        /// Grid-steps inside one timed repeat, held **equal across fleet sizes**.
        ///
        /// <para>
        /// **This used to be eight rounds whatever the fleet, and that is a comparison of two
        /// instruments rather than of two fleets** (`P6`). A round advances every grid once, so
        /// eight rounds is 32 grid-steps at four grids and 512 at sixty-four — one timed window of
        /// about four milliseconds and one of about seventy. Best-of-five over four milliseconds is
        /// a reading a single scheduler hiccup lands inside all five times, and the two rungs were
        /// then divided by each other as though they had been measured the same way. That is what
        /// made the lump read 0.75 ms at four grids against 0.13 ms at sixty-four whenever the
        /// machine was busy (backlog.md `A11`).
        /// </para>
        ///
        /// <para>
        /// Five hundred and twelve keeps the largest rung's window exactly as it was and lengthens
        /// the smaller ones to match, so no figure this lab has ever published moves and every rung
        /// is now read through the same instrument.
        /// </para>
        /// </summary>
        public const int GridStepsPerRepeat = 512;

        /// <summary>
        /// Rounds a fleet of this size needs to make one timed repeat
        /// <see cref="GridStepsPerRepeat"/> grid-steps long. At least one, so a fleet larger than
        /// the window still measures something.
        /// </summary>
        public static int RoundsFor(int grids)
        {
            if (grids <= 0) return 1;
            int rounds = GridStepsPerRepeat / grids;
            return rounds < 1 ? 1 : rounds;
        }

        public class Row
        {
            public int Grids;
            public int NodesEach;

            /// <summary>Frames one step is spread over, which is what the host's budget decides.</summary>
            public int Slices;

            /// <summary>
            /// Rounds inside one timed repeat. Printed because it is what makes two rungs
            /// comparable: it varies with the fleet so that `Grids * Rounds` does not.
            /// </summary>
            public int Rounds;

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

            /// <summary>
            /// Element visits one grid's whole step charges, summed over the fleet and divided by
            /// it — the same quantity <see cref="LumpMs"/> reports, in work rather than in time.
            ///
            /// <para>
            /// **This is the readable half of the lump claim.** A millisecond is a claim about the
            /// machine (`M4`), and a *ratio* of two milliseconds taken at two fleet sizes is a
            /// claim about the machine's cache — which a noise floor computed from repeats cannot
            /// see, because a busy machine biases the larger working set steadily rather than
            /// jitterily. The work a grid's step charges does not depend on how many other grids
            /// there are, on any machine, and that is the regression the claim is really for.
            /// </para>
            /// </summary>
            public double LumpWork;
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

                int rounds = RoundsFor(grids);
                row.Rounds = rounds;

                // Read once, before the timed repeats: it is the same on every one of them.
                Staggered(fleet, settings, state, 1);
                long work = 0;
                for (int i = 0; i < fleet.Count; i++) work += fleet[i].Solver.StepWorkUnits;
                row.LumpWork = work / (double)grids;

                Staggered(fleet, settings, state, 2);
                Spread(fleet, settings, state, 2, slices);

                double fastest, slowest;
                Time(delegate { Staggered(fleet, settings, state, rounds); },
                    out fastest, out slowest);
                row.StaggeredMs = fastest / rounds;
                row.StaggeredSpread = fastest <= 0d ? 0d : slowest / fastest;

                Time(delegate { Spread(fleet, settings, state, rounds, slices); },
                    out fastest, out slowest);
                row.SpreadMs = fastest / rounds;
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
            LabTiming.FastestOf(Repeats, action, out fastest, out slowest);
        }

        public static string Table(IList<Row> rows)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("grids".PadLeft(7))
              .Append("nodes ea".PadLeft(10))
              .Append("slices".PadLeft(8))
              .Append("rounds".PadLeft(8))
              .Append("staggered".PadLeft(11))
              .Append("spread".PadLeft(10))
              .Append("spreading costs".PadLeft(17))
              .Append("lump".PadLeft(9))
              .Append("lump work".PadLeft(12))
              .Append("noise".PadLeft(14))
              .Append('\n');

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];

                sb.Append(row.Grids.ToString("n0").PadLeft(7))
                  .Append(row.NodesEach.ToString("n0").PadLeft(10))
                  .Append(row.Slices.ToString("n0").PadLeft(8))
                  .Append(row.Rounds.ToString("n0").PadLeft(8))
                  .Append((row.StaggeredMs.ToString("n3") + " ms").PadLeft(11))
                  .Append((row.SpreadMs.ToString("n3") + " ms").PadLeft(10))
                  .Append(((row.Penalty - 1d) * 100d).ToString("n1").PadLeft(16))
                  .Append("%")
                  .Append((row.LumpMs.ToString("n3") + " ms").PadLeft(9))
                  .Append(row.LumpWork.ToString("n0").PadLeft(12))
                  .Append((row.StaggeredSpread.ToString("n2") + "/"
                      + row.SpreadSpread.ToString("n2") + "x").PadLeft(14))
                  .Append('\n');
            }

            return sb.ToString();
        }
    }
}
