using System;
using System.Collections.Generic;
using System.Diagnostics;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using static VRageRender.MyBillboard;

namespace Thermodynamics
{
    /// <summary>
    /// The block debug overlay: every block of the targeted grid drawn as a coloured box, visible
    /// through the hull. Two views draw something other than blocks: the solar view draws the skin,
    /// one quad per exposed face, and the room view draws the mapped air, one box per cell.
    ///
    /// Everything is drawn client side, per frame, as transparent geometry; nothing is written to
    /// the grid. The replaced block-colouring modes called <c>ColorBlocks</c>, which is a real,
    /// replicated and permanent change to a grid's paint.
    ///
    /// Every block is drawn whether or not something stands between it and the camera, since the
    /// blocks under inspection are usually buried. This is done by scaling each box about the eye
    /// onto a shallow band just in front of the near plane: a perspective projection is invariant
    /// under scaling about the eye, so each box appears exactly where the block is while nothing in
    /// the scene can occlude it. Relative depth order between boxes is preserved because they all
    /// take the same scale factor.
    /// </summary>
    public static class ThermalDebugView
    {
        public enum Mode
        {
            Off = 0,
            Temperature = 1,
            SolarWatts = 2,
            ExposedFaces = 3,
            FrictionWatts = 4,
            Rooms = 5,
        }

        /// <summary>
        /// Number of views, including off. Read by the keybind cycle, the config clamp and the menu
        /// dropdown rather than each repeating the count.
        /// </summary>
        public const int ModeCount = (int)Mode.Rooms + 1;

        /// <summary>Cycled by the keybind, seeded from <see cref="Settings.DebugBlockOverlay"/>.</summary>
        public static Mode Current;

        private static readonly MyStringId FaceMaterial = MyStringId.GetOrCompute("Square");
        private static readonly MyStringId LineMaterial = MyStringId.GetOrCompute("Square");

        /// <summary>Reused every frame so looking at a ship allocates nothing.</summary>
        private static readonly List<ThermalGrid> Targets = new List<ThermalGrid>();

        /// <summary>
        /// How much of the target is drawn. A capital ship is forty thousand blocks and each box is
        /// eighteen billboards, which the renderer cannot carry at frame rate.
        /// </summary>
        public static readonly OverlayBudget Budget = new OverlayBudget();

        /// <summary>Billboards handed to the renderer this frame, for the report.</summary>
        private static long billboards;

        /// <summary>Frame timer for the overlay's own cost. Only started while telemetry collects.</summary>
        private static readonly Stopwatch DrawClock = new Stopwatch();

        /// <summary>Cone containing the camera frustum, recomputed per frame.</summary>
        private static double coneSin, coneCos;

        /// <summary>
        /// The grid the readout describes: the one being looked at, or the one being controlled when
        /// the camera points at nothing. Null when the overlay is off or has no target.
        /// </summary>
        public static ThermalGrid Focus { get; private set; }

        /// <summary>Metres out to which a grid is picked up by the aim ray.</summary>
        private const double PickRange = 300;

        /// <summary>
        /// Distance from the eye to the band the boxes are scaled onto. Small enough that nothing in
        /// the scene can be in front of it, large enough to clear the near plane.
        /// </summary>
        private const double BandScale = 0.02;

        /// <summary>Alpha of a box face. Low because a hull is many boxes deep.</summary>
        private const float FaceAlpha = 0.22f;

        /// <summary>
        /// Alpha of a drawn surface. Higher than a box because the solar view draws only the skin,
        /// one quad deep, with nothing stacked behind it.
        /// </summary>
        private const float SurfaceAlpha = 0.75f;

        /// <summary>
        /// True when the selected view reads a per-mechanism watt figure. Those figures are recorded
        /// only when something reads them, so the overlay must declare itself a reader; otherwise
        /// the solar and friction views would draw a uniformly zero grid.
        /// </summary>
        public static bool NeedsWatts
        {
            get { return Current == Mode.SolarWatts || Current == Mode.FrictionWatts; }
        }

        public static void Cycle()
        {
            Current = (Mode)(((int)Current + 1) % ModeCount);
            Announce();
        }

        public static void Set(Mode mode)
        {
            Current = mode;
            Announce();
        }

        private static void Announce()
        {
            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated) return;
            MyAPIGateway.Utilities.ShowNotification("thermal overlay: " + Describe(Current), 2000, "White");
        }

        public static string Describe(Mode mode)
        {
            switch (mode)
            {
                case Mode.Temperature: return "temperature";
                case Mode.SolarWatts: return "solar watts";
                case Mode.ExposedFaces: return "exposed faces";
                case Mode.FrictionWatts: return "friction watts";
                case Mode.Rooms: return "rooms";
                default: return "off";
            }
        }

        public static void Draw()
        {
            if (Current == Mode.Off)
            {
                Focus = null;
                return;
            }

            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated) return;
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Camera == null) return;

            MatrixD camera = MyAPIGateway.Session.Camera.WorldMatrix;
            Vector3D eye = camera.Translation;

            CollectTargets(ref camera, ref eye);
            Focus = Targets.Count > 0 ? Targets[Targets.Count - 1] : null;

            BeginFrame();

            for (int i = 0; i < Targets.Count; i++)
            {
                DrawGrid(Targets[i], ref camera, ref eye);
            }

            EndFrame();
            Targets.Clear();
        }

        /// <summary>
        /// Sets the frame's budget and view cone, and starts the clock when anything is reading it.
        /// </summary>
        private static void BeginFrame()
        {
            Budget.MaxBoxes = Settings.Instance == null ? 12000 : Settings.Instance.DebugOverlayMaxBoxes;
            Budget.BeginFrame();
            billboards = 0;

            IMyCamera camera = MyAPIGateway.Session.Camera;
            Vector2 viewport = camera.ViewportSize;
            double aspect = viewport.Y > 0 ? viewport.X / viewport.Y : 1.0;
            double half = OverlayBudget.ConeHalfAngle(camera.FovWithZoom, aspect);

            coneSin = Math.Sin(half);
            coneCos = Math.Cos(half);

            if (!Telemetry.Enabled) return;

            DrawClock.Reset();
            DrawClock.Start();
        }

        private static void EndFrame()
        {
            Budget.EndFrame();

            if (!Telemetry.Enabled || Budget.Considered == 0) return;

            DrawClock.Stop();

            Telemetry.Overlay.Frame(
                Describe(Current), Budget.Considered, Budget.Drawn, Budget.OffScreen, Budget.OverBudget,
                billboards, Budget.Radius, DrawClock.Elapsed.TotalMilliseconds);
        }

        /// <summary>
        /// Whether a box is drawn: in the camera's cone, and inside the budget. Counts the decision
        /// either way, which is what fits the radius for the next frame.
        /// </summary>
        private static bool Wanted(ref Vector3D delta, double boxRadius, ref MatrixD camera)
        {
            Vector3D forward = camera.Forward;

            if (!OverlayBudget.InView(ref delta, boxRadius, ref forward, coneSin, coneCos))
            {
                Budget.Cull();
                return false;
            }

            return Budget.Accept(delta.Length());
        }

        /// <summary>
        /// One box on the band in front of the eye, counted against the billboards the renderer is
        /// handed: six quads and twelve lines for a solid box, twelve lines for an outline.
        /// </summary>
        private static void Box(
            ref MatrixD box, ref BoundingBoxD local, ref Color colour,
            MySimpleObjectRasterizer rasterizer, double thickness)
        {
            MySimpleObjectDraw.DrawTransparentBox(
                ref box,
                ref local,
                ref colour,
                rasterizer,
                1,
                (float)(thickness * BandScale),
                FaceMaterial,
                LineMaterial,
                false,
                -1,
                BlendTypeEnum.PostPP);

            billboards += rasterizer == MySimpleObjectRasterizer.Wireframe ? 12 : 18;
        }

        /// <summary>
        /// The grids to draw: the one being controlled, and the one being looked at.
        ///
        /// Limited to those two rather than every grid in range, since a box per block across many
        /// grids is a frame rate cost. Both chosen grids are drawn in full, with no radius cutoff,
        /// which would otherwise read as the far end of a grid being cold.
        /// </summary>
        private static void CollectTargets(ref MatrixD camera, ref Vector3D eye)
        {
            Targets.Clear();

            IMyEntity controlled = MyAPIGateway.Session.Player == null
                ? null
                : MyAPIGateway.Session.Player.Controller.ControlledEntity as IMyEntity;

            IMyCubeBlock seat = controlled as IMyCubeBlock;
            if (seat != null) Add(seat.CubeGrid as MyCubeGrid);

            IHitInfo hit;
            MyAPIGateway.Physics.CastRay(eye, eye + (camera.Forward * PickRange), out hit);
            if (hit != null) Add(hit.HitEntity as MyCubeGrid);
        }

        private static void Add(MyCubeGrid grid)
        {
            if (grid == null || grid.GameLogic == null) return;

            ThermalGrid thermals = grid.GameLogic.GetAs<ThermalGrid>();
            if (thermals == null || thermals.Simulation == null) return;
            if (Targets.Contains(thermals)) return;

            Targets.Add(thermals);
        }

        private static void DrawGrid(ThermalGrid thermals, ref MatrixD camera, ref Vector3D eye)
        {
            if (Current == Mode.Rooms)
            {
                DrawRooms(thermals, ref camera, ref eye);
                return;
            }

            if (Current == Mode.SolarWatts)
            {
                DrawSolarSurfaces(thermals, ref camera, ref eye);
                return;
            }

            MatrixD gridMatrix = thermals.Grid.WorldMatrix;
            float gridSize = thermals.Grid.GridSize;

            foreach (ThermalBlock bound in thermals.Blocks)
            {
                ThermalNode node = bound.Node;
                if (node == null) continue;

                Vector3D centre;
                bound.Block.ComputeWorldCenter(out centre);

                Vector3D delta = centre - eye;

                Vector3D half = (Vector3D)(bound.Block.Max - bound.Block.Min + Vector3I.One)
                    * (gridSize * 0.5);

                // Off screen, behind the eye, or beyond what the budget allows this frame. A box
                // scaled onto the band still costs its billboards wherever the block is.
                if (!Wanted(ref delta, half.Length(), ref camera)) continue;

                MatrixD box = gridMatrix;
                box.Translation = eye + (delta * BandScale);

                BoundingBoxD local = new BoundingBoxD(-half * BandScale, half * BandScale);
                Color colour = Colour(node);

                Box(ref box, ref local, ref colour, MySimpleObjectRasterizer.SolidAndWireframe, 0.02);
            }
        }

        /// <summary>
        /// Sunlight, drawn on the surfaces that receive it.
        ///
        /// Solar heating is a property of a face rather than a block, so this view draws the skin —
        /// one quad per exposed face — shaded by that face's own irradiance: the sun's energy times
        /// the face's incidence against it. A face dark from turning away is then distinguishable
        /// from a face dark from being shadowed.
        ///
        /// Faces turned away from the camera are dropped, since they are on the far side of the
        /// grid and would only stack colour behind the near skin.
        /// </summary>
        private static void DrawSolarSurfaces(ThermalGrid thermals, ref MatrixD camera, ref Vector3D eye)
        {
            EnvironmentState state = thermals.LastState;
            ThermalSolver solver = thermals.Simulation.Solver;
            MatrixD gridMatrix = thermals.Grid.WorldMatrix;
            float gridSize = thermals.Grid.GridSize;

            // The same three conditions the solver tests before computing a solar watt.
            bool lit = Settings.Instance.EnableSolarHeat
                && !state.IsSolarOccluded
                && state.SolarEnergy > 0f;

            Vector3 sun = state.SunDirectionLocal;

            foreach (ThermalBlock bound in thermals.Blocks)
            {
                ThermalNode node = bound.Node;
                if (node == null || node.TotalExposedFaces == 0) continue;

                Vector3D centre;
                bound.Block.ComputeWorldCenter(out centre);

                Vector3D delta = centre - eye;

                Vector3 half = ((Vector3)(bound.Block.Max - bound.Block.Min + Vector3I.One))
                    * (gridSize * 0.5f);

                if (!Wanted(ref delta, half.Length(), ref camera)) continue;

                for (int face = 0; face < Face.Count; face++)
                {
                    if (node.ExposedFaces[face] == 0) continue;

                    Vector3 localNormal = Face.Normals[face];
                    Vector3D normal = Vector3D.TransformNormal(localNormal, gridMatrix);

                    Vector3D position = centre + (normal * Extent(ref half, ref localNormal));
                    if (Vector3D.Dot(normal, position - eye) >= 0) continue;

                    float dot = Vector3.Dot(localNormal, sun);
                    float irradiance = lit && dot > 0f ? state.SolarEnergy * dot : 0f;

                    // Read from the solver rather than recomputed, so a face the grid shadows looks
                    // shadowed and the overlay agrees with the temperatures. Per face, as the model
                    // holds it: the far layer of a wall is dark towards the sun and lit on its
                    // flank, which one figure per block cannot express.
                    irradiance *= solver.SunLitFraction(node.Index, face);

                    // W/m2 rather than watts: irradiance is the per-face quantity. The panel
                    // reports total watts.
                    Color colour = ColorExtensions.HSVtoColor(
                        Tools.GetTemperatureColor(irradiance, 1400f, 1f, 1000f));
                    colour.A = (byte)(SurfaceAlpha * 255f);

                    Vector3 localLeft, localUp;
                    Tangents(face, out localLeft, out localUp);

                    Vector3 left = (Vector3)Vector3D.TransformNormal(localLeft, gridMatrix);
                    Vector3 up = (Vector3)Vector3D.TransformNormal(localUp, gridMatrix);

                    // Scaled onto the same band as every other view, so the skin cannot z-fight with
                    // the hull it is drawn over.
                    MyTransparentGeometry.AddBillboardOriented(
                        FaceMaterial,
                        colour,
                        eye + ((position - eye) * BandScale),
                        left,
                        up,
                        (float)(Extent(ref half, ref localLeft) * BandScale),
                        (float)(Extent(ref half, ref localUp) * BandScale),
                        Vector2.Zero,
                        BlendTypeEnum.PostPP);

                    billboards++;
                }
            }
        }

        /// <summary>The block's half-extent along a single-axis unit vector.</summary>
        private static float Extent(ref Vector3 half, ref Vector3 axis)
        {
            return (half.X * Math.Abs(axis.X)) + (half.Y * Math.Abs(axis.Y)) + (half.Z * Math.Abs(axis.Z));
        }

        /// <summary>The two axes spanning a face, in block-local space.</summary>
        private static void Tangents(int face, out Vector3 left, out Vector3 up)
        {
            switch (Face.Axis(face))
            {
                case 0:
                    left = Vector3.Up;
                    up = Vector3.Backward;
                    return;
                case 1:
                    left = Vector3.Right;
                    up = Vector3.Backward;
                    return;
                default:
                    left = Vector3.Right;
                    up = Vector3.Up;
                    return;
            }
        }

        /// <summary>
        /// The rooms, drawn as the air itself and coloured by its temperature.
        ///
        /// Uses the same ramp as every other view, so a cold compartment reads the same as a cold
        /// block. Room identity goes on the wireframe instead, which marks the boundary without
        /// altering the colour of the air inside it.
        ///
        /// A room holding no air has no temperature and is drawn as an empty outline.
        /// </summary>
        private static void DrawRooms(ThermalGrid thermals, ref MatrixD camera, ref Vector3D eye)
        {
            RoomMap map = thermals.Simulation.Rooms.Map;
            if (map == null || map.IsEmpty) return;

            MatrixD gridMatrix = thermals.Grid.WorldMatrix;
            float gridSize = thermals.Grid.GridSize;

            // Slightly under a full cell, so the boundary between two cells stays a visible seam.
            Vector3D half = new Vector3D(gridSize * 0.45);

            float min = Settings.Instance.RoomOverlayMinKelvin;
            float max = Settings.Instance.RoomOverlayMaxKelvin;

            IList<HashSet<Vector3I>> rooms = map.Rooms;

            for (int room = 0; room < rooms.Count; room++)
            {
                RoomAirNode air = AirOf(thermals, room);

                bool hasAir = air != null && air.HasAir;

                // Mapped and dry while the game has oxygen in it. Compares oxygen rather than
                // airtightness: a sealed space that was never filled is empty in both models and is
                // not a fault.
                bool disagrees = Disagrees(thermals, room);

                Color fill = hasAir
                    ? Fill(air.Temperature, min, max)
                    : DryFill(disagrees);

                Color edge = RoomColour(room, map.IsVented(room));

                foreach (Vector3I cell in rooms[room])
                {
                    Vector3D centre = thermals.Grid.GridIntegerToWorld(cell);
                    Vector3D delta = centre - eye;

                    if (!Wanted(ref delta, half.Length(), ref camera)) continue;

                    MatrixD box = gridMatrix;
                    box.Translation = eye + (delta * BandScale);

                    BoundingBoxD local = new BoundingBoxD(-half * BandScale, half * BandScale);

                    // Solid where there is air to colour. Where there is none the box is still
                    // drawn: faintly filled when the game reports air the model lacks, and as a bare
                    // outline when both agree the room is empty.
                    Box(ref box, ref local, ref fill,
                        hasAir || disagrees
                            ? MySimpleObjectRasterizer.SolidAndWireframe
                            : MySimpleObjectRasterizer.Wireframe,
                        0.02);

                    if (!hasAir)
                    {
                        // A dry room still carries its identity, so it can be matched against the
                        // report's list as a filled one can.
                        Color dryEdge = edge;
                        dryEdge.A = (byte)(disagrees ? 255 : 90);

                        Box(ref box, ref local, ref dryEdge, MySimpleObjectRasterizer.Wireframe,
                            disagrees ? 0.04 : 0.02);

                        continue;
                    }

                    // The edges carry the room's identity over its temperature colour.
                    Box(ref box, ref local, ref edge, MySimpleObjectRasterizer.Wireframe, 0.02);
                }
            }

            DrawLostRooms(thermals, ref camera, ref eye, gridMatrix, half);
        }

        /// <summary>
        /// The compartments the game seals and this model does not.
        ///
        /// A room the flood fill walked into from outside is absent from the map, so without this
        /// there is nothing drawn and no way to distinguish it from a room correctly found to be
        /// open. Drawn in a colour no other part of the view uses, so a gap in the model reads as a
        /// fault rather than an absence.
        ///
        /// Each keeps its own hue, derived from its index in the list the report prints, so one can
        /// be identified on screen and matched to a dump.
        /// </summary>
        private static void DrawLostRooms(
            ThermalGrid thermals, ref MatrixD camera, ref Vector3D eye, MatrixD gridMatrix, Vector3D half)
        {
            IList<ThermalGrid.LostRoom> lost = thermals.LostRooms;
            if (lost.Count == 0) return;

            for (int i = 0; i < lost.Count; i++)
            {
                ThermalGrid.LostRoom room = lost[i];
                if (room.Cells == null) continue;

                Color fill = LostRoomColour(room.Index, room.VentSaysPressurised);

                foreach (Vector3I cell in room.Cells)
                {
                    Vector3D centre = thermals.Grid.GridIntegerToWorld(cell);
                    Vector3D delta = centre - eye;

                    if (!Wanted(ref delta, half.Length(), ref camera)) continue;

                    MatrixD box = gridMatrix;
                    box.Translation = eye + (delta * BandScale);

                    BoundingBoxD local = new BoundingBoxD(-half * BandScale, half * BandScale);

                    Box(ref box, ref local, ref fill, MySimpleObjectRasterizer.SolidAndWireframe, 0.04);
                }
            }
        }

        /// <summary>
        /// Colour for a lost compartment, shifted round the wheel by its index so two adjacent ones
        /// are distinguishable and each can be matched to the report's list.
        ///
        /// Restricted to reds and oranges, away from the blue-to-white ramp room air uses, so a
        /// lost compartment cannot be mistaken for a cold room.
        /// </summary>
        private static Color LostRoomColour(int index, bool ventSaysPressurised)
        {
            // A sixth of the wheel, red to yellow, so every one still reads as a fault.
            float hue = ((index * 0.61803399f) % 1f) * 0.13f;

            Color colour = ColorExtensions.HSVtoColor(
                new Vector3(hue, 1f, ventSaysPressurised ? 1f : 0.55f));

            colour.A = (byte)(LostRoomFillAlpha * 255f);
            return colour;
        }

        /// <summary>Alpha of a lost compartment. Fainter than air, since it marks a fault rather than a reading.</summary>
        private const float LostRoomFillAlpha = 0.25f;

        /// <summary>
        /// Whether this is a room the model found, left dry, and the game holds air in.
        ///
        /// False when the verdicts have not been scanned yet: the scan runs on its own cadence, and
        /// an unmeasured room must not be drawn as a fault.
        /// </summary>
        private static bool Disagrees(ThermalGrid thermals, int room)
        {
            IList<ThermalGrid.RoomVerdict> verdicts = thermals.RoomVerdicts;
            if (room < 0 || room >= verdicts.Count) return false;

            return verdicts[room].IsDisagreement;
        }

        /// <summary>
        /// Colour for a room with no air.
        ///
        /// Magenta where the game holds air and this model does not: a colour no other part of this
        /// view uses, and off the temperature ramp so it cannot read as cold air. Faint grey where
        /// the room is genuinely empty, which is the common case and not a fault.
        /// </summary>
        private static Color DryFill(bool disagrees)
        {
            if (!disagrees) return new Color(90, 90, 90, 25);

            Color colour = new Color(255, 0, 200);
            colour.A = (byte)(DisagreementFillAlpha * 255f);
            return colour;
        }

        /// <summary>Alpha of a room the game holds air in and this model does not.</summary>
        private const float DisagreementFillAlpha = 0.3f;

        /// <summary>Air node of one room, or null when the solver holds none for it.</summary>
        private static RoomAirNode AirOf(ThermalGrid thermals, int room)
        {
            IList<RoomAirNode> air = thermals.Simulation.RoomAir;
            for (int i = 0; i < air.Count; i++)
            {
                if (air[i].RoomIndex == room) return air[i];
            }
            return null;
        }

        /// <summary>
        /// Room air on the mod's usual heat ramp, over a narrower span than a block's.
        ///
        /// Room air spans a few tens of degrees where hull spans hundreds, so the block ramp would
        /// render every room the same shade.
        /// </summary>
        private static Color Fill(float kelvin, float min, float max)
        {
            if (max <= min) max = min + 1f;

            float span = max - min;
            Color colour = ColorExtensions.HSVtoColor(
                Tools.GetTemperatureColor(kelvin - min, span, span * 0.05f, span * 0.9f));

            colour.A = (byte)(RoomFillAlpha * 255f);
            return colour;
        }

        /// <summary>Alpha of room air. Higher than a block box because air is one layer deep.</summary>
        private const float RoomFillAlpha = 0.35f;

        /// <summary>
        /// A colour per room index. The hue advances by a large irrational fraction of the circle
        /// rather than by index, so consecutive rooms land far apart on the wheel and the sequence
        /// repeats as late as possible.
        /// </summary>
        private static Color RoomColour(int room, bool vented)
        {
            float hue = (room * 0.61803399f) % 1f;
            Color colour = ColorExtensions.HSVtoColor(new Vector3(hue, vented ? 0.3f : 0.9f, 1f));

            colour.A = (byte)(255f * (vented ? 0.35f : 0.8f));
            return colour;
        }

        /// <summary>
        /// The displayed value mapped through the same ramp the HUD and terminal use, so a colour
        /// means the same thing throughout the mod.
        /// </summary>
        private static Color Colour(ThermalNode node)
        {
            Vector3 hsv;
            switch (Current)
            {
                case Mode.SolarWatts:
                    hsv = Tools.GetTemperatureColor(Math.Abs(node.LastSolarWatts), 20000, 100, 5000);
                    break;
                case Mode.ExposedFaces:
                    hsv = Tools.GetTemperatureColor(node.TotalExposedFaces, 6, 0, 6);
                    break;
                case Mode.FrictionWatts:
                    hsv = Tools.GetTemperatureColor(Math.Abs(node.LastFrictionWatts), 20000, 100, 5000);
                    break;
                default:
                    hsv = Tools.GetTemperatureColor(node.Temperature);
                    break;
            }

            Color colour = ColorExtensions.HSVtoColor(hsv);
            colour.A = (byte)(FaceAlpha * 255f);
            return colour;
        }
    }
}
