using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// The surface other mods bind to.
    ///
    /// Space Engineers mods cannot reference one another's assemblies, so the contract is a
    /// dictionary of delegates passed by mod message. A consumer registers a handler for
    /// <see cref="ChannelId"/>, receives <c>Dictionary&lt;string, Delegate&gt;</c>, and casts the
    /// entries it needs. Every entry is built from whitelisted types only.
    ///
    /// Two invariants hold throughout. Nothing throws into a caller: an invalid argument returns
    /// false or zero. And nothing a consumer does reaches the simulation: callbacks are invoked
    /// inside a try, and one that throws is dropped with a log entry rather than failing a grid's
    /// update.
    ///
    /// The dictionary is broadcast at session start and re-sent on request, since neither end
    /// controls mod load order.
    /// </summary>
    public static class ThermalApi
    {
        /// <summary>Mod message channel: the mod's workshop id, which cannot collide.</summary>
        public const long ChannelId = 2985582372;

        /// <summary>
        /// Incremented when an entry changes meaning or is removed. Consumers should refuse to bind to
        /// a major version they were not written against.
        /// </summary>
        public const int Version = 1;

        private static Dictionary<string, Delegate> methods;
        private static bool registered;

        /// <summary>Threshold subscriptions, by the id handed back at registration.</summary>
        private static readonly Dictionary<int, Action<IMySlimBlock, int, float, float, bool>> ThresholdHandlers =
            new Dictionary<int, Action<IMySlimBlock, int, float, float, bool>>();

        /// <summary>
        /// Thresholds registered globally and applied to every grid, so a grid created later still
        /// carries them. A threshold is a rule rather than an instance, so the list stays small.
        /// </summary>
        private static readonly List<ThermalThreshold> GlobalThresholds = new List<ThermalThreshold>();

        private static int nextThresholdId = 1;

        public static void Register()
        {
            if (registered) return;
            registered = true;

            Build();
            MyAPIGateway.Utilities.RegisterMessageHandler(ChannelId, OnRequest);
            Publish();
        }

        public static void Unregister()
        {
            if (!registered) return;
            registered = false;

            MyAPIGateway.Utilities.UnregisterMessageHandler(ChannelId, OnRequest);
            ThresholdHandlers.Clear();
            GlobalThresholds.Clear();
            methods = null;
        }

        /// <summary>
        /// Handles a request for the table. A consumer that loaded after the broadcast requests it by
        /// sending any message on the channel; re-sending is idempotent.
        /// </summary>
        private static void OnRequest(object payload)
        {
            if (payload is Dictionary<string, Delegate>) return;   // our own broadcast coming back
            Publish();
        }

        private static void Publish()
        {
            MyAPIGateway.Utilities.SendModMessage(ChannelId, methods);
        }

        private static void Build()
        {
            methods = new Dictionary<string, Delegate>();

            methods["ApiVersion"] = new Func<int>(() => Version);

            // ---- reading ------------------------------------------------------------------
            methods["GetBlockTemperature"] = new Func<IMySlimBlock, float>(GetBlockTemperature);
            methods["GetBlockThermals"] = new Func<IMySlimBlock, MyTuple<float, float, float, int>>(GetBlockThermals);
            methods["GetGridSummary"] = new Func<IMyCubeGrid, MyTuple<float, float, int, int>>(GetGridSummary);
            methods["GetRoom"] = new Func<IMyCubeGrid, Vector3I, MyTuple<bool, float, float, float>>(GetRoom);
            methods["GetGridHeatBalance"] = new Func<IMyCubeGrid, MyTuple<float, float>>(GetGridHeatBalance);

            // ---- writing ------------------------------------------------------------------
            methods["SetBlockTemperature"] = new Func<IMySlimBlock, float, bool>(SetBlockTemperature);
            methods["AddBlockHeat"] = new Func<IMySlimBlock, float, bool>(AddBlockHeat);
            methods["SetRoomPressure"] = new Func<IMyCubeGrid, Vector3I, float, bool>(SetRoomPressure);

            // ---- heat sources -------------------------------------------------------------
            methods["AddHeatSource"] = new Func<IMyEntity, float, float, int>(ThermalHeatSources.Add);
            methods["AddHeatSourceAt"] = new Func<Vector3D, float, float, int>(ThermalHeatSources.Add);
            methods["UpdateHeatSource"] = new Func<int, float, bool>(ThermalHeatSources.Update);
            methods["RemoveHeatSource"] = new Func<int, bool>(ThermalHeatSources.Remove);

            // ---- thresholds ---------------------------------------------------------------
            methods["AddThreshold"] =
                new Func<float, int, Action<IMySlimBlock, int, float, float, bool>, int>(AddThreshold);
            methods["RemoveThreshold"] = new Func<int, bool>(RemoveThreshold);

            // ---- settings -----------------------------------------------------------------
            methods["GetSetting"] = new Func<string, float>(name => Settings.Instance.GetValue(name));
            methods["SetSetting"] = new Func<string, float, bool>(SetSetting);
            methods["ListSettings"] = new Func<List<string>>(Settings.Names);
        }

        // ---- implementations ---------------------------------------------------------------

        private static ThermalGrid GridOf(IMySlimBlock block)
        {
            if (block == null || block.CubeGrid == null) return null;
            return block.CubeGrid.GameLogic == null ? null : block.CubeGrid.GameLogic.GetAs<ThermalGrid>();
        }

        private static ThermalBlock Bound(IMySlimBlock block)
        {
            ThermalGrid grid = GridOf(block);
            return grid == null ? null : grid.Get(block.Min);
        }

        private static float GetBlockTemperature(IMySlimBlock block)
        {
            ThermalBlock bound = Bound(block);
            return bound == null || bound.Node == null ? 0f : bound.Node.Temperature;
        }

        /// <summary>Temperature K, thermal mass J/K, critical temperature K, exposed faces.</summary>
        private static MyTuple<float, float, float, int> GetBlockThermals(IMySlimBlock block)
        {
            ThermalBlock bound = Bound(block);
            if (bound == null || bound.Node == null)
            {
                return new MyTuple<float, float, float, int>(0f, 0f, 0f, 0);
            }

            ThermalNode node = bound.Node;
            return new MyTuple<float, float, float, int>(
                node.Temperature,
                node.ThermalMass,
                node.Thermal.CriticalTemperature,
                node.TotalExposedFaces);
        }

        /// <summary>Hottest block K, ambient K, blocks over critical, coolant loops.</summary>
        private static MyTuple<float, float, int, int> GetGridSummary(IMyCubeGrid grid)
        {
            ThermalGrid thermals = grid == null || grid.GameLogic == null
                ? null
                : grid.GameLogic.GetAs<ThermalGrid>();

            if (thermals == null || thermals.Simulation == null)
            {
                return new MyTuple<float, float, int, int>(0f, 0f, 0, 0);
            }

            ThermalNode hottest = thermals.HottestNode;
            return new MyTuple<float, float, int, int>(
                hottest == null ? 0f : hottest.Temperature,
                thermals.LastState.AmbientTemperature,
                thermals.CriticalBlocks,
                thermals.Simulation.Solver.Loops.Count);
        }

        /// <summary>
        /// Watts the grid is venting to its surroundings, and watts it is making.
        ///
        /// The pair rather than either alone: venting says nothing about whether a ship is coping
        /// until it is read against what the ship produces, and a ship in balance vents exactly
        /// what it makes. Venting is zero while a grid is net absorbing, which a hull in sunlight
        /// or in warm atmosphere can be.
        /// </summary>
        private static MyTuple<float, float> GetGridHeatBalance(IMyCubeGrid grid)
        {
            ThermalGrid thermals = grid == null || grid.GameLogic == null
                ? null
                : grid.GameLogic.GetAs<ThermalGrid>();

            if (thermals == null || thermals.Simulation == null)
            {
                return new MyTuple<float, float>(0f, 0f);
            }

            return new MyTuple<float, float>(
                thermals.Simulation.VentedWatts, thermals.Simulation.HeatGainWatts);
        }

        /// <summary>Is a sealed room, air temperature K, pressure 0..1, volume m^3.</summary>
        private static MyTuple<bool, float, float, float> GetRoom(IMyCubeGrid grid, Vector3I cell)
        {
            ThermalGrid thermals = grid == null || grid.GameLogic == null
                ? null
                : grid.GameLogic.GetAs<ThermalGrid>();

            if (thermals == null || thermals.Simulation == null)
            {
                return new MyTuple<bool, float, float, float>(false, 0f, 0f, 0f);
            }

            RoomAirNode air = thermals.Simulation.GetRoomAir(cell);
            if (air == null) return new MyTuple<bool, float, float, float>(false, 0f, 0f, 0f);

            return new MyTuple<bool, float, float, float>(true, air.Temperature, air.Pressure, air.Volume);
        }

        private static bool SetBlockTemperature(IMySlimBlock block, float kelvin)
        {
            ThermalBlock bound = Bound(block);
            if (bound == null || bound.Node == null) return false;
            if (kelvin < ThermalConstants.MinimumTemperature) kelvin = ThermalConstants.MinimumTemperature;

            bound.Node.Temperature = kelvin;
            return true;
        }

        /// <summary>
        /// Adds energy in joules. Expressed as energy rather than temperature so callers need not know
        /// the block's heat capacity, and so the same joules applied to two blocks produce the
        /// temperature changes their capacities imply.
        /// </summary>
        private static bool AddBlockHeat(IMySlimBlock block, float joules)
        {
            ThermalBlock bound = Bound(block);
            if (bound == null || bound.Node == null) return false;

            ThermalNode node = bound.Node;
            float updated = node.Temperature + (joules / node.ThermalMass);
            node.Temperature = updated < ThermalConstants.MinimumTemperature
                ? ThermalConstants.MinimumTemperature
                : updated;
            return true;
        }

        private static bool SetRoomPressure(IMyCubeGrid grid, Vector3I cell, float pressure)
        {
            ThermalGrid thermals = grid == null || grid.GameLogic == null
                ? null
                : grid.GameLogic.GetAs<ThermalGrid>();

            if (thermals == null || thermals.Simulation == null) return false;
            return thermals.Simulation.SetRoomPressure(cell, pressure);
        }

        private static bool SetSetting(string name, float value)
        {
            if (Settings.Instance == null) return false;
            if (!Settings.Instance.SetValue(name, value)) return false;

            Settings.Instance.Apply();
            return true;
        }

        // ---- thresholds --------------------------------------------------------------------

        /// <summary>
        /// Registers a temperature to watch on every grid. <paramref name="direction"/> is 0
        /// rising, 1 falling, 2 both. The callback receives the block, the threshold id, the
        /// threshold, the temperature reached, and whether it was rising.
        /// </summary>
        private static int AddThreshold(
            float temperature, int direction, Action<IMySlimBlock, int, float, float, bool> callback)
        {
            if (callback == null) return 0;

            ThresholdDirection resolved =
                direction == 1 ? ThresholdDirection.Falling :
                direction == 2 ? ThresholdDirection.Both :
                ThresholdDirection.Rising;

            int id = nextThresholdId++;
            ThermalThreshold threshold = new ThermalThreshold(id, temperature, resolved);

            GlobalThresholds.Add(threshold);
            ThresholdHandlers[id] = callback;

            IList<ThermalGrid> grids = ThermalGrid.LiveGrids;
            for (int i = 0; i < grids.Count; i++)
            {
                if (grids[i].Simulation != null) grids[i].Simulation.Thresholds.Add(threshold);
            }

            return id;
        }

        private static bool RemoveThreshold(int id)
        {
            if (!ThresholdHandlers.Remove(id)) return false;

            for (int i = 0; i < GlobalThresholds.Count; i++)
            {
                if (GlobalThresholds[i].Id != id) continue;
                GlobalThresholds.RemoveAt(i);
                break;
            }

            IList<ThermalGrid> grids = ThermalGrid.LiveGrids;
            for (int i = 0; i < grids.Count; i++)
            {
                if (grids[i].Simulation != null) grids[i].Simulation.Thresholds.Remove(id);
            }

            return true;
        }

        /// <summary>Applies every registered threshold to a grid that has just been created.</summary>
        public static void ApplyThresholds(ThermalSimulation simulation)
        {
            if (simulation == null) return;

            for (int i = 0; i < GlobalThresholds.Count; i++)
            {
                simulation.Thresholds.Add(GlobalThresholds[i]);
            }
        }

        /// <summary>Hands one crossing to its subscriber.</summary>
        public static void RaiseThreshold(ThresholdCrossing crossing, IMySlimBlock block)
        {
            Action<IMySlimBlock, int, float, float, bool> handler;
            if (!ThresholdHandlers.TryGetValue(crossing.ThresholdId, out handler)) return;

            try
            {
                handler(block, crossing.ThresholdId, crossing.Threshold, crossing.Temperature, crossing.Rising);
            }
            catch (Exception e)
            {
                // A subscriber that throws is dropped, so one misbehaving consumer cannot stop the
                // simulation for the rest.
                Telemetry.Exception("ThermalApi.RaiseThreshold", e);
                ThresholdHandlers.Remove(crossing.ThresholdId);
            }
        }
    }
}
