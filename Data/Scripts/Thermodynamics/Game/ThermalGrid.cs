using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// The adapter between one Space Engineers grid and one <see cref="ThermalSimulation"/>.
    ///
    /// The simulation knows nothing about the game: it takes a block layout, an environment sample
    /// and a frame length, and returns temperatures and overheat events. Everything on the game
    /// side of that boundary — definitions, entity events, raycasts, damage, storage — lives here
    /// and in the partials beside it.
    ///
    /// The component polls on the ten-frame tick rather than every frame. Step pacing belongs to
    /// the simulation (<see cref="SimulationScheduler"/>), so polling faster would only add entity
    /// update callbacks.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid), true)]
    public partial class ThermalGrid : MyGameLogicComponent
    {
        /// <summary>Real seconds between <see cref="UpdateBeforeSimulation10"/> calls.</summary>
        public const float TickSeconds = 10f / 60f;

        private static readonly MyStringHash ThermalDamage = MyStringHash.GetOrCompute("thermal");

        public MyCubeGrid Grid;

        /// <summary>The block layout the simulation reads. Mirrors the game grid.</summary>
        public GridModel Model;

        /// <summary>The simulation itself. Null until <see cref="Init"/> has run.</summary>
        public ThermalSimulation Simulation;

        /// <summary>
        /// This grid's data collection record. Null when telemetry is disabled, so every use is
        /// guarded; the record outlives the grid and is owned by <see cref="Telemetry"/>.
        /// </summary>
        public GridTelemetry Stats;

        /// <summary>
        /// Placed blocks by their minimum cell, one entry per block rather than per cell.
        ///
        /// Keyed on <c>Min</c> rather than the game's <c>SlimBlock.Position</c> because that is the
        /// identity the model uses (<see cref="BlockInstance.Position"/>). The two agree for a
        /// 1x1x1 block and differ for every larger one.
        /// </summary>
        private readonly Dictionary<Vector3I, ThermalBlock> blocks =
            new Dictionary<Vector3I, ThermalBlock>(Vector3I.Comparer);

        /// <summary>
        /// The same blocks in a list, so the mass sweep can resume where it stopped.
        ///
        /// A dictionary cannot be walked a slice at a time, and the sweep must be resumable to
        /// avoid a pass over every block landing inside one tick. Kept in step with the dictionary
        /// on every add and remove, with each block holding its own slot so a removal is a swap
        /// rather than a search.
        /// </summary>
        private readonly List<ThermalBlock> sweepOrder = new List<ThermalBlock>();

        /// <summary>Where the rolling mass sweep is up to in <see cref="sweepOrder"/>.</summary>
        private int massSweepCursor;

        /// <summary>
        /// Temperatures of recently removed blocks, so a section cut off the grid keeps its heat
        /// when it becomes its own grid, and rebuilding a block does not reset it.
        ///
        /// An entry is consumed when its position is built on again. Most never are, so the map is
        /// dropped wholesale once it exceeds what a split could plausibly need; otherwise a grid
        /// being ground down would accumulate one entry per destroyed block for the life of the
        /// world.
        /// </summary>
        public readonly Dictionary<Vector3I, float> RecentlyRemoved =
            new Dictionary<Vector3I, float>(Vector3I.Comparer);

        /// <summary>
        /// Remembered removals retained before the map is cleared. A split hands over its blocks in
        /// the same frame they are removed, so no entry needs to survive long.
        /// </summary>
        public const int MaxRecentlyRemoved = 4096;

        /// <summary>
        /// Air vents on this grid, held separately so the pressurisation sweep visits only the blocks
        /// that can report on a room rather than every block on the grid.
        /// </summary>
        private readonly List<ThermalBlock> vents = new List<ThermalBlock>();

        /// <summary>
        /// The air vents on this grid. Read by the diagnostics, which identify a compartment by the
        /// vent standing in it — the only name a player can read from a terminal.
        /// </summary>
        public IList<ThermalBlock> Vents
        {
            get { return vents; }
        }

        internal void RegisterVent(ThermalBlock bound)
        {
            if (bound != null && !vents.Contains(bound)) vents.Add(bound);
        }

        internal void UnregisterVent(ThermalBlock bound)
        {
            vents.Remove(bound);
        }

        /// <summary>
        /// Heat pumps on this grid, held separately for the same reason as the vents: the state they
        /// exchange with the game is theirs alone, and a grid with none should not scan its blocks.
        /// </summary>
        private readonly List<ThermalBlock> heatPumps = new List<ThermalBlock>();

        internal void RegisterHeatPump(ThermalBlock bound)
        {
            if (bound != null && !heatPumps.Contains(bound)) heatPumps.Add(bound);
        }

        internal void UnregisterHeatPump(ThermalBlock bound)
        {
            heatPumps.Remove(bound);
        }

        /// <summary>The hottest block on the grid, refreshed on a slow cadence for readouts.</summary>
        public ThermalNode HottestNode;

        /// <summary>Blocks above their critical temperature during the last step.</summary>
        public int CriticalBlocks;

        private bool started;
        private bool disabled;

        /// <summary>
        /// Every live grid component, so telemetry switched on mid-session can still find grids that
        /// already exist.
        /// </summary>
        private static readonly List<ThermalGrid> Live = new List<ThermalGrid>();

        public static IList<ThermalGrid> LiveGrids
        {
            get { return Live; }
        }

        public IEnumerable<ThermalBlock> Blocks
        {
            get { return blocks.Values; }
        }

        public int BlockCount
        {
            get { return blocks.Count; }
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            Settings.EnsureLoaded();

            Grid = Entity as MyCubeGrid;
            if (Grid == null)
            {
                disabled = true;
                return;
            }

            Model = new GridModel(Grid.GridSize);
            Simulation = new ThermalSimulation(Settings.Instance.ToCore(), Model);

            Stats = Telemetry.RegisterGrid(this);
            if (Stats != null) Simulation.Profiler = Stats.Profiler;

            // Thresholds are registered against the mod rather than a grid, so a grid created after
            // a registration still reports it.
            ThermalApi.ApplyThresholds(Simulation);

            Live.Add(this);

            if (Entity.Storage == null)
            {
                Entity.Storage = new MyModStorageComponent();
            }

            Grid.OnBlockAdded += BlockAdded;
            Grid.OnBlockRemoved += BlockRemoved;
            Grid.OnGridSplit += GridSplit;
            Grid.OnGridMerge += GridMerge;

            // OR into these flags, never assign. The descriptor requests entity updates, so this
            // property is the grid entity's own update flags rather than this component's, and
            // assigning clears what the grid set for itself. MyCubeGrid drives its scheduled work,
            // including the ship control system's recalculation, off EACH_FRAME and re-arms that
            // flag only when its queue goes from empty to non-empty. Clearing it once leaves the
            // queue permanently undrained, so the grid stops recalculating who controls it.
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (disabled) return;

            if (Grid.Physics == null)
            {
                // Projections and blueprints have no physics and never simulate. The grid's update
                // flags are left as they are; `disabled` is what stops this component working.
                disabled = true;
                return;
            }

            Simulation.LoopProperties = ThermalBlockCatalog.ToLoopProperties(
                ThermalLoopDefintion.GetDefinition(ThermalLoopDefintion.DefaultLoopDefinitionId));

            AddExistingBlocks();

            // One full build is cheaper than the incremental path replayed per block, and leaves
            // the room map complete before the first step rather than after it.
            //
            // Timed as a root of its own: this runs from the entity's callback, so it is inside
            // neither the session frame nor a grid's update, and it is the largest single call a
            // grid ever makes.
            if (Stats != null) Stats.BuildTime.Begin();
            Simulation.RebuildAll();
            if (Stats != null) Stats.BuildTime.End();

            Load();
            started = true;

        }

        /// <summary>
        /// Registers blocks that already existed when the component attached. Grid load order does
        /// not guarantee <see cref="BlockAdded"/> fires for them.
        /// </summary>
        private void AddExistingBlocks()
        {
            foreach (IMySlimBlock existing in Grid.GetBlocks())
            {
                AddBlock(existing);
            }
        }

        public override bool IsSerialized()
        {
            Save();
            return base.IsSerialized();
        }

        /// <summary>
        /// Handles grid closure. The telemetry record takes its final snapshot while the nodes still
        /// exist and stays in the registry, so a destroyed grid is still reported.
        /// </summary>
        public override void Close()
        {
            if (Stats != null)
            {
                Stats.Close();
                Stats = null;
            }

            if (Grid != null)
            {
                Grid.OnBlockAdded -= BlockAdded;
                Grid.OnBlockRemoved -= BlockRemoved;
                Grid.OnGridSplit -= GridSplit;
                Grid.OnGridMerge -= GridMerge;
            }

            foreach (ThermalBlock block in blocks.Values)
            {
                block.Detach();
            }
            blocks.Clear();
            sweepOrder.Clear();
            massSweepCursor = 0;

            Live.Remove(this);

            base.Close();
        }

        // ---- block lifecycle ---------------------------------------------------------------

        private void BlockAdded(IMySlimBlock block)
        {
            AddBlock(block);
        }

        private void AddBlock(IMySlimBlock block)
        {
            if (disabled || block == null) return;

            if (Grid.EntityId != block.CubeGrid.EntityId)
            {
                if (Stats != null) Stats.ForeignBlockEvents++;
                return;
            }

            if (blocks.ContainsKey(block.Min)) return;

            try
            {
                BlockModel model = ThermalBlockCatalog.Get(block);
                if (model == null || model.Thermal.ExcludeFromSimulation)
                {
                    if (Stats != null) Stats.BlocksIgnored++;
                    return;
                }

                ThermalBlock bound = new ThermalBlock(this, block, model);

                // The model owns the layout and can refuse a block; an already-occupied cell means
                // the two views have diverged. The binding is registered only after acceptance, so
                // this side never holds a block the simulation does not.
                float temperature = StartingTemperature(block.Min);
                bound.Node = Simulation.AddBlock(bound.Instance, temperature);

                blocks.Add(block.Min, bound);
                bound.SweepSlot = sweepOrder.Count;
                sweepOrder.Add(bound);
                bound.Attach();

                if (Stats != null) Stats.BlocksAdded++;
                if (bound.Stats != null) bound.Stats.OnPlaced(bound);
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalGrid.AddBlock", e);
            }
        }

        /// <summary>
        /// Starting temperature for a newly placed block: the heat of a block just removed from the
        /// same position, or the grid's default. This carries temperature across a grid split, where
        /// a position is removed from one grid and added to another in the same frame.
        /// </summary>
        private float StartingTemperature(Vector3I position)
        {
            float carried;
            if (RecentlyRemoved.TryGetValue(position, out carried))
            {
                RecentlyRemoved.Remove(position);
                return carried;
            }
            return Simulation.DefaultTemperature;
        }

        private void BlockRemoved(IMySlimBlock block)
        {
            if (disabled || block == null) return;

            if (Grid.EntityId != block.CubeGrid.EntityId)
            {
                if (Stats != null) Stats.ForeignBlockEvents++;
                return;
            }

            ThermalBlock bound;
            if (!blocks.TryGetValue(block.Min, out bound)) return;

            try
            {
                if (bound.Node != null)
                {
                    if (RecentlyRemoved.Count >= MaxRecentlyRemoved) RecentlyRemoved.Clear();
                    RecentlyRemoved[block.Min] = bound.Node.Temperature;
                }

                bound.Detach();
                Simulation.RemoveBlock(bound.Instance);
                blocks.Remove(block.Min);
                RemoveFromSweepOrder(bound);

                if (Stats != null) Stats.BlocksRemoved++;
                if (bound.Stats != null) bound.Stats.OnRemoved();
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalGrid.BlockRemoved", e);
            }
        }

        /// <summary>
        /// Refreshes the simulation's view of a block after its geometry or mounting changed.
        /// Rebuilds the conduction graph and the coolant loops with it.
        /// </summary>
        /// <summary>
        /// Rebuilds this grid's view of the definitions, after a profile's overlay changed what a
        /// definition says.
        ///
        /// Every block's thermal properties come from a cache keyed by definition, and the nodes
        /// hold what that cache handed them, so nothing short of rebuilding reaches them. This is
        /// as expensive as a world load for the grid and happens only when a profile changes.
        /// </summary>
        public void RefreshDefinitions()
        {
            if (Simulation == null || !started) return;

            try
            {
                Simulation.LoopProperties = ThermalBlockCatalog.ToLoopProperties(
                    ThermalLoopDefintion.GetDefinition(ThermalLoopDefintion.DefaultLoopDefinitionId));

                // The planet's properties are cached per entity and rebuilt on the next sample.
                PlanetProperties.Clear();
                currentPlanetId = 0;

                foreach (ThermalBlock bound in blocks.Values)
                {
                    if (bound != null) bound.RefreshProperties();
                }
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalGrid.RefreshDefinitions", e);
            }
        }

        public void RefreshBlock(ThermalBlock bound)
        {
            if (bound == null || bound.Instance == null) return;

            Simulation.RefreshBlock(bound.Instance);
            if (Stats != null) Stats.SurfaceRecalcs++;
        }

        /// <summary>
        /// Refreshes a block whose sealing changed and nothing else, such as a door. Doors cycle
        /// often, and neither conduction nor coolant plumbing depends on whether a face seals, so
        /// neither is rebuilt.
        /// </summary>
        public void RefreshBlockSealing(ThermalBlock bound)
        {
            if (bound == null || bound.Instance == null) return;

            Simulation.RefreshBlockSealing(bound.Instance);
            if (Stats != null) Stats.SurfaceRecalcs++;
        }

        /// <summary>
        /// Carries temperatures onto a section that has just split off.
        ///
        /// The blocks were removed from the parent this frame, so the parent holds their heat in
        /// <see cref="RecentlyRemoved"/>. Event order is not under a mod's control, so both
        /// orderings are handled: a block the child already holds is set directly, and one it has
        /// not built yet is handed over for <see cref="StartingTemperature"/> to apply on arrival.
        /// </summary>
        private void GridSplit(MyCubeGrid parent, MyCubeGrid child)
        {
            ThermalGrid a = parent.GameLogic.GetAs<ThermalGrid>();
            ThermalGrid b = child.GameLogic.GetAs<ThermalGrid>();
            if (a == null || b == null) return;

            if (a.Stats != null) a.Stats.Splits++;
            if (b.Stats != null) b.Stats.Splits++;

            a.HandOver(b);
        }

        /// <summary>
        /// Carries temperatures off a grid being absorbed into another.
        ///
        /// The two grids have different cell coordinates, so positions are mapped through world
        /// space rather than assumed to match.
        /// </summary>
        private void GridMerge(MyCubeGrid survivor, MyCubeGrid absorbed)
        {
            ThermalGrid a = survivor.GameLogic.GetAs<ThermalGrid>();
            ThermalGrid b = absorbed.GameLogic.GetAs<ThermalGrid>();
            if (a == null || b == null) return;

            if (a.Stats != null) a.Stats.Merges++;
            if (b.Stats != null) b.Stats.Merges++;

            foreach (KeyValuePair<Vector3I, ThermalBlock> entry in b.blocks)
            {
                if (entry.Value.Node == null) continue;
                a.Receive(MapCell(absorbed, survivor, entry.Key), entry.Value.Node.Temperature);
            }

            b.HandOver(a);
        }

        /// <summary>
        /// Passes this grid's remembered removals to another grid, mapping positions into its
        /// coordinates. Used by both split and merge; entries that neither grid claims are
        /// dropped with the rest of the map.
        /// </summary>
        private void HandOver(ThermalGrid other)
        {
            if (other == null || RecentlyRemoved.Count == 0) return;

            bool sameFrame = Grid == other.Grid;

            List<Vector3I> handled = new List<Vector3I>();
            foreach (KeyValuePair<Vector3I, float> entry in RecentlyRemoved)
            {
                Vector3I target = sameFrame ? entry.Key : MapCell(Grid, other.Grid, entry.Key);
                if (other.Receive(target, entry.Value)) handled.Add(target);
            }

            for (int i = 0; i < handled.Count; i++)
            {
                RecentlyRemoved.Remove(handled[i]);
            }
        }

        /// <summary>
        /// Applies a carried temperature: onto the block at that position if it exists, or into
        /// the remembered-removal map for whenever it is built.
        /// </summary>
        /// <returns>True when a live block took it.</returns>
        private bool Receive(Vector3I min, float temperature)
        {
            ThermalBlock bound = Get(min);
            if (bound != null && bound.Node != null)
            {
                bound.Node.Temperature = temperature;
                return true;
            }

            if (RecentlyRemoved.Count < MaxRecentlyRemoved)
            {
                RecentlyRemoved[min] = temperature;
            }
            return false;
        }

        /// <summary>Translates a cell from one grid's coordinates into another's, via world space.</summary>
        private static Vector3I MapCell(MyCubeGrid from, MyCubeGrid to, Vector3I cell)
        {
            if (from == to) return cell;
            return to.WorldToGridInteger(from.GridIntegerToWorld(cell));
        }

        // ---- lookups -----------------------------------------------------------------------

        /// <summary>
        /// Starts or stops feeding this grid's telemetry record, for the runtime toggle. A grid never
        /// registered is given a record here, so collection can be switched on without a reload.
        /// </summary>
        public void RefreshTelemetry()
        {
            if (!Telemetry.Enabled)
            {
                Simulation.Profiler = null;
                Stats = null;
                return;
            }

            if (Stats == null) Stats = Telemetry.RegisterGrid(this);
            if (Stats != null) Simulation.Profiler = Stats.Profiler;

            // A block resolves its per-definition record once, when placed. Blocks placed while
            // collection was off hold none, so switching it on must assign them one or the report
            // omits every block predating the switch.
            foreach (ThermalBlock bound in blocks.Values)
            {
                bound.RefreshStats();
            }
        }

        /// <summary>
        /// Removes a block from the sweep list by moving the last entry into its slot.
        ///
        /// The list is a rota rather than a sequence, so order does not matter. The cursor is left
        /// where it is; at worst one block is re-swept or skipped for one pass.
        /// </summary>
        private void RemoveFromSweepOrder(ThermalBlock bound)
        {
            int slot = bound.SweepSlot;
            if (slot < 0 || slot >= sweepOrder.Count || sweepOrder[slot] != bound)
            {
                // Should not occur, but a linear search is preferable to a corrupt list.
                sweepOrder.Remove(bound);
                bound.SweepSlot = -1;
                return;
            }

            int last = sweepOrder.Count - 1;
            if (slot != last)
            {
                ThermalBlock moved = sweepOrder[last];
                sweepOrder[slot] = moved;
                moved.SweepSlot = slot;
            }

            sweepOrder.RemoveAt(last);
            bound.SweepSlot = -1;
        }

        /// <summary>
        /// The bound block whose minimum cell is <paramref name="min"/>, or null.
        ///
        /// This is the model's key, shared by <see cref="BlockInstance.Position"/> and
        /// <see cref="OverheatEvent"/>. Callers holding a game block should pass <c>block.Min</c>;
        /// callers holding an arbitrary cell should use <see cref="GetAtCell"/>.
        /// </summary>
        public ThermalBlock Get(Vector3I min)
        {
            ThermalBlock bound;
            return blocks.TryGetValue(min, out bound) ? bound : null;
        }

        /// <summary>
        /// The bound block occupying a cell. For a multi-cell block this differs from the block's own
        /// position.
        /// </summary>
        public ThermalBlock GetAtCell(Vector3I cell)
        {
            ThermalBlock direct = Get(cell);
            if (direct != null) return direct;

            BlockInstance instance = Model.GetAtCell(cell);
            if (instance == null) return null;

            return Get(instance.Position);
        }
    }
}
