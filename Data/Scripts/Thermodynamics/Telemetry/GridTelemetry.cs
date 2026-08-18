using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// One block face, and the model's account of whether it is open to the sky.
    /// </summary>
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
    /// Everything observed about one grid, for the life of that grid.
    ///
    /// A record outlives its grid: when the grid closes, <see cref="Close"/> takes a final
    /// snapshot and drops the reference, but the record stays in the registry so a ship that was
    /// destroyed halfway through a session still appears in the report.
    ///
    /// Nothing here is reached at all when telemetry is off — the caller checks
    /// <see cref="Telemetry.Enabled"/> first, and the record is never even created.
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
        /// Block cells the room map read as open space. Anything but zero means the flood fill
        /// walked through structure, which is what turns a sealed room into no room at all.
        /// </summary>
        public readonly RunningStat LeakedCells = new RunningStat();

        /// <summary>
        /// Block cells the map leaves outdoors. Legitimate for anything not airtight, and the
        /// first number to read when a room that should be sealed is not.
        /// </summary>
        public readonly RunningStat OpenBlockCells = new RunningStat();

        /// <summary>The most recent room audit, kept so the report can name what leaked.</summary>
        public RoomAudit LastAudit;
        public bool HasAudit;

        /// <summary>Mapper passes completed when the last audit ran; audits follow passes.</summary>
        private int auditedPass = -1;
        public readonly RunningStat SurfaceEntries = new RunningStat();
        public readonly RunningStat CoolantLoops = new RunningStat();
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
        public long MapperCompletions;
        public long CoolantLoopsCreated;

        // ---- persistence ----------------------------------------------------------------
        public long Saves;
        public long Loads;
        public long SaveBytes;
        public long LoadBytes;

        // ---- simulation -----------------------------------------------------------------
        public long SimulationSteps;
        public long NodeUpdates;
        public long SampledNodes;

        /// <summary>Substeps the solver needed, per step. Above one means a stiff grid.</summary>
        public readonly RunningStat Substeps = new RunningStat();

        /// <summary>
        /// Steps that hit the substep cap and had to clamp. The redesign notes call for this
        /// specifically: it is how you find out whether real grids are stiffer than the explicit
        /// integrator can follow, rather than arguing about it from arithmetic.
        /// </summary>
        public long ClampedSteps;

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
        /// <see cref="OccludedSamples"/>, which counts only the samples where none of it was lit:
        /// a fleet flying through a station's shadow spends most of its time between the two.
        /// </summary>
        public readonly RunningStat OccludedShare = new RunningStat();
        public readonly RunningStat Speed = new RunningStat();
        public long EnvironmentSamples;
        public long OccludedSamples;
        public long InAtmosphereSamples;
        public readonly HashSet<string> Planets = new HashSet<string>();

        // ---- cost -----------------------------------------------------------------------
        public readonly TimingStat SimulationTime = new TimingStat("grid simulation");

        /// <summary>
        /// Stage timings, filled by the simulation itself. Attached to the simulation only while
        /// collection is on, so an uninstrumented world runs with no profiler at all.
        /// </summary>
        public readonly GridProfiler Profiler = new GridProfiler();
        public readonly TimingStat SolarTime = new TimingStat("solar occlusion");
        public readonly TimingStat SaveTime = new TimingStat("save");
        public readonly TimingStat LoadTime = new TimingStat("load");

        /// <summary>
        /// Steps between structure samples. Structure walks a handful of collection counts and
        /// changes only when blocks do, so there is nothing to learn from doing it every step.
        /// </summary>
        private const int StructureInterval = 8;

        private int stepsSinceStructure = StructureInterval;

        /// <summary>Rotates which slice of nodes the wide per-block sampling looks at.</summary>
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
        /// Called once per batch of solver steps. This is the whole hot path of the telemetry
        /// module: solver-level figures every time, structure occasionally, and a rotating slice
        /// of nodes so that full per-block coverage costs one pass spread over
        /// <see cref="Telemetry.SampleStride"/> steps rather than a pass every step.
        /// </summary>
        public void OnSteps(int steps)
        {
            if (Grid == null || Grid.Simulation == null) return;

            ThermalSolver solver = Grid.Simulation.Solver;
            int nodeCount = solver.Nodes.Count;

            SimulationSteps += steps;
            NodeUpdates += (long)nodeCount * steps;

            Substeps.Add(solver.LastSubsteps);
            if (solver.LastStepWasClamped) ClampedSteps++;
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

            SampleNodes(solver);
        }

        /// <summary>
        /// Walks one slice of the grid's nodes, feeding the per-definition statistics and the
        /// anomaly detector. The slice rotates, so every node is seen once per stride.
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
                }

                NoteTemperature(node);
                Telemetry.CheckNode(this, node);
            }
        }

        /// <summary>
        /// The structural shape of the grid. Every figure here is a collection count, except the
        /// link count, which the solver now maintains — the old code had to walk every cell for
        /// it.
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
            NeighborLinks.Add(simulation.Solver.Links.Count);
            SurfaceEntries.Add(simulation.Surfaces.CellCount);
            CoolantLoops.Add(simulation.Solver.Loops.Count);
            RecentlyRemovedSize.Add(Grid.RecentlyRemoved.Count);
            MapperQueueDepth.Add(simulation.Rooms.PendingCells);
            MapperCompletions = simulation.Rooms.CompletedPasses;

            // Only a map a pass actually produced is a measurement. The default one reads as a
            // grid with nothing anywhere, and averaged in it drags every figure here towards
            // zero for a grid that simply had not been mapped yet.
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
        /// The map only changes when a pass completes, so the audit runs once per pass rather
        /// than once per structure sample: on a grid nobody is building on, this costs one
        /// integer compare for the rest of the session.
        /// </summary>
        private void AuditRooms(ThermalSimulation simulation)
        {
            // A pass in flight means the published map predates the grid, and every block placed
            // since would audit as a disagreement it is not.
            if (simulation.Rooms.HasWorkPending) return;

            int pass = simulation.Rooms.CompletedPasses;

            // Before the first pass there is no map to audit, only the all-external default the
            // mapper hands out until it has built one. A grid that closes inside its first few
            // seconds — a paste preview, a subgrid — never gets that far, and auditing it against
            // the default reported its whole hull as unaccounted for.
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
            ConvectionCoefficient.Add(state.ConvectionCoefficient);
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
        /// Walks every live node once and records its end state. Called at shutdown, and for a
        /// grid that is destroyed mid-session, at the moment it closes.
        /// </summary>
        public void SnapshotFinalState()
        {
            if (Grid == null || Grid.Simulation == null) return;

            SampleStructure();
            SnapshotSurfaces();

            // Rebuilt rather than appended to, so a manual mid-session dump does not leave its
            // counts behind for the next report.
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

                // The strided sampler may never have seen a rare block. The final pass
                // guarantees at least one full observation of everything on the grid.
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
        /// Every face of every block, and why the model calls it exposed or not.
        ///
        /// Taken here rather than read at report time because the report is usually written as the
        /// world closes, by which point the grids are gone: the first version of this dump asked
        /// the live grid list and produced a file with nothing but a header in it. A telemetry
        /// record outlives the grid it describes, and this is part of the description.
        /// </summary>
        public readonly List<SurfaceRow> Surfaces = new List<SurfaceRow>();

        private void SnapshotSurfaces()
        {
            // A re-snapshot replaces this grid's rows, so the session budget gets them back first.
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
                    // Never truncate silently: a short file reads as a small ship.
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

        /// <summary>Rows one grid may contribute. Six per block, so about eight thousand blocks.</summary>
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
