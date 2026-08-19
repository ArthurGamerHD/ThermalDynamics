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
    /// <summary>
    /// The in-world readouts: a temperature billboard over the block the extinguisher is aimed
    /// at, and a grid summary in the cockpit.
    ///
    /// Client only, driven at draw rate rather than step rate, and every path first tests whether
    /// it has anything to draw.
    ///
    /// The text is drawn by the Rich HUD Framework, as the settings menu and debug readout are. The
    /// billboard is not: it is a world-space quad drawn through the mod API, so aiming the
    /// extinguisher still shows a temperature-coloured block without the framework, but no number.
    /// </summary>
    public static class ThermalHud
    {
        /// <summary>Draw calls between text updates.</summary>
        private const int TextInterval = 6;

        private static Label toolLabel;
        private static LabelBox gridPanel;
        private static int sinceText;

        private static readonly StringBuilder ToolText = new StringBuilder();

        /// <summary>Reused by the billboard pass so aiming at a block allocates nothing.</summary>
        private static readonly List<BlockInstance> NeighbourScratch = new List<BlockInstance>();

        /// <summary>Built when the framework registers, from <see cref="ThermalSettingsMenu"/>.</summary>
        public static void Build()
        {
            if (toolLabel != null) return;

            // Just off the crosshair, over the block being aimed at. A bare label rather than a
            // panel: it is one short line at the centre of the screen, where a box would obscure the
            // target.
            toolLabel = new Label(HudMain.HighDpiRoot)
            {
                ParentAlignment = ParentAlignments.Center,
                Offset = new Vector2(60f, 30f),
                Format = new GlyphFormat(Color.White, TextAlignment.Left, 1.05f),
                Visible = false,
            };

            // Top right, clear of the game's own cockpit readouts, and on a background: five lines
            // of unbacked text over a planet surface is unreadable.
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

        /// <summary>Body text, matching the framework's own panel colour.</summary>
        private static readonly GlyphFormat Body =
            new GlyphFormat(new Color(220, 235, 242), TextAlignment.Left, 0.95f);

        /// <summary>Labels, dimmer than their values so the numbers are what the eye lands on.</summary>
        private static readonly GlyphFormat Muted =
            new GlyphFormat(new Color(140, 158, 168), TextAlignment.Left, 0.95f);

        public static void Reset()
        {
            toolLabel = null;
            gridPanel = null;
        }

        public static void Draw()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;

            // The billboard follows the aim every frame; the text does not need to. Ten updates a
            // second avoids rebuilding a text board sixty times.
            sinceText++;
            bool publish = sinceText >= TextInterval;
            if (publish) sinceText = 0;

            // The billboard is drawn inside the tool pass, so that runs every frame. The summary is
            // text only, so it is built only on the frames that publish it.
            DrawToolHud();
            if (!publish) return;

            DrawGridHud();
            Publish(toolLabel, ToolText);

            if (gridPanel != null)
            {
                gridPanel.Visible = GridPanelText != null;
                if (GridPanelText != null) gridPanel.Text = GridPanelText;
            }
        }

        /// <summary>
        /// Pushes a built string onto its label, or does nothing when the framework never registered.
        /// The text is built either way, which costs a few appends and avoids a second code path for
        /// the case where the framework appears late.
        /// </summary>
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

            MatrixD matrix = MyAPIGateway.Session.Camera.WorldMatrix;

            Vector3D start = matrix.Translation;
            Vector3D end = start + (matrix.Forward * 15);

            IHitInfo hit;
            MyAPIGateway.Physics.CastRay(start, end, out hit);
            MyCubeGrid grid = hit == null ? null : hit.HitEntity as MyCubeGrid;
            if (grid == null) return;

            ThermalGrid thermals = grid.GameLogic.GetAs<ThermalGrid>();
            if (thermals == null || thermals.Simulation == null) return;

            Vector3I position = grid.WorldToGridInteger(hit.Position + (matrix.Forward * 0.005f));
            ThermalBlock bound = thermals.GetAtCell(position);
            if (bound == null || bound.Node == null) return;

            DrawBillboard(thermals, bound, matrix);

            NeighbourScratch.Clear();
            thermals.Model.GetNeighbours(bound.Instance, NeighbourScratch);
            for (int i = 0; i < NeighbourScratch.Count; i++)
            {
                ThermalBlock neighbour = thermals.Get(NeighbourScratch[i].Position);
                if (neighbour != null) DrawBillboard(thermals, neighbour, matrix);
            }

            ToolText.Append(Tools.KelvinToCelsiusString(bound.Node.Temperature));
        }

        private static void DrawGridHud()
        {
            GridPanelText = null;

            IMyCubeBlock controlledBlock = MyAPIGateway.Session == null || MyAPIGateway.Session.Player == null
                ? null
                : MyAPIGateway.Session.Player.Controller.ControlledEntity as IMyCubeBlock;
            if (controlledBlock == null) return;

            MyCubeGrid grid = controlledBlock.CubeGrid as MyCubeGrid;
            if (grid == null) return;

            ThermalGrid thermals = grid.GameLogic.GetAs<ThermalGrid>();
            if (thermals == null || thermals.Simulation == null) return;

            RichText text = new RichText();

            Row(text, "ambient", Tools.KelvinToCelsiusString(thermals.LastState.AmbientTemperature));

            ThermalNode hottest = thermals.HottestNode;
            if (hottest != null)
            {
                // Only the peak line is coloured: tinting the whole panel would apply a temperature
                // colour to figures that are not temperatures.
                Row(text, "peak", Tools.KelvinToCelsiusString(hottest.Temperature),
                    new GlyphFormat(
                        ColorExtensions.HSVtoColor(Tools.GetTemperatureColor(hottest.Temperature)),
                        TextAlignment.Left, 0.95f));

                // Per second, so the figure is comparable at any step rate.
                float perSecond = hottest.LastDeltaTemperature * Settings.Instance.StepsPerSecond;
                Row(text, "rate", perSecond.ToString("n2") + " K/s");
            }

            int critical = thermals.CriticalBlocks;
            Row(text, "critical", critical.ToString(),
                critical > 0
                    ? new GlyphFormat(new Color(226, 92, 80), TextAlignment.Left, 0.95f)
                    : (GlyphFormat?)null);

            Row(text, "loops", thermals.Simulation.Solver.Loops.Count.ToString());

            GridPanelText = text;
        }

        /// <summary>Appends a label and value, with the label dimmed and the column width fixed.</summary>
        private static void Row(RichText text, string label, string value, GlyphFormat? format = null)
        {
            text.Add(label.PadRight(9), Muted);
            text.Add(value + "\n", format ?? Body);
        }

        /// <summary>The panel's contents, or null when there is nothing to show.</summary>
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

        private static void DrawBillboard(ThermalGrid thermals, ThermalBlock bound, MatrixD cameraMatrix)
        {
            Vector3D position;
            bound.Block.ComputeWorldCenter(out position);

            float averageBlockLength = Vector3I.DistanceManhattan(bound.Block.Max + 1, bound.Block.Min) * 0.33f;

            Color color = ColorExtensions.HSVtoColor(Tools.GetTemperatureColor(bound.Node.Temperature));

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
