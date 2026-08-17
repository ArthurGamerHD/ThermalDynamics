using System;
using System.Collections.Generic;
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
    /// The block debug overlay: every block of the grid in front of you drawn as a coloured box,
    /// seen through the hull. Two views draw something else, because their subject is not the
    /// block: the solar view draws the skin, one quad per exposed face, and the room view draws the
    /// mapped air, a box per cell.
    ///
    /// This replaces the block-colouring debug modes. Those called <c>ColorBlocks</c>, which is a
    /// real, replicated, permanent change to the ship's paint — they showed heat by destroying the
    /// player's colour scheme, on the server, for everyone, and switching them off did not undo it.
    /// This draws transparent boxes instead: client side, per frame, nothing written to the grid.
    ///
    /// The x-ray is the point. A debugger wants the reactor buried in the middle of the ship, not
    /// the hull plate in front of it, so every block is drawn whether or not anything is between it
    /// and the camera. That is achieved by scaling each box about the eye onto a shallow band just
    /// in front of the near plane: a perspective projection is invariant under scaling about the
    /// eye, so the image on screen is exactly where the block is, but nothing in the scene can be
    /// in front of it. Relative depth order between boxes survives, because they are all scaled by
    /// the same factor.
    ///
    /// The same trick made thermal vision unreadable — a ship becomes a pile of overlapping
    /// translucent boxes, which is the opposite of what a camera shows. For reading state off a
    /// grid it is exactly right.
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
        /// Number of views, including off. Everything that has to know the range — the cycle, the
        /// config clamp, the menu's dropdown — takes it from here rather than repeating the count.
        /// </summary>
        public const int ModeCount = (int)Mode.Rooms + 1;

        /// <summary>Cycled by the keybind, seeded from <see cref="Settings.DebugBlockOverlay"/>.</summary>
        public static Mode Current;

        private static readonly MyStringId FaceMaterial = MyStringId.GetOrCompute("Square");
        private static readonly MyStringId LineMaterial = MyStringId.GetOrCompute("Square");

        /// <summary>Reused every frame so looking at a ship allocates nothing.</summary>
        private static readonly List<ThermalGrid> Targets = new List<ThermalGrid>();

        /// <summary>
        /// The grid the readout describes: the one being looked at, or the one being controlled
        /// when the camera is pointed at nothing. Null when the overlay is off or has no target.
        /// </summary>
        public static ThermalGrid Focus { get; private set; }

        /// <summary>Metres out to which a grid is picked up by the aim ray.</summary>
        private const double PickRange = 300;

        /// <summary>
        /// How far the band the boxes are scaled onto sits from the eye. Small enough that nothing
        /// in the scene can be in front of it, large enough to stay clear of the near plane.
        /// </summary>
        private const double BandScale = 0.02;

        /// <summary>Alpha of a box face. Low, because a hull is many boxes deep.</summary>
        private const float FaceAlpha = 0.22f;

        /// <summary>
        /// Alpha of a drawn surface. Higher than a box: the solar view draws only the skin, one
        /// quad deep, so nothing is stacked behind it to see through.
        /// </summary>
        private const float SurfaceAlpha = 0.75f;

        /// <summary>
        /// True when the selected view reads a per-mechanism watt figure. Those are only written
        /// when someone is going to read them, so the overlay has to say that it is that someone —
        /// otherwise the solar and friction views draw a grid that is uniformly zero, which reads
        /// as "no solar heating" rather than as "not measured".
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

            for (int i = 0; i < Targets.Count; i++)
            {
                DrawGrid(Targets[i], ref camera, ref eye);
            }

            Targets.Clear();
        }

        /// <summary>
        /// The grids worth drawing: the one being controlled, and the one being looked at.
        ///
        /// Deliberately not every grid in range. This is a tool for reading the ship in front of
        /// you, and a box per block of every wreck in a battle is how an overlay becomes a frame
        /// rate problem. What is drawn of the two it does pick is all of them: a debug view that
        /// stops at some radius invites the reading that the far end of the ship is cold.
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

                // Behind the camera: scaling about the eye would fold it in front of the player.
                if (Vector3D.Dot(delta, camera.Forward) <= 0) continue;

                Vector3D half = (Vector3D)(bound.Block.Max - bound.Block.Min + Vector3I.One)
                    * (gridSize * 0.5);

                MatrixD box = gridMatrix;
                box.Translation = eye + (delta * BandScale);

                BoundingBoxD local = new BoundingBoxD(-half * BandScale, half * BandScale);
                Color colour = Colour(node);

                MySimpleObjectDraw.DrawTransparentBox(
                    ref box,
                    ref local,
                    ref colour,
                    MySimpleObjectRasterizer.SolidAndWireframe,
                    1,
                    (float)(0.02 * BandScale),
                    FaceMaterial,
                    LineMaterial,
                    false,
                    -1,
                    BlendTypeEnum.PostPP);
            }
        }

        /// <summary>
        /// Sunlight, drawn on the surfaces that take it.
        ///
        /// Solar heating is a property of a face, not of a block: a hull plate with one side to the
        /// sun takes light on that side, and the block's total says nothing about which side or how
        /// squarely. So this view abandons the box and draws the skin — one quad per exposed face —
        /// shaded by that face's own irradiance, which is the sun's energy times how square the
        /// face is to it. A wall gone dark because it turned away is then plainly different from a
        /// wall gone dark because the ship is in shadow, and both are visible at a glance.
        ///
        /// Faces turned away from the camera are dropped: they are the far side of the ship, and
        /// keeping them only stacks the near skin with colour from a surface nobody is looking at.
        /// </summary>
        private static void DrawSolarSurfaces(ThermalGrid thermals, ref MatrixD camera, ref Vector3D eye)
        {
            EnvironmentState state = thermals.LastState;
            ThermalSolver solver = thermals.Simulation.Solver;
            MatrixD gridMatrix = thermals.Grid.WorldMatrix;
            float gridSize = thermals.Grid.GridSize;

            // The same three things the solver asks before it computes a watt.
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
                if (Vector3D.Dot(delta, camera.Forward) <= 0) continue;

                Vector3 half = ((Vector3)(bound.Block.Max - bound.Block.Min + Vector3I.One))
                    * (gridSize * 0.5f);

                for (int face = 0; face < Face.Count; face++)
                {
                    if (node.ExposedFaces[face] == 0) continue;

                    Vector3 localNormal = Face.Normals[face];
                    Vector3D normal = Vector3D.TransformNormal(localNormal, gridMatrix);

                    Vector3D position = centre + (normal * Extent(ref half, ref localNormal));
                    if (Vector3D.Dot(normal, position - eye) >= 0) continue;

                    float dot = Vector3.Dot(localNormal, sun);
                    float irradiance = lit && dot > 0f ? state.SolarEnergy * dot : 0f;

                    // What the solver believes, not a second opinion: a face the grid shadows must
                    // look shadowed, or the picture argues with the temperatures. Per face, because
                    // that is how the model holds it — the far layer of a wall is dark toward the
                    // sun and lit on the flank, and one number per block cannot say that.
                    irradiance *= solver.SunLitFraction(node.Index, face);

                    // W/m2 rather than watts: this is what the surface is standing in, which is
                    // the figure that belongs to a face. The panel reports the watts.
                    Color colour = ColorExtensions.HSVtoColor(
                        Tools.GetTemperatureColor(irradiance, 1400f, 1f, 1000f));
                    colour.A = (byte)(SurfaceAlpha * 255f);

                    Vector3 localLeft, localUp;
                    Tangents(face, out localLeft, out localUp);

                    Vector3 left = (Vector3)Vector3D.TransformNormal(localLeft, gridMatrix);
                    Vector3 up = (Vector3)Vector3D.TransformNormal(localUp, gridMatrix);

                    // Scaled onto the same band as every other view, so the skin cannot z-fight
                    // with the hull it is drawn over.
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
        /// The room map, drawn as the air itself: one box per cell the mapper put in a room,
        /// coloured by which room it is.
        ///
        /// Rooms are a property of cells rather than of blocks, so this is the one view that does
        /// not iterate blocks. It answers the question the room map exists to answer and that no
        /// readout can — whether two compartments the player thinks are separate came back as one
        /// room, and where the leak is when a room the player thinks is sealed reads as vented.
        /// Vented rooms are drawn faint, so a compartment losing its seal stands out from one
        /// holding it.
        /// </summary>
        private static void DrawRooms(ThermalGrid thermals, ref MatrixD camera, ref Vector3D eye)
        {
            RoomMap map = thermals.Simulation.Rooms.Map;
            if (map == null || map.IsEmpty) return;

            MatrixD gridMatrix = thermals.Grid.WorldMatrix;
            float gridSize = thermals.Grid.GridSize;

            // Shy of the full cell, so the boundary between two cells stays a visible seam rather
            // than a single unbroken block of colour.
            Vector3D half = new Vector3D(gridSize * 0.45);

            IList<HashSet<Vector3I>> rooms = map.Rooms;

            for (int room = 0; room < rooms.Count; room++)
            {
                Color colour = RoomColour(room, map.IsVented(room));

                foreach (Vector3I cell in rooms[room])
                {
                    Vector3D centre = thermals.Grid.GridIntegerToWorld(cell);
                    Vector3D delta = centre - eye;
                    if (Vector3D.Dot(delta, camera.Forward) <= 0) continue;

                    MatrixD box = gridMatrix;
                    box.Translation = eye + (delta * BandScale);

                    BoundingBoxD local = new BoundingBoxD(-half * BandScale, half * BandScale);

                    MySimpleObjectDraw.DrawTransparentBox(
                        ref box,
                        ref local,
                        ref colour,
                        MySimpleObjectRasterizer.SolidAndWireframe,
                        1,
                        (float)(0.02 * BandScale),
                        FaceMaterial,
                        LineMaterial,
                        false,
                        -1,
                        BlendTypeEnum.PostPP);
                }
            }
        }

        /// <summary>
        /// A colour per room index. Neighbouring indices have to be told apart at a glance, so the
        /// hue is stepped by a large irrational-ish fraction of the circle rather than by index:
        /// consecutive rooms land far apart on the wheel and the sequence does not repeat until it
        /// has to.
        /// </summary>
        private static Color RoomColour(int room, bool vented)
        {
            float hue = (room * 0.61803399f) % 1f;
            Color colour = ColorExtensions.HSVtoColor(new Vector3(hue, vented ? 0.35f : 1f, 0.6f));

            colour.A = (byte)(FaceAlpha * 255f * (vented ? 0.45f : 1f));
            return colour;
        }

        /// <summary>
        /// The value being shown, mapped through the same ramp the HUD and the terminal use, so a
        /// colour means the same thing everywhere in the mod. The ranges are the ones the old
        /// block-colouring modes used.
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
