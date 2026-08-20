using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>One block face, and the model's account of whether it is open to the sky.</summary>
    /// <summary>
    /// One compartment, whether this model mapped it or only the game holds it.
    ///
    /// Both kinds share one row type so a reader sees every compartment on the grid in one table
    /// rather than joining two by hand.
    ///
    /// Every figure is copied from <see cref="ThermalGrid.RoomVerdict"/> and
    /// <see cref="ThermalGrid.LostRoom"/>. Nothing here queries the game; the room scan does that
    /// once and this formats its result.
    /// </summary>
    public struct RoomRow
    {
        /// <summary>"mapped" when this model found it, "lost" when only the game holds it.</summary>
        public string Kind;

        /// <summary>Index within its own kind. Stable while the grid's shape is.</summary>
        public int Index;

        public int AnchorX;
        public int AnchorY;
        public int AnchorZ;

        public int CellCount;
        public float Volume;

        /// <summary>Mapped rooms only: standing open to the sky through a door.</summary>
        public bool Vented;

        /// <summary>Air state the simulation is running with for this room.</summary>
        public float Pressure;
        public float AirMass;
        public float TemperatureKelvin;

        /// <summary>
        /// Blocks the air is coupled to. Zero alongside a non-zero <see cref="AirMass"/> indicates
        /// a room that was given air but never linked, so its air is inert.
        /// </summary>
        public int LinkCount;

        /// <summary>The game's airtightness verdict at the anchor cell. Sealed is not the same as full.</summary>
        public bool GameAirtight;

        /// <summary>
        /// The game's oxygen level in the room, 0..1, or -1 when unavailable. Read from the grid's
        /// gas system, independently of this model's map and of any vent.
        /// </summary>
        public float GameOxygen;

        /// <summary>Air vents standing on it, by terminal name.</summary>
        public string Vents;

        /// <summary>True when a vent on it reports the game considers its room pressurised.</summary>
        public bool VentPressurised;

        /// <summary>Highest oxygen level any vent on it reports, or -1 when none reported.</summary>
        public float OxygenLevel;

        /// <summary>
        /// True when the game has oxygen in this room and this model runs no air. Compares oxygen
        /// rather than airtightness, so a sealed empty compartment is not flagged.
        /// </summary>
        public bool Disagreement;

        /// <summary>Lost rooms only: faces this model leaves open that the game seals.</summary>
        public int LeakCount;

        /// <summary>Lost rooms only: the block subtypes across those faces, worst first.</summary>
        public string LeakingBlocks;
    }

    public struct SurfaceRow
    {
        public string Block;
        public string Subtype;
        public Vector3I Cell;
        public Vector3I Size;
        public int Face;
        public int Cells;
        public int Exposed;
        public int Sealed;
        public int Mounted;
        public int Interior;
        public float SunDot;
        public float SunLitFraction;
        public float SolarWatts;
        public float Temperature;
        public float ExposedArea;
    }

    /// <summary>
    /// One raw reading of the world at a grid, for balancing a planet's climate.
    ///
    /// Kept unaggregated: balancing means comparing a measurement against what it should be at that
    /// altitude, latitude and hour, none of which survive an average. Each row carries the grid's
    /// position, the world's readings, this model's interpretation of them, and the game's own
    /// weather figures.
    /// </summary>
    public struct EnvironmentRow
    {
        public double Seconds;

        public string Planet;
        public double AltitudeSurface;
        public double AltitudeSealevel;
        public float LatitudeDegrees;

        public float SunElevationDegrees;
        public float AirDensity;
        public float AtmosphereFactor;

        public float AmbientKelvin;
        public bool Underground;

        /// <summary>Metres of ground over the grid. Zero or less is open air.</summary>
        public float Depth;

        /// <summary>Convective coefficient in force, W/(m^2 K), with wind and weather applied.</summary>
        public float ConvectionCoefficient;

        public float SolarEnergy;
        public float SolarOcclusion;

        public float WindSpeed;

        /// <summary>Where the wind is going, in degrees east of the planet's north.</summary>
        public float WindBearingDegrees;

        /// <summary>The game's wind figure at this point; the ceiling the wind field scales.</summary>
        public float WindCeiling;

        public float WeatherIntensity;

        /// <summary>The game's name for the weather over the grid; empty in clear air.</summary>
        public string Weather;

        /// <summary>Temperature offset that weather applied to the air, K.</summary>
        public float WeatherAmbientOffset;

        /// <summary>The game's own environment comfort scale at this point, 0..1.</summary>
        public float GameTemperature;

        public string SurfaceMaterial;

        public float GridMeanKelvin;
        public float GridPeakKelvin;
    }

    /// <summary>
    /// Everything observed about one grid over that grid's lifetime.
    ///
    /// A record outlives its grid: <see cref="Close"/> takes a final snapshot and drops the entity
    /// reference, but the record stays in the registry so a grid destroyed mid-session still
    /// appears in the report.
    ///
    /// Never reached when telemetry is off; the caller checks <see cref="Telemetry.Enabled"/> and
    /// the record is not created.
    /// </summary>
    public class GridTelemetry
    {
        public readonly long EntityId;
        public string Name;
        public string GridSize;
        public bool IsStatic;
        public float GridSizeMeters;

        public readonly double OpenedAtSeconds;
        public double ClosedAtSeconds = -1;
        public bool IsClosed;

        /// <summary>
        /// First and last session frame this record was ticked on.
        ///
        /// A record cannot tick more often than the session frames, since both happen in the same
        /// <c>Simulate</c> call, so <c>SimulationTime.Calls</c> must fit within
        /// <c>LastTickFrame - FirstTickFrame + 1</c>, which must fit within
        /// <c>Telemetry.FramesObserved</c>. Recorded so a report that violates this can be
        /// diagnosed from the file rather than inferred from totals.
        /// </summary>
        public long FirstTickFrame = -1;
        public long LastTickFrame = -1;

        /// <summary>Records that this grid was ticked on the session's current frame.</summary>
        public void NoteTick(long frame)
        {
            if (FirstTickFrame < 0) FirstTickFrame = frame;
            LastTickFrame = frame;
        }

        /// <summary>Null once the grid has closed, so the record does not keep the entity alive.</summary>
        public ThermalGrid Grid;

        // ---- structure ------------------------------------------------------------------
        public readonly RunningStat CellCount = new RunningStat();
        public readonly RunningStat BlockCount = new RunningStat();
        public readonly RunningStat NeighborLinks = new RunningStat();
        public readonly RunningStat RoomCount = new RunningStat();
        public readonly RunningStat ExternalCells = new RunningStat();
        public readonly RunningStat SolidCells = new RunningStat();
        public readonly RunningStat RoomCells = new RunningStat();

        /// <summary>
        /// Block cells the room map read as open space. Non-zero means the flood fill walked through
        /// structure, which merges a sealed room into the outdoors.
        /// </summary>
        public readonly RunningStat LeakedCells = new RunningStat();

        /// <summary>
        /// Block cells the map leaves outdoors. Expected for anything not airtight; the first figure
        /// to check when a room that should be sealed is not.
        /// </summary>
        public readonly RunningStat OpenBlockCells = new RunningStat();

        /// <summary>Most recent room audit, retained so the report can name what leaked.</summary>
        public RoomAudit LastAudit;
        public bool HasAudit;

        /// <summary>Mapper passes completed when the last audit ran; audits follow passes.</summary>
        private int auditedPass = -1;
        public readonly RunningStat SurfaceEntries = new RunningStat();
        public readonly RunningStat CoolantLoops = new RunningStat();

        // ---- what the plumbing is actually doing -----------------------------------------
        //
        // The loop count and a substep figure were the only coolant data a report carried, so a
        // dump could show four ships each holding a closed pumped ring and not say whether any of
        // them moved a watt. Absorbed and rejected are kept apart because a loop in balance nets to
        // about zero exactly when it is carrying the most heat.

        /// <summary>Coolant temperature across every loop on the grid, K.</summary>
        public readonly RunningStat LoopTemperature = new RunningStat();

        /// <summary>Heat every loop drew out of the blocks it touches, W.</summary>
        public readonly RunningStat LoopWattsAbsorbed = new RunningStat();

        /// <summary>Heat every loop pushed back into the blocks it touches, W.</summary>
        public readonly RunningStat LoopWattsRejected = new RunningStat();

        /// <summary>Pipe blocks per loop, so a report can tell a long ring from a short one.</summary>
        public readonly RunningStat LoopPipes = new RunningStat();

        /// <summary>Sink and pipe links per loop: how many blocks the fluid is coupled to.</summary>
        public readonly RunningStat LoopLinks = new RunningStat();

        /// <summary>Heat pumps on the grid, and how many of them are actually working.</summary>
        public readonly RunningStat HeatPumps = new RunningStat();
        public readonly RunningStat HeatPumpsRunning = new RunningStat();

        /// <summary>Pumps bolted to a block on only one face, so they can move nothing.</summary>
        public long HeatPumpUnconnectedSamples;

        /// <summary>Pumps connected but switched off by their terminal.</summary>
        public long HeatPumpDisabledSamples;

        /// <summary>Pumps connected and enabled but given no power by the grid.</summary>
        public long HeatPumpStarvedSamples;

        /// <summary>Samples of a pump that ran, with what it achieved.</summary>
        public readonly RunningStat HeatPumpLiftWatts = new RunningStat();
        public readonly RunningStat HeatPumpPowerWatts = new RunningStat();
        public readonly RunningStat HeatPumpRejectedWatts = new RunningStat();
        public readonly RunningStat HeatPumpCoefficient = new RunningStat();

        /// <summary>
        /// Which of the three limits bound a running pump. Docs call this the block's whole
        /// character, and it is the one thing a temperature cannot show.
        /// </summary>
        public long HeatPumpLimitedByRating;
        public long HeatPumpLimitedByCarnotOrHeat;
        public readonly RunningStat RecentlyRemovedSize = new RunningStat();
        public readonly RunningStat MapperQueueDepth = new RunningStat();
        public int PeakCellCount;

        // ---- lifecycle events -----------------------------------------------------------
        public long BlocksAdded;
        public long BlocksRemoved;
        public long BlocksIgnored;
        public long BlocksRestored;
        public long RoomsRestored;
        public long ForeignBlockEvents;
        public long Splits;
        public long Merges;
        public long DoorStateChanges;
        public long SurfaceRecalcs;

        /// <summary>
        /// Room pressure sweeps run, compartments visited by them, game API calls made, and how
        /// often the vent fallback was needed — plus the vents it walked when it was.
        ///
        /// Counted rather than only timed, because the question this answers is whether the sweep
        /// scales with a station's compartment count, and a millisecond figure on a twelve-room
        /// ship cannot answer it.
        /// </summary>
        public long RoomPressureSweeps;
        public long RoomPressureRoomVisits;
        public long RoomPressureGameQueries;
        public long RoomPressureVentScans;
        public long RoomPressureVentsWalked;
        public long MapperCompletions;

        // ---- persistence ----------------------------------------------------------------
        public long Saves;
        public long Loads;
        public long SaveBytes;
        public long LoadBytes;

        // ---- simulation -----------------------------------------------------------------
        public long SimulationSteps;
        public long NodeUpdates;
        public long SampledNodes;

        /// <summary>Substeps the solver needed per step. Above one indicates a stiff grid.</summary>
        public readonly RunningStat Substeps = new RunningStat();

        /// <summary>
        /// Steps that reached the substep cap and clamped. Measures whether real grids are stiffer
        /// than the explicit integrator can follow at the configured settings.
        /// </summary>
        public long ClampedSteps;

        /// <summary>
        /// Fraction of real time this grid's simulation keeps up with, 0..1, and the simulated
        /// seconds it declined to advance.
        ///
        /// Below one means the work budget is binding: the grid is too large to simulate at full
        /// rate and is taking shorter steps rather than coarser ones. That is the intended trade,
        /// but it also changes how long the grid takes to cool, so it is reported.
        /// </summary>
        public readonly RunningStat SimulationRate = new RunningStat();
        public double SimulatedSecondsSkipped;

        public readonly RunningStat NodesPerStep = new RunningStat();
        public readonly RunningStat CriticalBlocks = new RunningStat();
        public long DamageEvents;
        public double TotalDamage;
        public float PeakTemperature = float.MinValue;
        public string PeakTemperatureBlock = "-";
        public readonly RunningStat HottestBlockTemperature = new RunningStat();
        public readonly Histogram FinalTemperatures = new Histogram(Histogram.TemperatureEdges());

        // ---- environment ----------------------------------------------------------------
        public readonly RunningStat AmbientTemperature = new RunningStat();
        public readonly RunningStat AirDensity = new RunningStat();
        public readonly RunningStat AtmosphereFactor = new RunningStat();
        public readonly RunningStat WindSpeed = new RunningStat();
        public readonly RunningStat ConvectionCoefficient = new RunningStat();
        public readonly RunningStat EffectiveSolarEnergy = new RunningStat();

        /// <summary>
        /// Share of the grid in shadow per sample, 0..1. Distinct from
        /// <see cref="OccludedSamples"/>, which counts only samples where none of the grid was lit;
        /// partial occlusion falls between the two.
        /// </summary>
        public readonly RunningStat OccludedShare = new RunningStat();
        public readonly RunningStat Speed = new RunningStat();
        public long EnvironmentSamples;
        public long OccludedSamples;
        public long InAtmosphereSamples;
        public readonly HashSet<string> Planets = new HashSet<string>();

        // ---- substeps -------------------------------------------------------------------

        /// <summary>
        /// Substeps the stability estimate demanded, before the <c>MaxSubsteps</c> cap and before
        /// rounding. <see cref="Substeps"/> is what was granted; a difference means the cap is
        /// binding, and if <c>MaxElementVisitsPerStep</c> also binds the step was shortened rather
        /// than the substeps coarsened.
        /// </summary>
        public readonly RunningStat RequiredSubsteps = new RunningStat();

        /// <summary>Nodes <c>MaxSubstepsPerBlock</c> raised the heat capacity of, per step.</summary>
        public readonly RunningStat FlooredNodes = new RunningStat();

        /// <summary>
        /// Last full walk of what sets this grid's substep count: the responsible element, the
        /// distribution behind it, and the effect of each candidate cap.
        ///
        /// O(nodes), so taken infrequently, and retaken when the grid closes so a report has one
        /// even for a grid too short-lived for the periodic capture.
        /// </summary>
        public ThermalSolver.SubstepProfile Profile;

        /// <summary>
        /// Subtype and position of the block that set the substep count when the profile was last
        /// taken.
        ///
        /// Resolved at capture rather than at report time: the profile carries an index into the
        /// solver's node list, which is compacted on every block removal, so a later lookup would
        /// name a different block.
        /// </summary>
        public string WorstSubstepBlock = "-";

        private int stepsSinceProfile = ProfileInterval;

        /// <summary>Solver steps between full substep profiles.</summary>
        private const int ProfileInterval = 64;

        // ---- cost -----------------------------------------------------------------------
        public readonly TimingStat SimulationTime = new TimingStat("grid simulation");

        /// <summary>
        /// Stage timings, filled by the simulation. Attached only while collection is on, so an
        /// uninstrumented world carries no profiler.
        /// </summary>
        public readonly GridProfiler Profiler = new GridProfiler();
        public readonly TimingStat SolarTime = new TimingStat("solar occlusion");
        public readonly TimingStat SaveTime = new TimingStat("save");
        public readonly TimingStat LoadTime = new TimingStat("load");

        /// <summary>
        /// Steps between structure samples. Structure changes only when blocks do, so sampling it
        /// every step adds nothing.
        /// </summary>
        private const int StructureInterval = 8;

        private int stepsSinceStructure = StructureInterval;

        /// <summary>Cursor rotating which slice of nodes the per-block sampling visits.</summary>
        private int sampleOffset;

        public GridTelemetry(ThermalGrid grid)
        {
            Grid = grid;
            EntityId = grid.Grid != null ? grid.Grid.EntityId : 0;
            OpenedAtSeconds = Telemetry.SessionSeconds;
            RefreshIdentity();
        }

        public void RefreshIdentity()
        {
            if (Grid == null || Grid.Grid == null) return;

            Name = Grid.Grid.DisplayName;
            if (string.IsNullOrEmpty(Name)) Name = "(unnamed)";
            IsStatic = Grid.Grid.IsStatic;
            GridSizeMeters = Grid.Grid.GridSize;
            GridSize = Grid.Grid.GridSizeEnum == MyCubeSize.Large ? "Large" : "Small";
        }

        // ------------------------------------------------------------------------------------
        // Per-step collection
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Called once per batch of solver steps; the module's hot path. Records solver-level
        /// figures every call, structure occasionally, and one rotating slice of nodes, so full
        /// per-block coverage costs a single pass spread over <see cref="Telemetry.SampleStride"/>
        /// steps.
        /// </summary>
        public void OnSteps(int steps)
        {
            if (Grid == null || Grid.Simulation == null) return;

            ThermalSolver solver = Grid.Simulation.Solver;
            int nodeCount = solver.Nodes.Count;

            SimulationSteps += steps;
            NodeUpdates += (long)nodeCount * steps;

            Substeps.Add(solver.LastSubsteps);
            RequiredSubsteps.Add(solver.LastRequiredSubsteps);
            FlooredNodes.Add(solver.FlooredNodes);
            if (solver.LastStepWasClamped) ClampedSteps++;

            SimulationRate.Add((float)Grid.Simulation.SimulationRate);
            SimulatedSecondsSkipped = Grid.Simulation.SimulatedSecondsSkipped;
            NodesPerStep.Add(nodeCount);
            CriticalBlocks.Add(Grid.CriticalBlocks);

            if (Grid.HottestNode != null)
            {
                HottestBlockTemperature.Add(Grid.HottestNode.Temperature);
                NoteTemperature(Grid.HottestNode);
            }

            stepsSinceStructure += steps;
            if (stepsSinceStructure >= StructureInterval)
            {
                stepsSinceStructure = 0;
                SampleStructure();
            }

            stepsSinceProfile += steps;
            if (stepsSinceProfile >= ProfileInterval)
            {
                stepsSinceProfile = 0;
                CaptureProfile(solver);
            }

            SampleLoopsAndPumps(solver);
            SampleNodes(solver);
        }

        /// <summary>
        /// Records what every loop and heat pump on the grid achieved on the step just finished.
        ///
        /// Both lists are short — a ship carries a handful of each, against thousands of nodes — so
        /// this walks them whole rather than sampling a slice, and it reads figures the solver
        /// already accumulated rather than computing anything.
        /// </summary>
        private void SampleLoopsAndPumps(ThermalSolver solver)
        {
            IList<CoolantLoop> loops = solver.Loops;
            for (int i = 0; i < loops.Count; i++)
            {
                CoolantLoop loop = loops[i];
                LoopTemperature.Add(loop.Temperature);
                LoopWattsAbsorbed.Add(loop.LastWattsAbsorbed);
                LoopWattsRejected.Add(loop.LastWattsRejected);
                LoopPipes.Add(loop.PipeCount);
                LoopLinks.Add(loop.Links.Count);
            }

            IList<HeatPumpDevice> pumps = solver.HeatPumps;
            HeatPumps.Add(pumps.Count);

            int running = 0;
            for (int i = 0; i < pumps.Count; i++)
            {
                HeatPumpDevice pump = pumps[i];

                // Counted in order of what a player can fix: bolt something to it, switch it on,
                // then give it power.
                if (!pump.IsConnected)
                {
                    HeatPumpUnconnectedSamples++;
                    continue;
                }
                if (!pump.Enabled)
                {
                    HeatPumpDisabledSamples++;
                    continue;
                }
                if (pump.PowerAvailable <= 0f)
                {
                    HeatPumpStarvedSamples++;
                    continue;
                }

                running++;
                HeatPumpLiftWatts.Add(pump.LastLiftedWatts);
                HeatPumpPowerWatts.Add(pump.LastPowerWatts);
                HeatPumpRejectedWatts.Add(pump.LastRejectedWatts);
                HeatPumpCoefficient.Add(pump.LastCoefficient);

                // At the rating the machine ran out before the physics did; short of it the Carnot
                // cost or the heat available in the cold block was what stopped it.
                if (pump.LastLiftedWatts >= pump.RatedWatts - 1f) HeatPumpLimitedByRating++;
                else HeatPumpLimitedByCarnotOrHeat++;
            }

            HeatPumpsRunning.Add(running);
        }

        /// <summary>
        /// Takes a full substep profile and resolves the responsible block while its node index is
        /// still valid.
        /// </summary>
        private void CaptureProfile(ThermalSolver solver)
        {
            ThermalSolver.SubstepProfile profile = solver.ProfileSubsteps();
            Profile = profile;

            int index = profile.WorstNodeIndex;
            if (index < 0 || index >= solver.Nodes.Count)
            {
                WorstSubstepBlock = "-";
                return;
            }

            ThermalNode node = solver.Nodes[index];
            string name = node.Block == null || node.Block.Model == null ? "?" : node.Block.Model.Name;

            WorstSubstepBlock = name + " " + (node.Block == null ? "" : node.Block.Position.ToString())
                + " (" + node.ThermalMass.ToString("n0") + " J/K)";
        }

        /// <summary>
        /// Walks one slice of the grid's nodes, feeding the per-definition statistics and the
        /// anomaly detector. The slice rotates, so every node is visited once per stride.
        /// </summary>
        private void SampleNodes(ThermalSolver solver)
        {
            int stride = Telemetry.SampleStride;
            if (stride < 1) stride = 1;

            IList<ThermalNode> nodes = solver.Nodes;
            sampleOffset = (sampleOffset + 1) % stride;

            for (int i = sampleOffset; i < nodes.Count; i += stride)
            {
                ThermalNode node = nodes[i];
                SampledNodes++;

                ThermalBlock bound = Grid.Get(node.Block.Position);
                BlockTypeTelemetry type = bound != null ? bound.Stats : null;

                if (type != null)
                {
                    type.OnUpdate(node, EntityId);
                    type.Sample(node);
                    type.SampleSubstepDemand(solver.NodeSubstepDemand(i), node, EntityId);
                }

                NoteTemperature(node);
                Telemetry.CheckNode(this, node);
            }
        }

        /// <summary>
        /// The structural shape of the grid. Every figure is a collection count, including the link
        /// count, which the solver maintains.
        /// </summary>
        public void SampleStructure()
        {
            if (Grid == null || Grid.Grid == null || Grid.Simulation == null) return;

            RefreshIdentity();

            ThermalSimulation simulation = Grid.Simulation;

            int cells = simulation.Solver.Nodes.Count;
            CellCount.Add(cells);
            if (cells > PeakCellCount) PeakCellCount = cells;

            BlockCount.Add(Grid.Grid.BlocksCount);
            NeighborLinks.Add(simulation.Solver.LinkCount);
            SurfaceEntries.Add(simulation.Surfaces.CellCount);
            CoolantLoops.Add(simulation.Solver.Loops.Count);
            RecentlyRemovedSize.Add(Grid.RecentlyRemoved.Count);
            MapperQueueDepth.Add(simulation.Rooms.PendingCells);
            MapperCompletions = simulation.Rooms.CompletedPasses;

            // Only a map produced by a completed pass is a measurement. The default map reads as
            // an empty grid and would drag these averages towards zero for a grid not yet mapped.
            if (MapperCompletions > 0)
            {
                RoomCount.Add(simulation.Rooms.Map.RoomCount);
                ExternalCells.Add(simulation.Rooms.Map.ExternalCellCount);
                SolidCells.Add(simulation.Rooms.Map.SolidCellCount);
                RoomCells.Add(simulation.Rooms.Map.RoomCellCount);
            }

            AuditRooms(simulation);

            if (Grid.Grid.Physics != null)
            {
                Speed.Add(Grid.Grid.Physics.LinearVelocity.Length());
            }
        }

        /// <summary>
        /// Cross-checks the published room map against the blocks that produced it.
        ///
        /// The map changes only when a pass completes, so the audit runs once per pass rather than
        /// once per structure sample; on a static grid it costs one integer compare per sample.
        /// </summary>
        private void AuditRooms(ThermalSimulation simulation)
        {
            // While a pass is in flight the published map predates the grid, so every block placed
            // since would audit as a disagreement.
            if (simulation.Rooms.HasWorkPending) return;

            int pass = simulation.Rooms.CompletedPasses;

            // Before the first pass completes there is only the all-external default map. Auditing
            // against it reports the whole hull as unaccounted for, which is what a short-lived
            // grid — a paste preview, a transient subgrid — would otherwise produce.
            if (pass == 0) return;

            if (pass == auditedPass) return;
            auditedPass = pass;

            LastAudit = simulation.AuditRooms();
            HasAudit = true;
            LeakedCells.Add(LastAudit.LeakedCells);
            OpenBlockCells.Add(LastAudit.OpenBlockCells);

            if (LastAudit.HasLeak) Telemetry.NoteRoomLeak(this, LastAudit);
        }

        public void SampleEnvironment(ThermalGrid grid)
        {
            if (grid == null || grid.Simulation == null) return;

            EnvironmentSample sample = grid.LastSample;
            EnvironmentState state = grid.LastState;

            EnvironmentSamples++;
            if (sample.IsSolarOccluded) OccludedSamples++;
            OccludedShare.Add(sample.SolarOcclusion);
            if (sample.AirDensity > 0.01f) InAtmosphereSamples++;

            AmbientTemperature.Add(state.AmbientTemperature);
            AirDensity.Add(sample.AirDensity);
            AtmosphereFactor.Add(state.AtmosphereFactor);
            WindSpeed.Add(sample.RelativeWindSpeed);
            ConvectionCoefficient.Add(state.EffectiveConvectionCoefficient);
            EffectiveSolarEnergy.Add(state.SolarEnergy);
        }

        public void NotePlanet(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (Planets.Count < 32) Planets.Add(name);
        }

        public void NoteTemperature(ThermalNode node)
        {
            if (node == null || node.Temperature <= PeakTemperature) return;

            PeakTemperature = node.Temperature;
            PeakTemperatureBlock = node.Block.Name + " " + node.Block.Position;
        }

        /// <summary>
        /// Walks every live node once and records its end state. Called at shutdown, and when a
        /// grid closes mid-session.
        /// </summary>
        public void SnapshotFinalState()
        {
            if (Grid == null || Grid.Simulation == null) return;

            SampleStructure();
            SnapshotSurfaces();
            SnapshotRooms();

            // Retaken here rather than reusing the last periodic capture, so a report describes
            // the grid as of the moment it was requested.
            CaptureProfile(Grid.Simulation.Solver);

            // Rebuilt rather than appended to, so a manual mid-session dump does not carry its
            // counts into the next report.
            FinalTemperatures.Clear();

            IList<ThermalNode> nodes = Grid.Simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                ThermalNode node = nodes[i];
                FinalTemperatures.Add(node.Temperature);

                ThermalBlock bound = Grid.Get(node.Block.Position);
                BlockTypeTelemetry type = bound != null ? bound.Stats : null;
                if (type == null) continue;

                type.OnFinalTemperature(node.Temperature);

                // The strided sampler may never have visited a rare block; this pass guarantees at
                // least one observation of every block on the grid.
                type.Sample(node);
            }
        }

        public void Close()
        {
            if (IsClosed) return;

            SnapshotFinalState();
            IsClosed = true;
            ClosedAtSeconds = Telemetry.SessionSeconds;
            Grid = null;
        }

        /// <summary>
        /// Every face of every block, and why the model treats it as exposed or not.
        ///
        /// Captured here rather than read at report time: reports are usually written as the world
        /// closes, by which point the grids no longer exist. The record outlives the grid, so the
        /// description must be taken while the grid is live.
        /// </summary>
        public readonly List<SurfaceRow> Surfaces = new List<SurfaceRow>();

        /// <summary>Every compartment on the grid, mapped or lost, as of the last snapshot.</summary>
        public readonly List<RoomRow> Rooms = new List<RoomRow>();

        /// <summary>True when the lost-room scan stopped at its cell limit rather than finishing.</summary>
        public bool RoomScanTruncated;

        /// <summary>False until a scan has run, distinguishing "none found" from "not scanned".</summary>
        public bool RoomScanRan;

        /// <summary>Readings of the world at this grid, oldest first.</summary>
        public readonly List<EnvironmentRow> Environment = new List<EnvironmentRow>();

        /// <summary>
        /// Records one reading. Called on the host's sampling cadence rather than per step, since
        /// the intended resolution is a curve over a planetary day.
        /// </summary>
        public void NoteEnvironmentProfile(EnvironmentRow row)
        {
            if (Environment.Count >= MaxEnvironmentRows) return;

            row.Seconds = Telemetry.SessionSeconds;
            Environment.Add(row);

            if (Environment.Count == MaxEnvironmentRows)
            {
                MyLog.Default.Info("[" + Settings.Name + "] [Telemetry] environment log for "
                    + Name + " full at " + MaxEnvironmentRows + " rows");
            }
        }

        /// <summary>Readings kept per grid. At one every ten seconds, roughly eleven hours.</summary>
        private const int MaxEnvironmentRows = 4000;

        /// <summary>
        /// Every compartment on the grid: those this model mapped, and those only the game holds.
        ///
        /// Forces a scan rather than reusing the slow cadence's last result, so a dump describes
        /// the grid as it currently stands. This is the only call here that queries the game;
        /// everything below copies its output.
        /// </summary>
        private void SnapshotRooms()
        {
            Rooms.Clear();
            RoomScanRan = false;

            if (Grid == null || Grid.Simulation == null) return;

            Grid.ScanLostRooms();

            RoomScanTruncated = Grid.LostRoomScanTruncated;
            RoomScanRan = Grid.HasLostRoomScan;

            float cellVolume = Grid.Grid.GridSize * Grid.Grid.GridSize * Grid.Grid.GridSize;

            IList<ThermalGrid.RoomVerdict> verdicts = Grid.RoomVerdicts;
            for (int i = 0; i < verdicts.Count; i++)
            {
                ThermalGrid.RoomVerdict verdict = verdicts[i];

                RoomRow row = new RoomRow();
                row.Kind = "mapped";
                row.Index = i;

                row.AnchorX = verdict.Anchor.X;
                row.AnchorY = verdict.Anchor.Y;
                row.AnchorZ = verdict.Anchor.Z;

                row.CellCount = verdict.CellCount;
                row.Volume = verdict.CellCount * cellVolume;
                row.Vented = verdict.Vented;

                RoomAirNode air = AirOf(i);
                if (air != null)
                {
                    row.Pressure = air.Pressure;
                    row.AirMass = air.AirMass;
                    row.TemperatureKelvin = air.Temperature;
                    row.LinkCount = air.Links.Count;
                }

                row.GameAirtight = verdict.GameAirtight;
                row.GameOxygen = verdict.GameOxygen;
                row.Vents = verdict.Vents;
                row.VentPressurised = verdict.VentPressurised;
                row.OxygenLevel = verdict.VentOxygen;
                row.Disagreement = verdict.IsDisagreement;

                Rooms.Add(row);
            }

            IList<ThermalGrid.LostRoom> lost = Grid.LostRooms;
            for (int i = 0; i < lost.Count; i++)
            {
                ThermalGrid.LostRoom room = lost[i];

                RoomRow row = new RoomRow();
                row.Kind = "lost";
                row.Index = room.Index;

                row.AnchorX = room.Anchor.X;
                row.AnchorY = room.Anchor.Y;
                row.AnchorZ = room.Anchor.Z;

                row.CellCount = room.CellCount;
                row.Volume = room.Volume;

                // True by construction: every cell in the room was reported airtight by the game.
                row.GameAirtight = true;
                row.GameOxygen = room.OxygenLevel;
                row.Vents = room.Vents;
                row.VentPressurised = room.VentSaysPressurised;
                row.OxygenLevel = room.OxygenLevel;
                row.LeakCount = room.LeakCount;
                row.LeakingBlocks = room.LeakingBlocks;

                Rooms.Add(row);
            }
        }

        /// <summary>Air node of one mapped room, or null when the solver holds none for it.</summary>
        private RoomAirNode AirOf(int roomIndex)
        {
            IList<RoomAirNode> air = Grid.Simulation.RoomAir;
            for (int i = 0; i < air.Count; i++)
            {
                if (air[i].RoomIndex == roomIndex) return air[i];
            }
            return null;
        }

        private void SnapshotSurfaces()
        {
            // A re-snapshot replaces this grid's rows, so its previous rows return to the session
            // budget first.
            Telemetry.SurfaceRowsCaptured -= Surfaces.Count;
            Surfaces.Clear();

            if (Grid == null || Grid.Simulation == null) return;

            SurfaceMap surfaces = Grid.Simulation.Surfaces;
            RoomMap rooms = Grid.Simulation.Rooms.Map;
            SunShadowMap shadow = Grid.Simulation.Solver.SunShadow;
            Vector3 sun = Grid.LastState.SunDirectionLocal;

            FaceExposure[] faces = new FaceExposure[Face.Count];

            foreach (ThermalBlock bound in Grid.Blocks)
            {
                ThermalNode node = bound.Node;
                if (node == null) continue;

                if (Surfaces.Count >= SurfaceRowLimit
                    || Telemetry.SurfaceRowsCaptured >= Telemetry.MaxSurfaceRows)
                {
                    // Never truncate silently: a truncated file reads as a smaller grid.
                    MyLog.Default.Info("[" + Settings.Name + "] [Telemetry] surface capture for "
                        + Name + " stopped at " + Surfaces.Count + " rows ("
                        + Telemetry.SurfaceRowsCaptured + " captured this session)");
                    return;
                }

                BlockInstance block = bound.Instance;
                SurfaceAudit.Explain(surfaces, block, rooms, faces);

                for (int face = 0; face < Face.Count; face++)
                {
                    SurfaceRow row = new SurfaceRow();
                    row.Block = node.Block.Name;
                    row.Subtype = bound.Block.BlockDefinition.Id.SubtypeName;
                    row.Cell = block.Min;
                    row.Size = block.Extents;
                    row.Face = face;
                    row.Cells = faces[face].Cells;
                    row.Exposed = faces[face].Exposed;
                    row.Sealed = faces[face].Sealed;
                    row.Mounted = faces[face].Mounted;
                    row.Interior = faces[face].Interior;
                    row.SunDot = Vector3.Dot(Face.Normals[face], sun);
                    row.SunLitFraction = shadow.FaceLitFraction(block, face);
                    row.SolarWatts = node.LastSolarWatts;
                    row.Temperature = node.Temperature;
                    row.ExposedArea = node.ExposedArea;

                    Surfaces.Add(row);
                    Telemetry.SurfaceRowsCaptured++;
                }
            }
        }

        /// <summary>Rows one grid may contribute. Six per block, so roughly eight thousand blocks.</summary>
        private const int SurfaceRowLimit = 50000;

        public double LifetimeSeconds
        {
            get { return (ClosedAtSeconds < 0 ? Telemetry.SessionSeconds : ClosedAtSeconds) - OpenedAtSeconds; }
        }

        public double OccludedFraction
        {
            get { return EnvironmentSamples == 0 ? 0 : (double)OccludedSamples / EnvironmentSamples; }
        }

        public double AtmosphereFraction
        {
            get { return EnvironmentSamples == 0 ? 0 : (double)InAtmosphereSamples / EnvironmentSamples; }
        }

        public string PlanetList
        {
            get
            {
                if (Planets.Count == 0) return "none (space)";
                string result = "";
                foreach (string p in Planets)
                {
                    if (result.Length > 0) result += ", ";
                    result += p;
                }
                return result;
            }
        }
    }
}
