using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class FleetParallelLab
    {
        public class Row
        {
            public int Grids;
            public int NodesEach;
            public int Threads;

            public double SequentialMs;

            public double ParallelMs;

            public double HandoffMs;

            public double SequentialSpread;
            public double ParallelSpread;

            public double Speedup
            {
                get { return ParallelMs <= 0d ? 0d : SequentialMs / ParallelMs; }
            }

            public double PerGridMs
            {
                get { return Grids <= 0 ? 0d : SequentialMs / Grids; }
            }

            public double HandoffShare
            {
                get { return SequentialMs <= 0d ? 0d : HandoffMs / SequentialMs; }
            }
        }

        public const int Repeats = 5;

        public const int StepsPerRepeat = 20;

/// <summary>Fleet operation.</summary>
        public static List<ThermalSimulation> Fleet(int grids, int nodesEach, ThermalSettings settings)
        {
/// <summary>List operation.</summary>
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

/// <summary>Run operation.</summary>
        public static List<Row> Run(IList<int> fleetSizes, int nodesEach, int threads,
            Action<string> log = null)
        {
/// <summary>List operation.</summary>
            List<Row> rows = new List<Row>();

/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

/// <summary>EnvironmentState operation.</summary>
            EnvironmentState state = new EnvironmentState();
            bool haveState = false;

            for (int f = 0; f < fleetSizes.Count; f++)
            {
                int grids = fleetSizes[f];
                if (log != null) log(grids + " grids of " + nodesEach);

/// <summary>Fleet operation.</summary>
                List<ThermalSimulation> fleet = Fleet(grids, nodesEach, settings);
                if (!haveState)
                {
                    state = EnvironmentSolver.Solve(settings, fleet[0].Planet, Worlds.Shadow());
                    haveState = true;
                }

/// <summary>Row operation.</summary>
                Row row = new Row();
                row.Grids = grids;
                row.NodesEach = fleet[0].Solver.Nodes.Count;
                row.Threads = threads;

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

/// <summary>RunUneven operation.</summary>
        public static Row RunUneven(IList<int> gridSizes, int threads, Action<string> log = null)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

/// <summary>List operation.</summary>
            List<ThermalSimulation> fleet = new List<ThermalSimulation>();
            for (int i = 0; i < gridSizes.Count; i++)
            {
                if (log != null) log("grid of " + gridSizes[i]);
                fleet.AddRange(Fleet(1, gridSizes[i], settings));
            }

            EnvironmentState state =
                EnvironmentSolver.Solve(settings, fleet[0].Planet, Worlds.Shadow());

/// <summary>Row operation.</summary>
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

/// <summary>StepSequential operation.</summary>
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

/// <summary>StepParallel operation.</summary>
        public static void StepParallel(IList<ThermalSimulation> fleet, ThermalSettings settings,
            EnvironmentState state, int steps, int threads)
        {
/// <summary>ParallelOptions operation.</summary>
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

/// <summary>FanOutOnly operation.</summary>
        private static void FanOutOnly(int grids, int steps, int threads)
        {
/// <summary>ParallelOptions operation.</summary>
            ParallelOptions options = new ParallelOptions();
            options.MaxDegreeOfParallelism = threads;

            for (int s = 0; s < steps; s++)
            {
                Parallel.For(0, grids, options, delegate(int i) { Sink += i; });
            }
        }

        public static long Sink;

/// <summary>Time operation.</summary>
        private static void Time(Action action, out double fastest, out double slowest)
        {
            LabTiming.FastestOf(Repeats, action, out fastest, out slowest);
        }

/// <summary>Table operation.</summary>
        public static string Table(IList<Row> rows)
        {
/// <summary>StringBuilder operation.</summary>
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
