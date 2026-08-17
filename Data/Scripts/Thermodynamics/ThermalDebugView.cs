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
    /// seen through the hull.
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
        }

        /// <summary>Cycled by the keybind, seeded from <see cref="Settings.DebugBlockOverlay"/>.</summary>
        public static Mode Current;

        private static readonly MyStringId Face = MyStringId.GetOrCompute("Square");
        private static readonly MyStringId Line = MyStringId.GetOrCompute("Square");

        /// <summary>Reused every frame so looking at a ship allocates nothing.</summary>
        private static readonly List<ThermalGrid> Targets = new List<ThermalGrid>();

        /// <summary>Metres out to which a grid is picked up by the aim ray.</summary>
        private const double PickRange = 300;

        /// <summary>
        /// Metres out to which an individual block is drawn. A capital ship is thousands of blocks
        /// and every one of them is a box with six faces and twelve edges, so the pass is bounded
        /// by what is close enough to read rather than by what is in front of the camera.
        /// </summary>
        private const double BlockRange = 120;

        /// <summary>
        /// How far the band the boxes are scaled onto sits from the eye. Small enough that nothing
        /// in the scene can be in front of it, large enough to stay clear of the near plane.
        /// </summary>
        private const double BandScale = 0.02;

        /// <summary>Alpha of a box face. Low, because a hull is many boxes deep.</summary>
        private const float FaceAlpha = 0.22f;

        public static void Cycle()
        {
            Current = Current == Mode.FrictionWatts ? Mode.Off : (Mode)((int)Current + 1);
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
                default: return "off";
            }
        }

        public static void Draw()
        {
            if (Current == Mode.Off) return;
            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated) return;
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Camera == null) return;

            MatrixD camera = MyAPIGateway.Session.Camera.WorldMatrix;
            Vector3D eye = camera.Translation;

            CollectTargets(ref camera, ref eye);

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
        /// rate problem.
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
            MatrixD gridMatrix = thermals.Grid.WorldMatrix;
            float gridSize = thermals.Grid.GridSize;

            foreach (ThermalBlock bound in thermals.Blocks)
            {
                ThermalNode node = bound.Node;
                if (node == null) continue;

                Vector3D centre;
                bound.Block.ComputeWorldCenter(out centre);

                Vector3D delta = centre - eye;
                if (delta.LengthSquared() > BlockRange * BlockRange) continue;

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
                    Face,
                    Line,
                    false,
                    -1,
                    BlendTypeEnum.PostPP);
            }
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
