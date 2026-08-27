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
    /// Thermal readouts in the terminal's detail info panel — the only readout needing no HUD library
    /// and no held key. The detail panel rather than a control, because a terminal text box is a
    /// one-line field that clips a paragraph. See blocks.md, The terminal readout.
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

            AppendThrottle(block, controls);
            block.RefreshCustomInfo();
        }

        /// <summary>
        /// Adds the throttle slider to a coolant pump or a heat pump. Built once and carrying its own
        /// visibility test, because a terminal control is global to a block *interface*.
        /// </summary>
        private static void AppendThrottle(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (!(block is IMyUpgradeModule)) return;

            EnsureControls();

            if (IsCoolantPump(block)) controls.Add(pumpSpeed);
            else if (IsHeatPump(block)) controls.Add(heatPumpPower);
        }

        private static IMyTerminalControlSlider pumpSpeed;
        private static IMyTerminalControlSlider heatPumpPower;

        private static bool IsCoolantPump(IMyTerminalBlock block)
        {
            return PumpControl(block) != null;
        }

        private static bool IsHeatPump(IMyTerminalBlock block)
        {
            return HeatPumpControl(block) != null;
        }

        private static ThermalCoolantPumpBlock PumpControl(IMyTerminalBlock block)
        {
            return block == null || block.GameLogic == null
                ? null
                : block.GameLogic.GetAs<ThermalCoolantPumpBlock>();
        }

        private static ThermalHeatPumpBlock HeatPumpControl(IMyTerminalBlock block)
        {
            return block == null || block.GameLogic == null
                ? null
                : block.GameLogic.GetAs<ThermalHeatPumpBlock>();
        }

        private static void EnsureControls()
        {
            if (pumpSpeed != null) return;

            pumpSpeed = MyAPIGateway.TerminalControls
                .CreateControl<IMyTerminalControlSlider, IMyUpgradeModule>("Gauge_PumpSpeed");
            pumpSpeed.Title = MyStringId.GetOrCompute("Pump speed");
            pumpSpeed.Tooltip = MyStringId.GetOrCompute(
                "How fast this pump drives its ring, as a share of its full speed. Flow rises with"
                + " the square root of combined pumping, and power with the square of flow, so"
                + " half speed is a quarter of the power and about seven tenths of the flow.");
            pumpSpeed.SetLimits(0f, 1f);
            pumpSpeed.Visible = IsCoolantPump;
            pumpSpeed.Enabled = IsCoolantPump;
            pumpSpeed.Getter = b =>
            {
                ThermalCoolantPumpBlock pump = PumpControl(b);
                return pump == null ? 1f : pump.Speed;
            };
            pumpSpeed.Setter = (b, value) =>
            {
                ThermalCoolantPumpBlock pump = PumpControl(b);
                if (pump != null) pump.SetSpeed(value);
            };
            pumpSpeed.Writer = (b, sb) =>
            {
                ThermalCoolantPumpBlock pump = PumpControl(b);
                sb.Append(((pump == null ? 1f : pump.Speed) * 100f).ToString("n0")).Append('%');
            };

            heatPumpPower = MyAPIGateway.TerminalControls
                .CreateControl<IMyTerminalControlSlider, IMyUpgradeModule>("Gauge_HeatPumpPower");
            heatPumpPower.Title = MyStringId.GetOrCompute("Power limit");
            heatPumpPower.Tooltip = MyStringId.GetOrCompute(
                "How much of its rated power this heat pump may draw. Lowering it caps how much"
                + " heat the pump can move, which is the point on a grid whose supply is the thing"
                + " running out.");
            heatPumpPower.SetLimits(0f, 1f);
            heatPumpPower.Visible = IsHeatPump;
            heatPumpPower.Enabled = IsHeatPump;
            heatPumpPower.Getter = b =>
            {
                ThermalHeatPumpBlock pump = HeatPumpControl(b);
                return pump == null ? 1f : pump.PowerSetting;
            };
            heatPumpPower.Setter = (b, value) =>
            {
                ThermalHeatPumpBlock pump = HeatPumpControl(b);
                if (pump != null) pump.SetPowerSetting(value);
            };
            heatPumpPower.Writer = (b, sb) =>
            {
                ThermalHeatPumpBlock pump = HeatPumpControl(b);
                sb.Append(((pump == null ? 1f : pump.PowerSetting) * 100f).ToString("n0")).Append('%');
            };
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

            // Nothing to refresh into, and the refresh is the per-frame cost of the panel.
            if (Settings.Instance != null && !Settings.Instance.HeatTerminalPanel) return;

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

            // **Off means off** (`P8`): a world that has turned the panel off gets no text and no
            // `Describe` call, so the readout costs what it says it costs. The handler stays
            // attached, because the setting is client side and changes mid-session.
            if (Settings.Instance != null && !Settings.Instance.HeatTerminalPanel) return;

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
        /// The block's thermal state, for the terminal's detail pane. Every section is conditional: the
        /// pane is a few lines tall and shares them, so a line reading "Waste heat: 0.0 kW" on a block
        /// that makes none is a line spent saying nothing.
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

            // Two words at most, and only for the three states where nothing is happening at all —
            // they want three different fixes and no symbol tells them apart.
            if (!pump.IsConnected)
            {
                Text.Append("no block on one face\n");
                return;
            }
            if (!pump.Enabled)
            {
                Text.Append("off\n");
                return;
            }
            if (pump.PowerAvailable <= 0f)
            {
                Text.Append("unpowered\n");
                return;
            }

            Text.Append((pump.LastLiftedWatts / 1000f).ToString("n1")).Append(" kW for ")
                .Append((pump.LastPowerWatts / 1000f).ToString("n1")).Append(" kW   x")
                .Append(pump.LastCoefficient.ToString("n2")).Append('\n');

            // The actionable figure: how much gap the pump has left before it stops reaching its
            // rating, or how much it is over. A player can read a fix straight off it — move either
            // side by that much — where "at its rating" or "limited by the gap" only named a state.
            Row("Optimal");
            float margin = pump.LastOptimalMarginKelvin;
            Text.Append(margin >= 0f ? "+" : "").Append(margin.ToString("n0")).Append("°C");

            if (pump.PowerSetting < 1f)
            {
                Text.Append("   throttled ").Append((pump.PowerSetting * 100f).ToString("n0")).Append('%');
            }
            Text.Append('\n');
        }

        /// <summary>
        /// What this coolant block's loop is doing, or why it has none. The coolant temperature is a
        /// range rather than a figure, because a ring carried in parcels is not one temperature and
        /// the spread is what says whether it is circulating.
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

            // Signed, and the sign is the direction: a ring driven the other way is a working ring.
            //
            // Just the rate. A stopped ring has several causes — pumps switched off, pumps unpowered,
            // pumps fighting each other — and naming them costs a line each to say what a player can
            // read off the number in front of them and the pumps they built.
            float flow = loop.FlowMetresPerSecond;
            Row("Flow");
            if (flow == 0f) Text.Append("0m/s\n");
            else Text.Append(flow > 0f ? "+" : "").Append(flow.ToString("n1")).Append("m/s\n");

            Row("Transfer");
            Text.Append((loop.LastWattsAbsorbed / 1000f).ToString("n1")).Append(" kW in  ")
                .Append((loop.LastWattsRejected / 1000f).ToString("n1")).Append(" kW out\n");

            // **A ring that is not full is a ring that is not cooling properly**, and until this
            // line nothing said so: the level, the refill and the power it is drawing to refill
            // were all invisible to the player whose ship had just vented. A full ring says
            // nothing, because a line that is always there is a line nobody reads.
            if (loop.FillFraction >= 1f) return;

            Row("Coolant level");
            Text.Append((loop.FillFraction * 100f).ToString("n0")).Append("%  ")
                .Append(loop.HeldKilograms.ToString("n0")).Append(" of ")
                .Append(loop.CapacityKilograms.ToString("n0")).Append(" kg\n");

            Row("Refill");
            float refill = loop.RefillDemandWatts;
            if (refill <= 0f)
            {
                // The two reasons a ring that is not full is not refilling, and they are the
                // player's to fix. Naming them costs a line and saves a ship.
                Text.Append(loop.HasPump ? "pump off\n" : "no pump\n");
                return;
            }

            Text.Append((refill / 1000f).ToString("n1")).Append(" kW\n");
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
