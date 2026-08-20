using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// One grid's complete thermal simulation: block layout, surface map, room mapping, solver,
    /// coolant loops and scheduling, wired together.
    ///
    /// This is the entire surface a host needs: mirror block changes in and call
    /// <see cref="Update"/> once a frame. The game adapter and the test harness both drive it
    /// this way.
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

        /// <summary>
        /// Set when the room flood fill has to run again, which is a much larger claim than
        /// <see cref="topologyDirty"/>: the conduction graph is repaired proportionally to what
        /// changed, while a remap walks the grid's bounding volume. Anything that alters what a
        /// block seals sets both; a change to mounting alone sets only the first.
        /// </summary>
        private bool roomsDirty;
        private bool exposureDirty;

        /// <summary>Temperature new blocks start at, K.</summary>
        public float DefaultTemperature = 293.15f;

        /// <summary>
        /// Optional stage timing. Null disables instrumentation entirely, which is the shipping
        /// default; the hooks then cost one null check per stage.
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

            // One instance shared by both, so a caller reads a single set of counters for the
            // whole update rather than summing two.
            solver.Work = work;
            rooms.Work = work;

            rooms.Completed += OnRoomsCompleted;
        }

        /// <summary>
        /// Work counters for this simulation's one-shot stages. Counted rather than timed; see
        /// <see cref="SimulationWork"/>.
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
        /// Blocks damaged by heat during the last <see cref="Update"/> or <see cref="StepExact"/>,
        /// across every step it ran.
        ///
        /// Accumulated here because the solver clears its own list each step; a host reading the
        /// solver directly would see only the final step of a multi-step update.
        /// </summary>
        public IList<OverheatEvent> Overheats
        {
            get { return overheats; }
        }

        /// <summary>
        /// Threshold crossings during the last <see cref="Update"/> or <see cref="StepExact"/>,
        /// across every step it ran. Accumulated here because the solver's own list survives only
        /// one step.
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
        /// Simulated seconds this simulation has declined to advance because doing so would exceed
        /// what one step is allowed to cost.
        ///
        /// A grid running below real time is an intended state under the work budget, but it also
        /// changes how long the grid takes to cool, so it is reported rather than absorbed.
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

        /// <summary>Simulated seconds advanced.</summary>
        public double SimulatedSecondsRun { get; private set; }

        /// <summary>
        /// What one substep over this grid costs, in the units
        /// <see cref="ThermalSettings.MaxElementVisitsPerStep"/> is expressed in: its links, plus
        /// its nodes weighted by what a node is worth.
        ///
        /// Nodes are counted because a substep runs the environment pass once per node, which the
        /// old link-only count could not see — see [element-cost.md](../../../../docs/element-cost.md).
        /// Exposed faces are deliberately not counted: measured at a tenth to a half of a link
        /// each, they are inside the noise of the two terms that are here.
        /// </summary>
        public long SubstepCost
        {
            get
            {
                return solver.LinkCount
                    + ((long)ThermalSettings.NodeCostInLinks * solver.Nodes.Count);
            }
        }

        /// <summary>
        /// Substeps a step is currently allowed, from <c>MaxElementVisitsPerStep</c> and what one
        /// substep over this grid costs. <see cref="int.MaxValue"/> when the bound is switched off.
        /// </summary>
        public int SubstepBudget
        {
            get
            {
                int budgetVisits = settings.MaxElementVisitsPerStep;
                if (budgetVisits <= 0) return int.MaxValue;

                long cost = SubstepCost;
                if (cost <= 0) return int.MaxValue;

                long budget = budgetVisits / cost;
                if (budget < 1) return 1;
                return budget > int.MaxValue ? int.MaxValue : (int)budget;
            }
        }

        /// <summary>
        /// Longest step this grid can afford in simulated seconds, from the element visits a step
        /// is allowed and the grid's current stiffness. Returns the full step whenever it fits.
        ///
        /// Public so a benchmark can measure the bounded cost rather than the unbounded one; the
        /// two diverge on exactly the grids the budget exists for.
        /// </summary>
        public float AffordableStepSeconds(float seconds)
        {
            int budgetVisits = settings.MaxElementVisitsPerStep;
            if (budgetVisits <= 0) return seconds;

            long cost = SubstepCost;
            if (cost <= 0) return seconds;

            // At least one substep whatever the grid size: a step that cannot afford a single
            // pass over its elements would not advance at all.
            long substepBudget = budgetVisits / cost;
            if (substepBudget < 1) substepBudget = 1;

            float required = solver.RequiredSubsteps(seconds);
            if (required <= substepBudget) return seconds;

            // The substep estimate is proportional to step length, so scaling the length by the
            // ratio lands exactly on the budget.
            return seconds * (substepBudget / required);
        }

        /// <summary>
        /// Fractional work credit carried between frames, in element visits.
        ///
        /// A frame is owed <c>frameSeconds * StepsPerSecond</c> of a step. On a small grid that is
        /// less than one element visit, so discarding the fraction would stall it entirely.
        /// </summary>
        private double workCredit;

        /// <summary>
        /// Call interval the budgeted passes were sized against: a ten-frame tick.
        ///
        /// Those budgets are per call, and the host calls every frame, so applying them directly
        /// would run the room mapper and the exposure refresh ten times faster and ten times more
        /// expensively. Scaling by the caller's actual frame length holds the rate constant and
        /// independent of call frequency.
        /// </summary>
        private const float BudgetReferenceSeconds = 10f / 60f;

        private double roomCredit;
        private double exposureCredit;

        /// <summary>A frame's share of a budget that was expressed per tick.</summary>
        private static int Share(ref double credit, int perTick, float frameSeconds)
        {
            credit += perTick * (frameSeconds / (double)BudgetReferenceSeconds);

            // Never bank more than one tick's worth, so a long frame or a resumed session cannot
            // buy a burst of flood fill.
            if (credit > perTick) credit = perTick;

            int slice = (int)credit;
            credit -= slice;
            return slice;
        }

        /// <summary>Solver steps this simulation has completed.</summary>
        public long StepsCompleted { get; private set; }

        /// <summary>
        /// True when the next call will begin a step and so needs a fresh environment sample.
        ///
        /// A sample costs the host a planet lookup and sometimes a raycast. The simulation is
        /// advanced every frame, so sampling unconditionally would repeat that work for readings
        /// that only change between steps.
        /// </summary>
        public bool NeedsEnvironmentSample
        {
            get { return !solver.StepInFlight; }
        }

        /// <summary>True while a step is part way through its frame window.</summary>
        public bool StepInFlight
        {
            get { return solver.StepInFlight; }
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
        /// Call after a change to an existing block's geometry or mounting.
        ///
        /// Repairs everything that reads from the block's surfaces: the surface map, the
        /// conduction links touching it, the coolant loops and heat pumps that bind to its ports,
        /// and its own exposed faces. All of it is proportional to the block and its neighbours.
        ///
        /// The exception is sealing. What a block seals decides the shape of the rooms around it,
        /// which only the flood fill can find, so a change to the sealing bits asks for a remap
        /// and a change to mounting alone does not. Door state has its own path in
        /// <see cref="RefreshBlockSealing"/>, which never needs the remap because a door is
        /// already held as a portal between the same two rooms whether it is open or shut.
        /// </summary>
        public void RefreshBlock(BlockInstance block)
        {
            if (block == null) return;

            // RefreshSurfaces allocates fresh arrays, so the old references are a free snapshot.
            // Both layers are compared because they answer different questions: the structural one
            // decides the shape of a room, the live one only which rooms currently reach open air.
            int[] structuralBefore = block.StructuralSurfaces;
            int[] liveBefore = block.SelfSurfaces;

            block.RefreshSurfaces();
            surfaces.RemoveBlock(block);
            surfaces.AddBlock(block);

            // Contact area is the product of both ends' mount fractions, so every link touching
            // this block now carries a conductance derived from geometry it no longer has.
            solver.RefreshBlockLinks(block);

            bool structuralChanged = !SameSurfaces(structuralBefore, block.StructuralSurfaces);
            bool ventingChanged = !SameSurfaces(liveBefore, block.SelfSurfaces);

            // A wall moved, or a door the last pass never saw: only the flood fill can answer.
            if (structuralChanged || (ventingChanged && !rooms.Knows(block)))
            {
                MarkTopologyDirty();
                return;
            }

            MarkLayoutDirty();

            Begin(SimulationPhase.RoomMapping);

            // The rooms are the same rooms; what may have changed is which of them reach open
            // air through a portal. Skipped entirely when the sealing bits did not move, which is
            // the ordinary case for a change to mounting alone.
            bool venting = ventingChanged && rooms.Map.RefreshVenting();
            End(SimulationPhase.RoomMapping);

            Begin(SimulationPhase.Exposure);

            // Its own faces are recounted regardless: the surface map has just been rebuilt
            // underneath it, and this is one node's worth of work.
            solver.RefreshExposureOf(block, rooms.Map);

            if (venting)
            {
                solver.RefreshExposureAround(rooms.Map, rooms.Map.ChangedRooms);
                solver.RebuildRoomAir(rooms.Map);
            }
            End(SimulationPhase.Exposure);
        }

        /// <summary>
        /// Whether two of a block's per-cell surface arrays agree. A block keeps its cell count
        /// across a refresh, so a length change means the model itself was swapped.
        /// </summary>
        private static bool SameSurfaces(int[] before, int[] after)
        {
            if (before == null || after == null) return false;
            if (before.Length != after.Length) return false;

            for (int i = 0; i < before.Length; i++)
            {
                if (before[i] != after[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// Call after a change that alters only what a block seals, such as a door opening or
        /// closing.
        ///
        /// Does not remap the grid: the rooms either side of a door are the same rooms whether it
        /// is open or shut, and the mapper already holds the door as a portal between them. The
        /// work is to update the live surface bits, re-resolve which rooms reach open air through
        /// their portals, and refresh exposure for the blocks facing the rooms that changed —
        /// proportional to the doors and affected rooms rather than to the grid.
        /// </summary>
        public void RefreshBlockSealing(BlockInstance block)
        {
            if (block == null) return;

            block.RefreshSurfaces();
            surfaces.RemoveBlock(block);
            surfaces.AddBlock(block);

            // A door the mapper has not yet seen — one placed since the last pass — has no portal,
            // so the map cannot resolve it and must be rebuilt.
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

            // An opened room stops holding air and a closed one starts. Both rebuild a room rather
            // than the grid, so the work is proportional to the rooms that changed.
            solver.RebuildRoomAir(rooms.Map);
            End(SimulationPhase.Exposure);
        }

        /// <summary>
        /// True while any budgeted pass still has work left: the room flood fill or the exposure
        /// refresh that follows it. Callers waiting for a grid to settle should test this rather
        /// than the mapper alone, which would stop one stage early.
        /// </summary>
        public bool HasPendingWork
        {
            get { return rooms.HasWorkPending || solver.ExposureRefreshPending; }
        }

        /// <summary>Air masses of the grid's sealed rooms.</summary>
        public IList<RoomAirNode> RoomAir
        {
            get { return solver.RoomAir; }
        }

        /// <summary>
        /// Sets the air fill fraction of the room containing <paramref name="cell"/>, 0..1.
        /// Pressurisation belongs to the host's model, so a room holds no air until the host
        /// reports otherwise.
        /// </summary>
        /// <returns>True when a sealed room took the value.</returns>
        public bool SetRoomPressure(Vector3I cell, float pressure)
        {
            return solver.SetRoomPressure(rooms.Map, cell, pressure);
        }

        /// <summary>Air node of the room containing a cell, or null when it holds no air.</summary>
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
        /// The heat pump a block drives, or null when the block is not a pump.
        ///
        /// The host owns whether a pump runs, since it holds the terminal switch and the grid's
        /// power state. Set <see cref="HeatPumpDevice.Enabled"/> and
        /// <see cref="HeatPumpDevice.PowerAvailable"/> on the returned device, and read
        /// <see cref="HeatPumpDevice.LastPowerWatts"/> back to bill for the power drawn.
        /// </summary>
        public HeatPumpDevice GetHeatPump(BlockInstance block)
        {
            return solver.GetHeatPump(block);
        }

        /// <summary>
        /// Flags the conduction graph, room map and coolant loops as stale. Repeated calls
        /// before the next update collapse into one rebuild.
        ///
        /// Use <see cref="MarkLayoutDirty"/> instead when what a block seals cannot have changed:
        /// that spares the flood fill, which is the expensive half.
        /// </summary>
        public void MarkTopologyDirty()
        {
            topologyDirty = true;
            roomsDirty = true;
        }

        /// <summary>
        /// Flags the conduction graph, coolant loops and heat pumps as stale, without asking for a
        /// remap. For a change that cannot move a wall: mounting, ports, block properties.
        /// </summary>
        public void MarkLayoutDirty()
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
            roomsDirty = false;
            exposureDirty = false;
            appliedRevision = settings.Revision;
        }

        /// <summary>
        /// Reapplies settings changed after the simulation was built.
        ///
        /// Most settings are read from the shared object each step and need no action. Three do
        /// not: heat capacities are cached per node, and coolant loops and room air are built or
        /// skipped according to a switch. Guarded by a revision comparison, so every setting can
        /// be changed live for the cost of one integer compare per update.
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

        /// <summary>
        /// Re-runs the loop search collecting the reason every unclaimed coolant block is unclaimed.
        ///
        /// Diagnostic only, on the same terms as <see cref="AuditRooms"/>: nothing in the simulation
        /// reads it and it is never called unless a readout or a report asks. It answers the one
        /// question the loop list cannot, because a broken ring's symptom is that it is missing.
        /// </summary>
        public CoolantLoopDiagnostics DiagnoseLoops(
            int exampleLimit = CoolantLoopDiagnostics.DefaultExampleLimit)
        {
            CoolantLoopDiagnostics diagnostics = new CoolantLoopDiagnostics(exampleLimit);
            CoolantLoopBuilder.FindLoops(grid, loopProperties, DefaultTemperature, null, diagnostics);
            return diagnostics;
        }

        /// <summary>
        /// Why one coolant block is in no loop, or <see cref="CoolantFault.None"/> when it is.
        ///
        /// Costs one walk along that block's own run rather than a pass over the grid, which is what
        /// makes it safe to call from a terminal panel refreshing while a player watches it.
        /// <see cref="DiagnoseLoops"/> is the whole-grid form and is for reports.
        /// </summary>
        public CoolantFault DiagnoseBlock(BlockInstance block)
        {
            if (block == null || block.Model.Coolant == null) return CoolantFault.None;
            if (FindLoopContaining(block) != null) return CoolantFault.None;

            CoolantFault fault;
            CoolantLoopBuilder.TraceRing(grid, block, out fault);
            return fault;
        }

        /// <summary>The loop a coolant block belongs to, or null.</summary>
        public CoolantLoop FindLoopContaining(BlockInstance block)
        {
            if (block == null) return null;

            IList<CoolantLoop> loops = solver.Loops;
            for (int i = 0; i < loops.Count; i++)
            {
                if (loops[i].Contains(block)) return loops[i];
            }
            return null;
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

                // Rebuilt here rather than left dirty for the solver to discover. The graph must
                // exist before the next step either way, so deferring only moves the cost onto a
                // tick that also integrates and bills it to the solver stage. Rebuilding here also
                // lands it on the earliest tick after the change rather than the next stepping
                // one, so the two costs usually fall on separate frames.
                // Whichever route is valid: placed blocks are linked incrementally, anything that
                // could have invalidated an existing link forces a full rebuild.
                solver.BuildLinksIfNeeded();
                RebuildLoops();

                // A pump is bound to the nodes either side of it, either of which may have
                // changed.
                solver.RebuildHeatPumps();

                // Only when something could have changed the shape of a room. A remap walks the
                // grid's bounding volume; the rest of this branch is proportional to what moved.
                if (roomsDirty)
                {
                    roomsDirty = false;
                    rooms.RequestRestart(grid);
                }
                End(SimulationPhase.Topology);
            }

            if (rooms.HasWorkPending)
            {
                Begin(SimulationPhase.RoomMapping);
                Vector3I extents = (grid.Max - grid.Min) + Vector3I.One;
                int volume = Math.Max(1, extents.X * extents.Y * extents.Z);
                int cells = Share(ref roomCredit,
                    SimulationScheduler.RoomMappingBudget(volume), frameSeconds);
                if (cells > 0) rooms.Step(cells);
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

                bool more = solver.StepExposureRefresh(Share(ref exposureCredit,
                    SimulationScheduler.ExposureBudget(solver.Nodes.Count), frameSeconds));

                // A room's air is built from the blocks bounding it, so it is rebuilt only once
                // their exposure is current.
                if (!more) solver.RebuildRoomAir(rooms.Map);

                End(SimulationPhase.Exposure);
            }

            Begin(SimulationPhase.Solver);
            AdvanceSolver(frameSeconds, sample);
            End(SimulationPhase.Solver);
        }

        /// <summary>
        /// Performs this frame's share of the current step, and starts the next one when it
        /// completes.
        ///
        /// A step covers <c>1 / StepsPerSecond</c> of a second — fifteen frames at the default
        /// settings — and is spread evenly across them rather than run whole on one. Each frame is
        /// given the fraction of the step its own length represents:
        /// <c>frameSeconds * StepsPerSecond</c>.
        ///
        /// <c>Frequency</c> and <c>SimulationSpeed</c> are the factors of <c>StepsPerSecond</c>,
        /// so raising either makes every frame do proportionally more work rather than making
        /// whole steps arrive more often.
        /// </summary>
        private void AdvanceSolver(float frameSeconds, EnvironmentSample sample)
        {
            if (frameSeconds <= 0f) return;

            if (!solver.StepInFlight)
            {
                float seconds = AffordableStepSeconds(settings.StepSeconds);

                if (seconds < settings.StepSeconds)
                {
                    SimulatedSecondsSkipped += settings.StepSeconds - seconds;
                }
                SimulatedSecondsRun += seconds;

                EnvironmentState state = EnvironmentSolver.Solve(settings, planet, sample);
                if (!solver.BeginStep(seconds, state)) return;

                overheats.Clear();
                crossings.Clear();
            }

            workCredit += solver.StepWorkUnits * (double)frameSeconds * settings.StepsPerSecond;

            // A long frame or a paused session must not bank enough credit to run several steps
            // at once, which is the lump this pacing exists to avoid.
            double ceiling = solver.StepWorkUnits;
            if (workCredit > ceiling) workCredit = ceiling;

            long budget = (long)workCredit;
            if (budget <= 0) return;

            workCredit -= budget;

            if (!solver.AdvanceStep(budget))
            {
                workCredit += budget - solver.LastAdvanceWork;
                return;
            }

            // Unspent budget returns to the credit rather than being discarded. The work estimate
            // is proportional rather than exact, so discarding it would run the grid slightly below
            // its configured rate.
            workCredit += budget - solver.LastAdvanceWork;

            // The next step starts on the following frame, so a completion never pulls a second
            // step in behind it. This is what bounds a frame's cost.
            CollectStepOutput();
        }

        private void CollectStepOutput()
        {
            StepsCompleted++;
            scheduler.CountStep();

            IList<OverheatEvent> stepOverheats = solver.Overheats;
            for (int o = 0; o < stepOverheats.Count; o++) overheats.Add(stepOverheats[o]);

            IList<ThresholdCrossing> stepCrossings = solver.Crossings;
            for (int c = 0; c < stepCrossings.Count; c++) crossings.Add(stepCrossings[c]);
        }

        /// <summary>
        /// Advances by an exact number of solver steps, ignoring frame pacing. For tests and
        /// scenario scripts that want reproducible time.
        /// </summary>
        public void StepExact(int steps, EnvironmentSample sample)
        {
            ApplySettingsIfChanged();

            // Any step the frame pacing left part done is abandoned rather than finished against
            // an environment sampled earlier. Without this, a caller that mixes the two — a
            // scenario taking over a simulation the host was updating — is offset by a fraction of
            // a step, and two runs of the same scenario disagree.
            solver.AbandonStep();
            workCredit = 0d;
            EnvironmentState state = EnvironmentSolver.Solve(settings, planet, sample);
            RunSteps(steps, ref state);
        }

        // ---- persistence -------------------------------------------------------------------

        /// <summary>
        /// Rooms that took a saved air temperature on the last <see cref="Load"/>. Exposed as a
        /// property because <see cref="Load"/> returns a block count.
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

            // Only initialised air is written. An uninitialised room holds ambient as a
            // placeholder until it is first filled, and saving that would persist a placeholder as
            // a measured value.
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
        /// Room air is restored onto the rooms already in the map, so this must run after the map
        /// exists; the host calls it immediately after <see cref="RebuildAll"/>. A room whose shape
        /// changed while the world was closed is treated as a new room and starts from its walls.
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
