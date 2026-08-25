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

        /// <summary>
        /// One solver step of the full configured length, with as many substeps as the grid's
        /// stiffness asks for and no work budget applied.
        ///
        /// This is the cost of a full step of simulated time, which is the right figure for
        /// comparing sizes — but on a grid large enough for <c>MaxElementVisitsPerStep</c> to bite it
        /// is <em>not</em> what a tick pays, because the step is shortened to fit. See
        /// <see cref="BoundedStepMs"/>, and the hitch benchmark for the distribution.
        /// </summary>
        public double SolverStepMs;

        /// <summary>What a step costs once the work budget has shortened it — what a tick pays.</summary>
        public double BoundedStepMs;

        /// <summary>Substeps the budget allows at this size.</summary>
        public int SubstepBudget;

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

        /// <summary>
        /// The worst call to each stage over the run, and the tick it landed on.
        ///
        /// A distribution says how bad the tail is; this says what is in it. Without it a p99 of
        /// four times the median is a fact with no next step — the tail could be the solver
        /// doing its job on a grid that large, or a stage that is still unbudgeted, and those
        /// want opposite responses.
        /// </summary>
        public double TopologyMs;
        public double RoomMappingMs;
        public double ExposureMs;
        public double SolverMs;
        public int TopologyTick = -1;
        public int RoomMappingTick = -1;
        public int ExposureTick = -1;
        public int SolverTick = -1;

        /// <summary>Collections during the measured run, and bytes allocated by it.</summary>
        public int Gen0;
        public int Gen1;
        public int Gen2;
        public double AllocatedMb;

        public string DescribeGc()
        {
            return "GC during the run: " + Gen0 + "/" + Gen1 + "/" + Gen2
                + " collections, " + AllocatedMb.ToString("n0") + " MB allocated.";
        }

        public string DescribeStages()
        {
            return "worst call per stage: topology " + TopologyMs.ToString("n1")
                + " (tick " + TopologyTick + "), rooms " + RoomMappingMs.ToString("n1")
                + " (tick " + RoomMappingTick + "), exposure " + ExposureMs.ToString("n1")
                + " (tick " + ExposureTick + "), solver " + SolverMs.ToString("n1")
                + " (tick " + SolverTick + ") ms.";
        }
    }

    /// <summary>
    /// Synthetic load benchmarks: what the simulation costs rather than what it does, at sizes nobody
    /// would sit through in a session. **Steady cost and spike cost are different figures with
    /// different failure modes** and the distinction is what every measurement here rests on.
    /// See load-and-hitching.md, The distinction the whole exercise rests on.
    /// </summary>
    public static class LoadBenchmarks
    {
        /// <summary>
        /// Real seconds in one rendered frame. The host now updates every grid every frame, each
        /// doing its share of the step it is part way through, so this is the interval the
        /// frame-paced benchmarks measure.
        /// </summary>
        public const float FrameSeconds = 1f / 60f;

        /// <summary>
        /// A coarser interval, used only to settle a grid quickly before measuring it. Advancing a
        /// fixture at a sixtieth of a second would take ten times as many calls to reach the same
        /// state and measure nothing extra.
        /// </summary>
        public const float TickSeconds = 10f / 60f;

        /// <summary>The default ladder. Every rung is roughly four times the one below it.</summary>
        public static readonly int[] DefaultSizes = { 8000, 32000, 125000, 500000, 1000000 };

        public static readonly string[] Names =
            { "scale", "hitch", "weld", "load", "spike", "pace", "reach", "memory", "floor" };

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

            // Built from the measured block census, because substep count is set by the
            // *stiffest* node on the grid and therefore by its lightest block. This ladder used
            // to build armour with a grating in eight, which was a step in the right direction
            // from one block type and still had a lightest block twelve times heavier than a
            // real ship's — so it asked for three substeps where a field ship asks for sixteen
            // to thirty-two, and every millisecond below was measured on a hull an order of
            // magnitude softer than the ones it described. See <see cref="Census"/>.
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(cells);

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
            // A row measured on a hull whose map never finished is not comparable with the rows
            // above it: every block reads exposed, nothing holds air, and the environment pass runs
            // its expensive branch everywhere. The ladder used to report exactly that at the top
            // rung without saying so.
            if (!simulation.Rooms.RunToCompletion())
            {
                throw new InvalidOperationException(
                    "the room map did not finish on " + shape + " at " + targetCells
                    + " blocks, so every figure from this rung would describe an unmapped hull");
            }
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

            // And again through the step length the work budget actually allows, which on a large
            // grid is a fraction of the full one. Same total work per simulated second; the point
            // is that it arrives in even pieces instead of in lurches.
            row.SubstepBudget = simulation.SubstepBudget;

            float affordable = simulation.AffordableStepSeconds(step);
            for (int i = 0; i < 2; i++) simulation.Solver.Step(affordable, state);

            watch.Restart();
            for (int i = 0; i < measured; i++) simulation.Solver.Step(affordable, state);
            watch.Stop();

            row.BoundedStepMs = watch.Elapsed.TotalMilliseconds / measured;

            // ---- what one block placed costs ----
            //
            // Not the block: the rebuild it triggers. Placing one block marks the topology dirty,
            // and the next update rebuilds the conduction graph, the coolant loops, the heat
            // pumps and the room map for the whole grid.

            Vector3I spare = simulation.Grid.Max + new Vector3I(0, 0, 1);
            simulation.AddBlock(
                new BlockInstance(Catalog.HeavyArmor(), spare, BlockOrientation.Identity), 293.15f);

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
        /// Whether the benchmarks turn on the solver's per-mechanism watt figures, which nothing in the
        /// simulation reads. Taking a telemetry dump turns them on, so every field measurement includes
        /// the cost of being measured. See benchmarks.md, What being measured costs.
        /// </summary>
        public static bool CollectDiagnostics;

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
            SettleMemory();
            simulation.Work.Reset();

            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);
            long allocated = GC.GetTotalAllocatedBytes(false);

            FrameTrace trace = new FrameTrace(result.Name);
            EnvironmentSample sample = Worlds.Space(new Vector3(0f, 1f, 0f));

            BlockModel armour = Catalog.HeavyArmor();
            Vector3I weldAt = simulation.Grid.Max + new Vector3I(0, 0, 2);
            const float frame = FrameSeconds;

            Stopwatch watch = new Stopwatch();

            for (int tick = 0; tick < ticks; tick++)
            {
                StageTimings timings = new StageTimings();
                simulation.Profiler = timings;

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
                simulation.Update(frame, sample);
                watch.Stop();

                trace.Add(watch.Elapsed.TotalMilliseconds,
                    what + ", " + simulation.Solver.LastSubsteps + " substeps, "
                    + simulation.Work.SolverSteps + " steps so far");
                RecordStages(result, timings, tick);
            }

            simulation.Profiler = null;
            result.Trace = trace;
            RecordGc(result, gen0, gen1, gen2, allocated);
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

            SettleMemory();

            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);
            long allocated = GC.GetTotalAllocatedBytes(false);

            FrameTrace trace = new FrameTrace(result.Name);
            EnvironmentSample sample = Worlds.Shadow();
            BlockModel armour = Catalog.HeavyArmor();

            // A run of new cells laid alongside the hull, so every placement is a real topology
            // change with a real neighbour rather than an isolated node in empty space.
            Vector3I start = simulation.Grid.Min - new Vector3I(2, 0, 0);
            Stopwatch watch = new Stopwatch();

            for (int tick = 0; tick < ticks; tick++)
            {
                StageTimings timings = new StageTimings();
                simulation.Profiler = timings;

                Vector3I at = start + new Vector3I(0, 0, tick);
                simulation.AddBlock(new BlockInstance(armour, at, BlockOrientation.Identity), 293.15f);

                watch.Restart();
                simulation.Update(FrameSeconds, sample);
                watch.Stop();

                trace.Add(watch.Elapsed.TotalMilliseconds, "block " + tick + " welded");
                RecordStages(result, timings, tick);
            }

            simulation.Profiler = null;
            result.Trace = trace;
            RecordGc(result, gen0, gen1, gen2, allocated);
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

        /// <summary>What a setting bundle did to a grid that is also radiating to space.</summary>
        public class StabilityRow
        {
            public string Label;
            public float HeatTimeScale;
            public int MaxSubsteps;

            public float MinTemperature;
            public float MaxTemperature;
            public float FinalSpread;
            public long ClampedSteps;
            public long Steps;
            public bool WentBad;
        }

        /// <summary>
        /// Runs a grid with the environment switched on and reports whether it stayed physical.
        ///
        /// This is the check that decides how far an arcade profile can be pushed. The overshoot
        /// clamp caps conduction at the energy that equalises a pair, so conduction is safe at any
        /// step length — but <b>radiation and convection are not clamped</b>. Their stiffness is in
        /// the substep estimate, so ordinarily the solver simply takes more substeps; cap the
        /// substeps and that protection is gone, and a block can be asked to shed more heat in one
        /// step than it holds.
        ///
        /// So "turn the transfer up and the substeps down" has a limit, and it is set by the
        /// environment rather than by conduction. Finding it is the difference between a profile
        /// and a guess.
        /// </summary>
        public static StabilityRow Stability(string label, int frequency, float heatTimeScale,
            int maxSubsteps, float realSeconds)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.Frequency = frequency;
            settings.HeatTimeScale = heatTimeScale;
            settings.MaxSubsteps = maxSubsteps;
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();
            return Stability(label, settings, realSeconds);
        }

        /// <summary>The same run, driven by a settings bundle a profile has filled in.</summary>
        public static StabilityRow Stability(string label, ThermalSettings settings, float realSeconds)
        {
            float heatTimeScale = settings.HeatTimeScale;
            int maxSubsteps = settings.MaxSubsteps;

            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(GridShapes.Ship(fuselageLength: 20, fuselageWidth: 7, bulkheadSpacing: 6));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            while (simulation.HasPendingWork) simulation.Update(FrameSeconds, Worlds.Shadow());

            // A hot spot and a cold hull, radiating into space: the ordinary case.
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++) nodes[i].Temperature = 293.15f;
            nodes[nodes.Count / 2].Temperature = 1200f;

            StabilityRow row = new StabilityRow();
            row.Label = label;
            row.HeatTimeScale = heatTimeScale;
            row.MaxSubsteps = maxSubsteps;
            row.MinTemperature = float.MaxValue;
            row.MaxTemperature = float.MinValue;

            EnvironmentSample sample = Worlds.Space(new Vector3(0f, 1f, 0f));
            int frames = (int)(realSeconds / FrameSeconds);

            for (int f = 0; f < frames; f++)
            {
                simulation.Update(FrameSeconds, sample);

                if (simulation.Solver.LastStepWasClamped && simulation.Work.SolverSteps > row.Steps)
                {
                    row.ClampedSteps++;
                }
                row.Steps = simulation.Work.SolverSteps;

                for (int i = 0; i < nodes.Count; i++)
                {
                    float t = nodes[i].Temperature;
                    if (float.IsNaN(t) || float.IsInfinity(t)) row.WentBad = true;
                    if (t < row.MinTemperature) row.MinTemperature = t;
                    if (t > row.MaxTemperature) row.MaxTemperature = t;
                }
            }

            float low = float.MaxValue;
            float high = float.MinValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                float t = nodes[i].Temperature;
                if (t < low) low = t;
                if (t > high) high = t;
            }
            row.FinalSpread = high - low;

            return row;
        }

        public static string StabilityTable(IList<StabilityRow> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("case".PadRight(18)).Append("heatScale".PadLeft(11))
              .Append("maxSub".PadLeft(8)).Append("min K".PadLeft(10))
              .Append("max K".PadLeft(12)).Append("end spread".PadLeft(12))
              .Append("clamped".PadLeft(9)).Append("bad".PadLeft(6)).Append('\n');

            for (int i = 0; i < rows.Count; i++)
            {
                StabilityRow r = rows[i];
                sb.Append(r.Label.PadRight(18))
                  .Append(r.HeatTimeScale.ToString("n0").PadLeft(11))
                  .Append(r.MaxSubsteps.ToString().PadLeft(8))
                  .Append(r.MinTemperature.ToString("n1").PadLeft(10))
                  .Append(r.MaxTemperature.ToString("n1").PadLeft(12))
                  .Append(r.FinalSpread.ToString("n1").PadLeft(12))
                  .Append((r.Steps == 0 ? "-" : (100.0 * r.ClampedSteps / r.Steps).ToString("n0") + "%").PadLeft(9))
                  .Append((r.WentBad ? "YES" : "no").PadLeft(6))
                  .Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>How far and how fast heat travelled, and what that cost.</summary>
        public class ReachRow
        {
            public string Label;
            public int Frequency;
            public float Speed;
            public float HeatTimeScale;
            public int MaxSubsteps;

            /// <summary>Blocks the front of the heat had crossed when the run ended.</summary>
            public int BlocksReached;

            /// <summary>Real seconds the run covered.</summary>
            public float RealSeconds;

            /// <summary>Blocks per real second — the number a player feels as responsiveness.</summary>
            public double BlocksPerRealSecond
            {
                get { return RealSeconds <= 0f ? 0d : BlocksReached / (double)RealSeconds; }
            }

            public long Substeps;
            public double SubstepsPerRealSecond;

            /// <summary>Element visits per real second: the cost, machine-independently.</summary>
            public double WorkPerRealSecond;

            public double MillisecondsPerRealSecond;

            /// <summary>Steps that wanted more substeps than they were allowed.</summary>
            public long ClampedSteps;
            public long Steps;
        }

        /// <summary>
        /// How fast heat crosses a grid, and what that speed costs: blocks crossed along a held-hot run
        /// after a fixed number of real seconds, which is what a player means by responsiveness and is
        /// comparable across any settings. **One substep can move heat at most one block**, so
        /// responsiveness and cost are one dial seen from two sides.
        /// See profiles.md, Designing your own.
        /// </summary>
        public static ReachRow Reach(string label, int frequency, float speed, float heatTimeScale,
            int maxSubsteps, float realSeconds, int length)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.Frequency = frequency;
            settings.SimulationSpeed = speed;
            settings.HeatTimeScale = heatTimeScale;
            settings.MaxSubsteps = maxSubsteps;
            return Reach(label, settings, realSeconds, length);
        }

        /// <summary>The same measurement, driven by a settings bundle a profile has filled in.</summary>
        public static ReachRow Reach(string label, ThermalSettings settings, float realSeconds, int length)
        {
            int frequency = settings.Frequency;
            float speed = settings.SimulationSpeed;
            float heatTimeScale = settings.HeatTimeScale;
            int maxSubsteps = settings.MaxSubsteps;

            // Conduction alone: radiation and convection would bleed the front away and measure
            // the environment rather than how fast heat travels through metal.
            settings.EnableEnvironment = false;
            settings.EnableRadiation = false;
            settings.EnableConvection = false;
            settings.EnableSolarHeat = false;
            settings.EnableWasteHeat = false;
            settings.EnableRoomAir = false;
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            BlockModel armour = Catalog.HeavyArmor();
            for (int i = 0; i < length; i++)
            {
                builder.Place(armour, new Vector3I(0, 0, i));
            }

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);
            while (simulation.HasPendingWork) simulation.Update(FrameSeconds, Worlds.Shadow());

            ThermalNode[] run = new ThermalNode[length];
            for (int i = 0; i < length; i++)
            {
                run[i] = simulation.Solver.GetNodeAt(new Vector3I(0, 0, i));
                run[i].Temperature = 300f;
            }

            ReachRow row = new ReachRow();
            row.Label = label;
            row.Frequency = frequency;
            row.Speed = speed;
            row.HeatTimeScale = heatTimeScale;
            row.MaxSubsteps = maxSubsteps;
            row.RealSeconds = realSeconds;

            simulation.Work.Reset();
            EnvironmentSample sample = Worlds.Shadow();
            int frames = (int)(realSeconds / FrameSeconds);

            SettleMemory();
            Stopwatch watch = Stopwatch.StartNew();

            for (int f = 0; f < frames; f++)
            {
                // The source end is held, so the front is fed rather than the whole run drifting
                // to one average.
                run[0].Temperature = 1000f;
                simulation.Update(FrameSeconds, sample);
                if (simulation.Solver.LastStepWasClamped && simulation.Work.SolverSteps > row.Steps)
                {
                    row.ClampedSteps++;
                }
                row.Steps = simulation.Work.SolverSteps;
            }

            watch.Stop();

            // The front is the furthest block that has taken a tenth of the way to the source.
            for (int i = length - 1; i >= 0; i--)
            {
                if (run[i].Temperature < 370f) continue;
                row.BlocksReached = i;
                break;
            }

            row.Substeps = simulation.Work.SolverSubsteps;
            row.SubstepsPerRealSecond = row.Substeps / (double)realSeconds;
            row.WorkPerRealSecond = row.Substeps
                * (double)(simulation.Solver.Nodes.Count + simulation.Solver.LinkCount) / realSeconds;
            row.MillisecondsPerRealSecond = watch.Elapsed.TotalMilliseconds / realSeconds;

            return row;
        }

        public static string ReachTable(IList<ReachRow> rows)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("profile".PadRight(14)).Append("freq".PadLeft(6))
              .Append("speed".PadLeft(7)).Append("heatScale".PadLeft(11))
              .Append("maxSub".PadLeft(8)).Append("blocks/s".PadLeft(10))
              .Append("substep/s".PadLeft(11)).Append("work/s".PadLeft(12))
              .Append("ms/s".PadLeft(8)).Append("clamped".PadLeft(9)).Append('\n');

            for (int i = 0; i < rows.Count; i++)
            {
                ReachRow r = rows[i];
                sb.Append(r.Label.PadRight(14))
                  .Append(r.Frequency.ToString().PadLeft(6))
                  .Append(r.Speed.ToString("n2").PadLeft(7))
                  .Append(r.HeatTimeScale.ToString("n0").PadLeft(11))
                  .Append(r.MaxSubsteps.ToString().PadLeft(8))
                  .Append(r.BlocksPerRealSecond.ToString("n1").PadLeft(10))
                  .Append(r.SubstepsPerRealSecond.ToString("n1").PadLeft(11))
                  .Append(r.WorkPerRealSecond.ToString("n0").PadLeft(12))
                  .Append(r.MillisecondsPerRealSecond.ToString("n2").PadLeft(8))
                  .Append((r.Steps == 0 ? "-" : (100.0 * r.ClampedSteps / r.Steps).ToString("n0") + "%")
                      .PadLeft(9))
                  .Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>One setting pairing, and what it cost and achieved over the same real time.</summary>
        public class PaceRow
        {
            public float Speed;
            public float HeatTimeScale;
            public int Frequency;

            public long Steps;
            public long Substeps;
            public long WorkUnits;
            public double Milliseconds;

            /// <summary>Simulated seconds advanced per real second.</summary>
            public double SimulatedPerReal;

            /// <summary>How far the tracked block moved over the run, in kelvin.</summary>
            public float TemperatureChange;
        }

        /// <summary>
        /// Whether halving <c>SimulationSpeed</c> and doubling <c>HeatTimeScale</c> buys anything. The
        /// arithmetic says the two cancel while a grid is substep-limited and that the trade is real
        /// when it is not, so this runs the pairings and reports both the cost and what the heat did.
        /// See configuration.md, Trading simulation speed for heat transfer.
        /// </summary>
        public static List<PaceRow> Pace(string shape, int targetCells, float realSeconds)
        {
            List<PaceRow> rows = new List<PaceRow>();

            // Constant product: the same thermal pace against the wall clock, if the theory holds.
            float[] speeds = { 1f, 0.5f, 0.25f };
            float[] scales = { 225f, 450f, 900f };

            for (int i = 0; i < speeds.Length; i++)
            {
                rows.Add(MeasurePace(shape, targetCells, realSeconds, speeds[i], scales[i], 4));
            }

            // And one that changes only Frequency, to show it cancelling out.
            rows.Add(MeasurePace(shape, targetCells, realSeconds, 1f, 225f, 1));
            rows.Add(MeasurePace(shape, targetCells, realSeconds, 1f, 225f, 16));

            return rows;
        }

        private static PaceRow MeasurePace(string shape, int targetCells, float realSeconds,
            float speed, float heatTimeScale, int frequency)
        {
            PaceRow row = new PaceRow();
            row.Speed = speed;
            row.HeatTimeScale = heatTimeScale;
            row.Frequency = frequency;

            ThermalSimulation simulation = BuildSettled(shape, targetCells);
            while (simulation.HasPendingWork) simulation.Update(TickSeconds, Worlds.Shadow());

            ThermalSettings settings = simulation.Settings;
            settings.SimulationSpeed = speed;
            settings.HeatTimeScale = heatTimeScale;
            settings.Frequency = frequency;

            // The budget would bound the substep count and hide the very effect being measured.
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            SeedSpread(simulation);

            // One block watched all the way through, so "did the heat keep pace" is a number.
            ThermalNode tracked = simulation.Solver.Nodes[simulation.Solver.Nodes.Count / 2];
            float before = tracked.Temperature;

            EnvironmentSample sample = Worlds.Space(new Vector3(0f, 1f, 0f));
            int frames = (int)(realSeconds / FrameSeconds);

            simulation.Work.Reset();
            SettleMemory();

            Stopwatch watch = Stopwatch.StartNew();
            for (int f = 0; f < frames; f++)
            {
                simulation.Update(FrameSeconds, sample);
            }
            watch.Stop();

            row.Milliseconds = watch.Elapsed.TotalMilliseconds;
            row.Steps = simulation.Work.SolverSteps;
            row.Substeps = simulation.Work.SolverSubsteps;
            row.WorkUnits = simulation.Work.SolverSubsteps
                * (simulation.Solver.Nodes.Count + simulation.Solver.LinkCount);
            row.SimulatedPerReal = simulation.SimulatedSecondsRun / realSeconds;
            row.TemperatureChange = tracked.Temperature - before;

            return row;
        }

        public static string PaceTable(IList<PaceRow> rows, float realSeconds)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("speed".PadLeft(7)).Append("heatScale".PadLeft(11))
              .Append("freq".PadLeft(6)).Append("steps".PadLeft(8))
              .Append("substeps".PadLeft(10)).Append("work/s".PadLeft(14))
              .Append("ms/real s".PadLeft(11)).Append("sim s/real s".PadLeft(14))
              .Append("dT over run".PadLeft(13)).Append('\n');

            for (int i = 0; i < rows.Count; i++)
            {
                PaceRow r = rows[i];
                sb.Append(r.Speed.ToString("n2").PadLeft(7))
                  .Append(r.HeatTimeScale.ToString("n0").PadLeft(11))
                  .Append(r.Frequency.ToString().PadLeft(6))
                  .Append(r.Steps.ToString("n0").PadLeft(8))
                  .Append(r.Substeps.ToString("n0").PadLeft(10))
                  .Append((r.WorkUnits / realSeconds).ToString("n0").PadLeft(14))
                  .Append((r.Milliseconds / realSeconds).ToString("n1").PadLeft(11))
                  .Append(r.SimulatedPerReal.ToString("n2").PadLeft(14))
                  .Append(r.TemperatureChange.ToString("n2").PadLeft(13))
                  .Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>What one stage of building a grid cost in memory.</summary>
        public class MemoryRow
        {
            public string Stage;
            public double Megabytes;
            public long Entries;
            public string Note = "";

            public double BytesPerBlock;
        }

        /// <summary>
        /// Where a grid's memory goes, attributed to the structure that took it.
        ///
        /// Measured by building the grid in stages and reading the managed heap between them,
        /// rather than by adding up what the type layouts ought to cost. The two disagree: a
        /// dictionary keyed on a twelve-byte vector spends about forty bytes an entry once its
        /// buckets, hash codes and load factor are counted, and a hash set over a bounding volume
        /// spends that for every cell of empty space inside a ship's envelope.
        ///
        /// Collected before each reading so what is reported is what is retained rather than what
        /// happened to be uncollected.
        /// </summary>
        public static List<MemoryRow> Memory(string shape, int targetCells)
        {
            List<MemoryRow> rows = new List<MemoryRow>();

            HashSet<Vector3I> cells = LoadShapes.Build(shape, targetCells);
            BlockModel[] tiers = Census.Models();

            long baseline = Settled();

            // 1. the block instances themselves
            GridBuilder builder = GridBuilder.Large();
            List<BlockInstance> instances = new List<BlockInstance>();
            int index = 0;
            foreach (Vector3I cell in cells)
            {
                BlockInstance block = new BlockInstance(
                    tiers[Census.TierAt(index++)], cell, BlockOrientation.Identity);
                instances.Add(block);
            }

            long afterInstances = Settled();
            int blocks = instances.Count;
            rows.Add(Row("BlockInstance", afterInstances - baseline, blocks, blocks,
                "one object per block, plus its cell and surface arrays"));

            // 2. the grid's own indexes
            GridModel grid = builder.Grid;
            for (int i = 0; i < instances.Count; i++) grid.Add(instances[i]);

            long afterGrid = Settled();
            rows.Add(Row("GridModel indexes", afterGrid - afterInstances, blocks, blocks,
                "by cell, by slot, and the flat list"));

            // 3. surfaces
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), grid);
            simulation.Surfaces.Rebuild(grid);

            long afterSurfaces = Settled();
            rows.Add(Row("SurfaceMap", afterSurfaces - afterGrid, simulation.Surfaces.CellCount, blocks,
                "two dictionaries, one entry per occupied cell each"));

            // 4. solver nodes and the conduction graph
            for (int i = 0; i < instances.Count; i++)
            {
                simulation.Solver.AddBlock(instances[i], 293.15f);
            }
            simulation.Solver.RebuildLinks();

            long afterSolver = Settled();
            rows.Add(Row("Solver", afterSolver - afterSurfaces, simulation.Solver.LinkCount, blocks,
                "nodes, mirrored arrays, links and their chains"));

            // 5. the room map, which floods the whole bounding volume.
            //
            // Two figures, because they differ by an order of magnitude and only one of them is
            // usually quoted. What the finished map retains is modest. What the pass needs while
            // it runs is a visited set over every cell of the bounding box, and that is live for
            // the whole pass — on a large grid it is the high-water mark of the entire mod.
            Vector3I extents = (grid.Max - grid.Min) + Vector3I.One;
            long volume = (long)extents.X * extents.Y * extents.Z;

            simulation.Rooms.RequestRestart(grid);

            long peak = afterSolver;
            while (simulation.Rooms.HasWorkPending)
            {
                simulation.Rooms.Step(65536);
                long now = GC.GetTotalMemory(false);
                if (now > peak) peak = now;
            }

            long afterRooms = Settled();

            // Which part of the volume the map is holding, because the answer decides whether this
            // row scales with the ship or with the box around it. External cells are a count; solid
            // and room cells are stored, so a hull whose interior maps as rooms costs far more here
            // than one whose interior maps as open air, at the same block count.
            RoomMap map = simulation.Rooms.Map;
            rows.Add(Row("RoomMap retained", afterRooms - afterSolver, volume, blocks,
                "stored: " + map.SolidCellCount.ToString("n0") + " solid + "
                + map.RoomCellCount.ToString("n0") + " cells in " + map.RoomCount.ToString("n0")
                + " rooms; counted, not stored: " + map.ExternalCellCount.ToString("n0") + " external"));
            rows.Add(Row("RoomMapper peak", peak - afterSolver, volume, blocks,
                "high-water mark while a pass runs — a visited set over the whole bounding box"));

            rows.Add(Row("TOTAL retained", afterRooms - baseline, blocks, blocks, ""));
            rows.Add(Row("TOTAL peak", peak - baseline, blocks, blocks,
                "what the process actually has to hold"));

            // Without this the readings above are nonsense, and quietly so. Nothing uses these
            // after the last stage, so the collection inside the final measurement is entitled to
            // reclaim the entire grid — which reported the retained total as zero and the room map
            // as having freed 142 MB it never held.
            GC.KeepAlive(simulation);
            GC.KeepAlive(instances);
            GC.KeepAlive(grid);
            GC.KeepAlive(cells);

            return rows;
        }

        private static MemoryRow Row(string stage, long bytes, long entries, int blocks, string note)
        {
            MemoryRow row = new MemoryRow();
            row.Stage = stage;
            row.Megabytes = bytes / (1024d * 1024d);
            row.Entries = entries;
            row.Note = note;
            row.BytesPerBlock = blocks == 0 ? 0d : bytes / (double)blocks;
            return row;
        }

        private static long Settled()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return GC.GetTotalMemory(true);
        }

        public static string MemoryTable(IList<MemoryRow> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("stage".PadRight(24)).Append("MB".PadLeft(10))
              .Append("B/block".PadLeft(10)).Append("entries".PadLeft(14))
              .Append("  what holds it").Append('\n');

            for (int i = 0; i < rows.Count; i++)
            {
                MemoryRow r = rows[i];
                sb.Append(r.Stage.PadRight(24))
                  .Append(r.Megabytes.ToString("n1").PadLeft(10))
                  .Append(r.BytesPerBlock.ToString("n0").PadLeft(10))
                  .Append(r.Entries.ToString("n0").PadLeft(14))
                  .Append("  ").Append(r.Note).Append('\n');
            }
            return sb.ToString();
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

        private static void RecordGc(HitchResult result, int gen0, int gen1, int gen2, long allocated)
        {
            result.Gen0 = GC.CollectionCount(0) - gen0;
            result.Gen1 = GC.CollectionCount(1) - gen1;
            result.Gen2 = GC.CollectionCount(2) - gen2;
            result.AllocatedMb = (GC.GetTotalAllocatedBytes(false) - allocated) / (1024d * 1024d);
        }

        /// <summary>Folds one tick's stage timings into a run's per-stage worst calls.</summary>
        private static void RecordStages(HitchResult result, StageTimings timings, int tick)
        {
            double topology = timings.WorstMs(SimulationPhase.Topology);
            if (topology > result.TopologyMs) { result.TopologyMs = topology; result.TopologyTick = tick; }

            double rooms = timings.WorstMs(SimulationPhase.RoomMapping);
            if (rooms > result.RoomMappingMs) { result.RoomMappingMs = rooms; result.RoomMappingTick = tick; }

            double exposure = timings.WorstMs(SimulationPhase.Exposure);
            if (exposure > result.ExposureMs) { result.ExposureMs = exposure; result.ExposureTick = tick; }

            double solver = timings.WorstMs(SimulationPhase.Solver);
            if (solver > result.SolverMs) { result.SolverMs = solver; result.SolverTick = tick; }
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

        /// <summary>
        /// Clears the harness's own construction garbage before a measured run.
        ///
        /// Building a grid of half a million blocks leaves hundreds of megabytes of dead
        /// intermediate state — the shape's cell set, the builder's list, the flood fill's
        /// working map. Collecting it at some arbitrary point during the measured ticks charges
        /// a two-hundred-millisecond gen-2 collection to whichever tick was running, and it reads
        /// exactly like a simulation stall. The first run of the hitch benchmark reported one at
        /// tick 1 and it was this.
        ///
        /// Collections the simulation itself causes are still counted and reported, which is the
        /// distinction worth keeping: the harness's garbage is noise, and the mod's is a finding.
        /// </summary>
        private static void SettleMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>
        /// Set false to build hulls the way the benchmarks used to — heavy armour with a grating
        /// in eight — instead of from the measured block census.
        ///
        /// Kept only so an old figure can be reproduced. The legacy mix is not a ship: its
        /// lightest block is twelve times heavier than a real ship's, so it asks for 2.25 substeps
        /// where a real hull asks for twenty to thirty, and it makes the solver look an order of
        /// magnitude cheaper than it is. See <see cref="Census"/>.
        /// </summary>
        public static bool UseCensus = true;

        /// <summary>Builds a grid and takes it all the way to a mapped, settled state.</summary>
        public static ThermalSimulation BuildSettled(string shape, int targetCells)
        {
            HashSet<Vector3I> cells = LoadShapes.Build(shape, targetCells);

            GridBuilder builder = GridBuilder.Large();

            if (UseCensus)
            {
                builder.PlaceCensus(cells);
            }
            else
            {

                int index = 0;
                foreach (Vector3I cell in cells)
                {
                    builder.Place(Census.Models()[Census.TierAt(index++)], cell);
                }
            }

            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), builder.Grid);
            for (int i = 0; i < builder.Placed.Count; i++)
            {
                simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            }

            simulation.RebuildAll();
            simulation.Solver.CollectDiagnostics = CollectDiagnostics;
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


        // ---- the substep floor -------------------------------------------------------------

        /// <summary>One row of the substep floor sweep: what a cap buys, and what it costs.</summary>
        public class FloorRow
        {
            public int Cap;
            public float RequiredSubsteps;
            public double Milliseconds;
            public int Nodes;
            public int Floored;
            public float MaxError;
            public double RmsError;
            /// <summary>
            /// Hottest block at the end of the run, and how far that is from the uncapped run's.
            ///
            /// The figure that decides overheat damage, and the one a diffusion test cannot
            /// answer: a spread of temperatures left to even out says what the cap does to a
            /// transient, while a ship held at equilibrium by its own thrusters says what it does
            /// to the number a player is actually looking at.
            /// </summary>
            public float PeakTemperature;
            public float PeakError;

            /// <summary>
            /// Simulated seconds in one solver step, which every other figure in the row is
            /// relative to.
            ///
            /// A step of `dt` needs `dt * r_max / safety` substeps, so the demand — and with it
            /// which blocks a given cap reaches, and what that cap is worth — is proportional to
            /// this. A table without it cannot be read, and one read against the wrong value is
            /// wrong by exactly the ratio: the figures in stiffness.md were taken at a quarter
            /// second and quoted against a shipped default of an eighth, which doubled every
            /// speed-up on the page.
            /// </summary>
            public float StepSeconds;

            /// <summary>Airspeed the row was measured at, m/s. Zero is vacuum.</summary>
            public float AirSpeed;
        }

        /// <summary>
        /// What <c>MaxSubstepsPerBlock</c> buys and what it costs, swept across caps. Built from the
        /// block <see cref="Census"/>, whose *shape* is what decides how many blocks a cap reaches, and
        /// measured against the uncapped run rather than an analytic answer, because the question is
        /// how far the approximation moves it and on which blocks.
        /// See stiffness.md, A per-block substep cap.
        /// </summary>
        public static List<FloorRow> SubstepFloor(string shape, int size, int steps,
            IList<int> caps, Action<string> log = null, bool driven = false, int frequency = 0,
            float airSpeed = 0f)
        {
            List<FloorRow> rows = new List<FloorRow>();
            float[] reference = null;
            float referencePeak = 0f;

            // The first row measured would otherwise be measuring the JIT.
            RunFloor(shape, Math.Min(size, 2000), 2, 0, null, driven, frequency, airSpeed);

            for (int i = 0; i < caps.Count; i++)
            {
                if (log != null) log("cap " + caps[i]);

                FloorRow row = RunFloor(shape, size, steps, caps[i], reference, driven, frequency,
                    airSpeed);
                if (reference == null)
                {
                    reference = lastTemperatures;
                    referencePeak = row.PeakTemperature;
                }

                row.PeakError = row.PeakTemperature - referencePeak;
                rows.Add(row);
            }

            return rows;
        }

        /// <summary>The temperatures the last <see cref="RunFloor"/> ended on.</summary>
        private static float[] lastTemperatures;

        private static FloorRow RunFloor(string shape, int size, int steps, int cap,
            float[] reference, bool driven, int frequency, float airSpeed)
        {
            HashSet<Vector3I> cells = LoadShapes.Build(shape, size);

            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(cells);

            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubstepsPerBlock = cap;

            // Zero means the shipped default, which is what a table meant to inform a default
            // should be read at. Anything else is here so a table taken at another step length
            // can be reproduced rather than argued about.
            if (frequency > 0) settings.Frequency = frequency;

            // Both of the bounds that would otherwise hide what the floor does: one refuses the
            // substeps the estimate asks for, the other shortens the step rather than pay.
            settings.MaxSubsteps = 4096;
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            ThermalSimulation simulation = new ThermalSimulation(settings, builder.Grid);
            for (int i = 0; i < builder.Placed.Count; i++)
            {
                simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            }
            simulation.RebuildAll();

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int count = nodes.Count;

            if (driven)
            {
                // The measured share of heat producers, at the measured wattage, left to reach
                // the equilibrium they hold the hull at. Nothing is seeded: the gradient this
                // measures is the one the ship makes for itself.
                Census.DriveCensus(simulation);
            }
            else
            {
                SeedSpread(simulation);
            }


            // **Air is where a per-block floor has most to reach**, because convection is what
            // makes a light block stiff — the vacuum column is the one the floor was designed
            // against and the one that understates it. See backlog.md, `C3` and `C19`.
            EnvironmentSample sample = airSpeed > 0f
                ? Worlds.Flight(1f, airSpeed)
                : Worlds.Space(new Vector3(0f, 1f, 0f));

            FloorRow row = new FloorRow();
            row.Cap = cap;
            row.AirSpeed = airSpeed;
            row.StepSeconds = settings.StepSeconds;
            row.Nodes = count;
            // Both read after a step rather than before one, and both for the same reason: the
            // stability estimate and the floor read the environment, so a demand taken before the
            // grid has met its air is a vacuum figure wearing an atmospheric label — which is what
            // this table said the first time it was run in air.
            simulation.StepExact(1, sample);

            row.RequiredSubsteps = simulation.Solver.LastRequiredSubsteps;
            row.Floored = cap > 0 ? simulation.Solver.FlooredNodes : 0;

            Stopwatch watch = Stopwatch.StartNew();
            simulation.StepExact(steps, sample);
            watch.Stop();
            row.Milliseconds = watch.Elapsed.TotalMilliseconds;

            float[] result = new float[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = nodes[i].Temperature;
                if (result[i] > row.PeakTemperature) row.PeakTemperature = result[i];
            }
            lastTemperatures = result;

            if (reference != null && reference.Length == count)
            {
                double sumSquares = 0;
                for (int i = 0; i < count; i++)
                {
                    float error = Math.Abs(result[i] - reference[i]);
                    sumSquares += (double)error * error;
                    if (error > row.MaxError) row.MaxError = error;

                }

                row.RmsError = Math.Sqrt(sumSquares / count);
            }

            return row;
        }

        public static string FloorTable(IList<FloorRow> rows)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("cap".PadLeft(6))
              .Append("substeps".PadLeft(10))
              .Append("ms".PadLeft(10))
              .Append("x uncapped".PadLeft(12))
              .Append("floored".PadLeft(11))
              .Append("of".PadLeft(11))
              .Append("peak K".PadLeft(10))
              .Append("peakErr".PadLeft(10))
              .Append("maxErr K".PadLeft(11))
              .Append("rmsErr K".PadLeft(11))
              .Append('\n');

            // Stated rather than assumed: every figure below is proportional to it.
            if (rows.Count > 0)
            {
                sb.Append("  step ")
                  .Append(rows[0].StepSeconds.ToString("n4"))
                  .Append(" s (Frequency ")
                  .Append((1f / rows[0].StepSeconds).ToString("n0"))
                  .Append("), ")
                  .Append(rows[0].AirSpeed > 0f
                      ? "thick air at " + rows[0].AirSpeed.ToString("n0") + " m/s"
                      : "vacuum")
                  .Append(", and every substep count below is proportional to the step\n");
            }

            double baseline = rows.Count > 0 ? rows[0].Milliseconds : 0;

            for (int i = 0; i < rows.Count; i++)
            {
                FloorRow row = rows[i];

                sb.Append((row.Cap <= 0 ? "off" : row.Cap.ToString()).PadLeft(6))
                  .Append(row.RequiredSubsteps.ToString("n2").PadLeft(10))
                  .Append(row.Milliseconds.ToString("n0").PadLeft(10))
                  .Append((row.Milliseconds <= 0 ? "-" : (baseline / row.Milliseconds).ToString("n1") + "x").PadLeft(12))
                  .Append(row.Floored.ToString("n0").PadLeft(11))
                  .Append(row.Nodes.ToString("n0").PadLeft(11))
                  .Append(row.PeakTemperature.ToString("n1").PadLeft(10))
                  .Append((i == 0 ? "-" : row.PeakError.ToString("n3")).PadLeft(10))
                  .Append((i == 0 ? "-" : row.MaxError.ToString("n4")).PadLeft(11))
                  .Append((i == 0 ? "-" : row.RmsError.ToString("n4")).PadLeft(11))
                  .Append('\n');
            }

            return sb.ToString();
        }

        // ---- the substep ceiling -----------------------------------------------------------

        /// <summary>
        /// One row of the substep ceiling sweep: what refusing a demand does to the answer.
        ///
        /// The floor sweep above is the other half of the same question. `MaxSubstepsPerBlock`
        /// declines to resolve a *block* and says so by raising its capacity; `MaxSubsteps`
        /// declines to resolve the *step* and says nothing at all — it hands back the ceiling and
        /// integrates a demand it refused. See stiffness.md, What refusing the demand costs.
        /// </summary>
        public class CeilingRow
        {
            /// <summary>The <c>MaxSubsteps</c> this row ran at.</summary>
            public int Ceiling;

            /// <summary>What the stiffest element asked for at the start of the run.</summary>
            public float RequiredSubsteps;

            /// <summary>Substeps actually granted, averaged over the run.</summary>
            public double MeanGranted;

            /// <summary>
            /// How far the ceiling is over-subscribed: what was demanded over what it granted.
            ///
            /// **The ratio is the quantity, not the count.** A demand of 73 refused to 64 and a
            /// demand of 36 refused to 32 are the same approximation asked of the integrator, which
            /// is what lets a rig answer for a population it is not a member of.
            /// </summary>
            public float Oversubscription;

            /// <summary>Whether the ceiling refused what the estimate asked for.</summary>
            public bool Bound;

            public double Milliseconds;
            public int Nodes;

            /// <summary>Worst and root-mean-square departure from the run that was granted its demand.</summary>
            public float MaxError;
            public double RmsError;

            /// <summary>
            /// Hottest block at the end of the run, and how far that is from the granted run's.
            ///
            /// The figure overheat damage is taken off, so it is the one that says whether a
            /// refused demand changes what happens to a player's ship rather than only what the
            /// numbers look like.
            /// </summary>
            public float PeakTemperature;
            public float PeakError;

            /// <summary>Simulated seconds in one solver step; every substep figure is proportional to it.</summary>
            public float StepSeconds;

            /// <summary>Airspeed the run was made at, m/s, and the air density it met.</summary>
            public float Speed;
            public float AirDensity;

            /// <summary>Which hull this row was measured on. See <see cref="CeilingFixtures"/>.</summary>
            public string Fixture;

            /// <summary>What the fixture built, so a row cannot report a plumbed hull with no ring.</summary>
            public int CoolantLoops;
            public int RoomsWithAir;

            /// <summary>Hottest coolant parcel or room air at the end of the run, K.</summary>
            public float PeakCoupledTemperature;
        }

        /// <summary>
        /// The hulls the ceiling sweep can be asked for, one per element that carries heat.
        ///
        /// The ladder was measured on blocks alone for as long as it existed, and blocks are the
        /// one element whose exchanges are all pairwise. A coolant parcel and a room's air are
        /// each one mass carrying every link on it, which is the shape a pairwise bound cannot
        /// hold on its own. See stiffness.md, What refusing the demand costs.
        /// </summary>
        public static class CeilingFixtures
        {
            public const string Census = "census";
            public const string Plumbed = "plumbed";
            public const string Pressurised = "pressurised";

            /// <summary>Reactors cooled by rings: the hull where the plumbing sets the demand.</summary>
            public const string Rings = "rings";
        }

        /// <summary>
        /// What <c>MaxSubsteps</c> refusing a demand costs, swept across ceilings, in the
        /// environment where the demand is actually large.
        ///
        /// <para>
        /// **Air is the case, not vacuum.** The same hull demands a few substeps in vacuum and
        /// tens of them at flying speed in thick atmosphere, so a ceiling sweep taken in vacuum
        /// measures a bound that never binds. The default here is thick air at 200 m/s, which is
        /// the `reentry` scenario's airflow and the environment
        /// [balance.md](../../docs/balance.md) scores `G6` in.
        /// </para>
        ///
        /// <para>
        /// Error is against the run granted everything it asked for, in the same way the floor
        /// sweep is, because the question is how far the approximation moves the answer rather
        /// than whether either run is right in some absolute sense (`E7`).
        /// </para>
        /// </summary>
        public static List<CeilingRow> SubstepCeiling(string shape, int size, int steps,
            IList<int> ceilings, Action<string> log = null, bool driven = false, int frequency = 0,
            float speed = 200f, float airDensity = 1f, string fixture = CeilingFixtures.Census,
            float flow = 0f)
        {
            List<CeilingRow> rows = new List<CeilingRow>();
            float[] reference = null;
            float referencePeak = 0f;

            // The first row measured would otherwise be measuring the JIT.
            CeilingRow probe = RunCeiling(shape, Math.Min(size, 2000), 2, Hulls.Unbounded, null,
                driven, frequency, speed, airDensity, fixture, flow);

            // No ceilings given means the ladder of over-subscriptions rather than a ladder of
            // counts, resolved against what this hull in this air actually demands. A ceiling of 64
            // means nothing on a hull that asks for six.
            if (ceilings == null || ceilings.Count == 0)
            {
                ceilings = CeilingLadder(probe.RequiredSubsteps);
            }

            for (int i = 0; i < ceilings.Count; i++)
            {
                if (log != null) log("ceiling " + ceilings[i]);

                CeilingRow row = RunCeiling(shape, size, steps, ceilings[i], reference, driven,
                    frequency, speed, airDensity, fixture, flow);
                if (reference == null)
                {
                    reference = lastTemperatures;
                    referencePeak = row.PeakTemperature;
                }

                row.PeakError = row.PeakTemperature - referencePeak;
                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// The over-subscriptions the sweep asks about: what fraction of its demand a step is
        /// granted. **1.15 is the shipped configuration's own breach** — the 49-ship panel's p99
        /// demand of 73.4 against the 64 `MaxSubsteps` grants — and the rest of the ladder is there
        /// so a reader can see where the approximation stops being free.
        /// See [balance.md](../../docs/balance.md), Air is where the substep budget goes.
        /// </summary>
        public static readonly float[] Oversubscriptions = { 1.15f, 1.5f, 2f, 3f, 4.5f, 9f };

        /// <summary>Ceilings that put a demand at each rung of <see cref="Oversubscriptions"/>.</summary>
        public static List<int> CeilingLadder(float demand)
        {
            List<int> ceilings = new List<int>();
            ceilings.Add(Hulls.Unbounded);

            for (int i = 0; i < Oversubscriptions.Length; i++)
            {
                int ceiling = (int)Math.Round(demand / Oversubscriptions[i]);
                if (ceiling < 1) ceiling = 1;
                if (!ceilings.Contains(ceiling)) ceilings.Add(ceiling);
            }

            return ceilings;
        }

        private static CeilingRow RunCeiling(string shape, int size, int steps, int ceiling,
            float[] reference, bool driven, int frequency, float speed, float airDensity,
            string fixture, float flow)
        {
            ThermalSettings settings = new ThermalSettings();
            if (frequency > 0) settings.Frequency = frequency;

            // The ceiling is the subject. The visit budget is not: it shortens a step rather than
            // coarsening it, which is a different approximation and would be mixed into the error.
            settings.MaxSubsteps = ceiling;
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            // The fixture decides which element carries the heat. The two lumped masses are built
            // by WorstCases, which counts what it managed to build — a plumbed hull with no ring
            // is the failure this sweep would otherwise report as a result.
            ThermalSimulation simulation;
            WorstCases.Built built = null;

            if (fixture == CeilingFixtures.Plumbed)
            {
                built = WorstCases.Plumbed(shape, size, 8, settings);
                simulation = built.Simulation;
            }
            else if (fixture == CeilingFixtures.Rings)
            {
                // Sized in rings rather than in blocks: ten cells each, and the point of the
                // fixture is what one ring does rather than how many there are.
                built = WorstCases.HeatedRings(Math.Max(1, size / 10), Census.ProducerWatts, settings,
                    flow);
                simulation = built.Simulation;
            }
            else if (fixture == CeilingFixtures.Pressurised)
            {
                built = WorstCases.Pressurised(shape, size, settings);
                simulation = built.Simulation;
            }
            else
            {
                GridBuilder builder = GridBuilder.Large();
                builder.PlaceCensus(LoadShapes.Build(shape, size));

                simulation = new ThermalSimulation(settings, builder.Grid);
                for (int i = 0; i < builder.Placed.Count; i++)
                {
                    simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
                }
                simulation.RebuildAll();
            }

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int count = nodes.Count;

            if (driven)
            {
                Census.DriveCensus(simulation);
            }
            else
            {
                SeedSpread(simulation);
            }

            EnvironmentSample sample = Worlds.Flight(airDensity, speed);

            CeilingRow row = new CeilingRow();
            row.Ceiling = ceiling;
            row.StepSeconds = settings.StepSeconds;
            row.Nodes = count;
            row.Speed = speed;
            row.AirDensity = airDensity;
            row.Fixture = fixture;
            row.CoolantLoops = built != null ? built.CoolantLoops : 0;
            row.RoomsWithAir = built != null ? built.RoomsWithAir : 0;

            // Taken against the environment the run is made in: the estimate reads the convection
            // coefficient, so a demand sampled in vacuum is the wrong number by the whole reason
            // this sweep exists.
            simulation.StepExact(1, sample);
            row.RequiredSubsteps = simulation.Solver.LastRequiredSubsteps;

            simulation.Work.Reset();
            Stopwatch watch = Stopwatch.StartNew();
            simulation.StepExact(steps, sample);
            watch.Stop();
            row.Milliseconds = watch.Elapsed.TotalMilliseconds;

            row.MeanGranted = simulation.Work.SolverSteps > 0
                ? (double)simulation.Work.SolverSubsteps / simulation.Work.SolverSteps
                : 0d;
            row.Bound = ceiling < row.RequiredSubsteps;
            row.Oversubscription = ceiling > 0 && row.Bound
                ? row.RequiredSubsteps / ceiling
                : 1f;

            float[] result = new float[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = nodes[i].Temperature;
                if (result[i] > row.PeakTemperature) row.PeakTemperature = result[i];
            }
            lastTemperatures = result;

            // The lumped masses read apart from the blocks. A ring that has run away shows in a
            // block peak only through the links it is already overshooting on, and whether a
            // refused demand approximates or diverges on the path that carries the heat is the
            // whole question this sweep exists to ask.
            IList<CoolantLoop> loops = simulation.Solver.Loops;
            for (int l = 0; l < loops.Count; l++)
            {
                float hottest = loops[l].HottestSegment;
                if (hottest > row.PeakCoupledTemperature) row.PeakCoupledTemperature = hottest;
            }

            IList<RoomAirNode> air = simulation.Solver.RoomAir;
            for (int r = 0; r < air.Count; r++)
            {
                if (!air[r].HasAir) continue;
                if (air[r].Temperature > row.PeakCoupledTemperature)
                {
                    row.PeakCoupledTemperature = air[r].Temperature;
                }
            }

            if (reference != null && reference.Length == count)
            {
                double sumSquares = 0;
                for (int i = 0; i < count; i++)
                {
                    float error = Math.Abs(result[i] - reference[i]);
                    sumSquares += (double)error * error;
                    if (error > row.MaxError) row.MaxError = error;
                }

                row.RmsError = Math.Sqrt(sumSquares / count);
            }

            return row;
        }

        public static string CeilingTable(IList<CeilingRow> rows)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("ceiling".PadLeft(9))
              .Append("demanded".PadLeft(10))
              .Append("granted".PadLeft(10))
              .Append("over".PadLeft(7))
              .Append("ms".PadLeft(10))
              .Append("x granted".PadLeft(11))
              .Append("peak K".PadLeft(10))
              .Append("coupled K".PadLeft(12))
              .Append("peakErr".PadLeft(10))
              .Append("maxErr K".PadLeft(11))
              .Append("rmsErr K".PadLeft(11))
              .Append('\n');

            if (rows.Count > 0)
            {
                sb.Append("  step ")
                  .Append(rows[0].StepSeconds.ToString("n4"))
                  .Append(" s (Frequency ")
                  .Append((1f / rows[0].StepSeconds).ToString("n0"))
                  .Append("), air ")
                  .Append(rows[0].AirDensity.ToString("n2"))
                  .Append(" at ")
                  .Append(rows[0].Speed.ToString("n0"))
                  .Append(" m/s, ")
                  .Append(rows[0].Nodes.ToString("n0"))
                  .Append(" nodes, ")
                  .Append(rows[0].Fixture ?? CeilingFixtures.Census)
                  .Append(rows[0].CoolantLoops > 0 ? ", " + rows[0].CoolantLoops + " rings" : "")
                  .Append(rows[0].RoomsWithAir > 0 ? ", " + rows[0].RoomsWithAir + " rooms of air" : "")
                  .Append('\n');
            }

            double baseline = rows.Count > 0 ? rows[0].Milliseconds : 0;

            for (int i = 0; i < rows.Count; i++)
            {
                CeilingRow row = rows[i];

                sb.Append((row.Ceiling >= Hulls.Unbounded ? "granted" : row.Ceiling.ToString()).PadLeft(9))
                  .Append(row.RequiredSubsteps.ToString("n2").PadLeft(10))
                  .Append(row.MeanGranted.ToString("n2").PadLeft(10))
                  .Append((row.Bound ? row.Oversubscription.ToString("n2") + "x" : "-").PadLeft(7))
                  .Append(row.Milliseconds.ToString("n0").PadLeft(10))
                  .Append((row.Milliseconds <= 0 ? "-" : (baseline / row.Milliseconds).ToString("n2") + "x").PadLeft(11))
                  .Append(row.PeakTemperature.ToString("n1").PadLeft(10))
                  .Append((row.PeakCoupledTemperature > 0f
                      ? row.PeakCoupledTemperature.ToString("n1") : "-").PadLeft(12))
                  .Append((i == 0 ? "-" : row.PeakError.ToString("n3")).PadLeft(10))
                  .Append((i == 0 ? "-" : row.MaxError.ToString("n4")).PadLeft(11))
                  .Append((i == 0 ? "-" : row.RmsError.ToString("n4")).PadLeft(11))
                  .Append('\n');
            }

            return sb.ToString();
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
              .Append("tick ms".PadLeft(9))
              .Append("cap".PadLeft(5))
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
                  .Append(r.BoundedStepMs.ToString("n2").PadLeft(9))
                  .Append((r.SubstepBudget == int.MaxValue ? "-" : r.SubstepBudget.ToString()).PadLeft(5))
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
              .Append("exposureMs,solverStepMs,substeps,boundedStepMs,substepBudget,")
              .Append("nsPerLinkVisit,msPerSimulatedSecond,")
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
                  .Append(r.BoundedStepMs.ToString("f4")).Append(',')
                  .Append(r.SubstepBudget == int.MaxValue ? -1 : r.SubstepBudget).Append(',')
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
