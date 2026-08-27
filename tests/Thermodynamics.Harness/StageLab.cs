using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// One stage of a grid's life timed on its own, best of many: the surface map, the link build,
    /// the room pass, the exposure refresh, and a settled step of the solver. Each on one prebuilt
    /// grid, repeated, fastest kept, with the stage's own work counter beside the time.
    ///
    /// <para>
    /// **The build ladder cannot resolve a change to one of its stages.** Its `build` column is
    /// the sum of five stages and carries all five lots of noise, and on a shared machine that is
    /// six to twelve per cent between repeats of one tree — wide enough to hide a saving of a fifth
    /// (performance.md, Iteration 10) or to read one into nothing (Iteration 6). Timing the stage
    /// alone, best of N, resolves about a per cent, and the counter is what says two readings are
    /// the same work at two prices rather than two different walks (`M4`, `M5`, `P6`).
    /// See performance.md, How a pass is run.
    /// </para>
    /// </summary>
    public static class StageLab
    {
        /// <summary>Timed repeats per stage; the fastest is reported. Fifteen, because the worst of fifteen room passes on a shared machine is four times the best.</summary>
        public static int Repeats = 15;

        /// <summary>Steps per timed repeat of the solver stage, so a repeat is milliseconds rather than microseconds.</summary>
        public const int SolverStepsPerRepeat = 20;

        public class Row
        {
            public string Stage;
            public int Blocks;

            /// <summary>Milliseconds for one execution of the stage, the fastest of the repeats.</summary>
            public double BestMs;
            public double WorstMs;

            /// <summary>What the stage did, in its own unit — cells visited, links built, substeps run — identical across repeats or the reading is of two different walks.</summary>
            public long Work;
            public string WorkUnit;

            public double SpreadPercent
            {
                get { return BestMs <= 0d ? 0d : 100d * (WorstMs - BestMs) / BestMs; }
            }

            /// <summary>Nanoseconds per unit of work, which is the figure that transfers between sizes.</summary>
            public double NsPerWork
            {
                get { return Work <= 0 ? 0d : BestMs * 1e6d / Work; }
            }
        }

        public static readonly string[] Stages = { "surfaces", "links", "rooms", "exposure", "solver" };

        public static List<Row> Run(string shape, int blocks, IList<string> stages, Action<string> log = null)
        {
            List<Row> rows = new List<Row>();

            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build(shape, blocks));

            for (int i = 0; i < stages.Count; i++)
            {
                if (log != null) log(stages[i] + ", " + blocks.ToString("n0") + " blocks");
                rows.Add(Measure(stages[i], builder));
            }

            return rows;
        }

        private static ThermalSimulation Registered(GridBuilder builder)
        {
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings().Derive(), builder.Grid);
            for (int i = 0; i < builder.Placed.Count; i++)
            {
                simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            }
            return simulation;
        }

        private static Row Measure(string stage, GridBuilder builder)
        {
            switch (stage)
            {
                case "surfaces": return Surfaces(builder);
                case "links": return Links(builder);
                case "rooms": return Rooms(builder);
                case "exposure": return Exposure(builder);
                case "solver": return Solver(builder);
                default: throw new ArgumentException("Unknown stage: " + stage);
            }
        }

        private static Row NewRow(string stage, ThermalSimulation simulation, string unit)
        {
            Row row = new Row();
            row.Stage = stage;
            row.Blocks = simulation.Solver.Nodes.Count;
            row.BestMs = double.MaxValue;
            row.WorkUnit = unit;
            return row;
        }

        private static void Take(Row row, double ms)
        {
            if (ms < row.BestMs) row.BestMs = ms;
            if (ms > row.WorstMs) row.WorstMs = ms;
        }

        /// <summary>Whether a work figure repeated exactly; a stage whose work moves between repeats is not one stage.</summary>
        private static void Work(Row row, long work, int repeat)
        {
            if (repeat == 0) { row.Work = work; return; }
            if (row.Work != work)
            {
                throw new InvalidOperationException(row.Stage + " did " + work + " " + row.WorkUnit
                    + " on repeat " + repeat + " and " + row.Work + " on the first, so its readings are of different walks");
            }
        }

        private static Row Surfaces(GridBuilder builder)
        {
            ThermalSimulation simulation = Registered(builder);
            Row row = NewRow("surfaces", simulation, "cells");
            int repeats = Math.Max(1, Repeats);

            for (int r = 0; r < repeats; r++)
            {
                Stopwatch watch = Stopwatch.StartNew();
                simulation.Surfaces.Rebuild(simulation.Grid);
                watch.Stop();
                Take(row, watch.Elapsed.TotalMilliseconds);
                Work(row, simulation.Surfaces.CellCount, r);
            }

            return row;
        }

        private static Row Links(GridBuilder builder)
        {
            ThermalSimulation simulation = Registered(builder);
            simulation.Surfaces.Rebuild(simulation.Grid);
            Row row = NewRow("links", simulation, "links");
            int repeats = Math.Max(1, Repeats);

            for (int r = 0; r < repeats; r++)
            {
                Stopwatch watch = Stopwatch.StartNew();
                simulation.Solver.RebuildLinks();
                watch.Stop();
                Take(row, watch.Elapsed.TotalMilliseconds);
                Work(row, simulation.Solver.LinkCount, r);
            }

            return row;
        }

        private static Row Rooms(GridBuilder builder)
        {
            ThermalSimulation simulation = Registered(builder);
            simulation.Surfaces.Rebuild(simulation.Grid);
            Row row = NewRow("rooms", simulation, "cells visited");
            int repeats = Math.Max(1, Repeats);

            for (int r = 0; r < repeats; r++)
            {
                long before = simulation.Work.RoomCellsVisited;
                Stopwatch watch = Stopwatch.StartNew();
                simulation.Rooms.RequestRestart(simulation.Grid);
                if (!simulation.Rooms.RunToCompletion())
                {
                    throw new InvalidOperationException("the room pass did not finish, so there is no stage to time");
                }
                watch.Stop();
                Take(row, watch.Elapsed.TotalMilliseconds);
                Work(row, simulation.Work.RoomCellsVisited - before, r);
            }

            return row;
        }

        private static Row Exposure(GridBuilder builder)
        {
            ThermalSimulation simulation = Registered(builder);
            simulation.Surfaces.Rebuild(simulation.Grid);
            simulation.Rooms.RequestRestart(simulation.Grid);
            simulation.Rooms.RunToCompletion();
            Row row = NewRow("exposure", simulation, "nodes");
            int repeats = Math.Max(1, Repeats);

            for (int r = 0; r < repeats; r++)
            {
                long before = simulation.Work.ExposureNodeVisits;
                Stopwatch watch = Stopwatch.StartNew();
                simulation.Solver.RefreshExposure(simulation.Rooms.Map);
                watch.Stop();
                Take(row, watch.Elapsed.TotalMilliseconds);
                Work(row, simulation.Work.ExposureNodeVisits - before, r);
            }

            return row;
        }

        /// <summary>
        /// A settled, driven hull in flight, granted every substep it asks for: what a step costs
        /// once nothing about the grid is changing. Reported per step, over twenty steps a repeat.
        /// </summary>
        private static Row Solver(GridBuilder builder)
        {
            ThermalSimulation simulation = builder.BuildSimulation(Hulls.Uncapped(), 293.15f);
            LoadBenchmarks.SeedSpread(simulation);
            Census.DriveCensus(simulation);

            EnvironmentState state = EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, Worlds.Flight(1f, 300f));
            float step = simulation.Settings.StepSeconds;

            for (int i = 0; i < 3; i++) simulation.Solver.Step(step, state);

            Row row = NewRow("solver", simulation, "substeps");
            int repeats = Math.Max(1, Repeats);

            for (int r = 0; r < repeats; r++)
            {
                long before = simulation.Work.SolverSubsteps;
                Stopwatch watch = Stopwatch.StartNew();
                for (int i = 0; i < SolverStepsPerRepeat; i++) simulation.Solver.Step(step, state);
                watch.Stop();
                Take(row, watch.Elapsed.TotalMilliseconds / SolverStepsPerRepeat);
                Work(row, simulation.Work.SolverSubsteps - before, r);
            }

            return row;
        }

        public static string Table(IList<Row> rows)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("  stage        blocks       best ms      worst ms   spread          work  unit              ns/unit");
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-10} {1,9:n0}  {2,12:n3}  {3,12:n3}  {4,6:n0}%  {5,12:n0}  {6,-16}  {7,8:n1}",
                    row.Stage, row.Blocks, row.BestMs, row.WorstMs, row.SpreadPercent,
                    row.Work, row.WorkUnit, row.NsPerWork));
            }
            return text.ToString();
        }

        public static string Csv(IList<Row> rows)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("stage,blocks,best_ms,worst_ms,spread_percent,work,work_unit,ns_per_unit");
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                text.AppendLine(string.Join(",",
                    row.Stage,
                    row.Blocks.ToString(CultureInfo.InvariantCulture),
                    row.BestMs.ToString("r", CultureInfo.InvariantCulture),
                    row.WorstMs.ToString("r", CultureInfo.InvariantCulture),
                    row.SpreadPercent.ToString("r", CultureInfo.InvariantCulture),
                    row.Work.ToString(CultureInfo.InvariantCulture),
                    row.WorkUnit,
                    row.NsPerWork.ToString("r", CultureInfo.InvariantCulture)));
            }
            return text.ToString();
        }
    }
}
