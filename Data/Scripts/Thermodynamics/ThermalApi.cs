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
    /// The surface other mods bind to: a dictionary of delegates passed by mod message, since Space
    /// Engineers mods cannot reference one another's assemblies. Nothing throws into a caller, and
    /// nothing a consumer does reaches the simulation. **This table is a contract, and api.md is where
    /// it is written down** — `EveryModApiEntryIsDocumented` holds the two together.
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

        /// <summary>
        /// Wraps an entry so that nothing it does reaches the caller as an exception: a failure
        /// comes back as the return type's default — `false`, `0`, `NaN`, an empty tuple — and is
        /// recorded here instead.
        ///
        /// <para>
        /// **`W4` said this was discipline, and discipline had already missed one.** `GetSetting`
        /// read `Settings.Instance.GetValue(name)` with no guard while `SetSetting`, four lines
        /// below it in the same table, returned `false` when the instance was not there yet — so a
        /// consumer that asked for a setting before this mod had loaded its own got a
        /// `NullReferenceException` with this mod's name on it, which is the exact thing the rule
        /// exists to prevent. Auditing eighteen bodies is how that happened; wrapping the table is
        /// how it stops.
        /// </para>
        ///
        /// <para>
        /// The cost is a try/catch on a call nothing makes per frame, and it does not hide
        /// anything: the same `Telemetry.Exception` that `RaiseThreshold` uses records what
        /// happened and where. One overload per arity the table uses, because a delegate cannot be
        /// wrapped generically without knowing its shape and this project is C# 6 (`C1`).
        /// </para>
        /// </summary>
        private static Func<TResult> Guard<TResult>(Func<TResult> call, string name)
        {
            return () =>
            {
                try { return call(); }
                catch (Exception e) { Telemetry.Exception("ThermalApi." + name, e); return default(TResult); }
            };
        }

        private static Func<T1, TResult> Guard<T1, TResult>(Func<T1, TResult> call, string name)
        {
            return a =>
            {
                try { return call(a); }
                catch (Exception e) { Telemetry.Exception("ThermalApi." + name, e); return default(TResult); }
            };
        }

        private static Func<T1, T2, TResult> Guard<T1, T2, TResult>(Func<T1, T2, TResult> call, string name)
        {
            return (a, b) =>
            {
                try { return call(a, b); }
                catch (Exception e) { Telemetry.Exception("ThermalApi." + name, e); return default(TResult); }
            };
        }

        private static Func<T1, T2, T3, TResult> Guard<T1, T2, T3, TResult>(
            Func<T1, T2, T3, TResult> call, string name)
        {
            return (a, b, c) =>
            {
                try { return call(a, b, c); }
                catch (Exception e) { Telemetry.Exception("ThermalApi." + name, e); return default(TResult); }
            };
        }

        private static void Build()
        {
            methods = new Dictionary<string, Delegate>();

            methods["ApiVersion"] = Guard(new Func<int>(() => Version), "ApiVersion");

            // ---- reading ------------------------------------------------------------------
            methods["GetBlockTemperature"] = Guard(new Func<IMySlimBlock, float>(GetBlockTemperature), "GetBlockTemperature");
            methods["GetBlockThermals"] = Guard(new Func<IMySlimBlock, MyTuple<float, float, float, int>>(GetBlockThermals), "GetBlockThermals");
            methods["GetGridSummary"] = Guard(new Func<IMyCubeGrid, MyTuple<float, float, int, int>>(GetGridSummary), "GetGridSummary");
            methods["GetRoom"] = Guard(new Func<IMyCubeGrid, Vector3I, MyTuple<bool, float, float, float>>(GetRoom), "GetRoom");
            methods["GetGridHeatBalance"] = Guard(new Func<IMyCubeGrid, MyTuple<float, float>>(GetGridHeatBalance), "GetGridHeatBalance");
            methods["GetGridFrictionWatts"] = Guard(new Func<IMyCubeGrid, float>(GetGridFrictionWatts), "GetGridFrictionWatts");

            // ---- writing ------------------------------------------------------------------
            methods["SetBlockTemperature"] = Guard(new Func<IMySlimBlock, float, bool>(SetBlockTemperature), "SetBlockTemperature");
            methods["AddBlockHeat"] = Guard(new Func<IMySlimBlock, float, bool>(AddBlockHeat), "AddBlockHeat");
            methods["SetRoomPressure"] = Guard(new Func<IMyCubeGrid, Vector3I, float, bool>(SetRoomPressure), "SetRoomPressure");

            // ---- heat sources -------------------------------------------------------------
            methods["AddHeatSource"] = Guard(new Func<IMyEntity, float, float, int>(ThermalHeatSources.Add), "AddHeatSource");
            methods["AddHeatSourceAt"] = Guard(new Func<Vector3D, float, float, int>(ThermalHeatSources.Add), "AddHeatSourceAt");
            methods["UpdateHeatSource"] = Guard(new Func<int, float, bool>(ThermalHeatSources.Update), "UpdateHeatSource");
            methods["RemoveHeatSource"] = Guard(new Func<int, bool>(ThermalHeatSources.Remove), "RemoveHeatSource");

            // ---- thresholds ---------------------------------------------------------------
            methods["AddThreshold"] = Guard(new Func<float, int, Action<IMySlimBlock, int, float, float, bool>, int>(AddThreshold), "AddThreshold");
            methods["RemoveThreshold"] = Guard(new Func<int, bool>(RemoveThreshold), "RemoveThreshold");

            // ---- settings -----------------------------------------------------------------
            methods["GetSetting"] = Guard(new Func<string, float>(GetSetting), "GetSetting");
            methods["SetSetting"] = Guard(new Func<string, float, bool>(SetSetting), "SetSetting");
            methods["ListSettings"] = Guard(new Func<List<string>>(Settings.Names), "ListSettings");
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
        /// Watts the grid is venting to its surroundings, and watts it is making. The pair rather than
        /// either alone, since a ship in balance vents exactly what it makes. Venting reads zero while
        /// a grid is net absorbing. See telemetry.md, Grid heat balance.
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

        /// <summary>
        /// Watts the grid is taking from the air by aerodynamic friction.
        ///
        /// <para>
        /// **This is drag power in all but name.** The solver computes `FrictionScale x rho x
        /// v_rel^3 x area x windward exposure` summed over the grid's nodes, and real drag power is
        /// `1/2 C_d rho A v^3` — the same expression. The mod turns it into heat and takes nothing
        /// from the ship's motion, so a caller that wants to apply the force this implies has the
        /// magnitude here and nowhere else (backlog.md `K1`).
        /// </para>
        ///
        /// <para>
        /// It is one term of the second figure `GetGridHeatBalance` returns rather than a separate
        /// gain, so a caller adding the two would count it twice. Zero for a grid with no
        /// simulation, which is what every accessor here returns for one (`E8`).
        /// </para>
        /// </summary>
        private static float GetGridFrictionWatts(IMyCubeGrid grid)
        {
            ThermalGrid thermals = grid == null || grid.GameLogic == null
                ? null
                : grid.GameLogic.GetAs<ThermalGrid>();

            if (thermals == null || thermals.Simulation == null) return 0f;

            return thermals.Simulation.FrictionWatts;
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

        /// <summary>
        /// One setting's value, or zero where there is no such setting.
        ///
        /// <para>
        /// **`Settings.EnsureLoaded` rather than `Settings.Instance`, because a consumer cannot be
        /// asked to call in the right order.** That method exists for exactly this — its own
        /// summary says a mod cannot control load order — and it falls back to the defaults where
        /// the world storage is not readable yet, which is a better answer than zero: zero is what
        /// an unknown *name* returns, so returning it for *not loaded* would make the two
        /// indistinguishable.
        /// </para>
        ///
        /// <para>
        /// This was `name => Settings.Instance.GetValue(name)` until 2026-08-28, four lines above a
        /// `SetSetting` that guarded the same field and returned `false` — so the writer was safe
        /// to call early and the reader threw into its caller (`W4`).
        /// </para>
        /// </summary>
        private static float GetSetting(string name)
        {
            Settings settings = Settings.EnsureLoaded();
            return settings == null ? 0f : settings.GetValue(name);
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
