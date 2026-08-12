using System.Collections.Generic;
using VRage.Game;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// Everything observed about one grid, for the life of that grid.
    ///
    /// A record outlives its grid: when the grid closes, <see cref="Close"/> takes a final
    /// snapshot and drops the reference, but the record stays in the registry so a ship that was
    /// destroyed halfway through a session still appears in the report.
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
        public readonly RunningStat SurfaceEntries = new RunningStat();
        public readonly RunningStat CoolantLoops = new RunningStat();
        public readonly RunningStat RecentlyRemovedSize = new RunningStat();
        public readonly RunningStat ExternalQueueDepth = new RunningStat();
        public readonly RunningStat GridQueueDepth = new RunningStat();
        public int PeakCellCount;

        // ---- lifecycle events -----------------------------------------------------------
        public long BlocksAdded;
        public long BlocksRemoved;
        public long BlocksIgnored;
        public long ForeignBlockEvents;
        public long Splits;
        public long Merges;
        public long DoorsTracked;
        public long DoorStateChanges;
        public long SurfaceRecalcs;
        public long CrawlRestarts;
        public long MapperPasses;
        public long MapperCompletions;
        public long SurfaceUpdateSweeps;
        public long CoolantCrawls;
        public long CoolantLoopsCreated;
        public long CoolantLoopsRemoved;

        // ---- persistence ----------------------------------------------------------------
        public long Saves;
        public long Loads;
        public long SaveBytes;
        public long LoadBytes;

        // ---- simulation -----------------------------------------------------------------
        public long SimulationFrames;
        public long CellUpdates;
        public long SurfaceUpdates;
        public readonly RunningStat SimulationQuota = new RunningStat();
        public readonly RunningStat CellsPerFrame = new RunningStat();
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
        public readonly RunningStat AirDensityCurve = new RunningStat();
        public readonly RunningStat WindSpeed = new RunningStat();
        public readonly RunningStat ConvectionCoefficient = new RunningStat();
        public readonly RunningStat EffectiveSolarEnergy = new RunningStat();
        public readonly RunningStat Speed = new RunningStat();
        public long EnvironmentSamples;
        public long OccludedSamples;
        public long InAtmosphereSamples;
        public readonly HashSet<string> Planets = new HashSet<string>();

        // ---- cost -----------------------------------------------------------------------
        public readonly TimingStat SimulationTime = new TimingStat("grid simulation");
        public readonly TimingStat MapperTime = new TimingStat("room mapper");
        public readonly TimingStat SurfaceCalcTime = new TimingStat("surface states");
        public readonly TimingStat EnvironmentTime = new TimingStat("environment prep");
        public readonly TimingStat SolarTime = new TimingStat("solar occlusion");
        public readonly TimingStat CoolantTime = new TimingStat("coolant crawl");
        public readonly TimingStat SaveTime = new TimingStat("save");
        public readonly TimingStat LoadTime = new TimingStat("load");

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

        /// <summary>
        /// How many structure samples separate two conduction-link counts. Everything else here
        /// is a collection's Count; the link count is the one O(cells) walk, so it runs rarely.
        /// </summary>
        private const int LinkCountInterval = 16;
        private int _structureSamples;

        /// <summary>
        /// Called once per simulation step, not once per frame — this walks a few collections and
        /// is not worth doing 60 times a second.
        /// </summary>
        public void SampleStructure(bool countLinks = false)
        {
            if (Grid == null || Grid.Grid == null) return;

            RefreshIdentity();

            int cells = Grid.Thermals.Count;
            CellCount.Add(cells);
            if (cells > PeakCellCount) PeakCellCount = cells;

            BlockCount.Add(Grid.Grid.BlocksCount);
            SurfaceEntries.Add(Grid.Surfaces.Count);
            CoolantLoops.Add(Grid.ThermalLoops.Count);
            RecentlyRemovedSize.Add(Grid.RecentlyRemoved.Count);
            ExternalQueueDepth.Add(Grid.ExternalQueue.Count);
            GridQueueDepth.Add(Grid.GridQueue.Count);

            // Rooms[0] is the exterior set and Rooms[1] is the non-room set; the rest are sealed
            // rooms. Matches the accounting the debug HUD uses.
            RoomCount.Add(Grid.Rooms.Count - 2);
            ExternalCells.Add(Grid.Rooms[0].Count);

            if (Grid.HottestBlock != null)
            {
                HottestBlockTemperature.Add(Grid.HottestBlock.Temperature);
            }

            if (countLinks || (_structureSamples++ % LinkCountInterval) == 0)
            {
                long links = 0;
                for (int i = 0; i < Grid.Thermals.Count; i++)
                {
                    ThermalCell c = Grid.Thermals.Cells[i];
                    if (c != null) links += c.Neighbors.Count;
                }
                // Each joint is held by both ends, so the link count is half the sum of degrees.
                NeighborLinks.Add(links * 0.5f);
            }

            if (Grid.Grid.Physics != null)
            {
                Speed.Add(Grid.Grid.Physics.LinearVelocity.Length());
            }
        }

        public void SampleEnvironment()
        {
            if (Grid == null) return;

            EnvironmentSamples++;
            if (Grid.FrameSolarOccluded) OccludedSamples++;
            if (Grid.FrameAmbientDensity > 0.01f) InAtmosphereSamples++;

            AmbientTemperature.Add(Grid.FrameAmbientTemprature);
            AirDensity.Add(Grid.FrameAmbientDensity);
            AirDensityCurve.Add(Grid.FrameAirDensityCurve);
            WindSpeed.Add(Grid.FrameEffectiveWindSpeed);
            ConvectionCoefficient.Add(Grid.FrameEffectiveConvectionCoefficient);
            EffectiveSolarEnergy.Add(Grid.FrameEffectiveSolarEnergy);
        }

        public void NotePlanet(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (Planets.Count < 32) Planets.Add(name);
        }

        public void NoteTemperature(ThermalCell cell)
        {
            if (cell.Temperature <= PeakTemperature) return;

            PeakTemperature = cell.Temperature;
            PeakTemperatureBlock = cell.Block != null && cell.Block.BlockDefinition != null
                ? cell.Block.BlockDefinition.Id.SubtypeName + " " + cell.Block.Position
                : "(unknown)";
        }

        /// <summary>
        /// Walks every live cell once and records its end state. Called at shutdown, and for a
        /// grid that is destroyed mid-session, at the moment it closes.
        /// </summary>
        public void SnapshotFinalState()
        {
            if (Grid == null || Grid.Thermals == null) return;

            SampleStructure(true);

            // Rebuilt rather than appended to, so a manual mid-session dump does not leave its
            // counts behind for the next report.
            FinalTemperatures.Clear();

            for (int i = 0; i < Grid.Thermals.Count; i++)
            {
                ThermalCell c = Grid.Thermals.Cells[i];
                if (c == null || c.Block == null) continue;

                FinalTemperatures.Add(c.Temperature);

                BlockTypeTelemetry type = c.Stats;
                if (type != null)
                {
                    type.OnFinalTemperature(c.Temperature);
                    // The strided sampler may never have seen a rare block. The final pass
                    // guarantees at least one full observation of everything on the grid.
                    type.Sample(c);
                }
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
