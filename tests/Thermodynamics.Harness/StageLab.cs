using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    public static class StageLab
    {
        public static Action<string> TraceRepeat;

        public static int Repeats = 100;

        public static int ConfirmingRepeats = 5;

        public static double ConfirmingBand = 1.02;

        public static int MaxRepeats = 400;

        public const int SolverStepsPerRepeat = 20;

        public class Row
        {
            public string Stage;
            public int Blocks;

            public long AllocatedBytes;

            public int Repeats;
            public int RepeatsSinceBest;
            public int ConfirmedBest;

            public string Stop = Fixed;

/// <summary>List operation.</summary>
            public readonly List<double> Samples = new List<double>();

            public double BestMs;
            public double WorstMs;

            public long Work;
            public string WorkUnit;

            public double SpreadPercent
            {
                get { return BestMs <= 0d ? 0d : 100d * (WorstMs - BestMs) / BestMs; }
            }

            public string Note = "";

            public const string Confirmed = "confirmed";
            public const string Capped = "capped";
            public const string Fixed = "fixed";

            public double MedianMs;

            public double FastModeShare
            {
                get { return MedianMs <= 0d ? 0d : BestMs / MedianMs; }
            }

            public double NsPerWork
            {
                get { return Work <= 0 ? 0d : BestMs * 1e6d / Work; }
            }
        }

        public static readonly string[] Stages = { "place", "register", "surfaces", "links", "rooms", "exposure", "roomair", "solver" };

        public static readonly string[] ExtraStages = { "syncdirty", "syncclean", "shapenormals" };

/// <summary>Run operation.</summary>
        public static List<Row> Run(string shape, int blocks, IList<string> stages, Action<string> log = null)
        {
/// <summary>List operation.</summary>
            List<Row> rows = new List<Row>();

            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build(shape, blocks));

            for (int i = 0; i < stages.Count; i++)
            {
                if (log != null) log(stages[i] + ", " + blocks.ToString("n0") + " blocks");

                Settle();
/// <summary>Measure operation.</summary>
                Row row = Measure(stages[i], builder);
                Summarise(row);
                rows.Add(row);
            }

            return rows;
        }

/// <summary>Registers the API and message handler.</summary>
        internal static ThermalSimulation Registered(GridBuilder builder)
        {
/// <summary>ThermalSimulation operation.</summary>
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings().Derive(), builder.Grid);
            for (int i = 0; i < builder.Placed.Count; i++)
            {
                simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            }
            return simulation;
        }

/// <summary>Measure operation.</summary>
        internal static Row Measure(string stage, GridBuilder builder)
        {
            switch (stage)
            {
/// <summary>Place operation.</summary>
                case "place": return Place(builder);
/// <summary>Registers and opens communication.</summary>
                case "register": return Register(builder);
/// <summary>Surfaces operation.</summary>
                case "surfaces": return Surfaces(builder);
/// <summary>Links operation.</summary>
                case "links": return Links(builder);
/// <summary>Rooms operation.</summary>
                case "rooms": return Rooms(builder);
/// <summary>Exposure operation.</summary>
                case "exposure": return Exposure(builder);
/// <summary>ShapeNormals operation.</summary>
                case "shapenormals": return ShapeNormals(builder);
/// <summary>NodeSync operation.</summary>
                case "syncdirty": return NodeSync(builder, true);
/// <summary>NodeSync operation.</summary>
                case "syncclean": return NodeSync(builder, false);
/// <summary>RoomAir operation.</summary>
                case "roomair": return RoomAir(builder);
/// <summary>Solver operation.</summary>
                case "solver": return Solver(builder);
/// <summary>ArgumentException operation.</summary>
                default: throw new ArgumentException("Unknown stage: " + stage);
            }
        }

/// <summary>NewRow operation.</summary>
        private static Row NewRow(string stage, ThermalSimulation simulation, string unit)
        {
/// <summary>Row operation.</summary>
            Row row = new Row();
            row.Stage = stage;
            row.Blocks = simulation.Solver.Nodes.Count;
            row.BestMs = double.MaxValue;
            row.WorkUnit = unit;
            return row;
        }

/// <summary>Sets the tle.</summary>
        internal static void Settle()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

/// <summary>Allocated operation.</summary>
        internal static long Allocated()
        {
            return GC.GetAllocatedBytesForCurrentThread();
        }

/// <summary>Take operation.</summary>
        internal static void Take(Row row, double ms)
        {
            if (TraceRepeat != null) TraceRepeat(row.Stage + " " + ms.ToString("n3", CultureInfo.InvariantCulture));
            row.Samples.Add(ms);

            if (ms < row.BestMs)
            {
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

/// <summary>Sets the tled.</summary>
        internal static bool Settled(Row row)
        {
            if (row == null) return false;
            if (row.Repeats < Repeats) return false;

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

/// <summary>Summarise operation.</summary>
        internal static void Summarise(Row row)
        {
            if (row == null || row.Samples.Count == 0) return;

            double[] sorted = row.Samples.ToArray();
            Array.Sort(sorted);
            row.MedianMs = sorted[sorted.Length / 2];
        }

/// <summary>Work operation.</summary>
        internal static void Work(Row row, long work, int repeat)
        {
            if (repeat == 0) { row.Work = work; return; }
            if (row.Work != work)
            {
                throw new InvalidOperationException(row.Stage + " did " + work + " " + row.WorkUnit
                    + " on repeat " + repeat + " and " + row.Work + " on the first, so its readings are of different walks");
            }
        }

/// <summary>Place operation.</summary>
        private static Row Place(GridBuilder builder)
        {
            IList<BlockInstance> dealt = builder.Placed;
            Row row = null;

            for (int r = 0; !Settled(row); r++)
            {
/// <summary>GridModel operation.</summary>
                GridModel grid = new GridModel(builder.Grid.GridSize);

/// <summary>Allocated operation.</summary>
                long allocated = r == 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                for (int i = 0; i < dealt.Count; i++)
                {
                    grid.Add(new BlockInstance(dealt[i].Model, dealt[i].Min, dealt[i].Orientation));
                }
                watch.Stop();

                if (row == null)
                {
/// <summary>Row operation.</summary>
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

/// <summary>Registers the API and message handler.</summary>
        private static Row Register(GridBuilder builder)
        {
            Row row = null;

            for (int r = 0; !Settled(row); r++)
            {
/// <summary>ThermalSimulation operation.</summary>
                ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings().Derive(), builder.Grid);

/// <summary>Allocated operation.</summary>
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

/// <summary>Surfaces operation.</summary>
        private static Row Surfaces(GridBuilder builder)
        {
/// <summary>Registers and opens communication.</summary>
            ThermalSimulation simulation = Registered(builder);
/// <summary>NewRow operation.</summary>
            Row row = NewRow("surfaces", simulation, "cells");
            for (int r = 0; !Settled(row); r++)
            {
/// <summary>Allocated operation.</summary>
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

/// <summary>Links operation.</summary>
        private static Row Links(GridBuilder builder)
        {
/// <summary>Registers and opens communication.</summary>
            ThermalSimulation simulation = Registered(builder);
            simulation.Surfaces.Rebuild(simulation.Grid);
/// <summary>NewRow operation.</summary>
            Row row = NewRow("links", simulation, "links");
            for (int r = 0; !Settled(row); r++)
            {
/// <summary>Allocated operation.</summary>
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

/// <summary>Rooms operation.</summary>
        private static Row Rooms(GridBuilder builder)
        {
/// <summary>Registers and opens communication.</summary>
            ThermalSimulation simulation = Registered(builder);
            simulation.Surfaces.Rebuild(simulation.Grid);
/// <summary>NewRow operation.</summary>
            Row row = NewRow("rooms", simulation, "cells visited");

            const int WarmRepeat = 3;
            for (int r = 0; r <= WarmRepeat || !Settled(row); r++)
            {
                long before = simulation.Work.RoomCellsVisited;
/// <summary>Allocated operation.</summary>
                long allocated = r == WarmRepeat ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                simulation.Rooms.RequestRestart(simulation.Grid);
                if (!simulation.Rooms.RunToCompletion())
                {
                    throw new InvalidOperationException("the room pass did not finish, so there is no stage to time");
                }
                watch.Stop();
                if (r == WarmRepeat) row.AllocatedBytes = Allocated() - allocated;
                Take(row, watch.Elapsed.TotalMilliseconds);
                Work(row, simulation.Work.RoomCellsVisited - before, r);
            }

            return row;
        }

/// <summary>RoomAir operation.</summary>
        private static Row RoomAir(GridBuilder builder)
        {
/// <summary>Registers and opens communication.</summary>
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

/// <summary>NewRow operation.</summary>
            Row row = NewRow("roomair", simulation, "face probes");
            for (int r = 0; !Settled(row); r++)
            {
                long before = simulation.Work.RoomAirFaceProbes;
                long hitsBefore = simulation.Work.RoomAirFaceHits;
/// <summary>Allocated operation.</summary>
                long allocated = r == 1 ? Allocated() : 0;
                Stopwatch watch = Stopwatch.StartNew();
                simulation.Solver.RebuildRoomAir(map);
                watch.Stop();
                if (r == 1) row.AllocatedBytes = Allocated() - allocated;
                Take(row, watch.Elapsed.TotalMilliseconds);
                Work(row, simulation.Work.RoomAirFaceProbes - before, r);

                if (r == 0) row.Note = (simulation.Work.RoomAirFaceHits - hitsBefore).ToString("n0") + " hit";
            }

            return row;
        }

/// <summary>Exposure operation.</summary>
        private static Row Exposure(GridBuilder builder)
        {
/// <summary>Registers and opens communication.</summary>
            ThermalSimulation simulation = Registered(builder);
            simulation.Surfaces.Rebuild(simulation.Grid);
            simulation.Rooms.RequestRestart(simulation.Grid);
            simulation.Rooms.RunToCompletion();
/// <summary>NewRow operation.</summary>
            Row row = NewRow("exposure", simulation, "nodes");
            for (int r = 0; !Settled(row); r++)
            {
                long before = simulation.Work.ExposureNodeVisits;
/// <summary>Allocated operation.</summary>
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

/// <summary>ShapeNormals operation.</summary>
        private static Row ShapeNormals(GridBuilder builder)
        {
/// <summary>Registers and opens communication.</summary>
            ThermalSimulation simulation = Registered(builder);
            simulation.Surfaces.Rebuild(simulation.Grid);

/// <summary>NewRow operation.</summary>
            Row row = NewRow("shapenormals", simulation, "nodes");
            int nodes = simulation.Solver.Nodes.Count;

            for (int r = 0; !Settled(row); r++)
            {
/// <summary>Allocated operation.</summary>
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

/// <summary>NodeSync operation.</summary>
        private static Row NodeSync(GridBuilder builder, bool dirty)
        {
            ThermalSimulation simulation = builder.BuildSimulation(Hulls.Uncapped(), 293.15f);
            LoadBenchmarks.SeedSpread(simulation);
            Census.DriveCensus(simulation);

            EnvironmentState state = EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, Worlds.Flight(1f, 300f));
            for (int i = 0; i < 3; i++) simulation.Solver.Step(simulation.Settings.StepSeconds, state);

            IList<ThermalNode> nodes = simulation.Solver.Nodes;

/// <summary>NewRow operation.</summary>
            Row row = NewRow(dirty ? "syncdirty" : "syncclean", simulation, "nodes");
            for (int r = 0; !Settled(row); r++)
            {
                if (dirty)
                {
                    for (int i = 0; i < nodes.Count; i++) nodes[i].StateDirty = true;
                }

/// <summary>Allocated operation.</summary>
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

/// <summary>StepPhases operation.</summary>
        public static List<Row> StepPhases(string shape, int blocks, Action<string> log = null)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build(shape, blocks));

            ThermalSimulation simulation = builder.BuildSimulation(Hulls.Uncapped(), 293.15f);
            LoadBenchmarks.SeedSpread(simulation);
            Census.DriveCensus(simulation);

            EnvironmentState state = EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, Worlds.Flight(1f, 300f));
            float step = simulation.Settings.StepSeconds;

            if (log != null) log("step phases, " + blocks.ToString("n0") + " blocks");

            for (int i = 0; i < SolverStepsPerRepeat; i++) simulation.Solver.Step(step, state);

            Settle();
            simulation.Solver.ProfileStepPhases = true;

            int repeats = Math.Max(1, Repeats);
            double[] best = new double[ThermalSolver.StepPhaseProfile.PhaseCount];
            double[] worst = new double[ThermalSolver.StepPhaseProfile.PhaseCount];

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

/// <summary>List operation.</summary>
            List<Row> rows = new List<Row>();
            for (int p = 0; p < best.Length; p++)
            {
/// <summary>Row operation.</summary>
                Row row = new Row();
                row.Stage = ThermalSolver.StepPhaseProfile.Names[p];
                row.Blocks = simulation.Solver.Nodes.Count;
                row.BestMs = best[p];
                row.WorstMs = worst[p];
                row.Work = visits[p];
                row.WorkUnit = "visits";

                row.Repeats = repeats;
                row.Stop = Row.Fixed;
                row.Note = slices[p] + " slices";
                row.Samples.AddRange(samples[p]);
                Summarise(row);
                rows.Add(row);
            }

            return rows;
        }

/// <summary>Solver operation.</summary>
        private static Row Solver(GridBuilder builder)
        {
            ThermalSimulation simulation = builder.BuildSimulation(Hulls.Uncapped(), 293.15f);
            LoadBenchmarks.SeedSpread(simulation);
            Census.DriveCensus(simulation);

            EnvironmentState state = EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, Worlds.Flight(1f, 300f));
            float step = simulation.Settings.StepSeconds;

            for (int i = 0; i < 3; i++) simulation.Solver.Step(step, state);

/// <summary>NewRow operation.</summary>
            Row row = NewRow("solver", simulation, "substeps");
            for (int r = 0; !Settled(row); r++)
            {
                long before = simulation.Work.SolverSubsteps;
/// <summary>Allocated operation.</summary>
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

/// <summary>Table operation.</summary>
        public static string Table(IList<Row> rows)
        {
/// <summary>StringBuilder operation.</summary>
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

        public static readonly string TakenUtc =
            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        public static readonly string Host = Environment.MachineName;

/// <summary>Csv operation.</summary>
        public static string Csv(IList<Row> rows)
        {
/// <summary>StringBuilder operation.</summary>
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

/// <summary>ReadCsv operation.</summary>
        public static List<Row> ReadCsv(string path)
        {
/// <summary>List operation.</summary>
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
/// <summary>Row operation.</summary>
                Row row = new Row();
/// <summary>Field operation.</summary>
                row.Stage = Field(header, parts, "stage", path);
                row.Blocks = int.Parse(Field(header, parts, "blocks", path), CultureInfo.InvariantCulture);
                row.BestMs = double.Parse(Field(header, parts, "best_ms", path), CultureInfo.InvariantCulture);
                row.MedianMs = double.Parse(Field(header, parts, "median_ms", path), CultureInfo.InvariantCulture);
                row.WorstMs = double.Parse(Field(header, parts, "worst_ms", path), CultureInfo.InvariantCulture);
                row.Repeats = int.Parse(Field(header, parts, "repeats", path), CultureInfo.InvariantCulture);
                row.ConfirmedBest = int.Parse(Field(header, parts, "confirmed_best", path), CultureInfo.InvariantCulture);
/// <summary>Field operation.</summary>
                row.Stop = Field(header, parts, "stopped", path);
                row.Work = long.Parse(Field(header, parts, "work", path), CultureInfo.InvariantCulture);
/// <summary>Field operation.</summary>
                row.WorkUnit = Field(header, parts, "work_unit", path);
                row.AllocatedBytes = long.Parse(Field(header, parts, "allocated_bytes", path), CultureInfo.InvariantCulture);
                rows.Add(row);
            }

            return rows;
        }

/// <summary>Field operation.</summary>
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

/// <summary>SamplesCsv operation.</summary>
        public static string SamplesCsv(IList<Row> rows)
        {
/// <summary>StringBuilder operation.</summary>
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
