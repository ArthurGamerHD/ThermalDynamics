using System;
using System.Collections.Generic;
using System.Text;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using RichHudFramework.UI.Rendering;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// The readout that goes with the debug overlay: what the grid in front of you looks like in
    /// the view that is currently up.
    ///
    /// The overlay answers "where", by colouring geometry; this answers "how much", which colour
    /// cannot. Every view has its own set of figures, and each one is chosen to explain the picture
    /// on screen — the solar view reports whether the sun is occluded, because a grid drawn
    /// uniformly cold at noon is either shadowed or switched off, and the colours alone do not say
    /// which.
    ///
    /// Aggregates are swept from the node list, so they cost a pass over the grid. That pass runs a
    /// few times a second rather than per frame, which is far more often than a person can read and
    /// far less often than the frame rate.
    /// </summary>
    public static class ThermalDebugPanel
    {
        /// <summary>Draw calls between sweeps. 15 is roughly a quarter second.</summary>
        private const int RefreshInterval = 15;

        private static LabelBox panel;
        private static int sinceRefresh;
        private static ThermalDebugView.Mode lastMode;
        private static long lastGrid;
        private static bool warned;
        private static readonly StringBuilder Text = new StringBuilder();

        /// <summary>Reused by the room view so a big ship does not allocate a list per sweep.</summary>
        private static readonly List<int> RoomOrder = new List<int>();

        public static void Build()
        {
            if (panel != null) return;

            panel = new LabelBox(HudMain.HighDpiRoot)
            {
                // Top left, clear of the crosshair readout on the left edge of the screen and of
                // the cockpit summary on the right.
                ParentAlignment = ParentAlignments.Top | ParentAlignments.Left
                    | ParentAlignments.InnerV | ParentAlignments.InnerH,
                Offset = new Vector2(20f, -120f),
                BuilderMode = TextBuilderModes.Lined,
                AutoResize = true,
                TextPadding = new Vector2(20f, 16f),
                Color = new Color(20, 24, 28, 190),
                Format = new GlyphFormat(new Color(220, 235, 242), TextAlignment.Left, 0.9f),
                Visible = false,
            };
        }

        public static void Reset()
        {
            panel = null;
        }

        /// <summary>
        /// Called every draw. The panel is only up while the overlay is, and only while it has a
        /// grid to describe: a readout that stays on screen showing the last ship you looked at is
        /// worse than no readout.
        /// </summary>
        public static void Update()
        {
            if (panel == null)
            {
                WarnOnce();
                return;
            }

            ThermalGrid thermals = ThermalDebugView.Focus;
            bool wanted = ThermalDebugView.Current != ThermalDebugView.Mode.Off
                && thermals != null
                && thermals.Simulation != null
                && thermals.Grid != null
                && !thermals.Grid.Closed;

            panel.Visible = wanted;
            if (!wanted) return;

            // Cycling a view or looking at another ship redraws now rather than at the next
            // sweep: a readout that lags a keystroke by a quarter second reads as the wrong answer.
            bool changed = ThermalDebugView.Current != lastMode
                || thermals.Grid.EntityId != lastGrid
                || Text.Length == 0;

            lastMode = ThermalDebugView.Current;
            lastGrid = thermals.Grid.EntityId;

            sinceRefresh++;
            if (!changed && sinceRefresh < RefreshInterval) return;
            sinceRefresh = 0;

            Compose(thermals);
            panel.Text = new RichText(Text.ToString());
        }

        /// <summary>
        /// Says why there is no panel, once, the first time a view is switched on without it.
        ///
        /// Registration with Rich HUD Master fails silently by design — the framework simply never
        /// calls back — and a debug tool that is quietly absent is worse than one that is missing
        /// loudly.
        /// </summary>
        private static void WarnOnce()
        {
            if (warned) return;
            if (ThermalDebugView.Current == ThermalDebugView.Mode.Off) return;
            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated) return;

            warned = true;
            MyAPIGateway.Utilities.ShowNotification(
                "Thermodynamics: the overlay readout needs the Rich HUD Master mod", 5000, "Red");
        }

        private static void Compose(ThermalGrid thermals)
        {
            Text.Clear();

            ThermalDebugView.Mode mode = ThermalDebugView.Current;

            Text.Append("THERMODYNAMICS — ").Append(ThermalDebugView.Describe(mode).ToUpper()).Append('\n');
            Text.Append(thermals.Grid.DisplayName).Append("   ")
                .Append(thermals.BlockCount).Append(" blocks\n");

            EnvironmentState state = thermals.LastState;
            Text.Append("ambient ").Append(Tools.KelvinToCelsiusString(state.AmbientTemperature))
                .Append("   steps ").Append(thermals.StepsRun).Append('\n');
            Text.Append('\n');

            switch (mode)
            {
                case ThermalDebugView.Mode.Temperature: Temperature(thermals); break;
                case ThermalDebugView.Mode.SolarWatts: Solar(thermals, ref state); break;
                case ThermalDebugView.Mode.ExposedFaces: Exposed(thermals); break;
                case ThermalDebugView.Mode.FrictionWatts: Friction(thermals, ref state); break;
                case ThermalDebugView.Mode.Rooms: Rooms(thermals); break;
            }
        }

        private static void Temperature(ThermalGrid thermals)
        {
            float min = float.MaxValue, max = float.MinValue, total = 0f;
            float fastest = 0f;
            int count = 0;

            foreach (ThermalBlock bound in thermals.Blocks)
            {
                ThermalNode node = bound.Node;
                if (node == null) continue;

                float temperature = node.Temperature;
                if (temperature < min) min = temperature;
                if (temperature > max) max = temperature;
                total += temperature;
                count++;

                float rate = Math.Abs(node.LastDeltaTemperature);
                if (rate > fastest) fastest = rate;
            }

            if (count == 0) return;

            Text.Append("coldest  ").Append(Tools.KelvinToCelsiusString(min)).Append('\n');
            Text.Append("mean     ").Append(Tools.KelvinToCelsiusString(total / count)).Append('\n');
            Text.Append("hottest  ").Append(Tools.KelvinToCelsiusString(max)).Append('\n');

            ThermalNode hottest = thermals.HottestNode;
            if (hottest != null)
            {
                Text.Append("         ").Append(hottest.Block.Name).Append('\n');
            }

            // Per second, so the figure means the same whatever the step rate is.
            Text.Append("peak dT  ")
                .Append((fastest * Settings.Instance.StepsPerSecond).ToString("n3")).Append(" K/s\n");
            Text.Append("critical ").Append(thermals.CriticalBlocks).Append('\n');
            Text.Append("loops    ").Append(thermals.Simulation.Solver.Loops.Count).Append('\n');
        }

        private static void Solar(ThermalGrid thermals, ref EnvironmentState state)
        {
            float total = 0f, peak = 0f;
            int lit = 0;
            string hottest = null;

            foreach (ThermalBlock bound in thermals.Blocks)
            {
                ThermalNode node = bound.Node;
                if (node == null) continue;

                float watts = node.LastSolarWatts;
                total += watts;

                if (watts > 0f) lit++;
                if (watts > peak)
                {
                    peak = watts;
                    hottest = node.Block.Name;
                }
            }

            Text.Append("total    ").Append(Watts(total)).Append('\n');
            Text.Append("peak     ").Append(Watts(peak)).Append('\n');
            if (hottest != null) Text.Append("         ").Append(hottest).Append('\n');
            Text.Append("lit      ").Append(lit).Append(" blocks\n");
            Text.Append('\n');

            // Why the picture looks the way it does. A dark grid at noon is shadowed, switched off
            // or facing away, and only these three lines tell them apart.
            Text.Append("sunlight ").Append(state.SolarEnergy.ToString("n0")).Append(" W/m2")
                .Append(state.IsSolarOccluded ? "  (occluded)" : "").Append('\n');
            Text.Append("mechanism ")
                .Append(Settings.Instance.EnableSolarHeat ? "on" : "OFF").Append('\n');
            Text.Append("occlusion every ")
                .Append(Settings.Instance.SolarOcclusionInterval).Append(" steps\n");
            Text.Append("self-shadow ")
                .Append(Settings.Instance.SolarSelfShadowing ? "on" : "off");

            if (Settings.Instance.SolarSelfShadowing)
            {
                SunShadowMap shadow = thermals.Simulation.Solver.SunShadow;
                Text.Append("  (").Append(shadow.ColumnCount).Append(" columns)");
            }

            Text.Append('\n');
        }

        private static void Exposed(ThermalGrid thermals)
        {
            int faces = 0, buried = 0, blocks = 0;
            float area = 0f;

            foreach (ThermalBlock bound in thermals.Blocks)
            {
                ThermalNode node = bound.Node;
                if (node == null) continue;

                blocks++;
                faces += node.TotalExposedFaces;
                area += node.ExposedArea;
                if (node.TotalExposedFaces == 0) buried++;
            }

            Text.Append("faces    ").Append(faces).Append('\n');
            Text.Append("area     ").Append(area.ToString("n1")).Append(" m2\n");
            Text.Append("buried   ").Append(buried).Append(" of ").Append(blocks).Append('\n');
            Text.Append('\n');

            RoomMap map = thermals.Simulation.Rooms.Map;
            Text.Append("rooms    ").Append(map.RoomCount)
                .Append(" (").Append(map.AirtightRoomCount).Append(" sealed)\n");
            Text.Append("mapper   ")
                .Append(thermals.Simulation.Rooms.PendingCells).Append(" cells queued\n");
        }

        private static void Friction(ThermalGrid thermals, ref EnvironmentState state)
        {
            float total = 0f, peak = 0f;
            string hottest = null;

            foreach (ThermalBlock bound in thermals.Blocks)
            {
                ThermalNode node = bound.Node;
                if (node == null) continue;

                float watts = Math.Abs(node.LastFrictionWatts);
                total += watts;

                if (watts > peak)
                {
                    peak = watts;
                    hottest = node.Block.Name;
                }
            }

            Text.Append("total    ").Append(Watts(total)).Append('\n');
            Text.Append("peak     ").Append(Watts(peak)).Append('\n');
            if (hottest != null) Text.Append("         ").Append(hottest).Append('\n');
            Text.Append('\n');

            Text.Append("speed    ").Append(state.WindSpeed.ToString("n1")).Append(" m/s\n");
            Text.Append("threshold ")
                .Append(Settings.Instance.FrictionAtSpeedsAbove.ToString("n0")).Append(" m/s")
                .Append(state.FrictionActive ? "  (active)" : "  (below)").Append('\n');
            Text.Append("air      ").Append(state.AirDensity.ToString("n3")).Append(" kg/m3\n");
            Text.Append("mechanism ")
                .Append(Settings.Instance.EnableFriction ? "on" : "OFF").Append('\n');
        }

        private static void Rooms(ThermalGrid thermals)
        {
            RoomMap map = thermals.Simulation.Rooms.Map;

            Text.Append("rooms    ").Append(map.RoomCount)
                .Append(" (").Append(map.AirtightRoomCount).Append(" sealed)\n");
            Text.Append("cells    ").Append(map.RoomCellCount).Append(" room, ")
                .Append(map.ExternalCellCount).Append(" external\n");
            Text.Append("portals  ").Append(map.Portals.Count).Append('\n');
            Text.Append("mapper   ")
                .Append(thermals.Simulation.Rooms.PendingCells).Append(" cells queued\n");
            Text.Append('\n');

            IList<RoomAirNode> air = thermals.Simulation.RoomAir;
            if (air.Count == 0)
            {
                Text.Append(Settings.Instance.EnableRoomAir ? "no room air yet\n" : "room air OFF\n");
                return;
            }

            // Biggest first: on a ship with twenty compartments the ones worth reading are the ones
            // holding most of the air, and the list has to stop somewhere.
            RoomOrder.Clear();
            for (int i = 0; i < air.Count; i++) RoomOrder.Add(i);
            RoomOrder.Sort((a, b) => air[b].CellCount.CompareTo(air[a].CellCount));

            int shown = Math.Min(RoomOrder.Count, 8);
            Text.Append("room  cells   air      seal\n");

            for (int i = 0; i < shown; i++)
            {
                RoomAirNode room = air[RoomOrder[i]];

                Text.Append(room.RoomIndex.ToString().PadRight(6))
                    .Append(room.CellCount.ToString().PadRight(8))
                    .Append(Tools.KelvinToCelsiusString(room.Temperature).PadRight(9))
                    .Append(map.IsVented(room.RoomIndex) ? "vented" : "sealed")
                    .Append('\n');
            }

            if (RoomOrder.Count > shown)
            {
                Text.Append("+ ").Append(RoomOrder.Count - shown).Append(" more\n");
            }
        }

        /// <summary>Watts at a readable magnitude. A grid's total runs to megawatts.</summary>
        private static string Watts(float watts)
        {
            float magnitude = Math.Abs(watts);

            if (magnitude >= 1000000f) return (watts / 1000000f).ToString("n2") + " MW";
            if (magnitude >= 1000f) return (watts / 1000f).ToString("n2") + " kW";
            return watts.ToString("n0") + " W";
        }
    }
}
