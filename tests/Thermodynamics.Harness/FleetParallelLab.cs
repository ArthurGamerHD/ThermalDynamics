using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **Does solving one grid per thread pay, and from what fleet size?** The measurement
    /// backlog.md `D19` asks for before anything is threaded.
    ///
    /// <para>
    /// The two figures the row carries point opposite ways: eight thousand blocks solve in
    /// 0.128 ms, which may be under the cost of a hand-off, while a 242-grid fleet spent 25.9 % of
    /// real time in the solver. Those are answers to different questions — one grid split across
    /// threads, and many grids one per thread — and this lab measures the second, because it is the
    /// one the fleet figure argues for.
    /// </para>
    ///
    /// <para>
    /// **What it can and cannot stand in for.** Every grid carries its own solver and the core holds
    /// no mutable static state, so a fleet is embarrassingly parallel and this measures the real
    /// arithmetic. It does *not* measure the engine's scheduler: the mod would fan out through
    /// `MyAPIGateway.Parallel`, backed by `ParallelTasks`, and this uses the framework's own
    /// <see cref="Parallel"/>. The hand-off column is therefore a lower bound on the engine's, and
    /// is measured rather than assumed by fanning out over the same grid count with no work in the
    /// body (`D6`).
    /// </para>
    ///
    /// <para>
    /// **The subject is the parallel execution itself**, which is the one case `M2` does not cover:
    /// a wall-clock figure taken while every core is busy is exactly what is being asked for here.
    /// Every row is the fastest of several repeats and the spread between fastest and slowest is
    /// reported beside it (`M4`, `M5`).
    /// </para>
    /// </summary>
    public static class FleetParallelLab
    {
        /// <summary>One fleet size, sequential against parallel.</summary>
        public class Row
        {
            public int Grids;
            public int NodesEach;
            public int Threads;

            /// <summary>Milliseconds for one fleet-step — every grid stepped once — in order.</summary>
            public double SequentialMs;

            /// <summary>The same fleet-step with the grids fanned out.</summary>
            public double ParallelMs;

            /// <summary>Fan-out and join for this grid count with nothing in the body.</summary>
            public double HandoffMs;

            /// <summary>Slowest repeat over fastest, for each of the two: the noise floor (`M4`).</summary>
            public double SequentialSpread;
            public double ParallelSpread;

            public double Speedup
            {
                get { return ParallelMs <= 0d ? 0d : SequentialMs / ParallelMs; }
            }

            /// <summary>Milliseconds of one grid's step, from the sequential run.</summary>
            public double PerGridMs
            {
                get { return Grids <= 0 ? 0d : SequentialMs / Grids; }
            }

            /// <summary>What the hand-off costs as a share of the work it is fanning out.</summary>
            public double HandoffShare
            {
                get { return SequentialMs <= 0d ? 0d : HandoffMs / SequentialMs; }
            }
        }

        /// <summary>Repeats per figure. The fastest is kept and the spread reported (`M4`).</summary>
        public const int Repeats = 5;

        /// <summary>Fleet-steps per timed repeat.</summary>
        public const int StepsPerRepeat = 20;

        /// <summary>
        /// A fleet of identical driven hulls, each with its own solver, room map and node list.
        ///
        /// Identical on purpose: a fleet of different ships measures the scheduler's ability to
        /// balance uneven work as well as the fan-out, and those are two findings in one number.
        /// The uneven case is <see cref="RunUneven"/>.
        /// </summary>
        public static List<ThermalSimulation> Fleet(int grids, int nodesEach, ThermalSettings settings)
        {
            List<ThermalSimulation> fleet = new List<ThermalSimulation>();

            for (int i = 0; i < grids; i++)
            {
                GridBuilder builder = GridBuilder.Large();
                builder.PlaceCensus(LoadShapes.Build("ship", nodesEach));

                ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
                simulation.RebuildAll();
                Census.DriveCensus(simulation);
                LoadBenchmarks.SeedSpread(simulation);

                fleet.Add(simulation);
            }

            return fleet;
        }

        /// <summary>
        /// Sequential against parallel over a ladder of fleet sizes, at one grid size.
        /// </summary>
        public static List<Row> Run(IList<int> fleetSizes, int nodesEach, int threads,
            Action<string> log = null)
        {
            List<Row> rows = new List<Row>();

            ThermalSettings settings = new ThermalSettings();
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            EnvironmentState state = new EnvironmentState();
            bool haveState = false;

            for (int f = 0; f < fleetSizes.Count; f++)
            {
                int grids = fleetSizes[f];
                if (log != null) log(grids + " grids of " + nodesEach);

                List<ThermalSimulation> fleet = Fleet(grids, nodesEach, settings);
                if (!haveState)
                {
                    state = EnvironmentSolver.Solve(settings, fleet[0].Planet, Worlds.Shadow());
                    haveState = true;
                }

                Row row = new Row();
                row.Grids = grids;
                row.NodesEach = fleet[0].Solver.Nodes.Count;
                row.Threads = threads;

                // Warm the JIT and the caches on both paths before either is timed.
                StepSequential(fleet, settings, state, 2);
                StepParallel(fleet, settings, state, 2, threads);

                double fastest, slowest;
                Time(delegate { StepSequential(fleet, settings, state, StepsPerRepeat); },
                    out fastest, out slowest);
                row.SequentialMs = fastest / StepsPerRepeat;
                row.SequentialSpread = fastest <= 0d ? 0d : slowest / fastest;

                Time(delegate { StepParallel(fleet, settings, state, StepsPerRepeat, threads); },
                    out fastest, out slowest);
                row.ParallelMs = fastest / StepsPerRepeat;
                row.ParallelSpread = fastest <= 0d ? 0d : slowest / fastest;

                Time(delegate { FanOutOnly(grids, StepsPerRepeat, threads); }, out fastest, out slowest);
                row.HandoffMs = fastest / StepsPerRepeat;

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// The same comparison on a fleet whose grids differ in size, which is what a server has.
        ///
        /// A fan-out over equal work is the best case for any scheduler. Real fleets are a few
        /// capital ships among many small ones, and the largest grid sets the floor under a
        /// fleet-step however many threads there are — so this reports that floor beside the
        /// speed-up (`P1`: the figure carries the population it was taken over).
        /// </summary>
        public static Row RunUneven(IList<int> gridSizes, int threads, Action<string> log = null)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            List<ThermalSimulation> fleet = new List<ThermalSimulation>();
            for (int i = 0; i < gridSizes.Count; i++)
            {
                if (log != null) log("grid of " + gridSizes[i]);
                fleet.AddRange(Fleet(1, gridSizes[i], settings));
            }

            EnvironmentState state =
                EnvironmentSolver.Solve(settings, fleet[0].Planet, Worlds.Shadow());

            Row row = new Row();
            row.Grids = fleet.Count;
            row.Threads = threads;

            int largest = 0;
            for (int i = 0; i < fleet.Count; i++)
            {
                int nodes = fleet[i].Solver.Nodes.Count;
                if (nodes > largest) largest = nodes;
            }
            row.NodesEach = largest;

            StepSequential(fleet, settings, state, 2);
            StepParallel(fleet, settings, state, 2, threads);

            double fastest, slowest;
            Time(delegate { StepSequential(fleet, settings, state, StepsPerRepeat); },
                out fastest, out slowest);
            row.SequentialMs = fastest / StepsPerRepeat;
            row.SequentialSpread = fastest <= 0d ? 0d : slowest / fastest;

            Time(delegate { StepParallel(fleet, settings, state, StepsPerRepeat, threads); },
                out fastest, out slowest);
            row.ParallelMs = fastest / StepsPerRepeat;
            row.ParallelSpread = fastest <= 0d ? 0d : slowest / fastest;

            Time(delegate { FanOutOnly(fleet.Count, StepsPerRepeat, threads); },
                out fastest, out slowest);
            row.HandoffMs = fastest / StepsPerRepeat;

            return row;
        }

        public static void StepSequential(IList<ThermalSimulation> fleet, ThermalSettings settings,
            EnvironmentState state, int steps)
        {
            for (int s = 0; s < steps; s++)
            {
                for (int i = 0; i < fleet.Count; i++)
                {
                    fleet[i].Solver.Step(settings.StepSeconds, state);
                }
            }
        }

        /// <summary>
        /// One grid per work item, joined at the end of every fleet-step.
        ///
        /// The barrier is the point: the mod would solve in parallel and apply on the game thread,
        /// so a step is only as short as its slowest grid however many threads there are.
        /// </summary>
        public static void StepParallel(IList<ThermalSimulation> fleet, ThermalSettings settings,
            EnvironmentState state, int steps, int threads)
        {
            ParallelOptions options = new ParallelOptions();
            options.MaxDegreeOfParallelism = threads;

            for (int s = 0; s < steps; s++)
            {
                Parallel.For(0, fleet.Count, options, delegate(int i)
                {
                    fleet[i].Solver.Step(settings.StepSeconds, state);
                });
            }
        }

        /// <summary>The fan-out and join with nothing in the body: what a hand-off costs.</summary>
        private static void FanOutOnly(int grids, int steps, int threads)
        {
            ParallelOptions options = new ParallelOptions();
            options.MaxDegreeOfParallelism = threads;

            for (int s = 0; s < steps; s++)
            {
                Parallel.For(0, grids, options, delegate(int i) { Sink += i; });
            }
        }

        /// <summary>Written to so the empty body cannot be optimised away.</summary>
        public static long Sink;

        private static void Time(Action action, out double fastest, out double slowest)
        {
            LabTiming.FastestOf(Repeats, action, out fastest, out slowest);
        }

        public static string Table(IList<Row> rows)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("grids".PadLeft(7))
              .Append("nodes ea".PadLeft(10))
              .Append("threads".PadLeft(9))
              .Append("serial ms".PadLeft(11))
              .Append("par ms".PadLeft(10))
              .Append("speed-up".PadLeft(10))
              .Append("per grid".PadLeft(10))
              .Append("hand-off".PadLeft(10))
              .Append("of work".PadLeft(9))
              .Append("noise".PadLeft(14))
              .Append('\n');

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];

                sb.Append(row.Grids.ToString("n0").PadLeft(7))
                  .Append(row.NodesEach.ToString("n0").PadLeft(10))
                  .Append(row.Threads.ToString("n0").PadLeft(9))
                  .Append(row.SequentialMs.ToString("n3").PadLeft(11))
                  .Append(row.ParallelMs.ToString("n3").PadLeft(10))
                  .Append((row.Speedup.ToString("n2") + "x").PadLeft(10))
                  .Append(row.PerGridMs.ToString("n4").PadLeft(10))
                  .Append(row.HandoffMs.ToString("n4").PadLeft(10))
                  .Append((row.HandoffShare * 100d).ToString("n1").PadLeft(8))
                  .Append("%")
                  .Append((row.SequentialSpread.ToString("n2") + "/"
                      + row.ParallelSpread.ToString("n2") + "x").PadLeft(14))
                  .Append('\n');
            }

            return sb.ToString();
        }
    }
}
