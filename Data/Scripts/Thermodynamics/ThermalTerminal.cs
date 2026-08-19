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

        /// <summary>Label column width. Everything lines up under it, which is most of what makes it readable.</summary>
        private const int LabelWidth = 9;

        /// <summary>Compact temperature: whole degrees. Hundredths are noise in a panel this size.</summary>
        private static string T(float kelvin)
        {
            return ThermalConstants.KelvinToCelsius(kelvin).ToString("n0") + "\u00b0C";
        }

        private static void Row(string label)
        {
            Text.Append(label);
            for (int i = label.Length; i < LabelWidth; i++) Text.Append(' ');
        }

        /// <summary>
        /// The block's thermal state, for the terminal's detail pane.
        ///
        /// Written to be dense and to say only what applies to the block in front of you. The pane is
        /// a few lines tall and shares them with whatever else the block reports, so a line that reads
        /// "Waste heat: 0.0 kW" on a block that generates none is a line spent telling you nothing.
        /// Every section below is conditional on being relevant to this block.
        /// </summary>
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

            // Temperature and where it is heading, on one line: neither means much without the other.
            float perSecond = node.LastDeltaTemperature * Settings.Instance.StepsPerSecond;
            Row("Temp");
            Text.Append(T(node.Temperature)).Append("  ")
                .Append(perSecond >= 0f ? "+" : "").Append(perSecond.ToString("n2")).Append(" K/s\n");

            float critical = node.Thermal.CriticalTemperature;
            if (critical > 0f)
            {
                Row("Critical");
                Text.Append(T(critical));
                if (node.Temperature > critical) Text.Append("   OVERHEATING");
                Text.Append('\n');
            }

            // Whether a block can see the sky decides most of its fate, so this stays even at zero —
            // "buried" is the single most useful thing the panel can tell you about a hot block.
            Row("Exposed");
            if (node.TotalExposedFaces <= 0) Text.Append("buried\n");
            else
            {
                Text.Append(node.TotalExposedFaces).Append(" faces  ")
                    .Append(node.ExposedArea.ToString("n1")).Append(" m2\n");
            }

            if (node.HeatGenerationWatts > 0f)
            {
                Row("Waste");
                Text.Append((node.HeatGenerationWatts / 1000f).ToString("n1")).Append(" kW\n");
            }

            RoomAirNode air = RoomOf(bound);
            if (air != null && air.HasAir)
            {
                Row("Room");
                Text.Append(T(air.Temperature)).Append("  ")
                    .Append((air.Pressure * 100f).ToString("n0")).Append("% air\n");
            }

            AppendHeatPump(bound);
            AppendCoolant(bound);

            // Two lines for the whole ship, because the block in front of you is the subject.
            Text.Append('\n');
            Row("Grid");
            Text.Append(T(grid.LastState.AmbientTemperature)).Append(" ambient");
            if (grid.HottestNode != null)
            {
                Text.Append("  peak ").Append(T(grid.HottestNode.Temperature));
            }
            if (grid.CriticalBlocks > 0)
            {
                Text.Append("  ").Append(grid.CriticalBlocks).Append(" CRITICAL");
            }
            Text.Append('\n');

            int loops = grid.Simulation.Solver.Loops.Count;
            int rooms = grid.Simulation.RoomAir.Count;
            if (loops > 0 || rooms > 0)
            {
                Row("");
                if (loops > 0) Text.Append(loops).Append(loops == 1 ? " loop" : " loops");
                if (loops > 0 && rooms > 0) Text.Append("  ");
                if (rooms > 0) Text.Append(rooms).Append(rooms == 1 ? " room" : " rooms");
            }

            return Text;
        }

        /// <summary>
        /// A heat pump's line. Nothing for any other block.
        ///
        /// The coefficient is the figure that matters — heat lifted per watt drawn — and which of the
        /// three limits is binding tells the player what to change: at the rating a narrower gap gains
        /// nothing, short of it, it gains a great deal.
        /// </summary>
        private static void AppendHeatPump(ThermalBlock bound)
        {
            HeatPumpDevice pump = bound.Grid.Simulation.GetHeatPump(bound.Instance);
            if (pump == null) return;

            Text.Append('\n');
            Row("Pump");

            if (!pump.IsConnected)
            {
                Text.Append("idle - needs a block on both ends\n");
                return;
            }
            if (!pump.Enabled)
            {
                Text.Append("off\n");
                return;
            }

            // A different line from "off", because the fix is the ship's power budget rather than
            // this block's switch.
            if (pump.PowerAvailable <= 0f)
            {
                Text.Append("on, but the grid supplies it nothing\n");
                return;
            }

            Text.Append((pump.LastLiftedWatts / 1000f).ToString("n1")).Append(" kW for ")
                .Append((pump.LastPowerWatts / 1000f).ToString("n1")).Append(" kW   cop ")
                .Append(pump.LastCoefficient.ToString("n2")).Append('\n');

            Row("");
            if (pump.LastLiftedWatts >= pump.RatedWatts - 1f) Text.Append("at its rating");
            else if (pump.LastWasLimited) Text.Append("limited by the gap");
            else Text.Append("running");

            if (pump.PowerSetting < 1f)
            {
                Text.Append("   throttled to ").Append((pump.PowerSetting * 100f).ToString("n0")).Append('%');
            }
            Text.Append('\n');
        }

        /// <summary>
        /// What this coolant block's loop is doing, or why it has none. Nothing for a block with no
        /// plumbing.
        ///
        /// The coolant temperature is given as a range rather than a figure, because with the fluid
        /// carried round in parcels a ring is not one temperature: the spread between the coolant
        /// arriving somewhere hot and the coolant leaving somewhere cold is what says whether the loop
        /// is circulating. A wide spread on a running pump means the flow cannot keep up; a wide
        /// spread with no flow means nothing is circulating at all.
        /// </summary>
        private static void AppendCoolant(ThermalBlock bound)
        {
            BlockInstance instance = bound.Instance;
            if (instance == null || instance.Model.Coolant == null) return;

            ThermalSimulation simulation = bound.Grid.Simulation;
            CoolantLoop loop = simulation.FindLoopContaining(instance);

            Text.Append('\n');

            if (loop == null)
            {
                Row("Coolant");
                Text.Append("no loop\n");
                Row("");
                Text.Append(CoolantLoopDiagnostics.Describe(simulation.DiagnoseBlock(instance)))
                    .Append('\n');
                return;
            }

            Row("Coolant");
            Text.Append(T(loop.Temperature)).Append("  (")
                .Append(ThermalConstants.KelvinToCelsius(loop.ColdestSegment).ToString("n0"))
                .Append(" - ")
                .Append(ThermalConstants.KelvinToCelsius(loop.HottestSegment).ToString("n0"))
                .Append(")\n");

            // Signed: a ring driven the other way is a working ring, so the direction is named rather
            // than shown as a minus.
            //
            // Just the rate. A stopped ring has several causes — pumps switched off, pumps unpowered,
            // pumps fighting each other — and naming them costs a line each to say what a player can
            // read off the number in front of them and the pumps they built.
            float flow = loop.FlowSegmentsPerSecond;
            Row("Flow");
            if (flow == 0f)
            {
                Text.Append("0/s\n");
            }
            else
            {
                Text.Append((flow < 0f ? -flow : flow).ToString("n1")).Append("/s ")
                    .Append(flow < 0f ? "reverse" : "forward").Append('\n');
            }

            Row("Transfer");
            Text.Append((loop.LastWattsAbsorbed / 1000f).ToString("n1")).Append(" kW in  ")
                .Append((loop.LastWattsRejected / 1000f).ToString("n1")).Append(" kW out\n");
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
