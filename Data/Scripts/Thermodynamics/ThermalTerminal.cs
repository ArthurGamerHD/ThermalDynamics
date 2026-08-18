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
    /// Every functional block reports its own temperature and what is happening to it, and the
    /// grid it belongs to, in the terminal's detail info panel. This is the one readout that needs
    /// no third party HUD library and no key held down: it is where a player already goes to ask
    /// what a block is doing.
    ///
    /// The detail panel rather than a control, because this is a paragraph and the terminal's
    /// controls are not. A text box is a one-line editable field: it took the fifteen lines it was
    /// given, showed one and a half of them, and clipped the rest — which is what "thermals in the
    /// terminal are broken" looked like.
    /// </summary>
    public static class ThermalTerminal
    {
        private static bool registered;
        private static readonly StringBuilder Text = new StringBuilder();

        /// <summary>Blocks whose detail panel this has already hooked.</summary>
        private static readonly HashSet<long> Hooked = new HashSet<long>();

        /// <summary>The block whose panel is on screen.</summary>
        private static IMyTerminalBlock shown;

        /// <summary>True while this class is the one asking, rather than the game.</summary>
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
        /// Hooks a block's detail panel the first time its terminal is opened.
        ///
        /// Lazily, rather than hooking every block on every grid when it is built: a station has
        /// thousands of blocks and a player looks at one at a time.
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
        /// Keeps the open panel live.
        ///
        /// The game asks for detail info when the panel is drawn and not again, so a panel left
        /// open would otherwise show the temperature the block had when it was opened. Only the
        /// block on screen is refreshed, and only while the control panel is the screen: it is a
        /// string every ten frames for one block.
        /// </summary>
        public static void Update()
        {
            if (shown == null) return;

            if (shown.Closed || MyAPIGateway.Gui.GetCurrentScreen != MyTerminalPageEnum.ControlPanel)
            {
                shown = null;
                return;
            }

            // Marked, because the refresh calls straight back into the appender, and a handler that
            // could not tell the two apart would treat this mod's own question as the game showing
            // a panel — and then keep answering it forever after the terminal closed.
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
        /// What a heat pump is actually achieving. Nothing at all for any other block.
        ///
        /// The coefficient is the line worth reading: it is the exchange rate between electricity
        /// and cooling, it moves with the gap the pump is working across, and it is the reason a
        /// pump that was keeping up yesterday cannot keep up with a hotter reactor today.
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

        /// <summary>The air of a room this block bounds, or null when it bounds none.</summary>
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
