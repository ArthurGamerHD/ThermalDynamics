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

            rooms.Completed += OnRoomsCompleted;
        }

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

        private readonly List<OverheatEvent> overheats = new List<OverheatEvent>();

        private void RunSteps(int steps, ref EnvironmentState state)
        {
            overheats.Clear();

            for (int i = 0; i < steps; i++)
            {
                solver.Step(settings.StepSeconds, state);

                IList<OverheatEvent> stepOverheats = solver.Overheats;
                for (int o = 0; o < stepOverheats.Count; o++)
                {
                    overheats.Add(stepOverheats[o]);
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
        /// closing. Conduction and coolant plumbing do not depend on sealing, so this refreshes
        /// the surface map and restarts the room fill without rebuilding the conduction graph or
        /// re-tracing the loops.
        /// </summary>
        public void RefreshBlockSealing(BlockInstance block)
        {
            if (block == null) return;

            block.RefreshSurfaces();
            surfaces.RemoveBlock(block);
            surfaces.AddBlock(block);
            rooms.RequestRestart(grid);
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
            End(SimulationPhase.Topology);

            Begin(SimulationPhase.RoomMapping);
            rooms.RequestRestart(grid);
            rooms.RunToCompletion();
            End(SimulationPhase.RoomMapping);

            Begin(SimulationPhase.Exposure);
            solver.RefreshExposure(rooms.Map);
            solver.RefreshHeatGeneration();
            End(SimulationPhase.Exposure);

            topologyDirty = false;
            exposureDirty = false;
        }

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

            List<CoolantLoop> found = CoolantLoopBuilder.FindLoops(grid, loopProperties, DefaultTemperature);
            solver.SetLoops(found);
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
            if (topologyDirty)
            {
                Begin(SimulationPhase.Topology);
                topologyDirty = false;
                solver.InvalidateLinks();
                RebuildLoops();
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
                Begin(SimulationPhase.Exposure);
                exposureDirty = false;
                solver.RefreshExposure(rooms.Map);
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
            EnvironmentState state = EnvironmentSolver.Solve(settings, planet, sample);
            RunSteps(steps, ref state);
        }

        // ---- persistence -------------------------------------------------------------------

        /// <summary>Encodes every block and loop temperature.</summary>
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

            return ThermalStorageCodec.Encode(blocks, loops);
        }

        /// <summary>
        /// Restores temperatures. Unknown positions are ignored, so a blueprint that lost blocks
        /// still loads.
        /// </summary>
        /// <returns>Number of blocks restored.</returns>
        public int Load(string data)
        {
            List<StoredTemperature> blocks = new List<StoredTemperature>();
            List<StoredLoop> storedLoops = new List<StoredLoop>();

            if (!ThermalStorageCodec.TryDecode(data, blocks, storedLoops)) return 0;

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

            return restored;
        }
    }
}
