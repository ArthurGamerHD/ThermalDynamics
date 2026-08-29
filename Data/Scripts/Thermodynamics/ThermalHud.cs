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
    /// <summary>
    /// The in-world readouts: a temperature billboard over the block the extinguisher is aimed at, and
    /// a grid summary in the cockpit. Client only, at draw rate. The text needs Rich HUD; the
    /// billboard is a world-space quad through the mod API and does not. See blocks.md.
    /// </summary>
    public static class ThermalHud
    {
        /// <summary>Draw calls between text updates.</summary>
        private const int TextInterval = 6;

        private static Label toolLabel;
        private static Label windLabel;
        private static Label airLabel;
        private static LabelBox gridPanel;
        private static int sinceText;

        private static readonly StringBuilder ToolText = new StringBuilder();
        private static readonly StringBuilder WindText = new StringBuilder();
        private static readonly StringBuilder AirText = new StringBuilder();

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

            // Under the needle, which is drawn in the world rather than by the framework. The two
            // are placed independently — one in pixels, one by field of view — so this offset is
            // eyeballed against NeedleScreenY rather than derived from it, and moving either wants
            // both looked at.
            windLabel = new Label(HudMain.HighDpiRoot)
            {
                ParentAlignment = ParentAlignments.Center,
                Offset = new Vector2(0f, -132f),
                Format = new GlyphFormat(new Color(200, 224, 236), TextAlignment.Center, 0.9f),
                Visible = false,
            };

            // **The one readout that is on by default**, so the mod announces itself in a healthy
            // world rather than only in a failing one (`B41`). Bottom centre, above where the game
            // puts its own hint line, small and unbacked: it is one short line and a box around it
            // would be the mod-shaped panel the intent page says not to draw.
            airLabel = new Label(HudMain.HighDpiRoot)
            {
                ParentAlignment = ParentAlignments.Bottom | ParentAlignments.InnerV,
                Offset = new Vector2(0f, 96f),
                Format = new GlyphFormat(new Color(206, 214, 220), TextAlignment.Center, 0.85f),
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

        /// <summary>For the two figures that mean something is wrong: starvation and criticals.</summary>
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

            // The billboard follows the aim every frame; the text does not need to. Ten updates a
            // second avoids rebuilding a text board sixty times.
            sinceText++;
            bool publish = sinceText >= TextInterval;
            if (publish) sinceText = 0;

            // The billboard is drawn inside the tool pass, so that runs every frame. The summary is
            // text only, so it is built only on the frames that publish it.
            DrawToolHud();

            // The needle turns with the camera, so it is drawn every frame; its speed figure is
            // text and rides the same interval as everything else.
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

            ToolText.Append(TemperatureScale.ToCelsiusString(bound.Node.Temperature));
        }

        /// <summary>
        /// The performance panel: what the simulation is doing right now across every live grid.
        /// **Every figure here is a count, not a clock**, so two players comparing notes are not
        /// comparing hardware. Ctrl+Shift+P, off by default.
        /// See blocks.md, The performance panel.
        /// </summary>
        private static void DrawGridHud()
        {
            GridPanelText = null;
            if (!ShowPerformancePanel) return;

            IList<ThermalGrid> grids = ThermalGrid.LiveGrids;
            if (grids == null || grids.Count == 0) return;

            int simulated = 0;
            long blocks = 0;
            long links = 0;
            int loops = 0;
            int critical = 0;
            int floored = 0;
            float vented = 0f;
            float made = 0f;

            // Worst rather than mean: a step costs what its stiffest grid demands, and an average
            // across a fleet would hide the one grid that is actually setting the bill.
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

                // Summed across the fleet rather than worst-of, unlike the substep figures: heat
                // is additive and a fleet's cooling is the sum of its ships'.
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

            // Starvation is the one number that predicts trouble: everything that has ever diverged
            // in this mod was refused the substeps it asked for. Red once any is being refused.
            float starved = demanded > granted && demanded > 0f
                ? (demanded - granted) / demanded : 0f;
            Pair(text, "substeps", granted + " / " + demanded.ToString("n1"),
                "starved", (starved * 100f).ToString("n0") + "%",
                starved > 0f ? Warning : (GlyphFormat?)null);

            Pair(text, "visits/s", Thousands((long)visitsPerSecond), "floored", Thousands(floored));

            // The cooling question, as the two figures that answer it: a fleet venting less than
            // it makes is heating up, whatever any single temperature currently reads. Warned on
            // that condition rather than on any absolute figure, since the numbers themselves are
            // a property of how big the ships are.
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

        /// <summary>Appends a label and value, with the label dimmed and the column width fixed.</summary>
        private static void Row(RichText text, string label, string value, GlyphFormat? format = null)
        {
            text.Add(label.PadRight(9), Muted);
            text.Add(value + "\n", format ?? Body);
        }

        /// <summary>
        /// Two label-value pairs on one line, which is what makes the panel compact enough to leave
        /// on. Six lines of paired figures against twelve of single ones is the difference between a
        /// readout a player tolerates in the corner of the screen and one they turn off.
        /// </summary>
        private static void Pair(RichText text, string leftLabel, string leftValue,
            string rightLabel, string rightValue, GlyphFormat? rightFormat = null)
        {
            text.Add(leftLabel.PadRight(9), Muted);
            text.Add(leftValue.PadRight(11), Body);
            text.Add(rightLabel.PadRight(9), Muted);
            text.Add(rightValue + "\n", rightFormat ?? Body);
        }

        /// <summary>
        /// Whether the performance panel is up. Toggled from <c>Session.PollKeys</c>.
        ///
        /// Off by default and remembered for the session only: it is a diagnostic, and a player who
        /// wanted it yesterday does not necessarily want it today.
        /// </summary>
        public static bool ShowPerformancePanel;

        /// <summary>Flips the panel and returns the new state, for the chat acknowledgement.</summary>
        public static bool TogglePerformancePanel()
        {
            ShowPerformancePanel = !ShowPerformancePanel;
            return ShowPerformancePanel;
        }

        private static string Thousands(long value)
        {
            return value.ToString("n0");
        }

        /// <summary>
        /// Watts at a readable magnitude. A ship's heat balance spans kilowatts on a miner to tens
        /// of megawatts on a capital ship, and a raw figure at either end is a wall of digits.
        /// </summary>
        private static string Watts(float watts)
        {
            return Units.Watts(watts);
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

        // ---- the wind indicator ---------------------------------------------------------------

        /// <summary>
        /// Where the needle sits, as a share of half the screen's height below the centre. Under the
        /// crosshair rather than in a corner: it is read while aiming or flying, and an indicator
        /// that needs the eyes to leave the middle of the screen is one nobody looks at.
        /// </summary>
        private const double NeedleScreenY = -0.17d;

        /// <summary>Needle length, on the same scale.</summary>
        private const double NeedleScreenRadius = 0.055d;

        /// <summary>
        /// Metres in front of the eye the needle is drawn. Small enough that no cockpit geometry can
        /// come between it and the camera, and its size is corrected for the distance, so this
        /// changes nothing but what can occlude it.
        /// </summary>
        private const double NeedleDistance = 0.06d;

        /// <summary>Below this the readout hides rather than showing a needle nobody should trust.</summary>
        private const float MinimumReadableWind = 0.5f;

        private static readonly MyStringId NeedleMaterial = MyStringId.GetOrCompute("Square");

        /// <summary>
        /// **The line the mod shows as a matter of course**: what the air is doing, and what the
        /// ship is doing about it.
        ///
        /// <para>
        /// What it says is `EnvironmentReadout`'s decision and is pinned by
        /// `EnvironmentReadoutTests`; this is the shell that finds the numbers and hands them over.
        /// The grid is the one the player is on or aiming at — the readout is about *their* ship,
        /// not about the fleet, which is what the performance panel is for.
        /// </para>
        ///
        /// <para>
        /// Off costs a comparison and nothing else (`C7`): the text is not built, the label is
        /// hidden, and no grid is walked.
        /// </para>
        /// </summary>
        private static void DrawEnvironmentReadout()
        {
            AirText.Clear();

            if (Settings.Instance == null || !Settings.Instance.ShowEnvironmentReadout) return;

            ThermalGrid thermals = PlayerGrid();
            if (thermals == null || thermals.Simulation == null) return;

            ThermalNode hottest = thermals.HottestNode;

            // No hottest node is a grid the solver has not stepped yet, and the air is still true
            // — so the line drops the ship's half rather than the whole readout.
            float peak = hottest == null ? float.NaN : hottest.Temperature;
            float critical = hottest == null ? 0f : hottest.Thermal.CriticalTemperature;

            AirText.Append(EnvironmentReadout.Line(
                thermals.LastState.AmbientTemperature, peak, critical));
        }

        /// <summary>
        /// The grid this readout is about: the one the player is standing on or piloting, and
        /// nothing when they are on foot outside.
        ///
        /// <para>
        /// Their own ship rather than the nearest or the hottest. A readout that followed whatever
        /// was worst in the world would be answering a question the player did not ask, and would
        /// change what it was talking about as they walked around.
        /// </para>
        /// </summary>
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
                // Standing on a grid: the one under their feet, which the engine already tracks.
                grid = character.Parent as IMyCubeGrid;
            }

            if (grid == null || grid.GameLogic == null) return null;
            return grid.GameLogic.GetAs<ThermalGrid>();
        }

        /// <summary>
        /// A compass needle for the wind under the crosshair. Screen up is the way the player faces, so
        /// the needle points where the wind is pushing them — the opposite of the meteorological
        /// convention. In a cockpit it is the *relative* wind, which is what heats the hull.
        /// See configuration.md, The wind indicator.
        /// </summary>
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

            // Half the screen at the needle's distance, so a screen fraction becomes metres. Taken
            // on height alone, so the dial keeps its shape and its place on any window shape, and
            // corrected for the field of view, so zooming does not shrink it.
            double halfHeight = NeedleDistance * Math.Tan(camera.FovWithZoom * 0.5f);

            Vector3D centre = view.Translation
                + (view.Forward * NeedleDistance)
                + (view.Up * (halfHeight * NeedleScreenY));

            // The bearing turned into a screen direction: clockwise from straight up, which is what
            // makes the top of the needle mean "the way you are facing".
            double radians = bearing * Math.PI / 180d;
            Vector3D screenUp = view.Up;
            Vector3D screenRight = view.Right;

            Vector3D needle = (screenUp * Math.Cos(radians)) + (screenRight * Math.Sin(radians));

            double length = halfHeight * NeedleScreenRadius;
            double thickness = length * 0.11d;

            // Strength in the colour as well as the length, matching the wind map's ramp so the two
            // readouts cannot disagree about what counts as a gale.
            float share = Clamp01(speed / GaleSpeed);
            Vector4 colour = WindOverlay.Colour(share).ToVector4();

            Vector3D tail = centre - (needle * (length * 0.35d));
            Vector3D tip = centre + (needle * length);

            MySimpleObjectDraw.DrawLine(tail, tip, NeedleMaterial, ref colour, (float)thickness);

            // A head, swept back in the screen plane, so a needle pointing away from the viewer is
            // still distinguishable from one pointing towards them.
            Vector3D sweep = (screenRight * Math.Cos(radians)) - (screenUp * Math.Sin(radians));
            Vector3D back = tip - (needle * (length * 0.35d));

            MySimpleObjectDraw.DrawLine(
                tip, back + (sweep * length * 0.2d), NeedleMaterial, ref colour, (float)thickness);
            MySimpleObjectDraw.DrawLine(
                tip, back - (sweep * length * 0.2d), NeedleMaterial, ref colour, (float)thickness);

            // A tick at the top of the dial, dim, so the needle has something to be read against.
            // Without it a needle a few degrees off vertical looks the same as one dead ahead.
            Vector4 tick = new Color(120, 140, 152).ToVector4();
            Vector3D tickBase = centre + (screenUp * (length * 1.25d));

            MySimpleObjectDraw.DrawLine(
                tickBase, tickBase + (screenUp * (length * 0.25d)), NeedleMaterial, ref tick,
                (float)(thickness * 0.6d));

            WindText.Append(speed.ToString("n1")).Append(" m/s");
        }

        /// <summary>
        /// Wind speed the needle draws at full strength, m/s. The storm end of an earthlike world's
        /// field, which is a little under half its 80 m/s ceiling.
        /// </summary>
        private const float GaleSpeed = 35f;

        /// <summary>
        /// The wind to show: the controlled grid's own relative wind where there is one, and the
        /// field at the player where there is not.
        /// </summary>
        private static bool CurrentWind(out Vector3 wind)
        {
            wind = Vector3.Zero;

            ThermalGrid thermals = ControlledGrid();
            if (thermals != null)
            {
                EnvironmentState state = thermals.LastState;
                if (state.WindSpeed > 0f && state.WindDirectionLocal.LengthSquared() > 1e-6f)
                {
                    // The solver keeps the relative wind in the grid's own frame, since that is
                    // where it meets the faces it heats. The needle wants it back in the world.
                    wind = (Vector3)Vector3D.TransformNormal(
                        state.WindDirectionLocal, thermals.Grid.WorldMatrix) * state.WindSpeed;
                    return true;
                }

                // A grid that has not sampled yet, or one parked in still air. Falling through to
                // the field would show the ground wind beside a ship that disagrees, so it stops.
                return false;
            }

            wind = WindOverlay.PlayerWind;
            return wind.LengthSquared() > 0f;
        }

        /// <summary>The thermal grid the player is controlling, or null when they are on foot.</summary>
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

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
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
