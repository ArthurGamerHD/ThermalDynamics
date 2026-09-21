using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    public class ThermalSimulation
    {
        private readonly ThermalSettings settings;
        private readonly GridModel grid;
        private readonly SurfaceMap surfaces;
        private readonly RoomMapper rooms;
        private readonly ThermalSolver solver;
        private readonly SimulationScheduler scheduler;
/// <summary>SimulationWork operation.</summary>
        private readonly SimulationWork work = new SimulationWork();

        private LoopThermalProperties loopProperties = LoopThermalProperties.Default();
        private PlanetThermalProperties planet = PlanetThermalProperties.Default();

        private bool topologyDirty;

        private bool roomsDirty;
        private bool exposureDirty;

        public float DefaultTemperature = 293.15f;

        public ISimulationProfiler Profiler;

/// <summary>ThermalSimulation operation.</summary>
        public ThermalSimulation(ThermalSettings settings, GridModel grid)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (grid == null) throw new ArgumentNullException("grid");

            this.settings = settings;
            this.grid = grid;

/// <summary>SurfaceMap operation.</summary>
            surfaces = new SurfaceMap();
/// <summary>RoomMapper operation.</summary>
            rooms = new RoomMapper(surfaces);
/// <summary>ThermalSolver operation.</summary>
            solver = new ThermalSolver(settings, grid, surfaces);
/// <summary>SimulationScheduler operation.</summary>
            scheduler = new SimulationScheduler(settings);

            solver.Work = work;
            rooms.Work = work;

            rooms.Completed += OnRoomsCompleted;
        }

        public SimulationWork Work { get { return work; } }

        public ThermalSettings Settings { get { return settings; } }
        public GridModel Grid { get { return grid; } }
        public SurfaceMap Surfaces { get { return surfaces; } }
        public RoomMapper Rooms { get { return rooms; } }
        public ThermalSolver Solver { get { return solver; } }

/// <summary>EnsureCapacity operation.</summary>
        public void EnsureCapacity(int blocks)
        {
            grid.EnsureCellCapacity(blocks);
            solver.EnsureNodeCapacity(blocks);
        }
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

        public IList<OverheatEvent> Overheats
        {
            get { return overheats; }
        }

        public IList<ThresholdCrossing> Crossings
        {
            get { return crossings; }
        }

        public ThermalThresholds Thresholds
        {
            get { return solver.Thresholds; }
        }

/// <summary>List operation.</summary>
        private readonly List<OverheatEvent> overheats = new List<OverheatEvent>();
/// <summary>List operation.</summary>
        private readonly List<ThresholdCrossing> crossings = new List<ThresholdCrossing>();

        public double SimulatedSecondsSkipped { get; private set; }

        public double SimulationRate
        {
            get
            {
                double owed = SimulatedSecondsRun + SimulatedSecondsSkipped;
                return owed <= 0d ? 1d : SimulatedSecondsRun / owed;
            }
        }

        public double SimulatedSecondsRun { get; private set; }

        public float VentedWatts
        {
            get { return solver.LastVentedWatts; }
        }

        public float FrictionWatts
        {
            get { return solver.LastFrictionWatts; }
        }

        public float HeatGainWatts
        {
            get { return solver.LastHeatGainWatts; }
        }

        public float EnvironmentWatts
        {
            get { return solver.LastEnvironmentWatts; }
        }

        public long SubstepCost
        {
            get
            {
                return solver.LinkCount
                    + ((long)ThermalSettings.NodeCostInLinks * solver.Nodes.Count);
            }
        }

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

/// <summary>AffordableStepSeconds operation.</summary>
        public float AffordableStepSeconds(float seconds)
        {
            float required;
            return AffordableStepSeconds(seconds, solver.RequiredSubsteps(seconds), out required);
        }

/// <summary>AffordableStepSeconds operation.</summary>
        private float AffordableStepSeconds(float seconds, float required, out float demand)
        {
            demand = required;

            int budgetVisits = settings.MaxElementVisitsPerStep;
            if (budgetVisits <= 0) return seconds;

            long cost = SubstepCost;
            if (cost <= 0) return seconds;

            long substepBudget = budgetVisits / cost;
            if (substepBudget < 1) substepBudget = 1;

            if (required <= substepBudget) return seconds;

            if (settings.FloorBlocksWhenOverBudget && solver.AdaptiveSubstepFloor != substepBudget)
            {
                solver.AdaptiveSubstepFloor =
                    substepBudget > int.MaxValue ? int.MaxValue : (int)substepBudget;

                demand = solver.RequiredSubsteps(seconds);
                if (demand <= substepBudget) return seconds;
            }

            demand = substepBudget;
            return seconds * (substepBudget / required);
        }

        private const long MinimumSliceWork = 2048;

        private double workCredit;

        private const float BudgetReferenceSeconds = 10f / 60f;

        private double roomCredit;
        private double exposureCredit;
        private double shapeNormalCredit;

/// <summary>Share operation.</summary>
        private static int Share(ref double credit, int perTick, float frameSeconds)
        {
            credit += perTick * (frameSeconds / (double)BudgetReferenceSeconds);

            if (credit > perTick) credit = perTick;

            int slice = (int)credit;
            credit -= slice;
            return slice;
        }

        public long StepsCompleted { get; private set; }

        public bool NeedsEnvironmentSample
        {
            get { return !solver.StepInFlight; }
        }

        public bool StepInFlight
        {
            get { return solver.StepInFlight; }
        }

/// <summary>RunSteps operation.</summary>
        private void RunSteps(int steps, ref EnvironmentState state)
        {
            overheats.Clear();
            crossings.Clear();

            for (int i = 0; i < steps; i++)
            {
                float demand;
/// <summary>AffordableStepSeconds operation.</summary>
                float seconds = AffordableStepSeconds(settings.StepSeconds,
                    solver.RequiredSubsteps(settings.StepSeconds, state), out demand);

                if (seconds < settings.StepSeconds)
                {
                    SimulatedSecondsSkipped += settings.StepSeconds - seconds;
                }
                SimulatedSecondsRun += seconds;

                RefillLoops(seconds);

                solver.Step(seconds, state, demand);

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


/// <summary>Adds a block.</summary>
        public ThermalNode AddBlock(BlockInstance block)
        {
/// <summary>Adds a block.</summary>
            return AddBlock(block, DefaultTemperature);
        }

/// <summary>Adds a block.</summary>
        public ThermalNode AddBlock(BlockInstance block, float initialTemperature)
        {
            grid.Add(block);
            surfaces.AddBlock(block);
            ThermalNode node = solver.AddBlock(block, initialTemperature);
            MarkTopologyDirty();
            return node;
        }

/// <summary>Removes the block.</summary>
        public void RemoveBlock(BlockInstance block)
        {
            solver.RemoveBlock(block);
            surfaces.RemoveBlock(block);
            grid.Remove(block);
            MarkTopologyDirty();
        }

/// <summary>RefreshBlock operation.</summary>
        public void RefreshBlock(BlockInstance block)
        {
            if (block == null) return;

            int[] structuralBefore = block.StructuralSurfaces;
            int[] liveBefore = block.SelfSurfaces;

            block.RefreshSurfaces();
            surfaces.RemoveBlock(block);
            surfaces.AddBlock(block);

            solver.RefreshBlockLinks(block);

            bool structuralChanged = !SameSurfaces(structuralBefore, block.StructuralSurfaces);
            bool ventingChanged = !SameSurfaces(liveBefore, block.SelfSurfaces);

            if (structuralChanged || (ventingChanged && !rooms.Knows(block)))
            {
                MarkTopologyDirty();
                return;
            }

            MarkLayoutDirty();

            Begin(SimulationPhase.RoomMapping);

            bool venting = ventingChanged && rooms.Map.RefreshVenting();
            End(SimulationPhase.RoomMapping);

            Begin(SimulationPhase.Exposure);

            solver.RefreshExposureOf(block, rooms.Map);

            if (venting)
            {
                solver.RefreshExposureAround(rooms.Map, rooms.Map.ChangedRooms);
                solver.RebuildRoomAir(rooms.Map);
            }

            appliedOnce = true;
            End(SimulationPhase.Exposure);
        }

/// <summary>SameSurfaces operation.</summary>
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

/// <summary>RefreshBlockSealing operation.</summary>
        public void RefreshBlockSealing(BlockInstance block)
        {
            if (block == null) return;

            block.RefreshSurfaces();
            surfaces.RemoveBlock(block);
            surfaces.AddBlock(block);

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

            solver.RebuildRoomAir(rooms.Map);
            End(SimulationPhase.Exposure);
        }

        public bool HasPendingWork
        {
            get { return rooms.HasWorkPending || solver.ExposureRefreshPending; }
        }

        public IList<RoomAirNode> RoomAir
        {
            get { return solver.RoomAir; }
        }

/// <summary>Sets the roompressure.</summary>
        public bool SetRoomPressure(Vector3I cell, float pressure)
        {
            return solver.SetRoomPressure(rooms.Map, cell, pressure);
        }

/// <summary>Returns the roomair.</summary>
        public RoomAirNode GetRoomAir(Vector3I cell)
        {
            return solver.GetRoomAir(rooms.Map, cell);
        }

        public IList<HeatPumpDevice> HeatPumps
        {
            get { return solver.HeatPumps; }
        }

/// <summary>Returns the heatpump.</summary>
        public HeatPumpDevice GetHeatPump(BlockInstance block)
        {
            return solver.GetHeatPump(block);
        }

/// <summary>MarkTopologyDirty operation.</summary>
        public void MarkTopologyDirty()
        {
            topologyDirty = true;
            roomsDirty = true;
        }

/// <summary>MarkLayoutDirty operation.</summary>
        public void MarkLayoutDirty()
        {
            topologyDirty = true;
        }

/// <summary>RebuildAll operation.</summary>
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
            solver.RefreshShapeNormals();
            solver.RebuildRoomAir(rooms.Map);
            solver.RefreshHeatGeneration();
            End(SimulationPhase.Exposure);

            Begin(SimulationPhase.Topology);
            solver.PrepareForSteps();
            End(SimulationPhase.Topology);

            topologyDirty = false;
            roomsDirty = false;
            exposureDirty = false;
            appliedRevision = settings.Revision;
        }

/// <summary>Applies the settingsifchanged.</summary>
        private void ApplySettingsIfChanged()
        {
            if (appliedRevision == settings.Revision) return;
            appliedRevision = settings.Revision;

            if (!appliedOnce || appliedHeatTimeScale != settings.HeatTimeScale)
            {
                appliedHeatTimeScale = settings.HeatTimeScale;

                IList<ThermalNode> nodes = solver.Nodes;
                for (int i = 0; i < nodes.Count; i++)
                {
                    nodes[i].HeatTimeScale = settings.HeatTimeScale;
                }
            }

            if (!appliedOnce || appliedCoolantLoops != settings.EnableCoolantLoops
                || appliedWellMixedCoolant != settings.WellMixedCoolant)
            {
                appliedCoolantLoops = settings.EnableCoolantLoops;
                appliedWellMixedCoolant = settings.WellMixedCoolant;
                RebuildLoops();
            }

            if (!appliedOnce || appliedRoomAir != settings.EnableRoomAir
                || appliedRoomConvection != settings.RoomConvectionCoefficient
                || appliedRoomDensity != settings.RoomAirDensity)
            {
                appliedRoomAir = settings.EnableRoomAir;
                appliedRoomConvection = settings.RoomConvectionCoefficient;
                appliedRoomDensity = settings.RoomAirDensity;
                solver.RebuildRoomAir(rooms.Map);
            }
        }

        private int appliedRevision = -1;

        private bool appliedOnce;

        private float appliedHeatTimeScale = float.NaN;
        private bool appliedCoolantLoops;
        private bool appliedWellMixedCoolant;
        private bool appliedRoomAir;
        private float appliedRoomConvection = float.NaN;
        private float appliedRoomDensity = float.NaN;

/// <summary>Begin operation.</summary>
        private void Begin(SimulationPhase phase)
        {
            if (Profiler != null) Profiler.Begin(phase);
        }

/// <summary>End operation.</summary>
        private void End(SimulationPhase phase)
        {
            if (Profiler != null) Profiler.End(phase);
        }

/// <summary>RebuildLoops operation.</summary>
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

/// <summary>AuditRooms operation.</summary>
        public RoomAudit AuditRooms(int exampleLimit = RoomAuditor.DefaultExampleLimit)
        {
            return RoomAuditor.Audit(grid, surfaces, rooms.Map, exampleLimit);
        }

/// <summary>DiagnoseLoops operation.</summary>
        public CoolantLoopDiagnostics DiagnoseLoops(
            int exampleLimit = CoolantLoopDiagnostics.DefaultExampleLimit)
        {
/// <summary>CoolantLoopDiagnostics operation.</summary>
            CoolantLoopDiagnostics diagnostics = new CoolantLoopDiagnostics(exampleLimit);
            CoolantLoopBuilder.FindLoops(grid, loopProperties, DefaultTemperature, null, diagnostics);
            return diagnostics;
        }

/// <summary>DiagnoseBlock operation.</summary>
        public CoolantFault DiagnoseBlock(BlockInstance block)
        {
            if (block == null || block.Model.Coolant == null) return CoolantFault.None;
            if (FindLoopContaining(block) != null) return CoolantFault.None;

            CoolantFault fault;
            CoolantLoopBuilder.TraceRing(grid, block, out fault);
            return fault;
        }

/// <summary>FindLoopContaining operation.</summary>
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

/// <summary>OnRoomsCompleted operation.</summary>
        private void OnRoomsCompleted()
        {
            exposureDirty = true;
        }


/// <summary>Update operation.</summary>
        public void Update(float frameSeconds, EnvironmentSample sample)
        {
            ApplySettingsIfChanged();

            if (topologyDirty)
            {
                Begin(SimulationPhase.Topology);
                topologyDirty = false;

                solver.BuildLinksIfNeeded();
                RebuildLoops();

                solver.RebuildHeatPumps();

                solver.PrepareForSteps();

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
/// <summary>Share operation.</summary>
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

                if (!more) solver.RebuildRoomAir(rooms.Map);

                End(SimulationPhase.Exposure);
            }

            if (solver.BeginShapeNormalRefresh())
            {
                solver.StepShapeNormalRefresh(Share(ref shapeNormalCredit,
                    SimulationScheduler.ShapeNormalBudget(solver.Nodes.Count), frameSeconds));
            }

            Begin(SimulationPhase.Solver);
            AdvanceSolver(frameSeconds, sample);
            End(SimulationPhase.Solver);
        }

/// <summary>AdvanceSolver operation.</summary>
        private void AdvanceSolver(float frameSeconds, EnvironmentSample sample)
        {
            if (frameSeconds <= 0f) return;

            if (!solver.StepInFlight)
            {
                EnvironmentState state = EnvironmentSolver.Solve(settings, planet, sample);

                float demand;
/// <summary>AffordableStepSeconds operation.</summary>
                float seconds = AffordableStepSeconds(settings.StepSeconds,
                    solver.RequiredSubsteps(settings.StepSeconds, state), out demand);

                if (seconds < settings.StepSeconds)
                {
                    SimulatedSecondsSkipped += settings.StepSeconds - seconds;
                }
                SimulatedSecondsRun += seconds;

                RefillLoops(seconds);

                if (!solver.BeginStep(seconds, state, demand)) return;

                overheats.Clear();
                crossings.Clear();
            }

            workCredit += solver.StepWorkUnits * (double)frameSeconds * settings.StepsPerSecond;

            double ceiling = solver.StepWorkUnits;
            if (workCredit > ceiling) workCredit = ceiling;

            long budget = (long)workCredit;
            if (budget <= 0) return;

            long floor = MinimumSliceWork;
            if (floor > solver.StepWorkRemaining) floor = solver.StepWorkRemaining;
            if (budget < floor) return;

            workCredit -= budget;

            if (!solver.AdvanceStep(budget))
            {
                workCredit += budget - solver.LastAdvanceWork;
                return;
            }

            workCredit += budget - solver.LastAdvanceWork;

            CollectStepOutput();
        }

/// <summary>RefillLoops operation.</summary>
        private void RefillLoops(float seconds)
        {
            IList<CoolantLoop> all = solver.Loops;

            for (int i = 0; i < all.Count; i++)
            {
                CoolantLoop loop = all[i];
                if (loop.Pumps.Count == 0) continue;

                CoolantPump pump = loop.Pumps[0];
                if (pump.Block == null) continue;

                float watts = loop.Refill(seconds, pump.PowerAvailable);

                if (watts == pump.LastRefillWatts) continue;

                pump.Block.PowerConsumedWatts =
                    Math.Max(0f, pump.Block.PowerConsumedWatts - pump.LastRefillWatts) + watts;
                pump.LastRefillWatts = watts;

                ThermalNode node = solver.GetNode(pump.Block);
                if (node != null) node.RefreshHeatGeneration();
            }
        }

/// <summary>CollectStepOutput operation.</summary>
        private void CollectStepOutput()
        {
            StepsCompleted++;
            scheduler.CountStep();

            IList<OverheatEvent> stepOverheats = solver.Overheats;
            for (int o = 0; o < stepOverheats.Count; o++) overheats.Add(stepOverheats[o]);

            IList<ThresholdCrossing> stepCrossings = solver.Crossings;
            for (int c = 0; c < stepCrossings.Count; c++) crossings.Add(stepCrossings[c]);
        }

/// <summary>StepExact operation.</summary>
        public void StepExact(int steps, EnvironmentSample sample)
        {
            ApplySettingsIfChanged();

            solver.AbandonStep();
            workCredit = 0d;
            EnvironmentState state = EnvironmentSolver.Solve(settings, planet, sample);
            RunSteps(steps, ref state);
        }


        public int RoomsRestored { get; private set; }

/// <summary>Save operation.</summary>
        public string Save()
        {
/// <summary>List operation.</summary>
            List<StoredTemperature> blocks = new List<StoredTemperature>(solver.Nodes.Count);

            List<StoredHeldCoolant> held = null;

            for (int i = 0; i < solver.Nodes.Count; i++)
            {
                ThermalNode node = solver.Nodes[i];
                blocks.Add(new StoredTemperature(node.Block.Position, node.Temperature));

                if (node.HeldCoolantCapacity <= 0f) continue;
                if (held == null) held = new List<StoredHeldCoolant>();
                held.Add(new StoredHeldCoolant(node.Block.Position, node.HeldCoolantCapacity));
            }

/// <summary>List operation.</summary>
            List<StoredLoop> loops = new List<StoredLoop>(solver.Loops.Count);

            List<StoredLoopFill> fills = null;

            for (int i = 0; i < solver.Loops.Count; i++)
            {
                CoolantLoop loop = solver.Loops[i];
                loops.Add(new StoredLoop(loop.Signature, loop.Temperature));

                if (loop.FillFraction >= 1f) continue;
                if (fills == null) fills = new List<StoredLoopFill>();
                fills.Add(new StoredLoopFill(loop.Signature, loop.FillFraction));
            }

            IList<RoomAirNode> air = solver.RoomAir;
/// <summary>List operation.</summary>
            List<StoredRoom> rooms = new List<StoredRoom>(air.Count);
            for (int i = 0; i < air.Count; i++)
            {
                if (!air[i].Initialised) continue;
                rooms.Add(new StoredRoom(air[i].Anchor, air[i].Temperature));
            }

            return ThermalStorageCodec.Encode(blocks, loops, rooms, held, fills);
        }

/// <summary>Load operation.</summary>
        public int Load(string data)
        {
            RoomsRestored = 0;

/// <summary>List operation.</summary>
            List<StoredTemperature> blocks = new List<StoredTemperature>();
/// <summary>List operation.</summary>
            List<StoredLoop> storedLoops = new List<StoredLoop>();
/// <summary>List operation.</summary>
            List<StoredLoopFill> storedFills = new List<StoredLoopFill>();
/// <summary>List operation.</summary>
            List<StoredRoom> storedRooms = new List<StoredRoom>();
/// <summary>List operation.</summary>
            List<StoredHeldCoolant> storedHeld = new List<StoredHeldCoolant>();

            if (!ThermalStorageCodec.TryDecode(
                    data, blocks, storedLoops, storedRooms, storedHeld, storedFills))
            {
                return 0;
            }

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

            for (int l = 0; l < solver.Loops.Count; l++) solver.Loops[l].FillFraction = 1f;

            for (int i = 0; i < storedFills.Count; i++)
            {
                for (int l = 0; l < solver.Loops.Count; l++)
                {
                    if (solver.Loops[l].Signature != storedFills[i].Signature) continue;
                    solver.Loops[l].FillFraction = storedFills[i].Fill;
                    break;
                }
            }

            for (int i = 0; i < storedHeld.Count; i++)
            {
                BlockInstance block = grid.GetAtCell(storedHeld[i].Position);
                if (block == null) continue;

                ThermalNode node = solver.GetNode(block);
                if (node == null) continue;
                if (IsInALoop(block)) continue;

                node.HeldCoolantCapacity = storedHeld[i].Capacity;
            }

            RoomsRestored = solver.RestoreRoomAir(storedRooms);

            return restored;
        }

/// <summary>IsInALoop operation.</summary>
        private bool IsInALoop(BlockInstance block)
        {
            IList<CoolantLoop> live = solver.Loops;
            for (int i = 0; i < live.Count; i++)
            {
                if (live[i].Contains(block)) return true;
            }
            return false;
        }

/// <summary>ExportHotTail operation.</summary>
        public int ExportHotTail(float bandKelvin, int budget, List<StoredTemperature> results)
        {
            return HotTailCodec.Select(solver.Nodes, bandKelvin, budget, results);
        }

/// <summary>ImportHotTail operation.</summary>
        public int ImportHotTail(IList<StoredTemperature> tail)
        {
            if (tail == null) return 0;

            int applied = 0;
            for (int i = 0; i < tail.Count; i++)
            {
                ThermalNode node = solver.GetNodeAt(tail[i].Position);
                if (node == null) continue;

                float value = Math.Max(ThermalConstants.MinimumTemperature, tail[i].Temperature);

                if (Math.Abs(node.Temperature - value) <= HotTailCodec.TemperatureStep) continue;

                node.Temperature = value;
                applied++;
            }

            return applied;
        }
    }
}
