using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace ThermalDynamics.Client
{
    // ---------------------------------------------------------------------------------------------
    //  Thermal Dynamics — drop-in client
    //
    //  A single file that binds another mod to Thermal Dynamics' API and hides the message-passing.
    //  Copy it into your mod's Data/Scripts folder, change the namespace if you like, and from your
    //  own session component:
    //
    //      private readonly ThermalDynamicsApi thermal = new ThermalDynamicsApi();
    //
    //      public override void LoadData()    { thermal.Init(); }              // or Init(OnReady)
    //      protected override void UnloadData() { thermal.Dispose(); }
    //
    //      // anywhere, any time — safe before the API has bound, returns the documented default:
    //      float k = thermal.GetBlockTemperature(block);
    //
    //  Nothing here throws: a call made before the API is ready, or against a block the simulation
    //  does not know, returns the same zero / false / NaN the API itself would. Check IsReady, or
    //  pass a callback to Init, if you need to know the moment it is bound.
    //
    //  The full contract this wraps is Thermal Dynamics' api.md; the how-and-why is api-guide.md.
    //  This file's binding is held identical to that contract by ThermalDynamicsClientTests in the
    //  mod's own suite, so it does not drift from the API it wraps.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Which way a threshold is watched. Matches the API's integer direction.</summary>
    public enum ThresholdDirection
    {
        Rising = 0,
        Falling = 1,
        Both = 2
    }

    /// <summary>A block's heat, unpacked from the API tuple.</summary>
    public struct BlockThermals
    {
        /// <summary>Temperature, K. Zero when the block is not simulated.</summary>
        public float Temperature;
        /// <summary>Heat capacity, J/K. Zero when the block is not simulated — a real block never is.</summary>
        public float HeatCapacity;
        /// <summary>Critical temperature, K, or zero when the block has no limit.</summary>
        public float Critical;
        /// <summary>Faces open to the environment.</summary>
        public int ExposedFaces;
    }

    /// <summary>A grid's headline thermal state.</summary>
    public struct GridSummary
    {
        public float HottestBlock;
        public float Ambient;
        public int BlocksOverCritical;
        public int CoolantLoops;
    }

    /// <summary>What a grid vents against what it makes. Equal when the ship is in balance.</summary>
    public struct GridHeatBalance
    {
        public float VentedWatts;
        public float GeneratedWatts;
    }

    /// <summary>The air in one cell's room.</summary>
    public struct RoomInfo
    {
        /// <summary>False when the cell is not in a sealed room.</summary>
        public bool IsRoom;
        public float Temperature;
        /// <summary>0..1.</summary>
        public float Pressure;
        /// <summary>m³.</summary>
        public float Volume;
    }

    /// <summary>A grid's aerodynamic forces, world newtons.</summary>
    public struct AeroForces
    {
        /// <summary>Along the relative wind.</summary>
        public Vector3 Drag;
        /// <summary>Perpendicular to it.</summary>
        public Vector3 Lift;
    }

    /// <summary>A coolant loop's state.</summary>
    public struct CoolantLoopInfo
    {
        /// <summary>False when the block is not on a loop or is not a pipe.</summary>
        public bool Found;
        public float MeanTemperature;
        public float HottestPipe;
        public float ColdestPipe;
        public int PipeCount;
    }

    /// <summary>A threshold crossing handed to a subscriber.</summary>
    public struct ThresholdEvent
    {
        public IMySlimBlock Block;
        public int ThresholdId;
        public float Threshold;
        public float Temperature;
        public bool Rising;
    }

    /// <summary>
    /// Binds to Thermal Dynamics and exposes its API as typed methods. One instance per consuming
    /// mod; call <see cref="Init"/> once and <see cref="Dispose"/> at unload.
    /// </summary>
    public class ThermalDynamicsApi
    {
        /// <summary>The mod-message channel — Thermal Dynamics' workshop id.</summary>
        public const long Channel = 2985582372L;

        /// <summary>The API major version this client is written against.</summary>
        public const int SupportedMajor = 1;

        /// <summary>True once the API has been received, version-checked and bound.</summary>
        public bool IsReady { get; private set; }

        /// <summary>
        /// The API keys this client knows but the connected Thermal Dynamics did not provide —
        /// empty on a matching build, non-empty against an older one missing keys added since.
        /// Calls to a missing key return the documented default.
        /// </summary>
        public IReadOnlyList<string> MissingKeys { get { return missing; } }

        private readonly List<string> missing = new List<string>();
        private bool registered;
        private Action onReady;

        // ---- the bound delegates -------------------------------------------------------------
        private Func<int> apiVersion;
        private Func<IMySlimBlock, float> getBlockTemperature;
        private Func<IMySlimBlock, MyTuple<float, float, float, int>> getBlockThermals;
        private Func<IMyCubeGrid, MyTuple<float, float, int, int>> getGridSummary;
        private Func<IMyCubeGrid, Vector3I, MyTuple<bool, float, float, float>> getRoom;
        private Func<IMyCubeGrid, MyTuple<float, float>> getGridHeatBalance;
        private Func<IMyCubeGrid, float> getGridFrictionWatts;
        private Func<IMyCubeGrid, MyTuple<Vector3, Vector3>> getGridAeroForces;
        private Func<IMySlimBlock, MyTuple<bool, float, float, float, int>> getCoolantLoop;
        private Func<IMySlimBlock, float[], bool> setBlockDragProfile;
        private Func<IMySlimBlock, bool> clearBlockDragProfile;
        private Func<IMySlimBlock, float, bool> setBlockTemperature;
        private Func<IMySlimBlock, float, bool> addBlockHeat;
        private Func<IMyCubeGrid, Vector3I, float, bool> setRoomPressure;
        private Func<IMyEntity, float, float, int> addHeatSource;
        private Func<Vector3D, float, float, int> addHeatSourceAt;
        private Func<int, float, bool> updateHeatSource;
        private Func<int, bool> removeHeatSource;
        private Func<float, int, Action<IMySlimBlock, int, float, float, bool>, int> addThreshold;
        private Func<int, bool> removeThreshold;
        private Func<string, float> getSetting;
        private Func<string, float, bool> setSetting;
        private Func<List<string>> listSettings;

        // ---- lifecycle -----------------------------------------------------------------------

        /// <summary>
        /// Registers for the API and requests it. Call once, at load. <paramref name="ready"/> is
        /// invoked the moment the API is bound, or never if Thermal Dynamics is not present or is a
        /// major this client was not written for. Safe to call before Thermal Dynamics has loaded.
        /// </summary>
        public void Init(Action ready = null)
        {
            if (registered) return;
            registered = true;
            onReady = ready;

            MyAPIGateway.Utilities.RegisterMessageHandler(Channel, OnMessage);

            // Ask, in case Thermal Dynamics loaded first and its one-time broadcast is already gone.
            MyAPIGateway.Utilities.SendModMessage(Channel, null);
        }

        /// <summary>Unregisters and drops every delegate. Call at unload.</summary>
        public void Dispose()
        {
            if (!registered) return;
            registered = false;
            IsReady = false;
            onReady = null;

            MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, OnMessage);

            apiVersion = null;
            getBlockTemperature = null;
            getBlockThermals = null;
            getGridSummary = null;
            getRoom = null;
            getGridHeatBalance = null;
            getGridFrictionWatts = null;
            getGridAeroForces = null;
            getCoolantLoop = null;
            setBlockDragProfile = null;
            clearBlockDragProfile = null;
            setBlockTemperature = null;
            addBlockHeat = null;
            setRoomPressure = null;
            addHeatSource = null;
            addHeatSourceAt = null;
            updateHeatSource = null;
            removeHeatSource = null;
            addThreshold = null;
            removeThreshold = null;
            getSetting = null;
            setSetting = null;
            listSettings = null;
            missing.Clear();
        }

        private void OnMessage(object payload)
        {
            if (IsReady) return;

            var table = payload as Dictionary<string, Delegate>;
            if (table == null) return;   // our own request echoing back, or another mod's traffic

            Delegate versionDelegate;
            var version = table.TryGetValue("ApiVersion", out versionDelegate)
                ? versionDelegate as Func<int>
                : null;
            if (version == null || version() != SupportedMajor) return;

            Bind(table);
            IsReady = true;

            var callback = onReady;
            if (callback != null) callback();
        }

        private void Bind(Dictionary<string, Delegate> table)
        {
            missing.Clear();

            apiVersion = Get<Func<int>>(table, "ApiVersion");
            getBlockTemperature = Get<Func<IMySlimBlock, float>>(table, "GetBlockTemperature");
            getBlockThermals = Get<Func<IMySlimBlock, MyTuple<float, float, float, int>>>(table, "GetBlockThermals");
            getGridSummary = Get<Func<IMyCubeGrid, MyTuple<float, float, int, int>>>(table, "GetGridSummary");
            getRoom = Get<Func<IMyCubeGrid, Vector3I, MyTuple<bool, float, float, float>>>(table, "GetRoom");
            getGridHeatBalance = Get<Func<IMyCubeGrid, MyTuple<float, float>>>(table, "GetGridHeatBalance");
            getGridFrictionWatts = Get<Func<IMyCubeGrid, float>>(table, "GetGridFrictionWatts");
            getGridAeroForces = Get<Func<IMyCubeGrid, MyTuple<Vector3, Vector3>>>(table, "GetGridAeroForces");
            getCoolantLoop = Get<Func<IMySlimBlock, MyTuple<bool, float, float, float, int>>>(table, "GetCoolantLoop");
            setBlockDragProfile = Get<Func<IMySlimBlock, float[], bool>>(table, "SetBlockDragProfile");
            clearBlockDragProfile = Get<Func<IMySlimBlock, bool>>(table, "ClearBlockDragProfile");
            setBlockTemperature = Get<Func<IMySlimBlock, float, bool>>(table, "SetBlockTemperature");
            addBlockHeat = Get<Func<IMySlimBlock, float, bool>>(table, "AddBlockHeat");
            setRoomPressure = Get<Func<IMyCubeGrid, Vector3I, float, bool>>(table, "SetRoomPressure");
            addHeatSource = Get<Func<IMyEntity, float, float, int>>(table, "AddHeatSource");
            addHeatSourceAt = Get<Func<Vector3D, float, float, int>>(table, "AddHeatSourceAt");
            updateHeatSource = Get<Func<int, float, bool>>(table, "UpdateHeatSource");
            removeHeatSource = Get<Func<int, bool>>(table, "RemoveHeatSource");
            addThreshold = Get<Func<float, int, Action<IMySlimBlock, int, float, float, bool>, int>>(table, "AddThreshold");
            removeThreshold = Get<Func<int, bool>>(table, "RemoveThreshold");
            getSetting = Get<Func<string, float>>(table, "GetSetting");
            setSetting = Get<Func<string, float, bool>>(table, "SetSetting");
            listSettings = Get<Func<List<string>>>(table, "ListSettings");
        }

        /// <summary>Casts one entry, recording a name whose key or signature the build did not supply.</summary>
        private T Get<T>(Dictionary<string, Delegate> table, string name) where T : class
        {
            Delegate entry;
            T bound = table.TryGetValue(name, out entry) ? entry as T : null;
            if (bound == null) missing.Add(name);
            return bound;
        }

        // ---- reading -------------------------------------------------------------------------

        /// <summary>Temperature K, or 0 when the block is not simulated or the API is not ready.</summary>
        public float GetBlockTemperature(IMySlimBlock block)
        {
            return getBlockTemperature == null || block == null ? 0f : getBlockTemperature(block);
        }

        /// <summary>A block's temperature, heat capacity, critical temperature and exposed faces.</summary>
        public BlockThermals GetBlockThermals(IMySlimBlock block)
        {
            BlockThermals result = new BlockThermals();
            if (getBlockThermals == null || block == null) return result;

            MyTuple<float, float, float, int> t = getBlockThermals(block);
            result.Temperature = t.Item1;
            result.HeatCapacity = t.Item2;
            result.Critical = t.Item3;
            result.ExposedFaces = t.Item4;
            return result;
        }

        /// <summary>A grid's hottest block, ambient, over-critical count and coolant loop count.</summary>
        public GridSummary GetGridSummary(IMyCubeGrid grid)
        {
            GridSummary result = new GridSummary();
            if (getGridSummary == null || grid == null) return result;

            MyTuple<float, float, int, int> t = getGridSummary(grid);
            result.HottestBlock = t.Item1;
            result.Ambient = t.Item2;
            result.BlocksOverCritical = t.Item3;
            result.CoolantLoops = t.Item4;
            return result;
        }

        /// <summary>The air in the room containing a cell.</summary>
        public RoomInfo GetRoom(IMyCubeGrid grid, Vector3I cell)
        {
            RoomInfo result = new RoomInfo();
            if (getRoom == null || grid == null) return result;

            MyTuple<bool, float, float, float> t = getRoom(grid, cell);
            result.IsRoom = t.Item1;
            result.Temperature = t.Item2;
            result.Pressure = t.Item3;
            result.Volume = t.Item4;
            return result;
        }

        /// <summary>Watts a grid vents against watts it makes.</summary>
        public GridHeatBalance GetGridHeatBalance(IMyCubeGrid grid)
        {
            GridHeatBalance result = new GridHeatBalance();
            if (getGridHeatBalance == null || grid == null) return result;

            MyTuple<float, float> t = getGridHeatBalance(grid);
            result.VentedWatts = t.Item1;
            result.GeneratedWatts = t.Item2;
            return result;
        }

        /// <summary>
        /// Watts a grid takes from the air by friction. One term inside
        /// <see cref="GetGridHeatBalance"/>'s generated figure, not a separate one.
        /// </summary>
        public float GetGridFrictionWatts(IMyCubeGrid grid)
        {
            return getGridFrictionWatts == null || grid == null ? 0f : getGridFrictionWatts(grid);
        }

        /// <summary>A grid's drag and lift force vectors, world newtons.</summary>
        public AeroForces GetGridAeroForces(IMyCubeGrid grid)
        {
            AeroForces result = new AeroForces();
            if (getGridAeroForces == null || grid == null) return result;

            MyTuple<Vector3, Vector3> t = getGridAeroForces(grid);
            result.Drag = t.Item1;
            result.Lift = t.Item2;
            return result;
        }

        /// <summary>The coolant loop a pipe block belongs to.</summary>
        public CoolantLoopInfo GetCoolantLoop(IMySlimBlock pipeBlock)
        {
            CoolantLoopInfo result = new CoolantLoopInfo();
            if (getCoolantLoop == null || pipeBlock == null) return result;

            MyTuple<bool, float, float, float, int> t = getCoolantLoop(pipeBlock);
            result.Found = t.Item1;
            result.MeanTemperature = t.Item2;
            result.HottestPipe = t.Item3;
            result.ColdestPipe = t.Item4;
            result.PipeCount = t.Item5;
            return result;
        }

        /// <summary>
        /// Tells the drag model a block is more slippery on some faces than its area implies: six
        /// multipliers in face order, each 0..1. A value may only reduce; anything outside 0..1 is
        /// treated as no change. False when the block has no node or the array is not six long.
        /// </summary>
        public bool SetBlockDragProfile(IMySlimBlock block, float[] faceMultipliers)
        {
            return setBlockDragProfile != null && block != null
                && setBlockDragProfile(block, faceMultipliers);
        }

        /// <summary>Removes a block's drag profile.</summary>
        public bool ClearBlockDragProfile(IMySlimBlock block)
        {
            return clearBlockDragProfile != null && block != null && clearBlockDragProfile(block);
        }

        // ---- writing -------------------------------------------------------------------------

        /// <summary>
        /// Sets a block's temperature outright. Prefer <see cref="AddBlockHeat"/> for anything
        /// physical — it is the energy-conserving one. Apply on the server for an authoritative
        /// change. False when the block is not simulated.
        /// </summary>
        public bool SetBlockTemperature(IMySlimBlock block, float kelvin)
        {
            return setBlockTemperature != null && block != null && setBlockTemperature(block, kelvin);
        }

        /// <summary>
        /// Adds joules, converted through the block's own heat capacity. Apply on the server for an
        /// authoritative change. False when the block is not simulated.
        /// </summary>
        public bool AddBlockHeat(IMySlimBlock block, float joules)
        {
            return addBlockHeat != null && block != null && addBlockHeat(block, joules);
        }

        /// <summary>Sets how full of air a cell's room is, 0..1. False when there is no room.</summary>
        public bool SetRoomPressure(IMyCubeGrid grid, Vector3I cell, float pressure)
        {
            return setRoomPressure != null && grid != null && setRoomPressure(grid, cell, pressure);
        }

        // ---- heat sources --------------------------------------------------------------------

        /// <summary>
        /// Registers a point source following an entity: watts, range in metres. Returns an id for
        /// <see cref="UpdateHeatSource"/> and <see cref="RemoveHeatSource"/>, or 0 if not added. The
        /// source disappears with the entity, so a projectile or wreck needs no cleanup.
        /// </summary>
        public int AddHeatSource(IMyEntity entity, float watts, float range)
        {
            return addHeatSource == null || entity == null ? 0 : addHeatSource(entity, watts, range);
        }

        /// <summary>Registers a point source fixed at a world position.</summary>
        public int AddHeatSourceAt(Vector3D worldPosition, float watts, float range)
        {
            return addHeatSourceAt == null ? 0 : addHeatSourceAt(worldPosition, watts, range);
        }

        /// <summary>Changes a registered source's output. False for an unknown id.</summary>
        public bool UpdateHeatSource(int id, float watts)
        {
            return updateHeatSource != null && updateHeatSource(id, watts);
        }

        /// <summary>Removes a registered source. False for an unknown id.</summary>
        public bool RemoveHeatSource(int id)
        {
            return removeHeatSource != null && removeHeatSource(id);
        }

        // ---- thresholds ----------------------------------------------------------------------

        /// <summary>
        /// Watches a temperature on every grid, now and on grids created later. The callback is
        /// raised once per step, after the step, on the server and clients alike; a callback that
        /// throws is dropped, so keep it short and gate world changes to the server yourself.
        /// Returns an id for <see cref="RemoveThreshold"/>, or 0 if it could not be registered.
        /// </summary>
        public int AddThreshold(float temperature, ThresholdDirection direction, Action<ThresholdEvent> callback)
        {
            if (addThreshold == null || callback == null) return 0;

            return addThreshold(temperature, (int)direction, (block, id, threshold, reached, rising) =>
            {
                ThresholdEvent crossing = new ThresholdEvent();
                crossing.Block = block;
                crossing.ThresholdId = id;
                crossing.Threshold = threshold;
                crossing.Temperature = reached;
                crossing.Rising = rising;
                callback(crossing);
            });
        }

        /// <summary>Stops watching a threshold. False for an unknown id.</summary>
        public bool RemoveThreshold(int id)
        {
            return removeThreshold != null && removeThreshold(id);
        }

        // ---- settings ------------------------------------------------------------------------

        /// <summary>Every setting's name, or an empty list when the API is not ready.</summary>
        public List<string> ListSettings()
        {
            return listSettings == null ? new List<string>() : (listSettings() ?? new List<string>());
        }

        /// <summary>One setting's value, or NaN for a name that does not exist.</summary>
        public float GetSetting(string name)
        {
            return getSetting == null ? float.NaN : getSetting(name);
        }

        /// <summary>
        /// Sets a setting for the session — live on the next step, not written to the config file.
        /// Switches are 0 and 1. False for an unknown name or before the API is ready.
        /// </summary>
        public bool SetSetting(string name, float value)
        {
            return setSetting != null && setSetting(name, value);
        }
    }
}
