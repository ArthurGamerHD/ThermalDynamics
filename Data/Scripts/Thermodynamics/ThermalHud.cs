using System.Collections.Generic;
using System.Text;
using Draygo.API;
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
    /// </summary>
    public static class ThermalHud
    {
        public static HudAPIv2 hudBase;
        public static HudAPIv2.HUDMessage hudStatusTool;
        public static HudAPIv2.HUDMessage hudStatusGrid;

        private static readonly StringBuilder ToolText = new StringBuilder();
        private static readonly StringBuilder GridText = new StringBuilder();

        /// <summary>Reused by the billboard pass so aiming at a block allocates nothing.</summary>
        private static readonly List<BlockInstance> NeighbourScratch = new List<BlockInstance>();

        public static void Initialize()
        {
            hudBase = new HudAPIv2(HudInit);
        }

        private static void HudInit()
        {
            hudStatusTool = new HudAPIv2.HUDMessage(ToolText, new Vector2D(-1, 0), null, -1, 1, true, false, null, BlendTypeEnum.PostPP, "white");
            hudStatusTool.InitialColor = Color.White;
            hudStatusTool.ShadowColor = Color.White;
            hudStatusTool.Scale *= 1;
            hudStatusTool.Origin = new Vector2D(0.02f, 0.015f);
            hudStatusTool.Visible = true;

            hudStatusGrid = new HudAPIv2.HUDMessage(GridText, new Vector2D(-1, 0), null, -1, 1, true, false, null, BlendTypeEnum.PostPP, "white");
            hudStatusGrid.InitialColor = Color.White;
            hudStatusGrid.ShadowColor = Color.White;
            hudStatusGrid.Scale *= 1;
            hudStatusGrid.Origin = new Vector2D(0.75f, -0.45f);
            hudStatusGrid.Visible = true;
        }

        public static void Draw()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;

            DrawToolHud();
            DrawGridHud();
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
                if (hudStatusGrid != null)
                {
                    hudStatusGrid.InitialColor = ColorExtensions.HSVtoColor(
                        Tools.GetTemperatureColor(hottest.Temperature));
                }

                GridText.Append("Peak T: ")
                    .Append(Tools.KelvinToCelsiusString(hottest.Temperature))
                    .Append('\n');

                // Per second, so the number stays comparable whatever the step rate is.
                float perSecond = hottest.LastDeltaTemperature * Settings.Instance.PerSecond;
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
