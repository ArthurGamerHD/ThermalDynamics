using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
    /// the sum of five stages and carries all five lots of noise, which is six to twelve per cent
    /// between repeats of one tree — wide enough to hide a saving of a fifth
    /// (performance.md, Iteration 10) or to read one into nothing (Iteration 6). Timing the stage
    /// alone, best of N, resolves about a per cent, and the counter is what says two readings are
    /// the same work at two prices rather than two different walks (`M4`, `M5`, `P6`).
    /// See performance.md, How a pass is run.
    /// </para>
    /// </summary>
    public static class StageLab
    {
        /// <summary>
        /// Called with every repeat's time as it is taken, when a caller wants the shape of a
        /// reading rather than its best and worst.
        ///
        /// <para>
        /// **A best-of-N hides the shape of N.** The link stage was found to be *bimodal* at
        /// 126,731 blocks — two windows measuring the same two commits disagreed about which was
        /// faster, one reading 16.9 ms for the code the other read at 32.8 — and best, worst and
        /// spread cannot tell a run that warmed into a fast state from one that never did.
        /// See performance.md, Pass 8, Iteration 1.
        /// </para>
        /// </summary>
        public static Action<string> TraceRepeat;

        /// <summary>
        /// **Fewest** timed repeats per stage. The fastest is reported, and repeats continue past
        /// this until the fastest has been reproduced — see <see cref="ConfirmingRepeats"/>.
        ///
        /// <para>
        /// **Fifteen was not enough, and that was measured rather than assumed.** Three runs of the
        /// same binary on the same hull, best of fifteen: the surface rebuild read 7.4, 9.6 and
        /// 6.5 ms — a spread of 48 % — the room pass 28 %, and the link build **67 %**. At a
        /// hundred and fifty repeats the same three readings come within 2 %, 4 % and 9 %. The
        /// per-repeat distribution has a long lower tail and fifteen samples reach an unpredictable
        /// depth into it; the statistic was right and the sample size was not.
        /// </para>
        ///
        /// <para>
        /// The solver stage was the exception, at 3.5 % on fifteen — because each of its repeats is
        /// twenty steps, so it was already taking three hundred samples. That is the shape of the
        /// fix: sample until the answer stops moving. See performance.md, Pass 8, Iteration 3.
        /// </para>
        ///
        /// <para>
        /// **Agreement alone is not enough, which cost an iteration to find out.** A rule that
        /// stopped when five readings landed within two per cent of the best settled a *plateau*
        /// rather than a minimum: on a contended machine a run's first readings cluster tightly at
        /// a slow value, five of them agree, and the fast mode is never sampled — two legs of one
        /// pairing read 75 ms and 154 ms for the same stage with the control flat. The floor is a
        /// hundred repeats now, so a stage has to look before it is allowed to be satisfied.
        /// </para>
        /// </summary>
        public static int Repeats = 100;

        /// <summary>
        /// How many repeats must land within <see cref="ConfirmingBand"/> of the fastest before a
        /// stage is called settled.
        ///
        /// <para>
        /// **A fastest reading is trustworthy when it has been reproduced, not when it is old.**
        /// Two rules were tried on elapsed evidence — a fixed count of repeats since the best, and
        /// a count scaled to how long the best took to find — and each settled some stages and not
        /// others, because how rare a stage's fast repeats are differs by stage and by what else
        /// the machine is doing. Counting how many repeats *agree* with the best asks the question
        /// directly: a minimum seen once is a fluke, and a minimum seen five times is the cost.
        /// See performance.md, Pass 8, Iteration 3.
        /// </para>
        /// </summary>
        public static int ConfirmingRepeats = 5;

        /// <summary>How close a repeat must come to the fastest to confirm it. Two per cent.</summary>
        public static double ConfirmingBand = 1.02;

        /// <summary>A stage that will not settle stops here rather than running for ever.</summary>
        public static int MaxRepeats = 400;

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

            /// <summary>Repeats taken, how many since the fastest was seen, and how many agreed with it.</summary>
            public int Repeats;
            public int RepeatsSinceBest;
            public int ConfirmedBest;

            /// <summary>
            /// Why the repeats stopped, in one word, for the table and the CSV.
            ///
            /// <para>
            /// **A row that hit the cap is a row whose best was never confirmed, and its number
            /// should not be compared with anything.** The lab has counted the confirmations since
            /// pass 8 and a test has asserted that every row is either confirmed or capped — but
            /// neither the table a person reads nor the CSV a comparison is built from carried the
            /// answer, so the one reader who needed it could not see it. That is the shape of every
            /// finding on this page: the instrument knew and did not say (`P2`, `E9`).
            /// </para>
            ///
            /// <para>
            /// `confirmed` — the fastest reading was reproduced <see cref="ConfirmingRepeats"/>
            /// times within <see cref="ConfirmingBand"/>. `capped` — it was not, and
            /// <see cref="MaxRepeats"/> ended the row. `fixed` — the stage does not use the rule at
            /// all, which is <see cref="StepPhases"/>, whose repeat is twenty steps.
            /// See performance.md, Pass 9, Iteration 2.
            /// </para>
            /// </summary>
            public string Stop = Fixed;

            /// <summary>
            /// Every repeat's milliseconds, in the order they were taken.
            ///
            /// <para>
            /// **The best of a sample is a statistic, and a statistic that has never been compared
            /// with the alternatives is a choice nobody made.** Fastest-of-N was chosen in pass 1
            /// because timing noise is one-sided, which is true and does not say that the minimum
            /// of a sample this size is *reproducible* — and iteration 2 found five stages of eight
            /// that could not reproduce theirs in four hundred repeats. Answering that needs the
            /// repeats themselves rather than a summary of them, in enough runs to see between-run
            /// spread, which is what this carries and `bench samplestats` reads.
            /// See performance.md, Pass 9, Iteration 3.
            /// </para>
            /// </summary>
            public readonly List<double> Samples = new List<double>();

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

            /// <summary>The three values <see cref="Stop"/> takes, so a reader and a test spell them the same way.</summary>
            public const string Confirmed = "confirmed";
            public const string Capped = "capped";
            public const string Fixed = "fixed";

            /// <summary>
            /// The middle repeat, which is the other half of the answer.
            ///
            /// <para>
            /// **The fastest repeat is not reproducible for every stage, and which stages it fails
            /// for cannot be guessed.** Four runs of one binary at 126,731 blocks, four hundred
            /// repeats each, every candidate summary compared: the minimum reproduces to 4.5 % on
            /// the room pass and to 97 % on `register`; the *median* reproduces to 0.9 % on
            /// exposure and to 4.4 % on the solver, where the minimum manages 30 % and 16 %. No
            /// summary is best for more than three of the eight, and for `links` and `register`
            /// nothing tried reproduces at all.
            /// </para>
            ///
            /// <para>
            /// What separates them is whether a stage has a **fast mode it reaches rarely**. Where
            /// the minimum sits far below the bulk — exposure's is 45 % of its median — best-of-N
            /// samples that mode to a depth that is an independent draw per run, which is precisely
            /// pass 8's finding and is not fixed by taking more repeats: four hundred did not fix
            /// it. Where the distribution is one mode with additive noise, as the room pass's is at
            /// 85 %, the minimum is the right statistic and reproduces.
            /// </para>
            ///
            /// <para>
            /// So both are reported and neither is privileged. <see cref="FastModeShare"/> is the
            /// ratio that says which kind of stage a row is, and a pairing is readable only where
            /// the two legs agree on **both** figures. See performance.md, Pass 9, Iteration 5.
            /// </para>
            /// </summary>
            public double MedianMs;

            /// <summary>
            /// The fastest repeat as a share of the middle one — how far below its own bulk a
            /// stage's best reading sits, and so how much of a lottery <see cref="BestMs"/> is.
            /// Near 1 the stage has one mode and its minimum is sound; near 0.45 it has a fast mode
            /// reached a few times in four hundred and its minimum is a draw. See
            /// <see cref="MedianMs"/>.
            /// </summary>
            public double FastModeShare
            {
                get { return MedianMs <= 0d ? 0d : BestMs / MedianMs; }
            }

            /// <summary>Nanoseconds per unit of work, which is the figure that transfers between sizes.</summary>
            public double NsPerWork
            {
                get { return Work <= 0 ? 0d : BestMs * 1e6d / Work; }
            }
        }

        public static readonly string[] Stages = { "place", "register", "surfaces", "links", "rooms", "exposure", "roomair", "solver" };

        /// <summary>
        /// Stages that are not part of a grid's life in order and so are not in <see cref="Stages"/>:
        /// they answer one question and are asked for by name. Named here rather than left to a
        /// reader of the switch, so `bench stages --stages` has somewhere to point.
        /// </summary>
        public static readonly string[] ExtraStages = { "syncdirty", "syncclean", "shapenormals" };

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
                Row row = Measure(stages[i], builder);
                Summarise(row);
                rows.Add(row);
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
                case "shapenormals": return ShapeNormals(builder);
                case "syncdirty": return NodeSync(builder, true);
                case "syncclean": return NodeSync(builder, false);
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
            if (TraceRepeat != null) TraceRepeat(row.Stage + " " + ms.ToString("n3", CultureInfo.InvariantCulture));
            row.Samples.Add(ms);

            if (ms < row.BestMs)
            {
                // A new best restarts the count: what confirmed the old one says nothing about it.
                row.BestMs = ms;
                row.RepeatsSinceBest = 0;
                row.ConfirmedBest = 1;
            }
            else
            {
                row.RepeatsSinceBest++;
                if (ms <= row.BestMs * ConfirmingBand) row.ConfirmedBest++;
            }

            row.Repeats++;
            if (ms > row.WorstMs) row.WorstMs = ms;
        }

        /// <summary>
        /// Whether a stage has taken enough repeats: at least <see cref="Repeats"/> of them, and
        /// then <see cref="ConfirmingRepeats"/> of them within <see cref="ConfirmingBand"/> of it,
        /// or <see cref="MaxRepeats"/> in all. <see cref="Row.Stop"/> records which.
        /// </summary>
        private static bool Settled(Row row)
        {
            if (row == null) return false;
            if (row.Repeats < Repeats) return false;

            // Recorded where it is decided rather than inferred afterwards: a reader deriving the
            // reason from the counts would have to know the dials the run used, and the dials are
            // static and settable. See <see cref="Row.Stop"/>.
            if (row.ConfirmedBest >= ConfirmingRepeats)
            {
                row.Stop = Row.Confirmed;
                return true;
            }

            if (row.Repeats >= MaxRepeats)
            {
                row.Stop = Row.Capped;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Fills in <see cref="Row.MedianMs"/> from the repeats, once a row has stopped taking them.
        /// By nearest rank on the sorted samples, so the figure reported is a reading the instrument
        /// actually took rather than the average of two it did not.
        /// </summary>
        private static void Summarise(Row row)
        {
            if (row == null || row.Samples.Count == 0) return;

            double[] sorted = row.Samples.ToArray();
            Array.Sort(sorted);
            row.MedianMs = sorted[sorted.Length / 2];
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

            for (int r = 0; !Settled(row); r++)
            {
                GridModel grid = new GridModel(builder.Grid.GridSize);

                long allocated = r == 1 ? Allocated() : 0;
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
                if (r == 1) row.AllocatedBytes = Allocated() - allocated;
                Take(row, watch.Elapsed.TotalMilliseconds);
                Work(row, grid.BlockCount, r);
            }

            return row;
        }

        /// <summary>Registering every placed block with a fresh simulation's solver: a node each, and its links pending.</summary>
        private static Row Register(GridBuilder builder)
        {
            Row row = null;

            for (int r = 0; !Settled(row); r++)
            {
                ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings().Derive(), builder.Grid);

                long allocated = r == 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                for (int i = 0; i < builder.Placed.Count; i++)
                {
                    simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
                }
                watch.Stop();
                if (r == 1) row.AllocatedBytes = Allocated() - allocated;

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
            for (int r = 0; !Settled(row); r++)
            {
                long allocated = r == 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                simulation.Surfaces.Rebuild(simulation.Grid);
                watch.Stop();
                if (r == 1) row.AllocatedBytes = Allocated() - allocated;
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
            for (int r = 0; !Settled(row); r++)
            {
                long allocated = r == 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                simulation.Solver.RebuildLinks();
                watch.Stop();
                if (r == 1) row.AllocatedBytes = Allocated() - allocated;
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
            for (int r = 0; !Settled(row); r++)
            {
                long before = simulation.Work.RoomCellsVisited;
                long allocated = r == 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                simulation.Rooms.RequestRestart(simulation.Grid);
                if (!simulation.Rooms.RunToCompletion())
                {
                    throw new InvalidOperationException("the room pass did not finish, so there is no stage to time");
                }
                watch.Stop();
                if (r == 1) row.AllocatedBytes = Allocated() - allocated;
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
            for (int r = 0; !Settled(row); r++)
            {
                long before = simulation.Work.RoomAirFaceProbes;
                long hitsBefore = simulation.Work.RoomAirFaceHits;
                long allocated = r == 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                simulation.Solver.RebuildRoomAir(map);
                watch.Stop();
                if (r == 1) row.AllocatedBytes = Allocated() - allocated;
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
            for (int r = 0; !Settled(row); r++)
            {
                long before = simulation.Work.ExposureNodeVisits;
                long allocated = r == 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                simulation.Solver.RefreshExposure(simulation.Rooms.Map);
                watch.Stop();
                if (r == 1) row.AllocatedBytes = Allocated() - allocated;
                Take(row, watch.Elapsed.TotalMilliseconds);
                Work(row, simulation.Work.ExposureNodeVisits - before, r);
            }

            return row;
        }

        /// <summary>
        /// What reconstructing every node's effective surface normal costs.
        ///
        /// <para>
        /// **Not one of the stages of a grid's life**, because it runs only where `EnableShapeDrag`
        /// is on and only when the layout changes — but it is a full walk of the nodes with a
        /// neighbourhood read each, so it is the pass that decides whether the shape term is
        /// affordable (backlog.md `K22`). `exposure` is the control worth reading it against: the
        /// same walk over the same nodes, doing a different thing at each.
        /// </para>
        /// </summary>
        private static Row ShapeNormals(GridBuilder builder)
        {
            ThermalSimulation simulation = Registered(builder);
            simulation.Surfaces.Rebuild(simulation.Grid);

            Row row = NewRow("shapenormals", simulation, "nodes");
            int nodes = simulation.Solver.Nodes.Count;

            for (int r = 0; !Settled(row); r++)
            {
                long allocated = r == 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                simulation.Solver.RebuildShapeNormals();
                watch.Stop();
                if (r == 1) row.AllocatedBytes = Allocated() - allocated;
                Take(row, watch.Elapsed.TotalMilliseconds);
                Work(row, nodes, r);
            }

            return row;
        }

        /// <summary>
        /// What mirroring the grid's node state into the solver's flat arrays costs, with every
        /// node asking to be mirrored and with none of them.
        ///
        /// <para>
        /// **This is the price of a dirty flag, and it is the whole value of Pass 9's exposure
        /// skip.** A refresh over an unchanged hull used to mark all 126,731 nodes `StateDirty`, so
        /// the next step rewrote every mirrored row; it now marks none. The difference is inside a
        /// fifteen-millisecond step and cannot be read off it, which is why this is its own stage
        /// and why <c>SyncNodeState</c> is internal.
        /// </para>
        ///
        /// <para>
        /// Marking the nodes is done outside the stopwatch, so what is timed is the sync and not
        /// the marking. The two rows share a simulation and differ only in that flag.
        /// </para>
        /// </summary>
        private static Row NodeSync(GridBuilder builder, bool dirty)
        {
            ThermalSimulation simulation = builder.BuildSimulation(Hulls.Uncapped(), 293.15f);
            LoadBenchmarks.SeedSpread(simulation);
            Census.DriveCensus(simulation);

            // **Stepped first, and not for warm-up.** `SyncNodeState` writes rows of buffers that
            // `PrepareStepState` allocates immediately before calling it, and calling it on a
            // solver that has never stepped indexes past the end of arrays that are not there yet.
            // A step is the only public thing that sets that up. It is also what leaves the heap
            // and the JIT in the state the timed repeats want.
            EnvironmentState state = EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, Worlds.Flight(1f, 300f));
            for (int i = 0; i < 3; i++) simulation.Solver.Step(simulation.Settings.StepSeconds, state);

            IList<ThermalNode> nodes = simulation.Solver.Nodes;

            Row row = NewRow(dirty ? "syncdirty" : "syncclean", simulation, "nodes");
            for (int r = 0; !Settled(row); r++)
            {
                if (dirty)
                {
                    for (int i = 0; i < nodes.Count; i++) nodes[i].StateDirty = true;
                }

                long allocated = r == 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                simulation.Solver.SyncNodeState();
                watch.Stop();
                if (r == 1) row.AllocatedBytes = Allocated() - allocated;
                Take(row, watch.Elapsed.TotalMilliseconds);
                Work(row, nodes.Count, r);
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

            // Every repeat kept here too, so a phase carries the same evidence a stage does: its
            // median as well as its best, and the ratio that says whether the best is a rare draw.
            // See Row.MedianMs.
            List<double>[] samples = new List<double>[ThermalSolver.StepPhaseProfile.PhaseCount];
            for (int p = 0; p < samples.Length; p++) samples[p] = new List<double>();
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
                    samples[p].Add(ms);

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

                // A fixed count, not the settle rule: a repeat here is twenty steps, so this path
                // was already taking `20 * Repeats` samples when the stage lab was taking fifteen.
                // Reported as `fixed` rather than left to read as a confirmation (`E9`).
                row.Repeats = repeats;
                row.Stop = Row.Fixed;
                row.Note = slices[p] + " slices";
                row.Samples.AddRange(samples[p]);
                Summarise(row);
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
            for (int r = 0; !Settled(row); r++)
            {
                long before = simulation.Work.SolverSubsteps;
                long allocated = r == 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                for (int i = 0; i < SolverStepsPerRepeat; i++) simulation.Solver.Step(step, state);
                watch.Stop();
                if (r == 1) row.AllocatedBytes = (Allocated() - allocated) / SolverStepsPerRepeat;
                Take(row, watch.Elapsed.TotalMilliseconds / SolverStepsPerRepeat);
                Work(row, simulation.Work.SolverSubsteps - before, r);
            }

            return row;
        }

        public static string Table(IList<Row> rows)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("  stage        blocks       best ms    median ms   best/med      worst ms   spread  repeats  stopped          work  unit              ns/unit      alloc KB  note");
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-10} {1,9:n0}  {2,12:n3}  {3,11:n3}  {4,8:n2}  {5,12:n3}  {6,6:n0}%  {7,7:n0}  {8,-9}  {9,12:n0}  {10,-16}  {11,8:n1}  {12,12:n0}  {13}",
                    row.Stage, row.Blocks, row.BestMs, row.MedianMs, row.FastModeShare,
                    row.WorstMs, row.SpreadPercent,
                    row.Repeats, row.Stop,
                    row.Work, row.WorkUnit, row.NsPerWork, row.AllocatedBytes / 1024, row.Note));
            }
            return text.ToString();
        }

        /// <summary>
        /// When this process started, in UTC, to the second — the stamp every artefact this lab
        /// writes carries.
        ///
        /// <para>
        /// **A stage figure is only comparable with one taken in the same window.** Pass 9,
        /// Iteration 6 measured the exposure stage at a 4.75 ms median across twelve processes of
        /// one held window, agreeing to 1.3 %, against the 11.40 ms Iteration 5 recorded for
        /// bit-identical code — and eliminated the stage order, the build configuration, the
        /// instrument's own changes and the source in turn. Whatever moved it moved the *whole*
        /// distribution, so no summary of the repeats can detect it and nothing in the artefact
        /// said which session it came from. This is what says so.
        /// </para>
        ///
        /// <para>
        /// Taken once per process rather than per row, because the question a reader asks of it is
        /// *were these two figures taken together*, and rows of one run were.
        /// </para>
        /// </summary>
        public static readonly string TakenUtc =
            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        /// <summary>The machine, for the same reason as <see cref="TakenUtc"/>: figures from two of them are not one measurement.</summary>
        public static readonly string Host = Environment.MachineName;

        public static string Csv(IList<Row> rows)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("stage,blocks,best_ms,median_ms,fast_mode_share,worst_ms,spread_percent,repeats,confirmed_best,stopped,work,work_unit,ns_per_unit,allocated_bytes,taken_utc,host");
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                text.AppendLine(string.Join(",",
                    row.Stage,
                    row.Blocks.ToString(CultureInfo.InvariantCulture),
                    row.BestMs.ToString("r", CultureInfo.InvariantCulture),
                    row.MedianMs.ToString("r", CultureInfo.InvariantCulture),
                    row.FastModeShare.ToString("r", CultureInfo.InvariantCulture),
                    row.WorstMs.ToString("r", CultureInfo.InvariantCulture),
                    row.SpreadPercent.ToString("r", CultureInfo.InvariantCulture),
                    row.Repeats.ToString(CultureInfo.InvariantCulture),
                    row.ConfirmedBest.ToString(CultureInfo.InvariantCulture),
                    row.Stop,
                    row.Work.ToString(CultureInfo.InvariantCulture),
                    row.WorkUnit,
                    row.NsPerWork.ToString("r", CultureInfo.InvariantCulture),
                    row.AllocatedBytes.ToString(CultureInfo.InvariantCulture),
                    TakenUtc,
                    Host));
            }
            return text.ToString();
        }

        /// <summary>
        /// Reads back what <see cref="Csv"/> wrote, so a caller that ran a stage in a process of its
        /// own can put the row in its own table.
        ///
        /// <para>
        /// **Only the columns a comparison uses**: the stage, its blocks, both statistics, the work
        /// and the allocation. The stopping reason comes back too, because a capped row is not
        /// comparable with anything and a reader who cannot see that is the reader `E9` is about.
        /// A row that does not parse throws rather than being skipped, since a table quietly short
        /// of a row is a table over a different set of stages (`E4`).
        /// </para>
        /// </summary>
        public static List<Row> ReadCsv(string path)
        {
            List<Row> rows = new List<Row>();
            string[] lines = File.ReadAllLines(path);

            if (lines.Length < 2)
            {
                throw new InvalidOperationException(path + " holds no rows, so the stage it was"
                    + " supposed to time reported nothing");
            }

            string[] header = lines[0].Split(',');

            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length == 0) continue;

                string[] parts = lines[i].Split(',');
                Row row = new Row();
                row.Stage = Field(header, parts, "stage", path);
                row.Blocks = int.Parse(Field(header, parts, "blocks", path), CultureInfo.InvariantCulture);
                row.BestMs = double.Parse(Field(header, parts, "best_ms", path), CultureInfo.InvariantCulture);
                row.MedianMs = double.Parse(Field(header, parts, "median_ms", path), CultureInfo.InvariantCulture);
                row.WorstMs = double.Parse(Field(header, parts, "worst_ms", path), CultureInfo.InvariantCulture);
                row.Repeats = int.Parse(Field(header, parts, "repeats", path), CultureInfo.InvariantCulture);
                row.ConfirmedBest = int.Parse(Field(header, parts, "confirmed_best", path), CultureInfo.InvariantCulture);
                row.Stop = Field(header, parts, "stopped", path);
                row.Work = long.Parse(Field(header, parts, "work", path), CultureInfo.InvariantCulture);
                row.WorkUnit = Field(header, parts, "work_unit", path);
                row.AllocatedBytes = long.Parse(Field(header, parts, "allocated_bytes", path), CultureInfo.InvariantCulture);
                rows.Add(row);
            }

            return rows;
        }

        /// <summary>One named column of one row, by header rather than by position, so a column added in the middle cannot silently shift the rest.</summary>
        private static string Field(string[] header, string[] parts, string column, string path)
        {
            int index = Array.IndexOf(header, column);
            if (index < 0 || index >= parts.Length)
            {
                throw new InvalidOperationException(path + " has no \"" + column + "\" column, so it"
                    + " was not written by this lab");
            }
            return parts[index];
        }

        /// <summary>
        /// Every repeat of every stage, one row each, in the order they were taken. The artefact
        /// `bench samplestats` reads; see <see cref="Row.Samples"/> for why it exists.
        /// </summary>
        public static string SamplesCsv(IList<Row> rows)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("stage,blocks,repeat,ms,taken_utc");
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                for (int r = 0; r < row.Samples.Count; r++)
                {
                    text.AppendLine(string.Join(",",
                        row.Stage,
                        row.Blocks.ToString(CultureInfo.InvariantCulture),
                        r.ToString(CultureInfo.InvariantCulture),
                        row.Samples[r].ToString("r", CultureInfo.InvariantCulture),
                        TakenUtc));
                }
            }
            return text.ToString();
        }
    }
}
