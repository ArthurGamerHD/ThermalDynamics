using System;
using System.Collections.Generic;
using System.Text;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using RichHudFramework.UI.Rendering;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using static VRageRender.MyBillboard;

namespace Thermodynamics
{
    public static class ThermalHud
    {
        private const int TextInterval = 6;

        private static Label toolLabel;
        private static Label windLabel;
        private static Label airLabel;
        private static LabelBox gridPanel;
        private static int sinceText;

        private static readonly StringBuilder ToolText = new StringBuilder();
        private static readonly StringBuilder WindText = new StringBuilder();
        private static readonly StringBuilder AirText = new StringBuilder();

        private static readonly List<BlockInstance> NeighbourScratch = new List<BlockInstance>();

        public static void Build()
        {
            if (toolLabel != null) return;

            toolLabel = new Label(HudMain.HighDpiRoot)
            {
                ParentAlignment = ParentAlignments.Center,
                Offset = new Vector2(60f, 30f),
                Format = new GlyphFormat(Color.White, TextAlignment.Left, 1.05f),
                Visible = false,
            };

            windLabel = new Label(HudMain.HighDpiRoot)
            {
                ParentAlignment = ParentAlignments.Center,
                Offset = new Vector2(0f, -132f),
                Format = new GlyphFormat(new Color(200, 224, 236), TextAlignment.Center, 0.9f),
                Visible = false,
            };

            airLabel = new Label(HudMain.HighDpiRoot)
            {
                ParentAlignment = ParentAlignments.Bottom | ParentAlignments.InnerV,
                Offset = new Vector2(0f, 96f),
                Format = new GlyphFormat(new Color(206, 214, 220), TextAlignment.Center, 0.85f),
                Visible = false,
            };

            gridPanel = new LabelBox(HudMain.HighDpiRoot)
            {
                ParentAlignment = ParentAlignments.Top | ParentAlignments.Right
                    | ParentAlignments.InnerV | ParentAlignments.InnerH,
                Offset = new Vector2(-20f, -120f),
                BuilderMode = TextBuilderModes.Lined,
                AutoResize = true,
                TextPadding = new Vector2(18f, 14f),
                Color = new Color(20, 24, 28, 190),
                Format = Body,
                Visible = false,
            };
        }

        private static readonly GlyphFormat Body =
            new GlyphFormat(new Color(220, 235, 242), TextAlignment.Left, 0.95f);

        private static readonly GlyphFormat Muted =
            new GlyphFormat(new Color(140, 158, 168), TextAlignment.Left, 0.95f);

        private static readonly GlyphFormat Warning =
            new GlyphFormat(new Color(226, 92, 80), TextAlignment.Left, 0.95f);

        public static void Reset()
        {
            toolLabel = null;
            windLabel = null;
            airLabel = null;
            gridPanel = null;
        }

        public static void Draw()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;

            sinceText++;
            bool publish = sinceText >= TextInterval;
            if (publish) sinceText = 0;

            DrawToolHud();

            DrawWindNeedle();

            if (!publish) return;

            DrawGridHud();
            DrawEnvironmentReadout();
            Publish(toolLabel, ToolText);
            Publish(windLabel, WindText);
            Publish(airLabel, AirText);

            if (gridPanel != null)
            {
                gridPanel.Visible = GridPanelText != null;
                if (GridPanelText != null) gridPanel.Text = GridPanelText;
            }
        }

        private static void Publish(Label label, StringBuilder text)
        {
            if (label == null) return;

            if (text.Length == 0)
            {
                label.Visible = false;
                return;
            }

            label.Visible = true;
            label.Text = new RichText(text);
        }

        private static void DrawToolHud()
        {
            ToolText.Clear();
            if (!UsingExtinguisherTool()) return;

            Crosshair.Target target;
            if (!Crosshair.Resolve(out target)) return;

            ThermalGrid thermals = target.Thermals;
            ThermalBlock bound = target.Block;
            MatrixD matrix = target.Camera;

            DrawBillboard(thermals, bound, matrix);

            NeighbourScratch.Clear();
            thermals.Model.GetNeighbours(bound.Instance, NeighbourScratch);
            for (int i = 0; i < NeighbourScratch.Count; i++)
            {
                ThermalBlock neighbour = thermals.Get(NeighbourScratch[i].Position);
                if (neighbour != null) DrawBillboard(thermals, neighbour, matrix);
            }

            ToolText.Append(TemperatureScale.ToCelsiusString(bound.Node.Temperature));
        }

        private static void DrawGridHud()
        {
            GridPanelText = null;
            if (!ShowPerformancePanel) return;

            IList<ThermalGrid> grids = ThermalGrid.LiveGrids;
            if (grids.Count == 0) return;

            int simulated = 0;
            long blocks = 0;
            long links = 0;
            int loops = 0;
            int critical = 0;
            int floored = 0;
            float vented = 0f;
            float made = 0f;

            int granted = 0;
            float demanded = 0f;
            double visitsPerSecond = 0d;

            float peak = float.MinValue;
            float ambient = 0f;

            for (int i = 0; i < grids.Count; i++)
            {
                ThermalGrid thermals = grids[i];
                if (thermals == null || thermals.Simulation == null) continue;

                ThermalSolver solver = thermals.Simulation.Solver;
                simulated++;
                blocks += thermals.BlockCount;
                links += solver.LinkCount;
                loops += solver.Loops == null ? 0 : solver.Loops.Count;
                critical += thermals.CriticalBlocks;
                floored += solver.FlooredNodes;

                vented += solver.LastVentedWatts;
                made += solver.LastHeatGainWatts;

                if (solver.LastSubsteps > granted) granted = solver.LastSubsteps;
                if (solver.LastRequiredSubsteps > demanded) demanded = solver.LastRequiredSubsteps;

                visitsPerSecond += (double)solver.LinkCount * solver.LastSubsteps
                    * Settings.Instance.StepsPerSecond;

                ThermalNode hottest = thermals.HottestNode;
                if (hottest != null && hottest.Temperature > peak) peak = hottest.Temperature;
                ambient = thermals.LastState.AmbientTemperature;
            }

            if (simulated == 0) return;

            RichText text = new RichText();

            Pair(text, "grids", Thousands(simulated), "blocks", Thousands(blocks));
            Pair(text, "links", Thousands(links), "loops", loops.ToString());

            float starved = demanded > granted && demanded > 0f
                ? (demanded - granted) / demanded : 0f;
            Pair(text, "substeps", granted + " / " + demanded.ToString("n1"),
                "starved", (starved * 100f).ToString("n0") + "%",
                starved > 0f ? Warning : (GlyphFormat?)null);

            Pair(text, "visits/s", Thousands((long)visitsPerSecond), "floored", Thousands(floored));

            Pair(text, "vented", Watts(vented), "made", Watts(made),
                made > vented ? Warning : (GlyphFormat?)null);

            Pair(text, "ambient", TemperatureScale.ToCelsiusString(ambient),
                "peak", peak == float.MinValue ? "-" : TemperatureScale.ToCelsiusString(peak),
                peak == float.MinValue ? (GlyphFormat?)null : new GlyphFormat(
                    ColorExtensions.HSVtoColor(TemperatureScale.ToHsv(peak)),
                    TextAlignment.Left, 0.95f));

            Pair(text, "critical", critical.ToString(), "clock",
                Settings.Instance.HeatTimeScale.ToString("n0") + " / "
                + Settings.Instance.Frequency,
                critical > 0 ? Warning : (GlyphFormat?)null);

            GridPanelText = text;
        }

        private static void Row(RichText text, string label, string value, GlyphFormat? format = null)
        {
            text.Add(label.PadRight(9), Muted);
            text.Add(value + "\n", format ?? Body);
        }

        private static void Pair(RichText text, string leftLabel, string leftValue,
            string rightLabel, string rightValue, GlyphFormat? rightFormat = null)
        {
            text.Add(leftLabel.PadRight(9), Muted);
            text.Add(leftValue.PadRight(11), Body);
            text.Add(rightLabel.PadRight(9), Muted);
            text.Add(rightValue + "\n", rightFormat ?? Body);
        }

        public static bool ShowPerformancePanel;

        public static bool TogglePerformancePanel()
        {
            ShowPerformancePanel = !ShowPerformancePanel;
            return ShowPerformancePanel;
        }

        private static string Thousands(long value)
        {
            return value.ToString("n0");
        }

        private static string Watts(float watts)
        {
            return Units.Watts(watts);
        }

        private static RichText GridPanelText;

        private static bool UsingExtinguisherTool()
        {
            IMyCharacter character = MyAPIGateway.Session == null || MyAPIGateway.Session.Player == null
                ? null
                : MyAPIGateway.Session.Player.Controller.ControlledEntity as IMyCharacter;

            if (character == null || character.EquippedTool == null) return false;

            IMyAutomaticRifleGun extinguisher = character.EquippedTool as IMyAutomaticRifleGun;
            return extinguisher != null && extinguisher.DefinitionId.SubtypeId.String == "ExtinguisherGun";
        }


        private const double NeedleScreenY = -0.17d;

        private const double NeedleScreenRadius = 0.055d;

        private const double NeedleDistance = 0.06d;

        private const float MinimumReadableWind = 0.5f;

        private static readonly MyStringId NeedleMaterial = MyStringId.GetOrCompute("Square");

        private static void DrawEnvironmentReadout()
        {
            AirText.Clear();

            if (Settings.Instance == null || !Settings.Instance.ShowEnvironmentReadout) return;

            ThermalGrid thermals = PlayerGrid();
            if (thermals == null || thermals.Simulation == null) return;

            ThermalNode hottest = thermals.HottestNode;

            float peak = hottest == null ? float.NaN : hottest.Temperature;
            float critical = hottest == null ? 0f : hottest.Thermal.CriticalTemperature;

            AirText.Append(EnvironmentReadout.Line(
                thermals.LastState.AmbientTemperature, peak, critical));
        }

        private static ThermalGrid PlayerGrid()
        {
            if (MyAPIGateway.Session == null) return null;

            IMyCharacter character = MyAPIGateway.Session.Player == null
                ? null
                : MyAPIGateway.Session.Player.Character;

            IMyCubeGrid grid = null;

            IMyCockpit seat = character == null ? null : character.Parent as IMyCockpit;
            if (seat != null) grid = seat.CubeGrid;

            if (grid == null && character != null)
            {
                grid = character.Parent as IMyCubeGrid;
            }

            if (grid == null || grid.GameLogic == null) return null;
            return grid.GameLogic.GetAs<ThermalGrid>();
        }

        private static void DrawWindNeedle()
        {
            WindText.Clear();

            if (!Settings.Instance.DebugWindIndicator) return;
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Camera == null) return;
            if (MyAPIGateway.Gui != null && (MyAPIGateway.Gui.IsCursorVisible || MyAPIGateway.Gui.ChatEntryVisible)) return;

            Vector3 up = WindOverlay.PlayerUp;
            if (up.LengthSquared() < 1e-6f) return;

            Vector3 wind;
            if (!CurrentWind(out wind)) return;

            float speed = wind.Length();
            if (speed < MinimumReadableWind) return;

            var camera = MyAPIGateway.Session.Camera;
            MatrixD view = camera.WorldMatrix;

            float bearing;
            if (!WindCompass.Bearing(wind, view.Forward, up, out bearing)) return;

            double halfHeight = NeedleDistance * Math.Tan(camera.FovWithZoom * 0.5f);

            Vector3D centre = view.Translation
                + (view.Forward * NeedleDistance)
                + (view.Up * (halfHeight * NeedleScreenY));

            double radians = bearing * Math.PI / 180d;
            Vector3D screenUp = view.Up;
            Vector3D screenRight = view.Right;

            Vector3D needle = (screenUp * Math.Cos(radians)) + (screenRight * Math.Sin(radians));

            double length = halfHeight * NeedleScreenRadius;
            double thickness = length * 0.11d;

            float share = ThermalMath.Clamp01(speed / GaleSpeed);
            Vector4 colour = WindOverlay.Colour(share).ToVector4();

            Vector3D tail = centre - (needle * (length * 0.35d));
            Vector3D tip = centre + (needle * length);

            MySimpleObjectDraw.DrawLine(tail, tip, NeedleMaterial, ref colour, (float)thickness);

            Vector3D sweep = (screenRight * Math.Cos(radians)) - (screenUp * Math.Sin(radians));
            Vector3D back = tip - (needle * (length * 0.35d));

            MySimpleObjectDraw.DrawLine(
                tip, back + (sweep * length * 0.2d), NeedleMaterial, ref colour, (float)thickness);
            MySimpleObjectDraw.DrawLine(
                tip, back - (sweep * length * 0.2d), NeedleMaterial, ref colour, (float)thickness);

            Vector4 tick = new Color(120, 140, 152).ToVector4();
            Vector3D tickBase = centre + (screenUp * (length * 1.25d));

            MySimpleObjectDraw.DrawLine(
                tickBase, tickBase + (screenUp * (length * 0.25d)), NeedleMaterial, ref tick,
                (float)(thickness * 0.6d));

            WindText.Append(speed.ToString("n1")).Append(" m/s");
        }

        private const float GaleSpeed = 35f;

        private static bool CurrentWind(out Vector3 wind)
        {
            wind = Vector3.Zero;

            ThermalGrid thermals = ControlledGrid();
            if (thermals != null)
            {
                EnvironmentState state = thermals.LastState;
                if (state.WindSpeed > 0f && state.WindDirectionLocal.LengthSquared() > 1e-6f)
                {
                    wind = (Vector3)Vector3D.TransformNormal(
                        state.WindDirectionLocal, thermals.Grid.WorldMatrix) * state.WindSpeed;
                    return true;
                }

                return false;
            }

            wind = WindOverlay.PlayerWind;
            return wind.LengthSquared() > 0f;
        }

        private static ThermalGrid ControlledGrid()
        {
            if (MyAPIGateway.Session.Player == null || MyAPIGateway.Session.Player.Controller == null)
                return null;

            IMyShipController controller =
                MyAPIGateway.Session.Player.Controller.ControlledEntity as IMyShipController;

            if (controller == null || controller.CubeGrid == null) return null;

            MyCubeGrid grid = controller.CubeGrid as MyCubeGrid;
            if (grid == null || grid.GameLogic == null) return null;

            ThermalGrid thermals = grid.GameLogic.GetAs<ThermalGrid>();
            return thermals == null || thermals.Simulation == null ? null : thermals;
        }

        private static void DrawBillboard(ThermalGrid thermals, ThermalBlock bound, MatrixD cameraMatrix)
        {
            Vector3D position;
            bound.Block.ComputeWorldCenter(out position);

            float averageBlockLength = Vector3I.DistanceManhattan(bound.Block.Max + 1, bound.Block.Min) * 0.33f;

            Color color = ColorExtensions.HSVtoColor(TemperatureScale.ToHsv(bound.Node.Temperature));

            float distance = 0.01f;
            position = cameraMatrix.Translation + (position - cameraMatrix.Translation) * distance;
            float scaler = 1.2f * thermals.Grid.GridSizeHalf * averageBlockLength * distance;

            MyTransparentGeometry.AddBillboardOriented(
                MyStringId.GetOrCompute("GaugeThermalTexture"),
                color,
                position,
                cameraMatrix.Left,
                cameraMatrix.Up,
                scaler,
                scaler);
        }
    }
}
