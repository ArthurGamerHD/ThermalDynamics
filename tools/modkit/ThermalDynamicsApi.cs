using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace ThermalDynamics.Client
{

    public enum ThresholdDirection
    {
        Rising = 0,
        Falling = 1,
        Both = 2
    }

    public struct BlockThermals
    {
        public float Temperature;
        public float HeatCapacity;
        public float Critical;
        public int ExposedFaces;
    }

    public struct GridSummary
    {
        public float HottestBlock;
        public float Ambient;
        public int BlocksOverCritical;
        public int CoolantLoops;
    }

    public struct GridHeatBalance
    {
        public float VentedWatts;
        public float GeneratedWatts;
    }

    public struct RoomInfo
    {
        public bool IsRoom;
        public float Temperature;
        public float Pressure;
        public float Volume;
    }

    public struct AeroForces
    {
        public Vector3 Drag;
        public Vector3 Lift;
    }

    public struct CoolantLoopInfo
    {
        public bool Found;
        public float MeanTemperature;
        public float HottestPipe;
        public float ColdestPipe;
        public int PipeCount;
    }

    public struct ThresholdEvent
    {
        public IMySlimBlock Block;
        public int ThresholdId;
        public float Threshold;
        public float Temperature;
        public bool Rising;
    }

    public class ThermalDynamicsApi
    {
        public const long Channel = 2985582372L;

        public const int SupportedMajor = 1;

        public bool IsReady { get; private set; }

        public IReadOnlyList<string> MissingKeys { get { return missing; } }

/// <summary>List operation.</summary>
        private readonly List<string> missing = new List<string>();
        private bool registered;
        private Action onReady;

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


/// <summary>Init operation.</summary>
        public void Init(Action ready = null)
        {
            if (registered) return;
            registered = true;
            onReady = ready;

            MyAPIGateway.Utilities.RegisterMessageHandler(Channel, OnMessage);

            MyAPIGateway.Utilities.SendModMessage(Channel, null);
        }

/// <summary>Dispose operation.</summary>
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

/// <summary>OnMessage operation.</summary>
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

/// <summary>Bind operation.</summary>
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

/// <summary>Returns the .</summary>
        private T Get<T>(Dictionary<string, Delegate> table, string name) where T : class
        {
            Delegate entry;
            T bound = table.TryGetValue(name, out entry) ? entry as T : null;
            if (bound == null) missing.Add(name);
            return bound;
        }


/// <summary>Returns the blocktemperature.</summary>
        public float GetBlockTemperature(IMySlimBlock block)
        {
            return getBlockTemperature == null || block == null ? 0f : getBlockTemperature(block);
        }

/// <summary>Returns the blockthermals.</summary>
        public BlockThermals GetBlockThermals(IMySlimBlock block)
        {
/// <summary>BlockThermals operation.</summary>
            BlockThermals result = new BlockThermals();
            if (getBlockThermals == null || block == null) return result;

/// <summary>getBlockThermals operation.</summary>
            MyTuple<float, float, float, int> t = getBlockThermals(block);
            result.Temperature = t.Item1;
            result.HeatCapacity = t.Item2;
            result.Critical = t.Item3;
            result.ExposedFaces = t.Item4;
            return result;
        }

/// <summary>Returns the gridsummary.</summary>
        public GridSummary GetGridSummary(IMyCubeGrid grid)
        {
/// <summary>GridSummary operation.</summary>
            GridSummary result = new GridSummary();
            if (getGridSummary == null || grid == null) return result;

/// <summary>getGridSummary operation.</summary>
            MyTuple<float, float, int, int> t = getGridSummary(grid);
            result.HottestBlock = t.Item1;
            result.Ambient = t.Item2;
            result.BlocksOverCritical = t.Item3;
            result.CoolantLoops = t.Item4;
            return result;
        }

/// <summary>Returns the room.</summary>
        public RoomInfo GetRoom(IMyCubeGrid grid, Vector3I cell)
        {
/// <summary>RoomInfo operation.</summary>
            RoomInfo result = new RoomInfo();
            if (getRoom == null || grid == null) return result;

/// <summary>getRoom operation.</summary>
            MyTuple<bool, float, float, float> t = getRoom(grid, cell);
            result.IsRoom = t.Item1;
            result.Temperature = t.Item2;
            result.Pressure = t.Item3;
            result.Volume = t.Item4;
            return result;
        }

/// <summary>Returns the gridheatbalance.</summary>
        public GridHeatBalance GetGridHeatBalance(IMyCubeGrid grid)
        {
/// <summary>GridHeatBalance operation.</summary>
            GridHeatBalance result = new GridHeatBalance();
            if (getGridHeatBalance == null || grid == null) return result;

/// <summary>getGridHeatBalance operation.</summary>
            MyTuple<float, float> t = getGridHeatBalance(grid);
            result.VentedWatts = t.Item1;
            result.GeneratedWatts = t.Item2;
            return result;
        }

/// <summary>Returns the gridfrictionwatts.</summary>
        public float GetGridFrictionWatts(IMyCubeGrid grid)
        {
            return getGridFrictionWatts == null || grid == null ? 0f : getGridFrictionWatts(grid);
        }

/// <summary>Returns the gridaeroforces.</summary>
        public AeroForces GetGridAeroForces(IMyCubeGrid grid)
        {
/// <summary>AeroForces operation.</summary>
            AeroForces result = new AeroForces();
            if (getGridAeroForces == null || grid == null) return result;

/// <summary>getGridAeroForces operation.</summary>
            MyTuple<Vector3, Vector3> t = getGridAeroForces(grid);
            result.Drag = t.Item1;
            result.Lift = t.Item2;
            return result;
        }

/// <summary>Returns the coolantloop.</summary>
        public CoolantLoopInfo GetCoolantLoop(IMySlimBlock pipeBlock)
        {
/// <summary>CoolantLoopInfo operation.</summary>
            CoolantLoopInfo result = new CoolantLoopInfo();
            if (getCoolantLoop == null || pipeBlock == null) return result;

/// <summary>getCoolantLoop operation.</summary>
            MyTuple<bool, float, float, float, int> t = getCoolantLoop(pipeBlock);
            result.Found = t.Item1;
            result.MeanTemperature = t.Item2;
            result.HottestPipe = t.Item3;
            result.ColdestPipe = t.Item4;
            result.PipeCount = t.Item5;
            return result;
        }

/// <summary>Sets the blockdragprofile.</summary>
        public bool SetBlockDragProfile(IMySlimBlock block, float[] faceMultipliers)
        {
            return setBlockDragProfile != null && block != null
/// <summary>setBlockDragProfile operation.</summary>
                && setBlockDragProfile(block, faceMultipliers);
        }

/// <summary>ClearBlockDragProfile operation.</summary>
        public bool ClearBlockDragProfile(IMySlimBlock block)
        {
            return clearBlockDragProfile != null && block != null && clearBlockDragProfile(block);
        }


/// <summary>Sets the blocktemperature.</summary>
        public bool SetBlockTemperature(IMySlimBlock block, float kelvin)
        {
            return setBlockTemperature != null && block != null && setBlockTemperature(block, kelvin);
        }

/// <summary>Adds a blockheat.</summary>
        public bool AddBlockHeat(IMySlimBlock block, float joules)
        {
            return addBlockHeat != null && block != null && addBlockHeat(block, joules);
        }

/// <summary>Sets the roompressure.</summary>
        public bool SetRoomPressure(IMyCubeGrid grid, Vector3I cell, float pressure)
        {
            return setRoomPressure != null && grid != null && setRoomPressure(grid, cell, pressure);
        }


/// <summary>Adds a heatsource.</summary>
        public int AddHeatSource(IMyEntity entity, float watts, float range)
        {
            return addHeatSource == null || entity == null ? 0 : addHeatSource(entity, watts, range);
        }

/// <summary>Adds a heatsourceat.</summary>
        public int AddHeatSourceAt(Vector3D worldPosition, float watts, float range)
        {
            return addHeatSourceAt == null ? 0 : addHeatSourceAt(worldPosition, watts, range);
        }

/// <summary>UpdateHeatSource operation.</summary>
        public bool UpdateHeatSource(int id, float watts)
        {
            return updateHeatSource != null && updateHeatSource(id, watts);
        }

/// <summary>Removes the heatsource.</summary>
        public bool RemoveHeatSource(int id)
        {
            return removeHeatSource != null && removeHeatSource(id);
        }


/// <summary>Adds a threshold.</summary>
        public int AddThreshold(float temperature, ThresholdDirection direction, Action<ThresholdEvent> callback)
        {
            if (addThreshold == null || callback == null) return 0;

            return addThreshold(temperature, (int)direction, (block, id, threshold, reached, rising) =>
            {
/// <summary>ThresholdEvent operation.</summary>
                ThresholdEvent crossing = new ThresholdEvent();
                crossing.Block = block;
                crossing.ThresholdId = id;
                crossing.Threshold = threshold;
                crossing.Temperature = reached;
                crossing.Rising = rising;
                callback(crossing);
            });
        }

/// <summary>Removes the threshold.</summary>
        public bool RemoveThreshold(int id)
        {
            return removeThreshold != null && removeThreshold(id);
        }


/// <summary>ListSettings operation.</summary>
        public List<string> ListSettings()
        {
            return listSettings == null ? new List<string>() : (listSettings() ?? new List<string>());
        }

/// <summary>Returns the setting.</summary>
        public float GetSetting(string name)
        {
            return getSetting == null ? float.NaN : getSetting(name);
        }

/// <summary>Sets the setting.</summary>
        public bool SetSetting(string name, float value)
        {
            return setSetting != null && setSetting(name, value);
        }
    }
}
