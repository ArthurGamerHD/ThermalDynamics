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

            /// <summary>
            /// Bytes one execution of the stage allocated, from the last repeat's own delta.
            ///
            /// **A stage that churns the heap changes what the stage after it measures**, which is
            /// how a figure in this lab came to be unexplainable: `place` allocates a block instance
            /// and a grid per repeat, fifteen times, and everything after it read a different heap.
            /// The lab settles between stages now, and reports this so a reader can see which stage
            /// is the one doing it rather than inferring it from an odd row.
            /// See performance.md, Pass 3, Iteration 1.
            /// </summary>
            public long AllocatedBytes;

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

            /// <summary>
            /// Anything the stage wants to say about its own work beyond the count — the share of
            /// the air rebuild's probes that found a block, for instance. Blank for most stages.
            /// </summary>
            public string Note = "";

            /// <summary>Nanoseconds per unit of work, which is the figure that transfers between sizes.</summary>
            public double NsPerWork
            {
                get { return Work <= 0 ? 0d : BestMs * 1e6d / Work; }
            }
        }

        public static readonly string[] Stages = { "place", "register", "surfaces", "links", "rooms", "exposure", "roomair", "solver" };

        public static List<Row> Run(string shape, int blocks, IList<string> stages, Action<string> log = null)
        {
            List<Row> rows = new List<Row>();

            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build(shape, blocks));

            for (int i = 0; i < stages.Count; i++)
            {
                if (log != null) log(stages[i] + ", " + blocks.ToString("n0") + " blocks");

                // Settled before every stage, so the list's order cannot carry: whatever the stage
                // before it left on the heap is collected before this one is timed.
                Settle();
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
                case "place": return Place(builder);
                case "register": return Register(builder);
                case "surfaces": return Surfaces(builder);
                case "links": return Links(builder);
                case "rooms": return Rooms(builder);
                case "exposure": return Exposure(builder);
                case "roomair": return RoomAir(builder);
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

        /// <summary>Collects twice and waits, so a stage is timed against a settled heap rather than the last stage's garbage.</summary>
        private static void Settle()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>
        /// Records what one execution of a stage allocated; called around the last repeat only.
        ///
        /// **Per thread, not per process.** `GC.GetTotalAllocatedBytes` counts every thread, so
        /// under a suite running eight classes at once a stage's delta collects whatever the other
        /// seven allocated meanwhile — which is how the assertion that a settled step allocates
        /// nothing came to read half a megabyte and fail. Alone it passed, which is the worst way
        /// for a check to be wrong. See performance.md, Pass 3, Iteration 10.
        /// </summary>
        private static long Allocated()
        {
            return GC.GetAllocatedBytesForCurrentThread();
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

        /// <summary>
        /// Constructing every block instance and adding it to a fresh grid — what the adapter does
        /// per block when it mirrors a `MyCubeGrid` at world load, and what a paste does. The dealt
        /// builder supplies the models, cells and orientations; nothing else about it is reused.
        /// </summary>
        private static Row Place(GridBuilder builder)
        {
            IList<BlockInstance> dealt = builder.Placed;
            Row row = null;
            int repeats = Math.Max(1, Repeats);

            for (int r = 0; r < repeats; r++)
            {
                GridModel grid = new GridModel(builder.Grid.GridSize);

                long allocated = r == repeats - 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                for (int i = 0; i < dealt.Count; i++)
                {
                    grid.Add(new BlockInstance(dealt[i].Model, dealt[i].Min, dealt[i].Orientation));
                }
                watch.Stop();

                if (row == null)
                {
                    row = new Row();
                    row.Stage = "place";
                    row.Blocks = grid.BlockCount;
                    row.BestMs = double.MaxValue;
                    row.WorkUnit = "blocks";
                }
                if (r == repeats - 1) row.AllocatedBytes = Allocated() - allocated;
                Take(row, watch.Elapsed.TotalMilliseconds);
                Work(row, grid.BlockCount, r);
            }

            return row;
        }

        /// <summary>Registering every placed block with a fresh simulation's solver: a node each, and its links pending.</summary>
        private static Row Register(GridBuilder builder)
        {
            Row row = null;
            int repeats = Math.Max(1, Repeats);

            for (int r = 0; r < repeats; r++)
            {
                ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings().Derive(), builder.Grid);

                long allocated = r == repeats - 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                for (int i = 0; i < builder.Placed.Count; i++)
                {
                    simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
                }
                watch.Stop();
                if (r == repeats - 1) row.AllocatedBytes = Allocated() - allocated;

                if (row == null) row = NewRow("register", simulation, "blocks");
                Take(row, watch.Elapsed.TotalMilliseconds);
                Work(row, simulation.Solver.Nodes.Count, r);
            }

            return row;
        }

        private static Row Surfaces(GridBuilder builder)
        {
            ThermalSimulation simulation = Registered(builder);
            Row row = NewRow("surfaces", simulation, "cells");
            int repeats = Math.Max(1, Repeats);

            for (int r = 0; r < repeats; r++)
            {
                long allocated = r == repeats - 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                simulation.Surfaces.Rebuild(simulation.Grid);
                watch.Stop();
                if (r == repeats - 1) row.AllocatedBytes = Allocated() - allocated;
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
                long allocated = r == repeats - 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                simulation.Solver.RebuildLinks();
                watch.Stop();
                if (r == repeats - 1) row.AllocatedBytes = Allocated() - allocated;
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
                long allocated = r == repeats - 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                simulation.Rooms.RequestRestart(simulation.Grid);
                if (!simulation.Rooms.RunToCompletion())
                {
                    throw new InvalidOperationException("the room pass did not finish, so there is no stage to time");
                }
                watch.Stop();
                if (r == repeats - 1) row.AllocatedBytes = Allocated() - allocated;
                Take(row, watch.Elapsed.TotalMilliseconds);
                Work(row, simulation.Work.RoomCellsVisited - before, r);
            }

            return row;
        }

        /// <summary>
        /// Rebuilding every room's air from a published map: one air node a room, and one link per
        /// block bounding it, found by walking the room's cells and asking the grid what is across
        /// each face.
        ///
        /// <para>
        /// **The rooms have to be filled or this stage measures nothing.** A room at zero pressure
        /// has no air and therefore no links, so an unpressurised hull runs the outer loop and
        /// returns — which is what the first draft of this timed. The fill happens once, before the
        /// clock, and every repeat afterwards finds it again through the remembered air.
        /// </para>
        /// </summary>
        private static Row RoomAir(GridBuilder builder)
        {
            ThermalSimulation simulation = Registered(builder);
            simulation.Surfaces.Rebuild(simulation.Grid);
            simulation.Rooms.RequestRestart(simulation.Grid);
            simulation.Rooms.RunToCompletion();

            RoomMap map = simulation.Rooms.Map;
            simulation.Solver.RebuildRoomAir(map);

            int filled = 0;
            for (int r = 0; r < map.RoomCount; r++)
            {
                if (map.CellsInRoom(r) == 0) continue;
                if (simulation.Solver.SetRoomPressure(map, map.CellsOf(r)[0], 1f)) filled++;
            }

            if (filled == 0)
            {
                throw new InvalidOperationException(
                    "no room took air on a hull with " + map.RoomCount
                    + " rooms, so this stage would time an outer loop and nothing else");
            }

            Row row = NewRow("roomair", simulation, "face probes");
            int repeats = Math.Max(1, Repeats);

            for (int r = 0; r < repeats; r++)
            {
                long before = simulation.Work.RoomAirFaceProbes;
                long hitsBefore = simulation.Work.RoomAirFaceHits;
                long allocated = r == repeats - 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                simulation.Solver.RebuildRoomAir(map);
                watch.Stop();
                if (r == repeats - 1) row.AllocatedBytes = Allocated() - allocated;
                Take(row, watch.Elapsed.TotalMilliseconds);
                Work(row, simulation.Work.RoomAirFaceProbes - before, r);

                // What share of those probes found anything, which is the number that says whether
                // to make the probe cheaper or to stop making it.
                if (r == 0) row.Note = (simulation.Work.RoomAirFaceHits - hitsBefore).ToString("n0") + " hit";
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
                long allocated = r == repeats - 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                simulation.Solver.RefreshExposure(simulation.Rooms.Map);
                watch.Stop();
                if (r == repeats - 1) row.AllocatedBytes = Allocated() - allocated;
                Take(row, watch.Elapsed.TotalMilliseconds);
                Work(row, simulation.Work.ExposureNodeVisits - before, r);
            }

            return row;
        }

        /// <summary>
        /// The stages *inside* a settled step, each on its own clock: what a step spends on the
        /// environment pass, on conduction, on the coupled bodies, on turning watts into
        /// temperatures and on publishing.
        ///
        /// <para>
        /// Returned as one row per stage, so the caller sees the split rather than a total. The
        /// work column is the element visits the solver charges that stage, which is the same
        /// number every repeat or the readings are of different walks.
        /// </para>
        /// </summary>
        public static List<Row> StepPhases(string shape, int blocks, Action<string> log = null)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build(shape, blocks));

            // Built exactly as the `solver` stage builds it, and stepped through the same call, so
            // the split below adds up to the figure that stage reports rather than to some other
            // step's (`P6`).
            ThermalSimulation simulation = builder.BuildSimulation(Hulls.Uncapped(), 293.15f);
            LoadBenchmarks.SeedSpread(simulation);
            Census.DriveCensus(simulation);

            EnvironmentState state = EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, Worlds.Flight(1f, 300f));
            float step = simulation.Settings.StepSeconds;

            if (log != null) log("step phases, " + blocks.ToString("n0") + " blocks");

            // **A repeat is the same twenty steps the `solver` stage times**, and for the same two
            // reasons: a step is short enough that one of them is mostly clock, and a hull that has
            // not settled asks for a different number of substeps from one step to the next — which
            // the guard below reports as two different walks, correctly, and which is a property of
            // the hull rather than of the instrument. Twenty steps of warm-up first, for the same
            // reason again.
            for (int i = 0; i < SolverStepsPerRepeat; i++) simulation.Solver.Step(step, state);

            // The profile is reset per repeat and the fastest repeat is reported stage by stage, so
            // a collection landing in one repeat cannot flatter another.
            Settle();
            simulation.Solver.ProfileStepPhases = true;

            int repeats = Math.Max(1, Repeats);
            double[] best = new double[ThermalSolver.StepPhaseProfile.PhaseCount];
            double[] worst = new double[ThermalSolver.StepPhaseProfile.PhaseCount];
            long[] visits = new long[ThermalSolver.StepPhaseProfile.PhaseCount];
            long[] slices = new long[ThermalSolver.StepPhaseProfile.PhaseCount];
            for (int p = 0; p < best.Length; p++) best[p] = double.MaxValue;

            for (int r = 0; r < repeats; r++)
            {
                simulation.Solver.StepPhases.Reset();
                for (int i = 0; i < SolverStepsPerRepeat; i++) simulation.Solver.Step(step, state);

                for (int p = 0; p < best.Length; p++)
                {
                    double ms = simulation.Solver.StepPhases.MillisecondsOf(p) / SolverStepsPerRepeat;
                    if (ms < best[p]) best[p] = ms;
                    if (ms > worst[p]) worst[p] = ms;

                    long v = simulation.Solver.StepPhases.Visits[p] / SolverStepsPerRepeat;
                    if (r > 0 && v != visits[p])
                    {
                        throw new InvalidOperationException(
                            "the " + ThermalSolver.StepPhaseProfile.Names[p] + " stage charged "
                            + v + " visits this repeat and " + visits[p] + " last, so these are"
                            + " readings of two different walks");
                    }

                    visits[p] = v;
                    slices[p] = simulation.Solver.StepPhases.Slices[p] / SolverStepsPerRepeat;
                }
            }

            simulation.Solver.ProfileStepPhases = false;

            List<Row> rows = new List<Row>();
            for (int p = 0; p < best.Length; p++)
            {
                Row row = new Row();
                row.Stage = ThermalSolver.StepPhaseProfile.Names[p];
                row.Blocks = simulation.Solver.Nodes.Count;
                row.BestMs = best[p];
                row.WorstMs = worst[p];
                row.Work = visits[p];
                row.WorkUnit = "visits";
                row.Note = slices[p] + " slices";
                rows.Add(row);
            }

            return rows;
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
                long allocated = r == repeats - 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                for (int i = 0; i < SolverStepsPerRepeat; i++) simulation.Solver.Step(step, state);
                watch.Stop();
                if (r == repeats - 1) row.AllocatedBytes = (Allocated() - allocated) / SolverStepsPerRepeat;
                Take(row, watch.Elapsed.TotalMilliseconds / SolverStepsPerRepeat);
                Work(row, simulation.Work.SolverSubsteps - before, r);
            }

            return row;
        }

        public static string Table(IList<Row> rows)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("  stage        blocks       best ms      worst ms   spread          work  unit              ns/unit      alloc KB  note");
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-10} {1,9:n0}  {2,12:n3}  {3,12:n3}  {4,6:n0}%  {5,12:n0}  {6,-16}  {7,8:n1}  {8,12:n0}  {9}",
                    row.Stage, row.Blocks, row.BestMs, row.WorstMs, row.SpreadPercent,
                    row.Work, row.WorkUnit, row.NsPerWork, row.AllocatedBytes / 1024, row.Note));
            }
            return text.ToString();
        }

        public static string Csv(IList<Row> rows)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("stage,blocks,best_ms,worst_ms,spread_percent,work,work_unit,ns_per_unit,allocated_bytes");
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
                    row.NsPerWork.ToString("r", CultureInfo.InvariantCulture),
                    row.AllocatedBytes.ToString(CultureInfo.InvariantCulture)));
            }
            return text.ToString();
        }
    }
}
