using System;
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
    /// Every functional block gains a read-only panel giving its own temperature and what is
    /// happening to it, and the grid it belongs to. This is the one readout that needs no third
    /// party HUD library and no key held down: it is where a player already goes to ask what a
    /// block is doing.
    ///
    /// Controls are registered once for <see cref="IMyTerminalBlock"/>, and the visibility rule
    /// keeps them off blocks the simulation does not model.
    /// </summary>
    public static class ThermalTerminal
    {
        private static bool registered;
        private static readonly StringBuilder Text = new StringBuilder();

        public static void Register()
        {
            if (registered) return;
            registered = true;

            MyAPIGateway.TerminalControls.CustomControlGetter += OnCustomControlGetter;

            IMyTerminalControlLabel heading =
                MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyTerminalBlock>(
                    "Thermodynamics_Heading");
            heading.Label = MyStringId.GetOrCompute("Thermal");
            heading.Visible = HasThermals;
            MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(heading);

            IMyTerminalControlTextbox readout =
                MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlTextbox, IMyTerminalBlock>(
                    "Thermodynamics_Readout");
            readout.Visible = HasThermals;
            readout.Enabled = block => false;
            readout.Getter = Describe;
            readout.Setter = (block, value) => { };
            MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(readout);
        }

        public static void Unregister()
        {
            if (!registered) return;
            registered = false;

            MyAPIGateway.TerminalControls.CustomControlGetter -= OnCustomControlGetter;
        }

        /// <summary>
        /// The terminal caches control values, so a panel left open would show the temperature the
        /// block had when it was opened. Touching the block's custom info on every getter is what
        /// keeps the figure live.
        /// </summary>
        private static void OnCustomControlGetter(IMyTerminalBlock block, System.Collections.Generic.List<IMyTerminalControl> controls)
        {
            if (!HasThermals(block)) return;
            block.RefreshCustomInfo();
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
