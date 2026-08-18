using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>One row of the scale ladder: what a grid of this size costs, stage by stage.</summary>
    public class ScaleRow
    {
        public string Shape;
        public int TargetBlocks;
        public int Blocks;
        public int Links;
        public long BoundingVolume;
        public int ExposedBlocks;

        /// <summary>Adding every block and building every derived structure, once.</summary>
        public double BuildMs;

        /// <summary>The conduction graph alone, rebuilt from scratch.</summary>
        public double TopologyMs;

        /// <summary>One complete room mapping pass, run to completion in one call.</summary>
        public double RoomMapMs;

        /// <summary>One pass over every node recomputing its exposed faces.</summary>
        public double ExposureMs;

        /// <summary>One solver step at the configured frequency.</summary>
        public double SolverStepMs;

        public int Substeps;
        public double ResidentMb;

        /// <summary>Solver cost normalised so grids of different sizes are comparable.</summary>
        public double NsPerLinkVisit
        {
            get
            {
                long visits = (long)Links * Math.Max(1, Substeps);
                return visits == 0 ? 0d : SolverStepMs * 1e6d / visits;
            }
        }

        public double MsPerSimulatedSecond;

        /// <summary>
        /// The worst single tick after one block is placed — the frame the player sees stutter.
        ///
        /// Placing a block does not cost the block. It marks the topology dirty, and the next
        /// tick rebuilds the conduction graph, the coolant loops and the heat pumps, and restarts
        /// the room map. That whole set lands inside one call to <c>Update</c>, which is why this
        /// is measured as a worst tick and not as a total.
        /// </summary>
        public double SpikeAfterOneBlockMs;

        /// <summary>
        /// Ticks the room map takes to converge after that placement, and what they cost in
        /// total. The mapper is budgeted per tick, so this tail is smooth by construction — but
        /// it is also how long the grid runs on a stale exposure map.
        /// </summary>
        public int SettleTicks;
        public double SettleTotalMs;
    }

    /// <summary>A frame-pacing benchmark: what the ticks of a session actually cost.</summary>
    public class HitchResult
    {
        public string Name;
        public int Blocks;
        public int Links;
        public double BuildMs;
        public FrameTrace Trace;
        public string Notes = "";
    }

    /// <summary>
    /// Synthetic load benchmarks.
    ///
    /// The scenario library answers questions about the physics; these answer questions about
    /// the cost of running it, at sizes nobody would sit through in a game session. They exist
    /// for one target: a grid of a million blocks that ticks without stalling, accepting a lower
    /// simulation rate to get there but not accepting a stutter.
    ///
    /// Two figures matter and they are not the same figure:
    ///
    /// <list type="bullet">
    /// <item><b>Steady cost</b> — what a tick costs when nothing is changing. Sets how much of
    /// the frame the mod takes, and degrades gracefully: twice the cost is half the simulation
    /// rate, which a player experiences as heat moving more slowly.</item>
    /// <item><b>Spike cost</b> — what a tick costs when something changed. Sets whether the game
    /// stutters, which a player experiences as the game being broken. A single block placed
    /// today invalidates the conduction graph, the coolant loops, the heat pumps and the whole
    /// room map, so this is the number the target depends on.</item>
    /// </list>
    /// </summary>
    public static class LoadBenchmarks
    {
        /// <summary>Real seconds between host ticks — <c>ThermalGrid.UpdateBeforeSimulation10</c>.</summary>
        public const float TickSeconds = 10f / 60f;

        /// <summary>The default ladder. Every rung is roughly four times the one below it.</summary>
        public static readonly int[] DefaultSizes = { 8000, 32000, 125000, 500000, 1000000 };

        public static readonly string[] Names = { "scale", "hitch", "weld", "load", "spike" };

        // ---- the ladder --------------------------------------------------------------------

        /// <summary>
        /// Builds one grid per size and measures every stage of an update on it.
        ///
        /// Each stage is measured on its own rather than inferred from a total, because they
        /// behave completely differently as the grid grows: two of them run only when the layout
        /// changes and are the spikes, and two run every step and are the steady cost.
        /// </summary>
        public static List<ScaleRow> Scale(string shape, IList<int> sizes, Action<string> log = null)
        {
            List<ScaleRow> rows = new List<ScaleRow>();

            for (int i = 0; i < sizes.Count; i++)
            {
                if (log != null) log(shape + " " + sizes[i].ToString("n0") + " ...");
                rows.Add(MeasureScale(shape, sizes[i]));
            }

            return rows;
        }

        private static ScaleRow MeasureScale(string shape, int targetCells)
        {
            ScaleRow row = new ScaleRow();
            row.Shape = shape;
            row.TargetBlocks = targetCells;

            HashSet<Vector3I> cells = LoadShapes.Build(shape, targetCells);

            long before = GC.GetTotalMemory(true);

            Stopwatch build = Stopwatch.StartNew();

            GridBuilder builder = GridBuilder.Large();
            BlockModel armour = Catalog.HeavyArmor();
            BlockModel fitting = Catalog.Grating();

            // Two block types, because substep count is set by the stiffest node on the grid and
            // a hull built of one heavy block solves in a single substep however large it is.
            // A ship is armour with light fittings bolted through it, and the fitting is the
            // stiff node — so a benchmark of one block type measures a case the game never runs.
            int index = 0;
            foreach (Vector3I cell in cells)
            {
                builder.Place((index++ % 8) == 0 ? fitting : armour, cell);
            }

            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), builder.Grid);
            for (int i = 0; i < builder.Placed.Count; i++)
            {
                simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            }

            simulation.RebuildAll();
            build.Stop();
            row.BuildMs = build.Elapsed.TotalMilliseconds;

            row.ResidentMb = Math.Max(0d, (GC.GetTotalMemory(false) - before) / (1024d * 1024d));

            row.Blocks = simulation.Solver.Nodes.Count;
            row.Links = simulation.Solver.Links.Count;

            Vector3I extents = (simulation.Grid.Max - simulation.Grid.Min) + Vector3I.One;
            row.BoundingVolume = (long)extents.X * extents.Y * extents.Z;

            for (int i = 0; i < row.Blocks; i++)
            {
                if (simulation.Solver.Nodes[i].TotalExposedFaces > 0) row.ExposedBlocks++;
            }

            // ---- the one-shot stages, each on its own ----

            Stopwatch watch = Stopwatch.StartNew();
            simulation.Solver.RebuildLinks();
            watch.Stop();
            row.TopologyMs = watch.Elapsed.TotalMilliseconds;

            watch.Restart();
            simulation.Rooms.RequestRestart(simulation.Grid);
            simulation.Rooms.RunToCompletion();
            watch.Stop();
            row.RoomMapMs = watch.Elapsed.TotalMilliseconds;

            watch.Restart();
            simulation.Solver.RefreshExposure(simulation.Rooms.Map);
            watch.Stop();
            row.ExposureMs = watch.Elapsed.TotalMilliseconds;

            // ---- the steady stage ----

            SeedSpread(simulation);

            EnvironmentState state = EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, Worlds.Space(new Vector3(0f, 1f, 0f)));

            float step = simulation.Settings.StepSeconds;
            for (int i = 0; i < 3; i++) simulation.Solver.Step(step, state);

            int measured = row.Blocks > 200000 ? 3 : (row.Blocks > 50000 ? 10 : 40);

            watch.Restart();
            for (int i = 0; i < measured; i++) simulation.Solver.Step(step, state);
            watch.Stop();

            row.SolverStepMs = watch.Elapsed.TotalMilliseconds / measured;
            row.Substeps = simulation.Solver.LastSubsteps;
            row.MsPerSimulatedSecond = row.SolverStepMs * simulation.Settings.StepsPerSecond;

            // ---- what one block placed costs ----
            //
            // Not the block: the rebuild it triggers. Placing one block marks the topology dirty,
            // and the next update rebuilds the conduction graph, the coolant loops, the heat
            // pumps and the room map for the whole grid.

            Vector3I spare = simulation.Grid.Max + new Vector3I(0, 0, 1);
            simulation.AddBlock(new BlockInstance(armour, spare, BlockOrientation.Identity), 293.15f);

            double worst = 0d;
            double total = 0d;
            int settleTicks = 0;

            do
            {
                watch.Restart();
                simulation.Update(TickSeconds, Worlds.Shadow());
                watch.Stop();

                double ms = watch.Elapsed.TotalMilliseconds;
                if (ms > worst) worst = ms;
                total += ms;
                settleTicks++;
            }
            while (simulation.HasPendingWork && settleTicks < 100000);

            row.SpikeAfterOneBlockMs = worst;
            row.SettleTicks = settleTicks;
            row.SettleTotalMs = total;

            return row;
        }

        // ---- frame pacing ------------------------------------------------------------------

        /// <summary>
        /// Ticks a settled grid the way the host does, then disturbs it the way a player does,
        /// and records what every tick cost.
        ///
        /// The disturbances are the point. A benchmark that only ticks a finished ship measures
        /// the case that was never the problem; what stutters is the frame a block is welded, a
        /// door cycles or a section is shot away.
        /// </summary>
        public static HitchResult Hitch(string shape, int targetCells, int ticks = 400)
        {
            HitchResult result = new HitchResult();
            result.Name = "hitch " + shape + " " + targetCells.ToString("n0");

            Stopwatch build = Stopwatch.StartNew();
            ThermalSimulation simulation = BuildSettled(shape, targetCells);
            build.Stop();

            result.BuildMs = build.Elapsed.TotalMilliseconds;
            result.Blocks = simulation.Solver.Nodes.Count;
            result.Links = simulation.Solver.Links.Count;

            SeedSpread(simulation);

            FrameTrace trace = new FrameTrace(result.Name);
            EnvironmentSample sample = Worlds.Space(new Vector3(0f, 1f, 0f));

            BlockModel armour = Catalog.HeavyArmor();
            Vector3I weldAt = simulation.Grid.Max + new Vector3I(0, 0, 2);

            Stopwatch watch = new Stopwatch();

            for (int tick = 0; tick < ticks; tick++)
            {
                string what = "steady";

                // One block welded a quarter of the way in, and one ground off half way. Both
                // are single events in a session and both invalidate everything.
                if (tick == ticks / 4)
                {
                    simulation.AddBlock(new BlockInstance(armour, weldAt, BlockOrientation.Identity), 293.15f);
                    what = "one block placed";
                }
                else if (tick == ticks / 2)
                {
                    BlockInstance placed = simulation.Grid.GetAtCell(weldAt);
                    if (placed != null)
                    {
                        simulation.RemoveBlock(placed);
                        what = "one block removed";
                    }
                }

                watch.Restart();
                simulation.Update(TickSeconds, sample);
                watch.Stop();

                trace.Add(watch.Elapsed.TotalMilliseconds, what);
            }

            result.Trace = trace;
            return result;
        }

        /// <summary>
        /// Sustained construction: a block placed on every tick, which is what a welder does.
        ///
        /// Every one of them dirties the topology, so this is the worst realistic case for the
        /// rebuild path — and unlike the single placement in <see cref="Hitch"/>, it never gets
        /// a quiet tick to recover in.
        /// </summary>
        public static HitchResult Weld(string shape, int targetCells, int ticks = 120)
        {
            HitchResult result = new HitchResult();
            result.Name = "weld " + shape + " " + targetCells.ToString("n0");

            Stopwatch build = Stopwatch.StartNew();
            ThermalSimulation simulation = BuildSettled(shape, targetCells);
            build.Stop();

            result.BuildMs = build.Elapsed.TotalMilliseconds;
            result.Blocks = simulation.Solver.Nodes.Count;
            result.Links = simulation.Solver.Links.Count;

            FrameTrace trace = new FrameTrace(result.Name);
            EnvironmentSample sample = Worlds.Shadow();
            BlockModel armour = Catalog.HeavyArmor();

            // A run of new cells laid alongside the hull, so every placement is a real topology
            // change with a real neighbour rather than an isolated node in empty space.
            Vector3I start = simulation.Grid.Min - new Vector3I(2, 0, 0);
            Stopwatch watch = new Stopwatch();

            for (int tick = 0; tick < ticks; tick++)
            {
                Vector3I at = start + new Vector3I(0, 0, tick);
                simulation.AddBlock(new BlockInstance(armour, at, BlockOrientation.Identity), 293.15f);

                watch.Restart();
                simulation.Update(TickSeconds, sample);
                watch.Stop();

                trace.Add(watch.Elapsed.TotalMilliseconds, "block " + tick + " welded");
            }

            result.Trace = trace;
            result.Notes = ticks + " blocks welded, one per tick";
            return result;
        }

        /// <summary>
        /// World load: what building the simulation for a grid this size costs before the first
        /// tick. A player sees this as the loading screen, or as the freeze when a large
        /// blueprint is pasted.
        /// </summary>
        public static HitchResult Load(string shape, int targetCells)
        {
            HitchResult result = new HitchResult();
            result.Name = "load " + shape + " " + targetCells.ToString("n0");

            FrameTrace trace = new FrameTrace(result.Name);
            HashSet<Vector3I> cells = LoadShapes.Build(shape, targetCells);

            Stopwatch watch = Stopwatch.StartNew();

            GridBuilder builder = GridBuilder.Large();
            BlockModel armour = Catalog.HeavyArmor();
            foreach (Vector3I cell in cells) builder.Place(armour, cell);

            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), builder.Grid);
            for (int i = 0; i < builder.Placed.Count; i++)
            {
                simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            }
            double addMs = watch.Elapsed.TotalMilliseconds;

            watch.Restart();
            simulation.RebuildAll();
            watch.Stop();

            trace.Add(watch.Elapsed.TotalMilliseconds, "RebuildAll");

            result.Blocks = simulation.Solver.Nodes.Count;
            result.Links = simulation.Solver.Links.Count;
            result.BuildMs = addMs + watch.Elapsed.TotalMilliseconds;
            result.Trace = trace;
            result.Notes = "adding blocks " + addMs.ToString("n0") + " ms, RebuildAll "
                + watch.Elapsed.TotalMilliseconds.ToString("n0") + " ms";
            return result;
        }

        // ---- attribution --------------------------------------------------------------------

        /// <summary>
        /// What the ticks after a single block placement cost, per stage.
        ///
        /// Per stage worst rather than one worst tick, because the stages do not land together:
        /// the conduction rebuild goes on the tick after the placement, the room map converges
        /// hundreds of ticks later, and the exposure pass rides on the tick that publishes it.
        /// Reporting only the worst tick therefore hides two of the three stalls behind whichever
        /// one happened to be largest.
        /// </summary>
        public class SpikeReport
        {
            public int Blocks;
            public int Links;

            /// <summary>Worst single tick over the whole settle, and its stage split.</summary>
            public double WorstTickMs;

            /// <summary>The worst single call to each stage, anywhere in the settle.</summary>
            public double TopologyMs;
            public double RoomMappingMs;
            public double ExposureMs;
            public double SolverMs;

            /// <summary>
            /// Which tick each stage's worst call landed on. A stage whose worst call is on the
            /// first tick is doing setup work; one whose worst is on the last is doing teardown;
            /// one whose worst is in the middle is doing its actual job badly. Three different
            /// problems that a single millisecond figure cannot tell apart.
            /// </summary>
            public int TopologyTick = -1;
            public int RoomMappingTick = -1;
            public int ExposureTick = -1;
            public int SolverTick = -1;

            public int Ticks;
            public SimulationWork Work = new SimulationWork();

            /// <summary>
            /// Collections and bytes allocated over the settle.
            ///
            /// Worth reporting next to the stage timings because a stall is not always the code
            /// the stopwatch was wrapped around. A pass that replaces a map of the whole bounding
            /// volume makes the old one garbage all at once, and the collection that follows is
            /// charged to whichever stage happened to be running — so a room stage with a large
            /// worst call and a gen-2 collection against it is a memory problem wearing a
            /// mapping problem's clothes.
            /// </summary>
            public int Gen0;
            public int Gen1;
            public int Gen2;
            public double AllocatedMb;

            /// <summary>
            /// The largest thing that lands in one tick. This is the stall a player sees, and
            /// the figure the whole exercise is trying to drive down.
            /// </summary>
            public double WorstStageMs
            {
                get
                {
                    double worst = TopologyMs;
                    if (RoomMappingMs > worst) worst = RoomMappingMs;
                    if (ExposureMs > worst) worst = ExposureMs;
                    if (SolverMs > worst) worst = SolverMs;
                    return worst;
                }
            }

            public string Describe()
            {
                return "worst tick " + WorstTickMs.ToString("n1") + " ms over " + Ticks
                    + " ticks to settle. Worst call per stage: topology "
                    + TopologyMs.ToString("n1") + " (tick " + TopologyTick + "), rooms "
                    + RoomMappingMs.ToString("n1") + " (tick " + RoomMappingTick + "), exposure "
                    + ExposureMs.ToString("n1") + " (tick " + ExposureTick + "), solver "
                    + SolverMs.ToString("n1") + " (tick " + SolverTick + ") ms."
                    + " GC: " + Gen0 + "/" + Gen1 + "/" + Gen2 + " collections, "
                    + AllocatedMb.ToString("n0") + " MB allocated."
                    + " Ran: " + Work.TopologyRebuilds + " topology rebuilds, "
                    + Work.ExposureRefreshes + " exposure refreshes, "
                    + Work.RoomPassesBegun + " room passes, "
                    + Work.SolverSteps + " solver steps."
                    + " Touched: " + Work.TopologyNodeVisits.ToString("n0") + " nodes for topology, "
                    + Work.ExposureNodeVisits.ToString("n0") + " for exposure, "
                    + Work.LoopSearchCells.ToString("n0") + " for coolant loops, "
                    + Work.HeatPumpNodeVisits.ToString("n0") + " for heat pumps, "
                    + Work.RoomCellsVisited.ToString("n0") + " cells flooded.";
            }
        }

        /// <summary>
        /// Places one block on a settled grid and reports what the ticks after it spent, stage
        /// by stage and count by count.
        ///
        /// The ladder says the spike is large; this says which stage it is, which is the
        /// difference between a number to worry about and a line to change.
        /// </summary>
        public static SpikeReport Spike(string shape, int targetCells)
        {
            ThermalSimulation simulation = BuildSettled(shape, targetCells);
            while (simulation.HasPendingWork)
            {
                simulation.Update(TickSeconds, Worlds.Shadow());
            }

            SeedSpread(simulation);

            StageTimings timings = null;
            simulation.Work.Reset();

            SpikeReport report = new SpikeReport();

            // Read before the placement, and through LinkCount rather than Links. The Links
            // getter rebuilds a stale graph, so a benchmark that reads it after placing a block
            // measures its own instrumentation: the first run of this reported two topology
            // rebuilds for one placement, and one of them was this line.
            report.Blocks = simulation.Solver.Nodes.Count;
            report.Links = simulation.Solver.LinkCount;

            BlockModel armour = Catalog.HeavyArmor();
            Vector3I at = simulation.Grid.Max + new Vector3I(0, 0, 2);
            simulation.AddBlock(new BlockInstance(armour, at, BlockOrientation.Identity), 293.15f);

            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);
            long allocated = GC.GetTotalAllocatedBytes(false);

            Stopwatch watch = new Stopwatch();

            do
            {
                // A fresh set of timings per tick, so a stage's worst call can be attributed to
                // the tick it happened on rather than only to the stage.
                timings = new StageTimings();
                simulation.Profiler = timings;

                watch.Restart();
                simulation.Update(TickSeconds, Worlds.Shadow());
                watch.Stop();

                double ms = watch.Elapsed.TotalMilliseconds;
                if (ms > report.WorstTickMs) report.WorstTickMs = ms;

                Record(report, timings, SimulationPhase.Topology, report.Ticks);
                Record(report, timings, SimulationPhase.RoomMapping, report.Ticks);
                Record(report, timings, SimulationPhase.Exposure, report.Ticks);
                Record(report, timings, SimulationPhase.Solver, report.Ticks);

                report.Ticks++;
            }
            while (simulation.HasPendingWork && report.Ticks < 100000);

            report.Work = simulation.Work.Snapshot();
            report.Gen0 = GC.CollectionCount(0) - gen0;
            report.Gen1 = GC.CollectionCount(1) - gen1;
            report.Gen2 = GC.CollectionCount(2) - gen2;
            report.AllocatedMb = (GC.GetTotalAllocatedBytes(false) - allocated) / (1024d * 1024d);

            simulation.Profiler = null;
            return report;
        }

        private static void Record(SpikeReport report, StageTimings timings, SimulationPhase phase, int tick)
        {
            double ms = timings.WorstMs(phase);

            switch (phase)
            {
                case SimulationPhase.Topology:
                    if (ms > report.TopologyMs) { report.TopologyMs = ms; report.TopologyTick = tick; }
                    break;
                case SimulationPhase.RoomMapping:
                    if (ms > report.RoomMappingMs) { report.RoomMappingMs = ms; report.RoomMappingTick = tick; }
                    break;
                case SimulationPhase.Exposure:
                    if (ms > report.ExposureMs) { report.ExposureMs = ms; report.ExposureTick = tick; }
                    break;
                default:
                    if (ms > report.SolverMs) { report.SolverMs = ms; report.SolverTick = tick; }
                    break;
            }
        }

        // ---- shared -------------------------------------------------------------------------

        /// <summary>Builds a grid and takes it all the way to a mapped, settled state.</summary>
        public static ThermalSimulation BuildSettled(string shape, int targetCells)
        {
            HashSet<Vector3I> cells = LoadShapes.Build(shape, targetCells);

            GridBuilder builder = GridBuilder.Large();
            BlockModel armour = Catalog.HeavyArmor();
            BlockModel fitting = Catalog.Grating();

            int index = 0;
            foreach (Vector3I cell in cells)
            {
                builder.Place((index++ % 8) == 0 ? fitting : armour, cell);
            }

            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), builder.Grid);
            for (int i = 0; i < builder.Placed.Count; i++)
            {
                simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            }

            simulation.RebuildAll();
            return simulation;
        }

        /// <summary>
        /// Spreads temperatures across 250-750 K, deterministically.
        ///
        /// The conduction loop skips a link whose ends agree, so a grid left at one temperature
        /// measures the cost of not conducting. The generator is written out rather than taken
        /// from <c>Random</c> so the sequence is the same on any runtime — the same reason the
        /// scenario library does it this way.
        /// </summary>
        public static void SeedSpread(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            uint state = 0x9E3779B9u;

            for (int i = 0; i < nodes.Count; i++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                nodes[i].Temperature = 250f + (state % 500u);
            }
        }

        // ---- reporting ----------------------------------------------------------------------

        public static string Table(IList<ScaleRow> rows)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("shape".PadRight(8))
              .Append("blocks".PadLeft(10))
              .Append("links".PadLeft(11))
              .Append("bbox".PadLeft(12))
              .Append("exposed".PadLeft(9))
              .Append("build ms".PadLeft(10))
              .Append("topo ms".PadLeft(10))
              .Append("rooms ms".PadLeft(10))
              .Append("expos ms".PadLeft(10))
              .Append("step ms".PadLeft(9))
              .Append("sub".PadLeft(5))
              .Append("ns/link".PadLeft(9))
              .Append("ms/simsec".PadLeft(11))
              .Append("+1 spike ms".PadLeft(13))
              .Append("settle".PadLeft(8))
              .Append("MB".PadLeft(8))
              .Append('\n');

            for (int i = 0; i < rows.Count; i++)
            {
                ScaleRow r = rows[i];
                sb.Append(r.Shape.PadRight(8))
                  .Append(r.Blocks.ToString("n0").PadLeft(10))
                  .Append(r.Links.ToString("n0").PadLeft(11))
                  .Append(r.BoundingVolume.ToString("n0").PadLeft(12))
                  .Append(Percent(r.ExposedBlocks, r.Blocks).PadLeft(9))
                  .Append(r.BuildMs.ToString("n0").PadLeft(10))
                  .Append(r.TopologyMs.ToString("n1").PadLeft(10))
                  .Append(r.RoomMapMs.ToString("n1").PadLeft(10))
                  .Append(r.ExposureMs.ToString("n1").PadLeft(10))
                  .Append(r.SolverStepMs.ToString("n2").PadLeft(9))
                  .Append(r.Substeps.ToString().PadLeft(5))
                  .Append(r.NsPerLinkVisit.ToString("n1").PadLeft(9))
                  .Append(r.MsPerSimulatedSecond.ToString("n1").PadLeft(11))
                  .Append(r.SpikeAfterOneBlockMs.ToString("n1").PadLeft(13))
                  .Append((r.SettleTicks + "t").PadLeft(8))
                  .Append(r.ResidentMb.ToString("n0").PadLeft(8))
                  .Append('\n');
            }

            return sb.ToString();
        }

        private static string Percent(int part, int whole)
        {
            if (whole == 0) return "-";
            return (100d * part / whole).ToString("n0") + "%";
        }

        public static string Csv(IList<ScaleRow> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("shape,blocks,links,boundingVolume,exposed,buildMs,topologyMs,roomMapMs,")
              .Append("exposureMs,solverStepMs,substeps,nsPerLinkVisit,msPerSimulatedSecond,")
              .Append("spikeAfterOneBlockMs,settleTicks,settleTotalMs,residentMb\n");

            for (int i = 0; i < rows.Count; i++)
            {
                ScaleRow r = rows[i];
                sb.Append(r.Shape).Append(',')
                  .Append(r.Blocks).Append(',')
                  .Append(r.Links).Append(',')
                  .Append(r.BoundingVolume).Append(',')
                  .Append(r.ExposedBlocks).Append(',')
                  .Append(r.BuildMs.ToString("f3")).Append(',')
                  .Append(r.TopologyMs.ToString("f3")).Append(',')
                  .Append(r.RoomMapMs.ToString("f3")).Append(',')
                  .Append(r.ExposureMs.ToString("f3")).Append(',')
                  .Append(r.SolverStepMs.ToString("f4")).Append(',')
                  .Append(r.Substeps).Append(',')
                  .Append(r.NsPerLinkVisit.ToString("f3")).Append(',')
                  .Append(r.MsPerSimulatedSecond.ToString("f3")).Append(',')
                  .Append(r.SpikeAfterOneBlockMs.ToString("f3")).Append(',')
                  .Append(r.SettleTicks).Append(',')
                  .Append(r.SettleTotalMs.ToString("f3")).Append(',')
                  .Append(r.ResidentMb.ToString("f1")).Append('\n');
            }

            return sb.ToString();
        }
    }
}
