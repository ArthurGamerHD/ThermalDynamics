using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Utils;

namespace Thermodynamics
{
    /// <summary>
    /// One occurrence class of something the simulation should not have produced.
    /// </summary>
    public class AnomalyRecord
    {
        public string Kind;
        public long Count;
        public double FirstSeconds;
        public double LastSeconds;
        public string FirstExample;
        public string LastExample;
    }

    /// <summary>
    /// Session-wide data collection for the live mod.
    ///
    /// Everything the simulation does is funnelled through the hooks on this class, aggregated
    /// into streaming statistics, and written to a report when the world closes. Nothing here
    /// changes simulation behaviour; every entry point is a no-op when <see cref="Enabled"/> is
    /// false, and every entry point swallows its own exceptions, because a telemetry fault must
    /// never take the mod down with it.
    ///
    /// The design constraint is a session that runs for hours: no sample buffers, no per-cell
    /// history, no unbounded dictionaries. Memory is bounded by the number of block definitions
    /// and the number of grids that have existed.
    /// </summary>
    public static class Telemetry
    {
        /// <summary>
        /// Read on the hottest path in the mod, so it is a plain static field rather than a
        /// property or a Settings lookup.
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
        public const int MaxAnomalyKinds = 64;
        public const int MaxBlockTypes = 4096;

        /// <summary>A temperature above this is recorded as an anomaly; the sun is about 5772 K.</summary>
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

        public static readonly Dictionary<string, AnomalyRecord> Anomalies = new Dictionary<string, AnomalyRecord>();
        public static long AnomalyKindsDropped;

        /// <summary>Wall clock spent inside the mod's own per-frame entry points.</summary>
        public static readonly TimingStat SessionFrameTime = new TimingStat("session frame");

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
        /// Turns collection on or off during a session, so a test run does not need a reload and
        /// so an experiment can be ended before it costs anything further.
        ///
        /// Switching on attaches records and stage profilers to grids that already exist;
        /// switching off detaches them, and every hook in the mod goes back to a single static
        /// bool read.
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
        /// Session identity is not reliably available from the component constructor, so it is
        /// captured on the first frame that can see it and kept for the report — by the time
        /// UnloadData runs, most of it is already gone.
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
                SettingsSnapshot = Settings.Instance;

                _identityCaptured = true;
            }
            catch (Exception e)
            {
                Exception("Telemetry.CaptureIdentity", e);
                _identityCaptured = true;
            }
        }

        /// <summary>Called once per rendered/simulated frame from the session component.</summary>
        public static void FrameTick()
        {
            if (!Enabled) return;

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

                // The final-state histograms are rebuilt from scratch, so that a manual dump
                // taken mid-session does not leave its counts behind for the next one.
                foreach (BlockTypeTelemetry type in BlockTypes.Values)
                {
                    type.FinalTemperatures.Clear();
                }

                // A grid that is still alive has never had its final state read.
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

        /// <summary>Clears everything. Used when a session ends so a second world starts clean.</summary>
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
            BlockTypeRecordsDropped = 0;
            AnomalyKindsDropped = 0;

            Grids.Clear();
            GridsById.Clear();
            BlockTypes.Clear();
            Anomalies.Clear();
            SessionClock.Reset();
        }

        // ------------------------------------------------------------------------------------
        // Registration
        // ------------------------------------------------------------------------------------

        public static GridTelemetry RegisterGrid(ThermalGrid grid)
        {
            // A grid can be created before the session component's Init has run, so registration
            // is also what starts collection if nothing else has.
            if (!_started) Start();
            if (!Enabled || grid == null) return null;

            try
            {
                GridsSeen++;

                if (Grids.Count >= MaxGridRecords)
                {
                    GridRecordsDropped++;
                    return null;
                }

                GridTelemetry record = new GridTelemetry(grid);
                Grids.Add(record);
                GridsById[record.EntityId] = record;
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
        /// The solver steps a whole grid at once, so this replaces the old per-cell hook: the
        /// grid-level figures are read from the solver directly, and per-block detail comes from
        /// a rotating slice of nodes rather than from a callback on every block.
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
            if (!Enabled) return;

            try
            {
                AnomalyRecord record;
                if (!Anomalies.TryGetValue(kind, out record))
                {
                    if (Anomalies.Count >= MaxAnomalyKinds)
                    {
                        AnomalyKindsDropped++;
                        return;
                    }

                    record = new AnomalyRecord
                    {
                        Kind = kind,
                        FirstSeconds = SessionSeconds,
                        FirstExample = example
                    };
                    Anomalies.Add(kind, record);
                }

                record.Count++;
                record.LastSeconds = SessionSeconds;
                record.LastExample = example;
            }
            catch { }
        }

        public static void Exception(string where, Exception e)
        {
            Anomaly("exception in " + where, e == null ? "(null)" : e.Message);
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
