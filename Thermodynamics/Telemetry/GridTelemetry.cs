using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    public struct RoomRow
    {
        public string Kind;

        public int Index;

        public int AnchorX;
        public int AnchorY;
        public int AnchorZ;

        public int CellCount;
        public float Volume;

        public bool Vented;

        public float Pressure;
        public float AirMass;
        public float TemperatureKelvin;

        public int LinkCount;

        public bool GameAirtight;

        public float GameOxygen;

        public string Vents;

        public bool VentPressurised;

        public float OxygenLevel;

        public bool Disagreement;

        public int LeakCount;

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

        public float Depth;

        public float ConvectionCoefficient;

        public float SolarEnergy;
        public float SolarOcclusion;

        public float WindSpeed;

        public float WindBearingDegrees;

        public float WindCeiling;


        public float WindHeightAboveGround;

        public float WindBurial;

        public float WindBandShare;

        public float WindProfileFactor;

        public float WindHeating;

        public float WindSpeedUp;

        public float WindShelter;

        public float WindChannelDegrees;

        public float GridSpeed;

        public float WeatherIntensity;

        public string Weather;

        public float WeatherAmbientOffset;

        public float GameComfort;

        public string SurfaceMaterial;

        public float GridMeanKelvin;
        public float GridPeakKelvin;
    }

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

        public long FirstTickFrame = -1;
        public long LastTickFrame = -1;

/// <summary>NoteTick operation.</summary>
        public void NoteTick(long frame)
        {
            if (FirstTickFrame < 0) FirstTickFrame = frame;
            LastTickFrame = frame;
        }

        public ThermalGrid Grid;

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat CellCount = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat BlockCount = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat NeighborLinks = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat RoomCount = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat ExternalCells = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat SolidCells = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat RoomCells = new RunningStat();

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat LeakedCells = new RunningStat();

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat OpenBlockCells = new RunningStat();

        public RoomAudit LastAudit;
        public bool HasAudit;

        private int auditedPass = -1;
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat SurfaceEntries = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat CoolantLoops = new RunningStat();


/// <summary>RunningStat operation.</summary>
        public readonly RunningStat LoopTemperature = new RunningStat();

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat LoopWattsAbsorbed = new RunningStat();

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat LoopWattsRejected = new RunningStat();

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat LoopPipes = new RunningStat();

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat LoopLinks = new RunningStat();

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat HeatPumps = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat HeatPumpsRunning = new RunningStat();

        public long HeatPumpUnconnectedSamples;

        public long HeatPumpDisabledSamples;

        public long HeatPumpStarvedSamples;

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat HeatPumpLiftWatts = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat HeatPumpPowerWatts = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat HeatPumpRejectedWatts = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat HeatPumpCoefficient = new RunningStat();

        public long HeatPumpLimitedByRating;
        public long HeatPumpLimitedByCarnotOrHeat;
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat RecentlyRemovedSize = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat MapperQueueDepth = new RunningStat();
        public int PeakCellCount;

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

        public long RoomPressureSweeps;
        public long RoomPressureRoomVisits;
        public long RoomPressureGameQueries;
        public long RoomPressureVentScans;
        public long RoomPressureVentsWalked;
        public long MapperCompletions;

        public long Saves;
        public long Loads;
        public long SaveBytes;
        public long LoadBytes;

        public long SimulationSteps;
        public long NodeUpdates;
        public long SampledNodes;

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat Substeps = new RunningStat();

        public long ClampedSteps;

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat SimulationRate = new RunningStat();
        public double SimulatedSecondsSkipped;

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat NodesPerStep = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat CriticalBlocks = new RunningStat();
        public long DamageEvents;
        public double TotalDamage;
        public float PeakTemperature = float.MinValue;
        public string PeakTemperatureBlock = "-";
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat HottestBlockTemperature = new RunningStat();
/// <summary>Histogram operation.</summary>
        public readonly Histogram FinalTemperatures = new Histogram(Histogram.TemperatureEdges());

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat AmbientTemperature = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat AirDensity = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat AtmosphereFactor = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat WindSpeed = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat ConvectionCoefficient = new RunningStat();

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat VentedWatts = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat HeatGainWatts = new RunningStat();

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat FrictionWatts = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat EffectiveSolarEnergy = new RunningStat();

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat OccludedShare = new RunningStat();
/// <summary>RunningStat operation.</summary>
        public readonly RunningStat Speed = new RunningStat();
        public long EnvironmentSamples;
        public long OccludedSamples;
        public long InAtmosphereSamples;
/// <summary>HashSet operation.</summary>
        public readonly HashSet<string> Planets = new HashSet<string>();


/// <summary>RunningStat operation.</summary>
        public readonly RunningStat RequiredSubsteps = new RunningStat();

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat FlooredNodes = new RunningStat();

        public ThermalSolver.SubstepProfile Profile;

        public string WorstSubstepBlock = "-";

        private int stepsSinceProfile = ProfileInterval;

        private const int ProfileInterval = 64;

/// <summary>TimingStat operation.</summary>
        public readonly TimingStat SimulationTime = new TimingStat("grid simulation");

/// <summary>GridProfiler operation.</summary>
        public readonly GridProfiler Profiler = new GridProfiler();
/// <summary>TimingStat operation.</summary>
        public readonly TimingStat SolarTime = new TimingStat("solar occlusion");
/// <summary>TimingStat operation.</summary>
        public readonly TimingStat SaveTime = new TimingStat("save");
/// <summary>TimingStat operation.</summary>
        public readonly TimingStat LoadTime = new TimingStat("load");

/// <summary>TimingStat operation.</summary>
        public readonly TimingStat BuildTime = new TimingStat("build");

/// <summary>TimingStat operation.</summary>
        public readonly TimingStat BlockEventTime = new TimingStat("block events");

        private const int StructureInterval = 8;

        private int stepsSinceStructure = StructureInterval;

        private int sampleOffset;

/// <summary>GridTelemetry operation.</summary>
        public GridTelemetry(ThermalGrid grid)
        {
            Grid = grid;
            EntityId = grid.Grid != null ? grid.Grid.EntityId : 0;
            OpenedAtSeconds = Telemetry.SessionSeconds;
            RefreshIdentity();
        }

/// <summary>RefreshIdentity operation.</summary>
        public void RefreshIdentity()
        {
            if (Grid == null || Grid.Grid == null) return;

            Name = Grid.Grid.DisplayName;
            if (string.IsNullOrEmpty(Name)) Name = "(unnamed)";
            IsStatic = Grid.Grid.IsStatic;
            GridSizeMeters = Grid.Grid.GridSize;
            GridSize = Grid.Grid.GridSizeEnum == MyCubeSize.Large ? "Large" : "Small";
        }


/// <summary>OnSteps operation.</summary>
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

/// <summary>SampleLoopsAndPumps operation.</summary>
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

                if (pump.LastLiftedWatts >= pump.RatedWatts - 1f) HeatPumpLimitedByRating++;
                else HeatPumpLimitedByCarnotOrHeat++;
            }

            HeatPumpsRunning.Add(running);
        }

/// <summary>CaptureProfile operation.</summary>
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

/// <summary>SampleNodes operation.</summary>
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

/// <summary>SampleStructure operation.</summary>
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

/// <summary>AuditRooms operation.</summary>
        private void AuditRooms(ThermalSimulation simulation)
        {
            if (simulation.Rooms.HasWorkPending) return;

            int pass = simulation.Rooms.CompletedPasses;

            if (pass == 0) return;

            if (pass == auditedPass) return;
            auditedPass = pass;

            LastAudit = simulation.AuditRooms();
            HasAudit = true;
            LeakedCells.Add(LastAudit.LeakedCells);
            OpenBlockCells.Add(LastAudit.OpenBlockCells);

            if (LastAudit.HasLeak) Telemetry.NoteRoomLeak(this, LastAudit);
        }

/// <summary>SampleEnvironment operation.</summary>
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

            VentedWatts.Add(grid.Simulation.VentedWatts);
            HeatGainWatts.Add(grid.Simulation.HeatGainWatts);
            FrictionWatts.Add(grid.Simulation.FrictionWatts);
        }

/// <summary>NotePlanet operation.</summary>
        public void NotePlanet(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (Planets.Count < 32) Planets.Add(name);
        }

/// <summary>NoteTemperature operation.</summary>
        public void NoteTemperature(ThermalNode node)
        {
            if (node == null || node.Temperature <= PeakTemperature) return;

            PeakTemperature = node.Temperature;
            PeakTemperatureBlock = node.Block.Name + " " + node.Block.Position;
        }

/// <summary>SnapshotFinalState operation.</summary>
        public void SnapshotFinalState()
        {
            if (Grid == null || Grid.Simulation == null) return;

            SampleStructure();
            SnapshotSurfaces();
            SnapshotRooms();

            CaptureProfile(Grid.Simulation.Solver);

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

                type.NotePeak(node.Temperature, EntityId);
                type.Sample(node);
            }
        }

/// <summary>Close operation.</summary>
        public void Close()
        {
            if (IsClosed) return;

            SnapshotFinalState();
            IsClosed = true;
            ClosedAtSeconds = Telemetry.SessionSeconds;
            Grid = null;
        }

/// <summary>List operation.</summary>
        public readonly List<SurfaceRow> Surfaces = new List<SurfaceRow>();

/// <summary>List operation.</summary>
        public readonly List<RoomRow> Rooms = new List<RoomRow>();

        public bool RoomScanTruncated;

        public bool RoomScanRan;

/// <summary>List operation.</summary>
        public readonly List<EnvironmentRow> Environment = new List<EnvironmentRow>();

/// <summary>NoteEnvironmentProfile operation.</summary>
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

        private const int MaxEnvironmentRows = 4000;

/// <summary>SnapshotRooms operation.</summary>
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

/// <summary>RoomRow operation.</summary>
                RoomRow row = new RoomRow();
                row.Kind = "mapped";
                row.Index = i;

                row.AnchorX = verdict.Anchor.X;
                row.AnchorY = verdict.Anchor.Y;
                row.AnchorZ = verdict.Anchor.Z;

                row.CellCount = verdict.CellCount;
                row.Volume = verdict.CellCount * cellVolume;
                row.Vented = verdict.Vented;

/// <summary>AirOf operation.</summary>
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

/// <summary>RoomRow operation.</summary>
                RoomRow row = new RoomRow();
                row.Kind = "lost";
                row.Index = room.Index;

                row.AnchorX = room.Anchor.X;
                row.AnchorY = room.Anchor.Y;
                row.AnchorZ = room.Anchor.Z;

                row.CellCount = room.CellCount;
                row.Volume = room.Volume;

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

/// <summary>AirOf operation.</summary>
        private RoomAirNode AirOf(int roomIndex)
        {
            IList<RoomAirNode> air = Grid.Simulation.RoomAir;
            for (int i = 0; i < air.Count; i++)
            {
                if (air[i].RoomIndex == roomIndex) return air[i];
            }
            return null;
        }

/// <summary>SnapshotSurfaces operation.</summary>
        private void SnapshotSurfaces()
        {
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
                    MyLog.Default.Info("[" + Settings.Name + "] [Telemetry] surface capture for "
/// <summary>rows operation.</summary>
                        + Name + " stopped at " + Surfaces.Count + " rows ("
                        + Telemetry.SurfaceRowsCaptured + " captured this session)");
                    return;
                }

                BlockInstance block = bound.Instance;
                SurfaceAudit.Explain(surfaces, block, rooms, faces);

                for (int face = 0; face < Face.Count; face++)
                {
/// <summary>SurfaceRow operation.</summary>
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

        private const int SurfaceRowLimit = 50000;

        public double LifetimeSeconds
        {
/// <summary>return operation.</summary>
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
