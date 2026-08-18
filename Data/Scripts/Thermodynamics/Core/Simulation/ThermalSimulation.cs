using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// One grid's complete thermal simulation: block layout, surface map, room mapping, solver,
    /// coolant loops and scheduling, wired together.
    ///
    /// This is the whole public surface a host needs. The game adapter mirrors block changes in
    /// and pumps <see cref="Update"/> once a frame; the test harness does exactly the same thing
    /// with synthetic data.
    /// </summary>
    public class ThermalSimulation
    {
        private readonly ThermalSettings settings;
        private readonly GridModel grid;
        private readonly SurfaceMap surfaces;
        private readonly RoomMapper rooms;
        private readonly ThermalSolver solver;
        private readonly SimulationScheduler scheduler;
        private readonly SimulationWork work = new SimulationWork();

        private LoopThermalProperties loopProperties = LoopThermalProperties.Default();
        private PlanetThermalProperties planet = PlanetThermalProperties.Default();

        private bool topologyDirty;
        private bool exposureDirty;

        /// <summary>Temperature new blocks start at, K.</summary>
        public float DefaultTemperature = 293.15f;

        /// <summary>
        /// Optional stage timing. Null means no instrumentation at all, which is what shipping
        /// worlds run with; the cost of leaving the hooks in is one null check per stage.
        /// </summary>
        public ISimulationProfiler Profiler;

        public ThermalSimulation(ThermalSettings settings, GridModel grid)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (grid == null) throw new ArgumentNullException("grid");

            this.settings = settings;
            this.grid = grid;

            surfaces = new SurfaceMap();
            rooms = new RoomMapper(surfaces);
            solver = new ThermalSolver(settings, grid, surfaces);
            scheduler = new SimulationScheduler(settings);

            // One instance shared by both, so a caller reads one set of counters for the whole
            // update rather than adding up two objects' and hoping it caught them all.
            solver.Work = work;
            rooms.Work = work;

            rooms.Completed += OnRoomsCompleted;
        }

        /// <summary>
        /// How much work this simulation's one-shot stages have done. Counted, not timed — see
        /// <see cref="SimulationWork"/> for why a load test wants counts and a stutter wants
        /// milliseconds.
        /// </summary>
        public SimulationWork Work { get { return work; } }

        public ThermalSettings Settings { get { return settings; } }
        public GridModel Grid { get { return grid; } }
        public SurfaceMap Surfaces { get { return surfaces; } }
        public RoomMapper Rooms { get { return rooms; } }
        public ThermalSolver Solver { get { return solver; } }
        public SimulationScheduler Scheduler { get { return scheduler; } }

        public PlanetThermalProperties Planet
        {
            get { return planet; }
            set { planet = value ?? PlanetThermalProperties.Default(); }
        }

        public LoopThermalProperties LoopProperties
        {
            get { return loopProperties; }
            set
            {
                loopProperties = (value ?? LoopThermalProperties.Default());
                topologyDirty = true;
            }
        }

        /// <summary>
        /// Blocks damaged by heat during the last <see cref="Update"/> or
        /// <see cref="StepExact"/>, across every step it ran.
        ///
        /// The solver clears its own list each step, so a host reading that directly would miss
        /// the damage from all but the final step of a multi-step update — which is exactly what
        /// happens whenever the simulation is running faster than the host polls.
        /// </summary>
        public IList<OverheatEvent> Overheats
        {
            get { return overheats; }
        }

        /// <summary>
        /// Threshold crossings during the last <see cref="Update"/> or <see cref="StepExact"/>,
        /// across every step it ran. Accumulated here for the same reason overheats are: the
        /// solver's own list only survives one step.
        /// </summary>
        public IList<ThresholdCrossing> Crossings
        {
            get { return crossings; }
        }

        /// <summary>Temperatures being watched on this grid.</summary>
        public ThermalThresholds Thresholds
        {
            get { return solver.Thresholds; }
        }

        private readonly List<OverheatEvent> overheats = new List<OverheatEvent>();
        private readonly List<ThresholdCrossing> crossings = new List<ThresholdCrossing>();

        /// <summary>
        /// Simulated seconds this simulation has chosen not to advance, because advancing them
        /// would have cost more than one step is allowed.
        ///
        /// Reported rather than hidden. A grid running below real time is a legitimate state and
        /// the one this trade is for, but it is also the difference between a ship that cools in
        /// a minute and one that takes three, so it has to be visible to anyone reading a report
        /// and wondering why heat is moving slowly.
        /// </summary>
        public double SimulatedSecondsSkipped { get; private set; }

        /// <summary>
        /// How much of real time this simulation is keeping up with, 0..1. One when nothing has
        /// been skipped.
        /// </summary>
        public double SimulationRate
        {
            get
            {
                double owed = SimulatedSecondsRun + SimulatedSecondsSkipped;
                return owed <= 0d ? 1d : SimulatedSecondsRun / owed;
            }
        }

        /// <summary>Simulated seconds actually advanced.</summary>
        public double SimulatedSecondsRun { get; private set; }

        /// <summary>
        /// Substeps a step is currently allowed, from <c>MaxLinkVisitsPerStep</c> and the size of
        /// the conduction graph. <see cref="int.MaxValue"/> when the bound is switched off.
        /// </summary>
        public int SubstepBudget
        {
            get
            {
                int budgetVisits = settings.MaxLinkVisitsPerStep;
                if (budgetVisits <= 0) return int.MaxValue;

                int links = solver.LinkCount;
                if (links <= 0) return int.MaxValue;

                int budget = budgetVisits / links;
                return budget < 1 ? 1 : budget;
            }
        }

        /// <summary>
        /// The longest step this grid can afford, in simulated seconds, given how many link
        /// visits a step is allowed and how stiff the grid currently is.
        ///
        /// Returns the full step whenever it fits, which on anything below roughly a hundred
        /// thousand blocks is always.
        ///
        /// Public so a benchmark can measure what a tick actually pays rather than what an
        /// unbounded step would cost — the two diverge on exactly the grids the budget is for.
        /// </summary>
        public float AffordableStepSeconds(float seconds)
        {
            int budgetVisits = settings.MaxLinkVisitsPerStep;
            if (budgetVisits <= 0) return seconds;

            int links = solver.LinkCount;
            if (links <= 0) return seconds;

            // At least one substep, however large the grid: a step that cannot afford a single
            // pass over its links is a grid that cannot be simulated at all, and running slowly
            // is better than not running.
            int substepBudget = budgetVisits / links;
            if (substepBudget < 1) substepBudget = 1;

            float required = solver.RequiredSubsteps(seconds);
            if (required <= substepBudget) return seconds;

            // The substep estimate is proportional to the step length, so scaling the length by
            // the ratio lands exactly on the budget.
            return seconds * (substepBudget / required);
        }

        private void RunSteps(int steps, ref EnvironmentState state)
        {
            overheats.Clear();
            crossings.Clear();

            for (int i = 0; i < steps; i++)
            {
                float seconds = AffordableStepSeconds(settings.StepSeconds);

                if (seconds < settings.StepSeconds)
                {
                    SimulatedSecondsSkipped += settings.StepSeconds - seconds;
                }
                SimulatedSecondsRun += seconds;

                solver.Step(seconds, state);

                IList<OverheatEvent> stepOverheats = solver.Overheats;
                for (int o = 0; o < stepOverheats.Count; o++)
                {
                    overheats.Add(stepOverheats[o]);
                }

                IList<ThresholdCrossing> stepCrossings = solver.Crossings;
                for (int c = 0; c < stepCrossings.Count; c++)
                {
                    crossings.Add(stepCrossings[c]);
                }
            }
        }

        // ---- topology ----------------------------------------------------------------------

        /// <summary>Adds a block to both the layout and the simulation.</summary>
        public ThermalNode AddBlock(BlockInstance block)
        {
            return AddBlock(block, DefaultTemperature);
        }

        public ThermalNode AddBlock(BlockInstance block, float initialTemperature)
        {
            grid.Add(block);
            surfaces.AddBlock(block);
            ThermalNode node = solver.AddBlock(block, initialTemperature);
            MarkTopologyDirty();
            return node;
        }

        public void RemoveBlock(BlockInstance block)
        {
            solver.RemoveBlock(block);
            surfaces.RemoveBlock(block);
            grid.Remove(block);
            MarkTopologyDirty();
        }

        /// <summary>
        /// Call after changing anything that alters sealing or mounting on an existing block —
        /// a door opening, a block finishing construction.
        /// </summary>
        public void RefreshBlock(BlockInstance block)
        {
            if (block == null) return;
            block.RefreshSurfaces();
            surfaces.RemoveBlock(block);
            surfaces.AddBlock(block);
            MarkTopologyDirty();
        }

        /// <summary>
        /// Call after a change that alters only what a block <em>seals</em> — a door opening or
        /// closing.
        ///
        /// This does not remap the grid. A door does not move a wall: the rooms either side of it
        /// are the same rooms whether it is open or shut, and the mapper already knows the door
        /// as a portal between them. So the work here is to update the live surface bits, resolve
        /// the portals into which rooms now reach open air, and refresh the exposure of the
        /// blocks facing the rooms that changed. That is a walk over the doors and a handful of
        /// blocks, against a flood fill of the whole bounding box and a pass over every node —
        /// which, on a ship with a busy airlock, is the difference between free and not.
        /// </summary>
        public void RefreshBlockSealing(BlockInstance block)
        {
            if (block == null) return;

            block.RefreshSurfaces();
            surfaces.RemoveBlock(block);
            surfaces.AddBlock(block);

            // A block the mapper has never seen as a door — one placed since the last pass —
            // has no portal, so the map cannot answer for it and has to be rebuilt.
            if (!rooms.Knows(block))
            {
                rooms.RequestRestart(grid);
                return;
            }

            Begin(SimulationPhase.RoomMapping);
            bool changed = rooms.Map.RefreshVenting();
            End(SimulationPhase.RoomMapping);

            if (!changed) return;

            Begin(SimulationPhase.Exposure);
            solver.RefreshExposureAround(rooms.Map, rooms.Map.ChangedRooms);

            // A room that has just been opened stops holding air, and one that has just been shut
            // starts. Both are rebuilds of a room, not of the ship: the work is proportional to
            // the rooms that changed.
            solver.RebuildRoomAir(rooms.Map);
            End(SimulationPhase.Exposure);
        }

        /// <summary>
        /// True while some budgeted pass still has work left — the room flood fill, or the
        /// exposure refresh that follows it.
        ///
        /// Worth having as one question rather than two. Now that both stages are spread over
        /// ticks, a caller that waits on only the mapper stops one stage early, and everything
        /// that waits for a grid to settle — the benchmarks, the load tests, the scenario runner
        /// — was written when the mapper was the only budgeted thing there was.
        /// </summary>
        public bool HasPendingWork
        {
            get { return rooms.HasWorkPending || solver.ExposureRefreshPending; }
        }

        /// <summary>The air masses of the grid's sealed rooms.</summary>
        public IList<RoomAirNode> RoomAir
        {
            get { return solver.RoomAir; }
        }

        /// <summary>
        /// Sets how full of air the room containing <paramref name="cell"/> is, 0..1.
        ///
        /// The simulation cannot work this out for itself — pressurisation is the host's model,
        /// not a thermal property — so a room holds no air until something reports that it does.
        /// </summary>
        /// <returns>True when a sealed room took the value.</returns>
        public bool SetRoomPressure(Vector3I cell, float pressure)
        {
            return solver.SetRoomPressure(rooms.Map, cell, pressure);
        }

        /// <summary>The air of the room containing a cell, or null when it holds none.</summary>
        public RoomAirNode GetRoomAir(Vector3I cell)
        {
            return solver.GetRoomAir(rooms.Map, cell);
        }

        /// <summary>The grid's heat pumps.</summary>
        public IList<HeatPumpDevice> HeatPumps
        {
            get { return solver.HeatPumps; }
        }

        /// <summary>
        /// The heat pump a block drives, or null when the block is not one.
        ///
        /// The host owns whether a pump runs: it holds the terminal switch and knows whether the
        /// grid could supply the power. Set <see cref="HeatPumpDevice.Enabled"/> and
        /// <see cref="HeatPumpDevice.PowerAvailable"/> on the returned device, and read
        /// <see cref="HeatPumpDevice.LastPowerWatts"/> back to know what to bill for.
        /// </summary>
        public HeatPumpDevice GetHeatPump(BlockInstance block)
        {
            return solver.GetHeatPump(block);
        }

        /// <summary>
        /// Flags the conduction graph, room map and coolant loops as stale. Repeated calls
        /// before the next update collapse into one rebuild.
        /// </summary>
        public void MarkTopologyDirty()
        {
            topologyDirty = true;
        }

        /// <summary>
        /// Builds everything from the current grid contents in one pass, skipping the
        /// incremental machinery. Intended for load time and for tests.
        /// </summary>
        public void RebuildAll()
        {
            Begin(SimulationPhase.Topology);
            surfaces.Rebuild(grid);
            solver.RebuildLinks();
            RebuildLoops();
            solver.RebuildHeatPumps();
            End(SimulationPhase.Topology);

            Begin(SimulationPhase.RoomMapping);
            rooms.RequestRestart(grid);
            rooms.RunToCompletion();
            End(SimulationPhase.RoomMapping);

            Begin(SimulationPhase.Exposure);
            solver.RefreshExposure(rooms.Map);
            solver.RebuildRoomAir(rooms.Map);
            solver.RefreshHeatGeneration();
            End(SimulationPhase.Exposure);

            topologyDirty = false;
            exposureDirty = false;
            appliedRevision = settings.Revision;
        }

        /// <summary>
        /// Reapplies settings that were changed after the simulation was built.
        ///
        /// Most settings are read straight off the shared object every step and need nothing.
        /// These three do not: heat capacities are cached per node, coolant loops are built or not
        /// built according to a switch, and room air is built the same way. Checking a revision
        /// number costs one integer comparison per update against reading nothing at all, and it
        /// is what makes every switch a live switch.
        /// </summary>
        private void ApplySettingsIfChanged()
        {
            if (appliedRevision == settings.Revision) return;
            appliedRevision = settings.Revision;

            IList<ThermalNode> nodes = solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].HeatTimeScale = settings.HeatTimeScale;
            }

            RebuildLoops();
            solver.RebuildRoomAir(rooms.Map);
        }

        /// <summary>The settings revision this simulation has already acted on.</summary>
        private int appliedRevision = -1;

        private void Begin(SimulationPhase phase)
        {
            if (Profiler != null) Profiler.Begin(phase);
        }

        private void End(SimulationPhase phase)
        {
            if (Profiler != null) Profiler.End(phase);
        }

        private void RebuildLoops()
        {
            if (!settings.EnableCoolantLoops)
            {
                solver.SetLoops(null);
                return;
            }

            List<CoolantLoop> found = CoolantLoopBuilder.FindLoops(
                grid, loopProperties, DefaultTemperature, work);
            solver.SetLoops(found);
        }

        /// <summary>
        /// Checks the published room map against the grid. Diagnostic only: nothing in the
        /// simulation reads the result, and it is never called unless something is asking.
        /// </summary>
        public RoomAudit AuditRooms(int exampleLimit = RoomAuditor.DefaultExampleLimit)
        {
            return RoomAuditor.Audit(grid, surfaces, rooms.Map, exampleLimit);
        }

        private void OnRoomsCompleted()
        {
            exposureDirty = true;
        }

        // ---- update ------------------------------------------------------------------------

        /// <summary>
        /// Advances the simulation. Call once per host frame.
        /// </summary>
        /// <param name="frameSeconds">Real seconds since the previous call.</param>
        /// <param name="sample">Environment readings for this grid.</param>
        public void Update(float frameSeconds, EnvironmentSample sample)
        {
            ApplySettingsIfChanged();

            if (topologyDirty)
            {
                Begin(SimulationPhase.Topology);
                topologyDirty = false;

                // Rebuilt here rather than left dirty for the solver to notice.
                //
                // The graph has to exist before the next step either way, so deferring it saved
                // nothing — it only moved the cost onto a tick that was also going to integrate,
                // and billed it to the solver stage. A report then blamed the solver for a stall
                // that was a topology rebuild, which is the opposite of what stage timings are
                // for. Doing it here also puts the rebuild on the earliest tick after the change
                // rather than on the next stepping one, so the two costs land separately more
                // often than not.
                // Whichever route is valid: blocks placed are linked in place, anything that
                // could have invalidated an existing link rebuilds the graph.
                solver.BuildLinksIfNeeded();
                RebuildLoops();

                // A pump is bound to the two nodes either side of it, so whatever changed may
                // have been one of them.
                solver.RebuildHeatPumps();
                rooms.RequestRestart(grid);
                End(SimulationPhase.Topology);
            }

            if (rooms.HasWorkPending)
            {
                Begin(SimulationPhase.RoomMapping);
                Vector3I extents = (grid.Max - grid.Min) + Vector3I.One;
                int volume = Math.Max(1, extents.X * extents.Y * extents.Z);
                rooms.Step(SimulationScheduler.RoomMappingBudget(volume));
                End(SimulationPhase.RoomMapping);
            }

            if (exposureDirty)
            {
                exposureDirty = false;
                solver.BeginExposureRefresh(rooms.Map);
            }

            if (solver.ExposureRefreshPending)
            {
                Begin(SimulationPhase.Exposure);

                bool more = solver.StepExposureRefresh(
                    SimulationScheduler.ExposureBudget(solver.Nodes.Count));

                // The air of a room is built from the blocks bounding it, so it is rebuilt once
                // the exposure they carry is current — not part way through.
                if (!more) solver.RebuildRoomAir(rooms.Map);

                End(SimulationPhase.Exposure);
            }

            int steps = scheduler.StepsDue(frameSeconds);
            if (steps <= 0) return;

            Begin(SimulationPhase.Solver);
            EnvironmentState state = EnvironmentSolver.Solve(settings, planet, sample);
            RunSteps(steps, ref state);
            End(SimulationPhase.Solver);
        }

        /// <summary>
        /// Advances by an exact number of solver steps, ignoring frame pacing. For tests and
        /// scenario scripts that want reproducible time.
        /// </summary>
        public void StepExact(int steps, EnvironmentSample sample)
        {
            ApplySettingsIfChanged();
            EnvironmentState state = EnvironmentSolver.Solve(settings, planet, sample);
            RunSteps(steps, ref state);
        }

        // ---- persistence -------------------------------------------------------------------

        /// <summary>
        /// How many rooms took a saved air temperature on the last <see cref="Load"/>. Reported
        /// rather than returned because the return value is a block count the host already logs.
        /// </summary>
        public int RoomsRestored { get; private set; }

        /// <summary>Encodes every block, loop and room air temperature.</summary>
        public string Save()
        {
            List<StoredTemperature> blocks = new List<StoredTemperature>(solver.Nodes.Count);
            for (int i = 0; i < solver.Nodes.Count; i++)
            {
                ThermalNode node = solver.Nodes[i];
                blocks.Add(new StoredTemperature(node.Block.Position, node.Temperature));
            }

            List<StoredLoop> loops = new List<StoredLoop>(solver.Loops.Count);
            for (int i = 0; i < solver.Loops.Count; i++)
            {
                CoolantLoop loop = solver.Loops[i];
                loops.Add(new StoredLoop(loop.Signature, loop.Temperature));
            }

            // Only air that means something is written. An uninitialised room holds a placeholder
            // — ambient, standing in until the room is first filled — and saving that would turn a
            // guess into a remembered fact.
            IList<RoomAirNode> air = solver.RoomAir;
            List<StoredRoom> rooms = new List<StoredRoom>(air.Count);
            for (int i = 0; i < air.Count; i++)
            {
                if (!air[i].Initialised) continue;
                rooms.Add(new StoredRoom(air[i].Anchor, air[i].Temperature));
            }

            return ThermalStorageCodec.Encode(blocks, loops, rooms);
        }

        /// <summary>
        /// Restores temperatures. Unknown positions are ignored, so a blueprint that lost blocks
        /// still loads.
        ///
        /// Room air is restored onto the rooms the map already holds, so this has to run after the
        /// map exists — which is why the host loads immediately after <see cref="RebuildAll"/>. A
        /// room whose shape changed while the world was closed is a different room and starts from
        /// its walls, exactly as it would have done mid-session.
        /// </summary>
        /// <returns>Number of blocks restored.</returns>
        public int Load(string data)
        {
            RoomsRestored = 0;

            List<StoredTemperature> blocks = new List<StoredTemperature>();
            List<StoredLoop> storedLoops = new List<StoredLoop>();
            List<StoredRoom> storedRooms = new List<StoredRoom>();

            if (!ThermalStorageCodec.TryDecode(data, blocks, storedLoops, storedRooms)) return 0;

            int restored = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                BlockInstance block = grid.GetAtCell(blocks[i].Position);
                if (block == null) continue;

                ThermalNode node = solver.GetNode(block);
                if (node == null) continue;

                node.Temperature = Math.Max(ThermalConstants.MinimumTemperature, blocks[i].Temperature);
                restored++;
            }

            for (int i = 0; i < storedLoops.Count; i++)
            {
                for (int l = 0; l < solver.Loops.Count; l++)
                {
                    if (solver.Loops[l].Signature != storedLoops[i].Signature) continue;
                    solver.Loops[l].Temperature = Math.Max(ThermalConstants.MinimumTemperature, storedLoops[i].Temperature);
                    break;
                }
            }

            RoomsRestored = solver.RestoreRoomAir(storedRooms);

            return restored;
        }
    }
}
