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
    /// Client only, draw rate rather than step rate, and every path starts by finding out
    /// whether it has anything to draw at all.
    ///
    /// The text is drawn by the Rich HUD Framework, the same as the settings menu and the debug
    /// readout. The billboard is not: it is a world-space quad over a block, which is the mod API's
    /// own job and needs no framework at all — so aiming the extinguisher still shows a
    /// temperature-coloured block when the framework is missing, it just shows no number.
    /// </summary>
    public static class ThermalHud
    {
        /// <summary>Draw calls between text updates.</summary>
        private const int TextInterval = 6;

        private static Label toolLabel;
        private static Label gridLabel;
        private static int sinceText;

        private static readonly StringBuilder ToolText = new StringBuilder();
        private static readonly StringBuilder GridText = new StringBuilder();

        /// <summary>Reused by the billboard pass so aiming at a block allocates nothing.</summary>
        private static readonly List<BlockInstance> NeighbourScratch = new List<BlockInstance>();

        /// <summary>Built when the framework registers, from <see cref="ThermalSettingsMenu"/>.</summary>
        public static void Build()
        {
            if (toolLabel != null) return;

            // Just off the crosshair, where the block being aimed at is.
            toolLabel = new Label(HudMain.HighDpiRoot)
            {
                ParentAlignment = ParentAlignments.Center,
                Offset = new Vector2(60f, 30f),
                Format = new GlyphFormat(Color.White, TextAlignment.Left, 1f),
                Visible = false,
            };

            // Right edge, below the middle: clear of the game's own cockpit readouts.
            gridLabel = new Label(HudMain.HighDpiRoot)
            {
                ParentAlignment = ParentAlignments.Right | ParentAlignments.InnerH,
                Offset = new Vector2(-40f, -160f),
                BuilderMode = TextBuilderModes.Lined,
                Format = new GlyphFormat(Color.White, TextAlignment.Left, 1f),
                Visible = false,
            };
        }

        public static void Reset()
        {
            toolLabel = null;
            gridLabel = null;
        }

        public static void Draw()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;

            // The billboard follows the aim every frame; the text does not have to. Ten updates a
            // second is past what anyone reads and saves rebuilding a text board sixty times.
            sinceText++;
            bool publish = sinceText >= TextInterval;
            if (publish) sinceText = 0;

            DrawToolHud();
            DrawGridHud();

            if (!publish) return;

            Publish(toolLabel, ToolText);
            Publish(gridLabel, GridText);
        }

        /// <summary>
        /// Pushes a built string onto its label, or does nothing at all when the framework never
        /// registered. The text is built either way: it is a few appends, and a readout that has to
        /// be rebuilt from scratch the moment the framework appears is a second code path to get
        /// wrong.
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
            GridText.Clear();

            IMyCubeBlock controlledBlock = MyAPIGateway.Session == null || MyAPIGateway.Session.Player == null
                ? null
                : MyAPIGateway.Session.Player.Controller.ControlledEntity as IMyCubeBlock;
            if (controlledBlock == null) return;

            MyCubeGrid grid = controlledBlock.CubeGrid as MyCubeGrid;
            if (grid == null) return;

            ThermalGrid thermals = grid.GameLogic.GetAs<ThermalGrid>();
            if (thermals == null || thermals.Simulation == null) return;

            GridText.Append("Ambient: ")
                .Append(Tools.KelvinToCelsiusString(thermals.LastState.AmbientTemperature))
                .Append('\n');

            ThermalNode hottest = thermals.HottestNode;
            if (hottest != null)
            {
                if (gridLabel != null)
                {
                    // The summary is tinted by the hottest block, so the panel itself reads as a
                    // warning before any of its numbers are read.
                    gridLabel.Format = new GlyphFormat(
                        ColorExtensions.HSVtoColor(Tools.GetTemperatureColor(hottest.Temperature)),
                        TextAlignment.Left, 1f);
                }

                GridText.Append("Peak T: ")
                    .Append(Tools.KelvinToCelsiusString(hottest.Temperature))
                    .Append('\n');

                // Per second, so the number stays comparable whatever the step rate is.
                float perSecond = hottest.LastDeltaTemperature * Settings.Instance.StepsPerSecond;
                GridText.Append("Peak dT/s: ").Append(perSecond.ToString("n3")).Append('\n');
            }

            GridText.Append("Critical Blocks: ").Append(thermals.CriticalBlocks).Append('\n');
            GridText.Append("Coolant Loops: ").Append(thermals.Simulation.Solver.Loops.Count).Append('\n');
        }

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
