using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Utils;

namespace Thermodynamics
{
    /// <summary>
    /// Session-wide data collection for the live mod.
    ///
    /// The simulation reports through the hooks on this class, which aggregate into streaming
    /// statistics and are written to a report when the world closes. Nothing here changes
    /// simulation behaviour, and every entry point swallows its own exceptions so a telemetry fault
    /// cannot fail the mod.
    ///
    /// **Observations are gated on <see cref="Enabled"/>; faults are not.** Every measurement hook
    /// is a no-op when collection is off, which is the default and is what keeps a shipped world
    /// paying a bool read and nothing more. <see cref="Exception"/> is the exception to that, in
    /// both senses: a caught exception costs nothing until the mod has already failed, and a
    /// failure nobody records is a bug that cannot be fixed. It records always, and puts the first
    /// of each kind in the game log.
    ///
    /// Sized for a session running for hours: no sample buffers, no per-cell history, no unbounded
    /// dictionaries. Memory is bounded by the number of block definitions and the number of grids
    /// that have existed.
    /// </summary>
    public static class Telemetry
    {
        /// <summary>
        /// Whether collection is running. A plain static field rather than a property or settings
        /// lookup, since it is read on the mod's hottest path.
        /// </summary>
        public static bool Enabled;

        /// <summary>
        /// One cell update in this many feeds the wide per-block-type statistics. Peak
        /// temperatures, update counts and damage are recorded on every update regardless.
        /// </summary>
        public static int SampleStride
        {
            get { return Gate.Stride; }
            set { Gate.Stride = value; }
        }

        /// <summary>Grid records kept in full. Beyond this, grids are counted but not detailed.</summary>
        public const int MaxGridRecords = 2048;

        /// <summary>
        /// Block faces the session holds for the surface dump, across every grid. Six per block, so
        /// roughly fifty thousand blocks.
        /// </summary>
        public const int MaxSurfaceRows = 300000;

        /// <summary>Rows currently held. A record returns its rows to this budget when it re-snapshots.</summary>
        public static int SurfaceRowsCaptured;
        public const int MaxAnomalyKinds = 64;
        public const int MaxBlockTypes = 4096;

        /// <summary>Stack frames kept with a recorded exception.</summary>
        public const int ExceptionFrames = 6;

        /// <summary>Temperature above which a reading is recorded as an anomaly. The sun is about 5772 K.</summary>
        public const float ImplausibleTemperature = 20000f;

        // ---- session identity -------------------------------------------------------------
        public static DateTime StartedUtc;
        public static string WorldName = "(unknown)";
        public static string OnlineMode = "(unknown)";
        public static bool IsServer;
        public static bool IsDedicated;
        public static bool IsMultiplayer;
        public static int MaxPlayers;
        public static string SessionPath = "";
        public static DateTime GameStartDate;
        public static string GameVersion = "(unknown)";

        /// <summary>
        /// The world's own settings, flattened from the serialised session settings, and the mods
        /// loaded beside this one. Both decide what the mod is allowed to do, so a dump that omits
        /// them cannot be read on its own.
        /// </summary>
        public static readonly List<KeyValuePair<string, string>> WorldSettingsRows = new List<KeyValuePair<string, string>>();
        public static readonly List<string> Mods = new List<string>();

        /// <summary>
        /// The climate each planet is actually being simulated with, and which of its values its
        /// definition supplied. A dump where a planet reads as vacuum in breathable air is answered
        /// by this line and by nothing else in the report.
        /// </summary>
        public static readonly Dictionary<string, string> PlanetProperties = new Dictionary<string, string>();
        public static DateTime GameEndDate;
        public static Settings SettingsSnapshot;

        public static long FramesObserved;
        public static long SimulationStepsObserved;
        public static long CellUpdatesObserved;

        private static readonly Stopwatch SessionClock = new Stopwatch();
        private static readonly SampleGate Gate = new SampleGate();
        private static bool _started;
        private static bool _finished;
        private static bool _identityCaptured;

        public static readonly List<GridTelemetry> Grids = new List<GridTelemetry>();
        public static readonly Dictionary<long, GridTelemetry> GridsById = new Dictionary<long, GridTelemetry>();
        public static long GridsSeen;
        public static long GridRecordsDropped;

        public static readonly Dictionary<MyDefinitionId, BlockTypeTelemetry> BlockTypes = new Dictionary<MyDefinitionId, BlockTypeTelemetry>(MyDefinitionId.Comparer);
        public static long BlockTypeRecordsDropped;

        /// <summary>
        /// Anomalies and faults, one record per kind. The gating rule — observations only while
        /// collecting, faults always — lives on the registry, which is free of game types and so
        /// testable outside a session.
        /// </summary>
        public static readonly AnomalyRegistry Faults = new AnomalyRegistry(MaxAnomalyKinds);

        public static Dictionary<string, AnomalyRecord> Anomalies
        {
            get { return Faults.Records; }
        }

        public static long AnomalyKindsDropped
        {
            get { return Faults.KindsDropped; }
        }

        /// <summary>
        /// Guards the three registries above.
        ///
        /// Grids and blocks are not created on one thread: the game builds pasted and projected
        /// grids on workers, so <see cref="RegisterGrid"/>, <see cref="GetBlockType"/> and
        /// <see cref="Anomaly"/> — the last called from wherever an adapter exception was thrown —
        /// can each run concurrently with themselves. Unsynchronised, this produced
        /// <c>ArgumentException</c> from <c>GetBlockType</c> for keys a <c>TryGetValue</c> had just
        /// reported missing, and null-reference throws from inside <c>Dictionary.Insert</c>.
        ///
        /// Registration happens once per grid and once per block type, so the lock is off every hot
        /// path; the per-record counters beneath it remain lock-free.
        /// </summary>
        private static readonly object RegistryLock = new object();

        /// <summary>
        /// What the block overlay costs the client. Replaced rather than cleared on reset, since a
        /// second world starts a second overlay.
        /// </summary>
        public static OverlayTelemetry Overlay = new OverlayTelemetry();

        /// <summary>Wall clock spent inside the mod's own per-frame entry points.</summary>
        public static readonly TimingStat SessionFrameTime = new TimingStat("session frame");

        /// <summary>
        /// Cost per frame across every grid, and the worst frames of the session.
        ///
        /// Every other cost figure is per grid, while a stutter is per frame: twenty grids each
        /// costing an acceptable two milliseconds on the same frame produce a forty-millisecond
        /// frame. Grids tick on the ten-frame cadence and the engine calls them together, so this is
        /// the usual shape of the cost rather than a corner case.
        /// </summary>
        public static readonly FrameCostTracker FrameCost = new FrameCostTracker();

        public static double SessionSeconds
        {
            get { return SessionClock.Elapsed.TotalSeconds; }
        }

        // ------------------------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------------------------

        public static void Start()
        {
            if (_started) return;
            _started = true;
            _finished = false;

            Enabled = Settings.Instance == null || Settings.Instance.EnableTelemetry;
            if (Settings.Instance != null && Settings.Instance.TelemetrySampleStride > 0)
            {
                SampleStride = Settings.Instance.TelemetrySampleStride;
            }

            StartedUtc = DateTime.UtcNow;
            SessionClock.Reset();
            SessionClock.Start();

            MyLog.Default.Info("[" + Settings.Name + "] [Telemetry] collection " + (Enabled ? "started" : "disabled"));
        }

        /// <summary>
        /// Turns collection on or off during a session, without a reload.
        ///
        /// Switching on attaches records and stage profilers to grids that already exist; switching
        /// off detaches them, after which every hook in the mod costs one static bool read.
        /// </summary>
        public static void SetEnabled(bool enabled)
        {
            if (!_started) Start();
            if (Enabled == enabled) return;

            Enabled = enabled;
            _finished = false;

            IList<ThermalGrid> grids = ThermalGrid.LiveGrids;
            for (int i = 0; i < grids.Count; i++)
            {
                grids[i].RefreshTelemetry();
            }

            MyLog.Default.Info("[" + Settings.Name + "] [Telemetry] collection "
                + (enabled ? "enabled" : "disabled") + " at runtime");
        }

        /// <summary>
        /// Captures session identity for the report. Not reliably available from the component
        /// constructor, and mostly gone by the time <c>UnloadData</c> runs, so it is taken on the
        /// first frame that can see it.
        /// </summary>
        private static void CaptureIdentity()
        {
            if (_identityCaptured) return;

            try
            {
                if (MyAPIGateway.Session == null) return;

                WorldName = MyAPIGateway.Session.Name;
                OnlineMode = MyAPIGateway.Session.OnlineMode.ToString();
                MaxPlayers = MyAPIGateway.Session.MaxPlayers;
                IsServer = MyAPIGateway.Session.IsServer;
                IsDedicated = MyAPIGateway.Utilities != null && MyAPIGateway.Utilities.IsDedicated;
                IsMultiplayer = MyAPIGateway.Multiplayer != null && MyAPIGateway.Multiplayer.MultiplayerActive;
                SessionPath = MyAPIGateway.Session.CurrentPath;
                GameStartDate = MyAPIGateway.Session.GameDateTime;
                GameVersion = MyAPIGateway.Session.Version.ToString();
                SettingsSnapshot = Settings.Instance;

                CaptureWorldSettings();
                CaptureMods();

                _identityCaptured = true;
            }
            catch (Exception e)
            {
                Exception("Telemetry.CaptureIdentity", e);
                _identityCaptured = true;
            }
        }

        /// <summary>
        /// Reads the world's settings through the serialiser rather than field by field, so a
        /// setting the game gains is dumped without a change here.
        /// </summary>
        private static void CaptureWorldSettings()
        {
            WorldSettingsRows.Clear();

            MyObjectBuilder_SessionSettings settings = MyAPIGateway.Session.SessionSettings;
            if (settings == null || MyAPIGateway.Utilities == null) return;

            WorldSettingsRows.AddRange(WorldSettings.Parse(MyAPIGateway.Utilities.SerializeToXML(settings)));
        }

        /// <summary>
        /// The mod list, which decides which block and planet definitions exist at all. A world that
        /// loads a planet pack answers different climate questions from one that does not.
        /// </summary>
        private static void CaptureMods()
        {
            Mods.Clear();

            List<MyObjectBuilder_Checkpoint.ModItem> mods = MyAPIGateway.Session.Mods;
            if (mods == null) return;

            for (int i = 0; i < mods.Count; i++)
            {
                MyObjectBuilder_Checkpoint.ModItem mod = mods[i];
                string name = string.IsNullOrEmpty(mod.FriendlyName) ? mod.Name : mod.FriendlyName;

                Mods.Add(mod.PublishedFileId != 0
                    ? name + " (" + mod.PublishedFileId + ")"
                    : name + " (local)");
            }
        }

        /// <summary>Records the climate in force for one planet, the first time it is resolved.</summary>
        public static void NotePlanetProperties(string planet, PlanetThermalProperties properties, string supplied)
        {
            if (!Enabled || properties == null) return;

            string name = string.IsNullOrEmpty(planet) ? "(unnamed)" : planet;

            lock (RegistryLock)
            {
                PlanetProperties[name] =
                    "day " + properties.DayTemperature.ToString("n1") + " K"
                    + ", night " + properties.NightTemperature.ToString("n1") + " K"
                    + ", pole drop " + properties.PoleTemperatureDrop.ToString("n1") + " K"
                    + ", lapse " + properties.AmbientLapseRate.ToString("n2") + " K/km"
                    + ", lag " + properties.AmbientLagSeconds.ToString("n0") + " s"
                    + ", underground " + properties.UndergroundTemperature.ToString("n1") + " K"
                    + ", damping " + properties.UndergroundDampingDepth.ToString("n0") + " m"
                    + ", core " + properties.CoreTemperature.ToString("n0") + " K"
                    + ", deadzone " + properties.SealevelDeadzone.ToString("n0") + " m"
                    + ", solar decay " + properties.SolarDecay.ToString("n2")
                    + ", convection " + properties.ConvectionCoefficient.ToString("n1") + " W/m2K"
                    + " [from definition: " + supplied + "]";
            }
        }

        /// <summary>Called once per rendered/simulated frame from the session component.</summary>
        public static void FrameTick()
        {
            if (!Enabled) return;

            // The frame being closed is the previous one. A mod cannot control the order in which
            // the engine runs session and entity components, and a frame closed before its grids
            // have run would record nothing.
            FrameCost.EndFrame(FramesObserved, SessionSeconds);

            FramesObserved++;
            CaptureIdentity();

            try
            {
                if (MyAPIGateway.Session != null) GameEndDate = MyAPIGateway.Session.GameDateTime;
            }
            catch { }
        }

        /// <summary>
        /// Writes the report. Safe to call more than once; only the first call produces output,
        /// unless <paramref name="force"/> is set, which the manual dump command uses.
        /// </summary>
        public static void Finish(string reason, bool force = false)
        {
            if (!_started || !Enabled) return;
            if (_finished && !force) return;
            if (!force) _finished = true;

            try
            {
                SessionClock.Stop();

                // The final-state histograms are rebuilt from scratch, so a manual mid-session dump
                // does not carry its counts into the next report.
                foreach (BlockTypeTelemetry type in BlockTypes.Values)
                {
                    type.FinalTemperatures.Clear();
                }

                // A grid that is still alive has not yet had its final state read.
                for (int i = 0; i < Grids.Count; i++)
                {
                    GridTelemetry g = Grids[i];
                    if (!g.IsClosed) g.SnapshotFinalState();
                }

                TelemetryReport.Write(reason);
            }
            catch (Exception e)
            {
                MyLog.Default.Error("[" + Settings.Name + "] [Telemetry] report failed: " + e);
            }
            finally
            {
                if (!force) Enabled = false;
                if (force) SessionClock.Start();
            }
        }

        /// <summary>Clears everything, so a second world in the same process starts clean.</summary>
        public static void Reset()
        {
            Enabled = false;
            _started = false;
            _finished = false;
            _identityCaptured = false;
            SampleStride = 4;
            Gate.Reset();

            FramesObserved = 0;
            SimulationStepsObserved = 0;
            CellUpdatesObserved = 0;
            GridsSeen = 0;
            GridRecordsDropped = 0;
            SurfaceRowsCaptured = 0;
            BlockTypeRecordsDropped = 0;
            WorldSettingsRows.Clear();
            Mods.Clear();
            PlanetProperties.Clear();
            GameVersion = "(unknown)";
            Overlay = new OverlayTelemetry();

            lock (RegistryLock)
            {
                Grids.Clear();
                GridsById.Clear();
                BlockTypes.Clear();
                Faults.Clear();
            }

            SessionClock.Reset();

            // Clearing the registry alone is insufficient: every live grid still holds the record
            // it was handed and keeps writing to it. Its cost, steps and node updates would then be
            // recorded but absent from every aggregate, and its opened-at would be read from a
            // stopwatch just reset to zero, producing a grid lifetime longer than its session. The
            // references are cleared along with the list.
            IList<ThermalGrid> live = ThermalGrid.LiveGrids;
            for (int i = 0; i < live.Count; i++)
            {
                live[i].RefreshTelemetry();
            }
        }

        // ------------------------------------------------------------------------------------
        // Registration
        // ------------------------------------------------------------------------------------

        public static GridTelemetry RegisterGrid(ThermalGrid grid)
        {
            // A grid can be created before the session component's Init has run, so registration
            // also starts collection if nothing else has.
            if (!_started) Start();
            if (!Enabled || grid == null) return null;

            try
            {
                GridTelemetry record = new GridTelemetry(grid);

                lock (RegistryLock)
                {
                    GridsSeen++;

                    if (Grids.Count >= MaxGridRecords)
                    {
                        GridRecordsDropped++;
                        return null;
                    }

                    Grids.Add(record);
                    GridsById[record.EntityId] = record;
                }

                return record;
            }
            catch (Exception e)
            {
                Exception("Telemetry.RegisterGrid", e);
                return null;
            }
        }

        public static BlockTypeTelemetry GetBlockType(MyDefinitionId id)
        {
            if (!Enabled) return null;

            try
            {
                lock (RegistryLock)
                {
                    BlockTypeTelemetry type;
                    if (BlockTypes.TryGetValue(id, out type)) return type;

                    if (BlockTypes.Count >= MaxBlockTypes)
                    {
                        BlockTypeRecordsDropped++;
                        return null;
                    }

                    type = new BlockTypeTelemetry(id);
                    BlockTypes.Add(id, type);
                    return type;
                }
            }
            catch (Exception e)
            {
                Exception("Telemetry.GetBlockType", e);
                return null;
            }
        }

        // ------------------------------------------------------------------------------------
        // Simulation hooks
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// One call per grid per batch of solver steps.
        ///
        /// The solver steps a whole grid at once, so grid-level figures are read from it directly
        /// and per-block detail comes from a rotating slice of nodes rather than a per-block
        /// callback.
        /// </summary>
        public static void OnGridStepped(ThermalGrid grid, int steps)
        {
            if (!Enabled || grid == null || grid.Stats == null) return;

            try
            {
                SimulationStepsObserved += steps;
                grid.Stats.OnSteps(steps);
            }
            catch (Exception e)
            {
                Exception("Telemetry.OnGridStepped", e);
            }
        }

        /// <summary>
        /// Classifies one node's temperature. Called from the sampling walk, so the cost is the
        /// same rotating fraction of the grid the rest of the sampling pays.
        /// </summary>
        public static void CheckNode(GridTelemetry grid, ThermalNode node)
        {
            CellUpdatesObserved++;

            float previous = node.Temperature - node.LastDeltaTemperature;
            TelemetryAnomalyKind kind = TelemetryAnomalies.Classify(
                node.Temperature, previous, ImplausibleTemperature);

            if (kind == TelemetryAnomalyKind.None) return;

            Anomaly(TelemetryAnomalies.Name(kind, ImplausibleTemperature),
                Describe(grid, node) + " T=" + node.Temperature.ToString("n2")
                + " from " + previous.ToString("n2"));
        }

        /// <summary>
        /// Records a room map that disagrees with the grid under it: the flood fill classified a cell
        /// holding a block as open space, so anything that block was meant to enclose is mapped as
        /// outdoors. Names the first offending block, which identifies the definition to correct.
        /// </summary>
        public static void NoteRoomLeak(GridTelemetry grid, RoomAudit audit)
        {
            string example = (grid == null ? "grid" : grid.Name) +
                ": " + audit.LeakedCells + " of " + audit.BlockCells + " fully sealed block cells read as open space" +
                ", rooms=" + audit.RoomCount;

            if (audit.Examples != null && audit.Examples.Count > 0)
            {
                example += " | " + audit.Examples[0];
            }

            Anomaly("room map treats structure as open space", example);
        }

        public static void OnCriticalDamage(ThermalBlock block, float damage)
        {
            if (!Enabled || block == null) return;

            if (block.Stats != null) block.Stats.OnCriticalDamage(damage);

            GridTelemetry grid = block.Grid != null ? block.Grid.Stats : null;
            if (grid != null)
            {
                grid.DamageEvents++;
                grid.TotalDamage += damage;
            }
        }

        // ------------------------------------------------------------------------------------
        // Anomalies and exceptions
        // ------------------------------------------------------------------------------------

        public static void Anomaly(string kind, string example)
        {
            Record(kind, example, false);
        }

        /// <summary>
        /// Records a caught exception, <b>whether or not collection is running</b>.
        ///
        /// Every <c>catch</c> in the simulation adapter routes here, and for a long time this went
        /// through <see cref="Anomaly"/> and so through the <see cref="Enabled"/> gate. Telemetry
        /// is off by default, so in an ordinary world all twenty-two of those handlers swallowed
        /// their exception, wrote nothing anywhere, and left a grid running in whatever state the
        /// throw abandoned it in. The guard around <c>ThermalGrid.Tick</c> says an exception named
        /// here is worth more than a crash dump; it named it nowhere.
        ///
        /// A fault is not data collection. Collection is a running cost paid on healthy frames and
        /// is rightly opt-in; a fault costs nothing until something has already gone wrong, and by
        /// then it is the only evidence there will be. So faults are always recorded, and the first
        /// of each kind goes to the game log, which is the one file a player can be asked for.
        /// </summary>
        public static void Exception(string where, Exception e)
        {
            Record("exception in " + where, Describe(e), true);
        }

        /// <summary>
        /// Records a grid whose published figures have gone bad — NaN, infinite, or a temperature
        /// past anything the model reaches. Filed as a fault, and so recorded and logged whether or
        /// not collection is running: it is a defect in the simulation rather than a reading from
        /// it, and it is the failure that costs a player their ship.
        /// </summary>
        public static void GridFault(ThermalGrid grid, string kind)
        {
            string example;

            try
            {
                example = (grid == null || grid.Entity == null ? "(unknown grid)" : grid.Entity.DisplayName)
                    + " vented=" + (grid == null ? 0f : grid.Simulation.VentedWatts)
                    + "W made=" + (grid == null ? 0f : grid.Simulation.HeatGainWatts)
                    + "W hottest=" + (grid == null || grid.HottestNode == null
                        ? "(none)"
                        : grid.HottestNode.Block.Name + " " + grid.HottestNode.Temperature + "K");
            }
            catch
            {
                example = "(undescribable grid)";
            }

            Record(kind, example, true);
        }

        private static void Record(string kind, string example, bool fault)
        {
            if (!Enabled && !fault) return;

            try
            {
                bool log;
                lock (RegistryLock)
                {
                    log = Faults.Record(kind, example, fault, Enabled, SessionSeconds);
                }

                if (log) LogLine(kind + "\n        " + example);
            }
            catch { }
        }

        /// <summary>
        /// One block naming every fault of the session, written as the world closes whether or not
        /// collection is running.
        /// </summary>
        public static void LogFaultSummary()
        {
            try
            {
                string summary;
                lock (RegistryLock)
                {
                    summary = Faults.FaultSummary(Enabled);
                }

                if (summary != null) LogLine(summary);
            }
            catch { }
        }

        private static void LogLine(string text)
        {
            try
            {
                MyLog.Default.Error("[" + Settings.Name + "] " + text);
            }
            catch { }
        }

        /// <summary>
        /// Message plus the top frames of the stack. The message alone does not identify the call
        /// that threw, and throws often originate inside game code the mod reaches indirectly.
        /// Bounded: only the first and last example of each kind are kept, and both are reported.
        /// </summary>
        private static string Describe(Exception e)
        {
            if (e == null) return "(null)";

            string message = e.GetType().Name + ": " + e.Message;

            try
            {
                string trace = e.StackTrace;
                if (string.IsNullOrEmpty(trace)) return message;

                string[] frames = trace.Split('\n');
                int take = frames.Length < ExceptionFrames ? frames.Length : ExceptionFrames;

                for (int i = 0; i < take; i++)
                {
                    message += "\n        " + frames[i].Trim();
                }
            }
            catch { }

            return message;
        }

        private static string Describe(GridTelemetry grid, ThermalNode node)
        {
            try
            {
                if (node == null) return "(null node)";

                string name = grid != null && grid.Name != null ? grid.Name : "(no grid)";
                return name + " / " + node.Block.Name + " " + node.Block.Position;
            }
            catch
            {
                return "(undescribable)";
            }
        }
    }
}
