using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using Thermodynamics.Core;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace Thermodynamics
{
    /// <summary>
    /// Thermal readouts in the terminal.
    ///
    /// Every functional block reports its own temperature, what is acting on it, and its grid's
    /// summary in the terminal's detail info panel. The only readout requiring no third-party HUD
    /// library and no held key.
    ///
    /// Written to the detail panel rather than a control because the content is a paragraph. A
    /// terminal text box is a one-line editable field: given fifteen lines it renders about one and
    /// a half and clips the rest.
    /// </summary>
    public static class ThermalTerminal
    {
        private static bool registered;
        private static readonly StringBuilder Text = new StringBuilder();

        /// <summary>Blocks whose detail panel this has already hooked.</summary>
        private static readonly HashSet<long> Hooked = new HashSet<long>();

        /// <summary>The block whose panel is on screen.</summary>
        private static IMyTerminalBlock shown;

        /// <summary>True while this class is requesting detail info, rather than the game.</summary>
        private static bool refreshing;

        public static void Register()
        {
            if (registered) return;
            registered = true;

            MyAPIGateway.TerminalControls.CustomControlGetter += OnCustomControlGetter;
        }

        public static void Unregister()
        {
            if (!registered) return;
            registered = false;

            MyAPIGateway.TerminalControls.CustomControlGetter -= OnCustomControlGetter;

            Hooked.Clear();
            shown = null;
        }

        /// <summary>
        /// Hooks a block's detail panel the first time its terminal is opened. Done lazily rather than
        /// hooking every block at build time, since a player opens one terminal at a time.
        /// </summary>
        private static void OnCustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (!HasThermals(block)) return;

            if (Hooked.Add(block.EntityId))
            {
                block.AppendingCustomInfo += AppendCustomInfo;
            }

            block.RefreshCustomInfo();
        }

        /// <summary>
        /// Keeps the open panel current.
        ///
        /// The game requests detail info when the panel is drawn and not again, so a panel left open
        /// would show the temperature the block held when it was opened. Only the block on screen is
        /// refreshed, and only while the control panel is open: one string every ten frames.
        /// </summary>
        public static void Update()
        {
            if (shown == null) return;

            if (shown.Closed || MyAPIGateway.Gui.GetCurrentScreen != MyTerminalPageEnum.ControlPanel)
            {
                shown = null;
                return;
            }

            // Flagged because the refresh calls back into the appender: without it the handler would
            // treat this mod's own request as the game opening a panel and keep refreshing after the
            // terminal closed.
            refreshing = true;
            try
            {
                shown.RefreshCustomInfo();
            }
            finally
            {
                refreshing = false;
            }
        }

        private static void AppendCustomInfo(IMyTerminalBlock block, StringBuilder info)
        {
            if (!refreshing) shown = block;

            info.Append(Describe(block));
        }

        private static bool HasThermals(IMyTerminalBlock block)
        {
            return Bound(block) != null;
        }

        private static ThermalBlock Bound(IMyTerminalBlock block)
        {
            if (block == null || block.CubeGrid == null || block.CubeGrid.GameLogic == null) return null;

            ThermalGrid thermals = block.CubeGrid.GameLogic.GetAs<ThermalGrid>();
            if (thermals == null || thermals.Simulation == null) return null;

            return thermals.Get(block.SlimBlock.Min);
        }

        private static StringBuilder Describe(IMyTerminalBlock block)
        {
            Text.Clear();

            ThermalBlock bound = Bound(block);
            if (bound == null || bound.Node == null)
            {
                Text.Append("Not simulated.");
                return Text;
            }

            ThermalNode node = bound.Node;
            ThermalGrid grid = bound.Grid;

            Text.Append("Temperature: ").Append(Tools.KelvinToCelsiusString(node.Temperature)).Append('\n');

            float perSecond = node.LastDeltaTemperature * Settings.Instance.StepsPerSecond;
            Text.Append("Change: ").Append(perSecond.ToString("n2")).Append(" K/s\n");

            float critical = node.Thermal.CriticalTemperature;
            if (critical > 0f)
            {
                Text.Append("Critical: ").Append(Tools.KelvinToCelsiusString(critical));
                if (node.Temperature > critical) Text.Append("  OVERHEATING");
                Text.Append('\n');
            }

            Text.Append("Exposed faces: ").Append(node.TotalExposedFaces)
                .Append(" (").Append(node.ExposedArea.ToString("n1")).Append(" m2)\n");

            Text.Append("Waste heat: ").Append((node.HeatGenerationWatts / 1000f).ToString("n1")).Append(" kW\n");

            AppendHeatPump(bound);

            RoomAirNode air = RoomOf(bound);
            if (air != null)
            {
                Text.Append("Room: ").Append(Tools.KelvinToCelsiusString(air.Temperature))
                    .Append(" at ").Append((air.Pressure * 100f).ToString("n0")).Append("% pressure\n");
            }

            Text.Append('\n');
            Text.Append("Grid ambient: ")
                .Append(Tools.KelvinToCelsiusString(grid.LastState.AmbientTemperature)).Append('\n');

            if (grid.HottestNode != null)
            {
                Text.Append("Grid peak: ")
                    .Append(Tools.KelvinToCelsiusString(grid.HottestNode.Temperature))
                    .Append(" (").Append(grid.HottestNode.Block.Name).Append(")\n");
            }

            Text.Append("Blocks over critical: ").Append(grid.CriticalBlocks).Append('\n');
            Text.Append("Coolant loops: ").Append(grid.Simulation.Solver.Loops.Count).Append('\n');
            Text.Append("Sealed rooms: ").Append(grid.Simulation.RoomAir.Count);

            return Text;
        }

        /// <summary>
        /// A heat pump's current performance. Emits nothing for any other block.
        ///
        /// The coefficient of performance is the key figure: it is the ratio of heat lifted to
        /// electricity drawn, and it falls as the temperature difference the pump works across grows.
        /// </summary>
        private static void AppendHeatPump(ThermalBlock bound)
        {
            HeatPumpDevice pump = bound.Grid.Simulation.GetHeatPump(bound.Instance);
            if (pump == null) return;

            Text.Append('\n');

            if (!pump.IsConnected)
            {
                Text.Append("Heat pump: not connected — needs a block on both ends\n");
                return;
            }

            if (!pump.Enabled)
            {
                Text.Append("Heat pump: off\n");
                return;
            }

            Text.Append("Heat pump: moving ")
                .Append((pump.LastLiftedWatts / 1000f).ToString("n1")).Append(" kW for ")
                .Append((pump.LastPowerWatts / 1000f).ToString("n1")).Append(" kW drawn\n");

            Text.Append("Coefficient: ").Append(pump.LastCoefficient.ToString("n2"));
            if (pump.LastWasLimited) Text.Append("  (limited by the gap)");
            Text.Append('\n');

            Text.Append("Rejecting: ").Append((pump.LastRejectedWatts / 1000f).ToString("n1"))
                .Append(" kW into the hot side\n");
        }

        /// <summary>Air node of a room this block bounds, or null when it bounds none.</summary>
        private static RoomAirNode RoomOf(ThermalBlock bound)
        {
            ThermalSimulation simulation = bound.Grid.Simulation;

            VRageMath.Vector3I[] cells = bound.Instance.Cells;
            for (int i = 0; i < cells.Length; i++)
            {
                for (int face = 0; face < Face.Count; face++)
                {
                    RoomAirNode air = simulation.GetRoomAir(cells[i] + Face.Offsets[face]);
                    if (air != null) return air;
                }
            }
            return null;
        }
    }
}
