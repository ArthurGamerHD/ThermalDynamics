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
    /// Thermal vision: a heat overlay drawn over the grid the player is looking at.
    ///
    /// This is what the block-colouring debug mode should always have been. That mode calls
    /// <c>ColorBlocks</c>, which is a real, replicated, permanent change to the ship's paint — it
    /// shows heat by destroying the player's colour scheme, on the server, for everyone. This
    /// draws billboards instead: client side, per frame, nothing written to the grid, and it
    /// switches off cleanly because there is nothing to undo.
    ///
    /// Two palettes, because they answer different questions. The heat ramp is easier to read a
    /// gradient in; greyscale is closer to what a real thermal camera shows and does not collide
    /// with the game's own colours.
    /// </summary>
    public static class ThermalVision
    {
        private static readonly MyStringId Material = MyStringId.GetOrCompute("GaugeThermalTexture");

        /// <summary>Reused every frame so aiming at a ship allocates nothing.</summary>
        private static readonly List<ThermalGrid> Targets = new List<ThermalGrid>();

        /// <summary>Toggled by the player, independently of the config default.</summary>
        public static bool Active;

        /// <summary>Temperature the ramp treats as cold, K.</summary>
        public const float ColdKelvin = 250f;

        /// <summary>Temperature the ramp treats as fully hot, K.</summary>
        public const float HotKelvin = 900f;

        public static void Toggle()
        {
            Active = !Active;
        }

        public static void Draw()
        {
            if (!Active) return;

            Settings settings = Settings.Instance;
            if (settings == null || !settings.EnableThermalVision) return;
            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated) return;
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Camera == null) return;

            MatrixD camera = MyAPIGateway.Session.Camera.WorldMatrix;
            Vector3D eye = camera.Translation;
            double range = settings.ThermalVisionRange;

            CollectTargets(ref eye, range);

            for (int i = 0; i < Targets.Count; i++)
            {
                DrawGrid(Targets[i], ref camera, ref eye, range, settings.ThermalVisionGreyscale);
            }

            Targets.Clear();
        }

        /// <summary>
        /// The grids worth drawing: the one the player is on, and the one being looked at.
        ///
        /// Deliberately not every grid in range. Thermal vision is a tool for reading the ship in
        /// front of you, and drawing a billboard per block of every wreck in a battle is how an
        /// overlay becomes a frame rate problem.
        /// </summary>
        private static void CollectTargets(ref Vector3D eye, double range)
        {
            Targets.Clear();

            IMyEntity controlled = MyAPIGateway.Session.Player == null
                ? null
                : MyAPIGateway.Session.Player.Controller.ControlledEntity as IMyEntity;

            IMyCubeBlock seat = controlled as IMyCubeBlock;
            if (seat != null) Add(seat.CubeGrid as MyCubeGrid);

            Vector3D end = eye + (MyAPIGateway.Session.Camera.WorldMatrix.Forward * range);

            IHitInfo hit;
            MyAPIGateway.Physics.CastRay(eye, end, out hit);
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

        private static void DrawGrid(
            ThermalGrid thermals, ref MatrixD camera, ref Vector3D eye, double range, bool greyscale)
        {
            double rangeSquared = range * range;
            float halfSize = thermals.Grid.GridSizeHalf;

            foreach (ThermalBlock bound in thermals.Blocks)
            {
                ThermalNode node = bound.Node;
                if (node == null) continue;

                Vector3D position;
                bound.Block.ComputeWorldCenter(out position);

                Vector3D delta = position - eye;
                if (delta.LengthSquared() > rangeSquared) continue;

                // Behind the camera: the billboard would be drawn inside out.
                if (Vector3D.Dot(delta, camera.Forward) <= 0) continue;

                Color color = Colour(node.Temperature, greyscale);

                Vector3I size = bound.Block.Max - bound.Block.Min + Vector3I.One;
                float scale = halfSize * Math.Max(size.X, Math.Max(size.Y, size.Z));

                MyTransparentGeometry.AddBillboardOriented(
                    Material,
                    color,
                    position,
                    camera.Left,
                    camera.Up,
                    scale,
                    BlendTypeEnum.PostPP);
            }
        }

        /// <summary>
        /// Colour for a temperature.
        ///
        /// Greyscale is a straight linear ramp from black to white, which is what a thermal camera
        /// shows and what makes small differences legible. The heat ramp reuses the same one the
        /// HUD and the extinguisher use, so a temperature reads the same everywhere in the mod.
        /// </summary>
        public static Color Colour(float kelvin, bool greyscale)
        {
            if (!greyscale)
            {
                return ColorExtensions.HSVtoColor(Tools.GetTemperatureColor(kelvin));
            }

            float t = (kelvin - ColdKelvin) / (HotKelvin - ColdKelvin);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            byte level = (byte)(t * 255f);
            return new Color(level, level, level, (byte)200);
        }
    }
}
