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
    /// The simulation knows nothing about the game: it is fed a block layout, an environment
    /// sample and a frame length, and hands back temperatures and overheat events. Everything on
    /// this side of that line — definitions, entity events, raycasts, damage, storage — lives
    /// here and in the partials next to it.
    ///
    /// The component polls on the ten-frame tick rather than every frame. Step pacing is the
    /// simulation's own business (<see cref="SimulationScheduler"/>), so polling faster only
    /// costs entity update callbacks.
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
        /// Placed blocks by their minimum cell, one entry per block, not per cell.
        ///
        /// Keyed on <c>Min</c> rather than the game's <c>SlimBlock.Position</c> because that is
        /// the identity the model uses (<see cref="BlockInstance.Position"/>). The two agree for
        /// a 1x1x1 block and differ for every larger one, so keying on Position silently loses
        /// exactly the blocks most likely to overheat.
        /// </summary>
        private readonly Dictionary<Vector3I, ThermalBlock> blocks =
            new Dictionary<Vector3I, ThermalBlock>(Vector3I.Comparer);

        /// <summary>
        /// Temperatures of blocks removed recently, so a section cut off the grid keeps its
        /// heat when it becomes a grid of its own, and so rebuilding a block does not reset it.
        ///
        /// Entries are consumed when the position is built on again, which most of them never
        /// are — a ship that is slowly ground down would otherwise accumulate one per block
        /// destroyed, for the life of the world. The map is dropped wholesale once it grows past
        /// what a split could plausibly need.
        /// </summary>
        public readonly Dictionary<Vector3I, float> RecentlyRemoved =
            new Dictionary<Vector3I, float>(Vector3I.Comparer);

        /// <summary>
        /// Remembered removals kept before the map is cleared. A split hands over its blocks in
        /// the same frame they are removed, so nothing that matters lives here for long.
        /// </summary>
        public const int MaxRecentlyRemoved = 4096;

        /// <summary>
        /// Air vents on this grid. Kept apart from the rest so the pressurisation sweep visits
        /// the handful of blocks that can answer for a room rather than every block on the ship.
        /// </summary>
        private readonly List<ThermalBlock> vents = new List<ThermalBlock>();

        internal void RegisterVent(ThermalBlock bound)
        {
            if (bound != null && !vents.Contains(bound)) vents.Add(bound);
        }

        internal void UnregisterVent(ThermalBlock bound)
        {
            vents.Remove(bound);
        }

        /// <summary>
        /// Heat pumps on this grid. Kept apart for the same reason the vents are: the state they
        /// exchange with the game — a switch, a power draw — is theirs alone, and a grid with none
        /// should not walk its blocks looking for them.
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
        /// Every live grid component. Kept so telemetry can be switched on mid-session and
        /// still find the grids that already exist.
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

            // Thresholds are registered against the mod, not against a grid, so a grid built after
            // a mod registered one still reports it.
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

            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (disabled) return;

            if (Grid.Physics == null)
            {
                // Projections and blueprints have no physics and never simulate.
                NeedsUpdate = MyEntityUpdateEnum.NONE;
                disabled = true;
                return;
            }

            Simulation.LoopProperties = ThermalBlockCatalog.ToLoopProperties(
                ThermalLoopDefintion.GetDefinition(ThermalLoopDefintion.DefaultLoopDefinitionId));

            AddExistingBlocks();

            // One full build is far cheaper than the incremental path replayed once per block,
            // and it leaves the room map complete before the first step instead of after it.
            Simulation.RebuildAll();

            Load();
            started = true;
        }

        /// <summary>
        /// Picks up blocks that already existed when the component attached. Grid load order does
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
        /// The grid is going away. The telemetry record takes its final snapshot now, while the
        /// nodes still exist; it stays in the registry so a destroyed ship is still reported.
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
                if (model == null || model.Thermal.IgnoreThermals)
                {
                    if (Stats != null) Stats.BlocksIgnored++;
                    return;
                }

                ThermalBlock bound = new ThermalBlock(this, block, model);

                // The model owns the layout and can refuse a block — a cell already occupied
                // means the two views have diverged. Registering the binding only after it has
                // accepted keeps this side from holding a block the simulation does not have.
                float temperature = StartingTemperature(block.Min);
                bound.Node = Simulation.AddBlock(bound.Instance, temperature);

                blocks.Add(block.Min, bound);
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
        /// A block placed where one was just removed inherits its heat; anything else starts at
        /// the grid's default. This is what carries temperature across a grid split, where the
        /// same position is removed from one grid and added to another in the same frame.
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
        public void RefreshBlock(ThermalBlock bound)
        {
            if (bound == null || bound.Instance == null) return;

            Simulation.RefreshBlock(bound.Instance);
            if (Stats != null) Stats.SurfaceRecalcs++;
        }

        /// <summary>
        /// Refreshes a block whose <em>sealing</em> changed and nothing else — a door. Doors
        /// cycle often, and neither conduction nor coolant plumbing depends on whether a face
        /// seals, so this deliberately does not touch either.
        /// </summary>
        public void RefreshBlockSealing(ThermalBlock bound)
        {
            if (bound == null || bound.Instance == null) return;

            Simulation.RefreshBlockSealing(bound.Instance);
            if (Stats != null) Stats.SurfaceRecalcs++;
        }

        /// <summary>
        /// Carries temperatures onto a section that has just been cut off.
        ///
        /// The blocks were removed from the parent this frame, so the parent holds their heat in
        /// <see cref="RecentlyRemoved"/>. Which of the two events the game raises first is not
        /// something a mod controls, so both orderings are handled: a block the child already
        /// has is set directly, and one it has not built yet is handed over for
        /// <see cref="StartingTemperature"/> to pick up when it arrives.
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
        /// Carries temperatures off a grid that is being absorbed into another.
        ///
        /// The two grids have different cell coordinates, so positions are mapped through world
        /// space rather than assumed to match — they only would if the merge happened to leave
        /// both origins aligned.
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
        /// Starts or stops feeding this grid's telemetry record, for the runtime toggle. A grid
        /// that has never been registered gets a record now, so a session can be switched into
        /// collection without a reload.
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

            // A block resolves its per-definition record once, when it is placed. Blocks placed
            // while collection was off hold none, so switching it on has to hand them one or the
            // report silently omits every block that predates the switch.
            foreach (ThermalBlock bound in blocks.Values)
            {
                bound.RefreshStats();
            }
        }

        /// <summary>
        /// The bound block whose minimum cell is <paramref name="min"/>, or null.
        ///
        /// This is the model's own key — <see cref="BlockInstance.Position"/> and
        /// <see cref="OverheatEvent"/> both speak it. Callers holding a game block should use
        /// <c>block.Min</c>, and callers holding an arbitrary cell should use
        /// <see cref="GetAtCell"/>.
        /// </summary>
        public ThermalBlock Get(Vector3I min)
        {
            ThermalBlock bound;
            return blocks.TryGetValue(min, out bound) ? bound : null;
        }

        /// <summary>
        /// The bound block occupying a cell, which for a multi-cell block is not the same as the
        /// block's own position.
        /// </summary>
        public ThermalBlock GetAtCell(Vector3I cell)
        {
            ThermalBlock direct = Get(cell);
            if (direct != null) return direct;

            BlockInstance instance = Model.GetAtCell(cell);
            if (instance == null) return null;

            return Get(instance.Position);
        }

        /// <summary>Temperature at a grid position in Kelvin, or 0 when nothing is there.</summary>
        public float TemperatureAt(Vector3I position)
        {
            ThermalBlock bound = GetAtCell(position);
            return (bound == null || bound.Node == null) ? 0f : bound.Node.Temperature;
        }
    }
}
