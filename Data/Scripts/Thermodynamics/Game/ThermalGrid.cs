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

        /// <summary>Placed blocks by their grid position, one entry per block, not per cell.</summary>
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

            if (Settings.Instance == null)
            {
                Settings.Instance = Settings.GetDefaults();
            }

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

            if (blocks.ContainsKey(block.Position)) return;

            try
            {
                BlockModel model = ThermalBlockCatalog.Get(block);
                if (model == null || model.Thermal.IgnoreThermals)
                {
                    if (Stats != null) Stats.BlocksIgnored++;
                    return;
                }

                ThermalBlock bound = new ThermalBlock(this, block, model);
                blocks.Add(block.Position, bound);

                float temperature = StartingTemperature(block.Position);
                bound.Node = Simulation.AddBlock(bound.Instance, temperature);
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
            if (!blocks.TryGetValue(block.Position, out bound)) return;

            try
            {
                if (bound.Node != null)
                {
                    if (RecentlyRemoved.Count >= MaxRecentlyRemoved) RecentlyRemoved.Clear();
                    RecentlyRemoved[block.Position] = bound.Node.Temperature;
                }

                bound.Detach();
                Simulation.RemoveBlock(bound.Instance);
                blocks.Remove(block.Position);

                if (Stats != null) Stats.BlocksRemoved++;
                if (bound.Stats != null) bound.Stats.OnRemoved();
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalGrid.BlockRemoved", e);
            }
        }

        /// <summary>
        /// Refreshes the simulation's view of a block after something changed about the block
        /// itself: a door opening, construction finishing, damage taken.
        /// </summary>
        public void RefreshBlock(ThermalBlock bound)
        {
            if (bound == null || bound.Instance == null) return;

            Simulation.RefreshBlock(bound.Instance);
            if (Stats != null) Stats.SurfaceRecalcs++;
        }

        private void GridSplit(MyCubeGrid parent, MyCubeGrid child)
        {
            ThermalGrid a = parent.GameLogic.GetAs<ThermalGrid>();
            ThermalGrid b = child.GameLogic.GetAs<ThermalGrid>();
            if (a == null || b == null) return;

            if (a.Stats != null) a.Stats.Splits++;
            if (b.Stats != null) b.Stats.Splits++;

            // The child's blocks were removed from the parent this frame, so the parent still
            // holds their temperatures.
            foreach (KeyValuePair<Vector3I, ThermalBlock> entry in b.blocks)
            {
                float carried;
                if (!a.RecentlyRemoved.TryGetValue(entry.Key, out carried)) continue;

                if (entry.Value.Node != null) entry.Value.Node.Temperature = carried;
                a.RecentlyRemoved.Remove(entry.Key);
            }
        }

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

                ThermalBlock target;
                if (!a.blocks.TryGetValue(entry.Key, out target) || target.Node == null) continue;

                target.Node.Temperature = entry.Value.Node.Temperature;
            }
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
        }

        /// <summary>The bound block at a grid position, or null.</summary>
        public ThermalBlock Get(Vector3I position)
        {
            ThermalBlock bound;
            return blocks.TryGetValue(position, out bound) ? bound : null;
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
