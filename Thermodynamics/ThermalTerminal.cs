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
    public static class ThermalTerminal
    {
        private static bool registered;
        private static readonly StringBuilder Text = new StringBuilder();

        private static readonly HashSet<long> Hooked = new HashSet<long>();

        private static IMyTerminalBlock shown;

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

        private static void AppendThrottle(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (!(block is IMyUpgradeModule)) return;

            EnsureControls();

            if (IsCoolantPump(block))
            {
                controls.Add(pumpSpeed);
            }
            else if (IsHeatPump(block))
            {
                controls.Add(heatPumpPower);
            }
            else if (IsHeatSource(block))
            {
                controls.Add(sourceLevel);
                controls.Add(sourceRange);
            }
        }

        private static IMyTerminalControlSlider pumpSpeed;
        private static IMyTerminalControlSlider heatPumpPower;
        private static IMyTerminalControlSlider sourceLevel;
        private static IMyTerminalControlSlider sourceRange;

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

        private static bool IsHeatSource(IMyTerminalBlock block)
        {
            return HeatSourceControl(block) != null;
        }

        private static ThermalHeatSourceBlock HeatSourceControl(IMyTerminalBlock block)
        {
            return block == null || block.GameLogic == null
                ? null
                : block.GameLogic.GetAs<ThermalHeatSourceBlock>();
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

            sourceLevel = MyAPIGateway.TerminalControls
                .CreateControl<IMyTerminalControlSlider, IMyUpgradeModule>("Gauge_HeatSourceLevel");
            sourceLevel.Title = MyStringId.GetOrCompute("Output");
            sourceLevel.Tooltip = MyStringId.GetOrCompute(
                "How much this source radiates, from nothing to a gigawatt over six decades of"
                + " travel. It spreads over the sphere at whatever distance it is read at, so a"
                + " megawatt is 2 W/m\u00b2 at 200 m and 78 W/m\u00b2 at 32 m, against"
                + " 1,000 W/m\u00b2 for the sun. At zero the source is removed outright rather"
                + " than left registered at nothing, so it costs no simulation time at all. This"
                + " block draws no power and takes nothing from the grid: it is a debug fixture and"
                + " its energy comes from nowhere.");
            sourceLevel.SetLimits(0f, 1f);
            sourceLevel.Visible = IsHeatSource;
            sourceLevel.Enabled = IsHeatSource;
            sourceLevel.Getter = b =>
            {
                ThermalHeatSourceBlock source = HeatSourceControl(b);
                return HeatSourceBlockSetting.PositionOfWatts(source == null
                    ? HeatSourceBlockSetting.DefaultWatts
                    : source.Setting.Watts);
            };
            sourceLevel.Setter = (b, value) =>
            {
                ThermalHeatSourceBlock source = HeatSourceControl(b);
                if (source != null) source.SetWatts(HeatSourceBlockSetting.WattsAtPosition(value));
            };
            sourceLevel.Writer = (b, sb) =>
            {
                ThermalHeatSourceBlock source = HeatSourceControl(b);
                float watts = source == null ? HeatSourceBlockSetting.DefaultWatts : source.Setting.Watts;

                if (watts <= 0f) sb.Append("off");
                else sb.Append(Units.Watts(watts, 2));
            };

            sourceRange = MyAPIGateway.TerminalControls
                .CreateControl<IMyTerminalControlSlider, IMyUpgradeModule>("Gauge_HeatSourceRange");
            sourceRange.Title = MyStringId.GetOrCompute("Range");
            sourceRange.Tooltip = MyStringId.GetOrCompute(
                "How far the source is felt. Beyond it nothing samples the source at all, which is"
                + " a cliff rather than a fade \u2014 and it is what the range is for: every grid"
                + " inside it pays one pass over its exposed blocks per step, so keep it to the"
                + " distance the effect should actually be measured at.");
            sourceRange.SetLimits(HeatSourceBlockSetting.MinRange, HeatSourceBlockSetting.MaxRange);
            sourceRange.Visible = IsHeatSource;
            sourceRange.Enabled = IsHeatSource;
            sourceRange.Getter = b =>
            {
                ThermalHeatSourceBlock source = HeatSourceControl(b);
                return source == null ? HeatSourceBlockSetting.DefaultRange : source.Setting.Range;
            };
            sourceRange.Setter = (b, value) =>
            {
                ThermalHeatSourceBlock source = HeatSourceControl(b);
                if (source != null) source.SetRange(value);
            };
            sourceRange.Writer = (b, sb) =>
            {
                ThermalHeatSourceBlock source = HeatSourceControl(b);
                sb.Append((source == null ? HeatSourceBlockSetting.DefaultRange : source.Setting.Range)
                    .ToString("n0")).Append(" m");
            };
        }

        public static void Update()
        {
            if (shown == null) return;

            if (Settings.Instance != null && !Settings.Instance.HeatTerminalPanel) return;

            if (shown.Closed || MyAPIGateway.Gui.GetCurrentScreen != MyTerminalPageEnum.ControlPanel)
            {
                shown = null;
                return;
            }

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

        private const int LabelWidth = 9;

        private static string T(float kelvin)
        {
            return ThermalConstants.KelvinToCelsius(kelvin).ToString("n0") + "\u00b0C";
        }

        private static void Row(string label)
        {
            Text.Append(label);
            for (int i = label.Length; i < LabelWidth; i++) Text.Append(' ');
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
            AppendHeatSource(block);

            return Text;
        }

        private static void AppendHeatSource(IMyTerminalBlock block)
        {
            ThermalHeatSourceBlock source = HeatSourceControl(block);
            if (source == null) return;

            HeatSourceBlockSetting setting = source.Setting;

            Text.Append('\n');
            Row("Source");

            if (!source.IsRunning)
            {
                Text.Append("off\n");
                return;
            }

            if (!setting.HasOutput)
            {
                Text.Append("output at zero, not registered\n");
                return;
            }

            if (!source.IsRadiating)
            {
                Text.Append("point sources are switched off for this world\n");
                return;
            }

            Text.Append(Units.Watts(setting.Watts, 2)).Append("  to ")
                .Append(setting.Range.ToString("n0")).Append(" m\n");

            Row("At 50m");
            Text.Append(setting.IrradianceAt(50f).ToString("n1")).Append(" W/m\u00b2\n");
        }

        private static void AppendHeatPump(ThermalBlock bound)
        {
            HeatPumpDevice pump = bound.Grid.Simulation.GetHeatPump(bound.Instance);
            if (pump == null) return;

            Text.Append('\n');
            Row("Pump");

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

            Row("Optimal");
            float margin = pump.LastOptimalMarginKelvin;
            Text.Append(margin >= 0f ? "+" : "").Append(margin.ToString("n0")).Append("°C");

            if (pump.PowerSetting < 1f)
            {
                Text.Append("   throttled ").Append((pump.PowerSetting * 100f).ToString("n0")).Append('%');
            }
            Text.Append('\n');
        }

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

            float flow = loop.FlowMetresPerSecond;
            Row("Flow");
            if (flow == 0f) Text.Append("0m/s\n");
            else Text.Append(flow > 0f ? "+" : "").Append(flow.ToString("n1")).Append("m/s\n");

            Row("Transfer");
            Text.Append((loop.LastWattsAbsorbed / 1000f).ToString("n1")).Append(" kW in  ")
                .Append((loop.LastWattsRejected / 1000f).ToString("n1")).Append(" kW out\n");

            if (loop.FillFraction >= 1f) return;

            Row("Coolant level");
            Text.Append((loop.FillFraction * 100f).ToString("n0")).Append("%  ")
                .Append(loop.HeldKilograms.ToString("n0")).Append(" of ")
                .Append(loop.CapacityKilograms.ToString("n0")).Append(" kg\n");

            Row("Refill");
            float refill = loop.RefillDemandWatts;
            if (refill <= 0f)
            {
                Text.Append(loop.HasPump ? "pump off\n" : "no pump\n");
                return;
            }

            Text.Append((refill / 1000f).ToString("n1")).Append(" kW\n");
        }

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
