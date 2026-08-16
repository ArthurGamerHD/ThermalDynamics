using System;
using System.Collections.Generic;
using Sandbox.Game;
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
    /// Thermal vision: the rendered world is blacked out and the frame is redrawn from billboards.
    ///
    /// A mod has no shader, no post-process and no frame buffer, so the ordinary view cannot be
    /// recoloured — but it can be replaced. Everything here is one mechanism: a black billboard at
    /// the back of the scene removes the world, and every body with a temperature is drawn in front
    /// of it as a billboard of its own.
    ///
    /// <para>
    /// **The projection is the trick.** Nothing is drawn at its real distance. Each billboard is
    /// scaled about the camera onto a shallow band a few hundred metres deep
    /// (<see cref="ScreenNear"/> to <see cref="ScreenNear"/> + <see cref="ScreenSpan"/>), keeping
    /// its direction and shrinking its size by the same factor. A perspective projection is
    /// invariant under scaling about the eye, so the image on screen is identical — but the mod now
    /// owns the depth order rather than borrowing it. Near things land near, far things land far,
    /// the renderer sorts them among themselves, and the blackout, sitting behind the whole band,
    /// can never come out in front of what it is supposed to be hiding.
    /// </para>
    ///
    /// <para>
    /// Because the world's own geometry is not drawn at all, nothing of it can occlude the thermal
    /// image, and nothing of it can show through. What is visible is exactly what this file draws:
    /// the ground, the sun, planets, asteroids, ship surfaces, characters, and heat sources other
    /// mods have registered.
    /// </para>
    /// </summary>
    public static class ThermalVision
    {
        private static readonly MyStringId Flat = MyStringId.GetOrCompute("GaugeThermalFlatThrough");
        private static readonly MyStringId Disc = MyStringId.GetOrCompute("GaugeThermalDisc");
        private static readonly MyStringId Glow = MyStringId.GetOrCompute("GaugeThermalGlow");

        /// <summary>Nearest the projection band comes to the camera, m.</summary>
        private const float ScreenNear = 2f;

        /// <summary>Depth of the projection band, m. Everything drawn lands inside it.</summary>
        private const float ScreenSpan = 500f;

        /// <summary>
        /// Distance at which a body lands halfway through the band. Larger values give near objects
        /// more of the band, which is where separation is worth having.
        /// </summary>
        private const double DepthSoftness = 300.0;

        /// <summary>The blackout sits behind the band, so it is drawn before everything else.</summary>
        private const float BlackoutDistance = ScreenNear + ScreenSpan + 200f;

        private const float SunAngularRadius = 0.0047f;

        /// <summary>
        /// One blend for the whole image. Every billboard is in the same band and the same pass, so
        /// the renderer's own back-to-front sort is what layers them — which is the entire reason
        /// for projecting into a band rather than drawing at real distances.
        /// </summary>
        private const BlendTypeEnum Layer = BlendTypeEnum.PostPP;

        /// <summary>Toggled by the player, independently of the config default.</summary>
        public static bool Active;

        private static readonly List<ThermalGrid> Targets = new List<ThermalGrid>();
        private static readonly List<IMyPlayer> Players = new List<IMyPlayer>();
        private static readonly List<IMyVoxelBase> Voxels = new List<IMyVoxelBase>();
        private static int voxelRefresh;

        public static void Toggle()
        {
            Active = !Active;
            if (!Active) ThermalTerrain.Clear();
        }

        public static void Draw()
        {
            if (!Active) return;

            Settings settings = Settings.Instance;
            if (settings == null || !settings.EnableThermalVision) return;
            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated) return;
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Camera == null) return;

            IMyCamera camera = MyAPIGateway.Session.Camera;
            MatrixD view = camera.WorldMatrix;
            Vector3D eye = view.Translation;

            Blackout(ref view, ref eye, camera, settings);

            DrawTerrain(ref view, ref eye, settings);
            DrawVoxelBodies(ref view, ref eye, settings);
            DrawSun(ref view, ref eye, settings);
            DrawPlanets(ref view, ref eye, settings);
            DrawGrids(ref view, ref eye, settings);
            DrawCharacters(ref view, ref eye, settings);
            DrawHeatSources(ref view, ref eye, settings);
        }

        // ---- projection --------------------------------------------------------------------

        /// <summary>
        /// Where in the band a body at this true distance belongs. Monotonic, so the band preserves
        /// the real depth order, and bounded, so nothing lands behind the blackout.
        /// </summary>
        private static float BandDepth(double distance)
        {
            if (distance <= 0.0) return ScreenNear;
            return ScreenNear + (float)(ScreenSpan * (distance / (distance + DepthSoftness)));
        }

        /// <summary>
        /// Scale factor that moves a body onto the band. Multiply both its offset from the camera
        /// and its size by this, and its projected image does not change.
        /// </summary>
        private static float BandScale(double distance)
        {
            if (distance <= 0.0) return 1f;
            return (float)(BandDepth(distance) / distance);
        }

        /// <summary>
        /// Projects a camera-facing body onto the band and draws it.
        /// </summary>
        private static void Emit(
            MyStringId material, Color colour, ref MatrixD view, ref Vector3D eye,
            Vector3D position, float radius)
        {
            Vector3D offset = position - eye;
            double distance = offset.Length();
            if (distance <= 0.0) return;

            float scale = BandScale(distance);

            MyTransparentGeometry.AddBillboardOriented(
                material, colour, eye + (offset * scale),
                view.Left, view.Up, radius * scale, Layer);
        }

        /// <summary>
        /// Projects an oriented quad onto the band. Its axes keep their world directions, so a
        /// surface at an angle stays at that angle: only the distance changes.
        /// </summary>
        private static void EmitOriented(
            MyStringId material, Color colour, ref Vector3D eye,
            Vector3D position, Vector3 left, Vector3 up, float halfWidth, float halfHeight)
        {
            Vector3D offset = position - eye;
            double distance = offset.Length();
            if (distance <= 0.0) return;

            float scale = BandScale(distance);

            MyTransparentGeometry.AddBillboardOriented(
                material, colour, eye + (offset * scale),
                left, up, halfWidth * scale, halfHeight * scale,
                Vector2.Zero, Layer);
        }

        // ---- blackout ----------------------------------------------------------------------

        /// <summary>
        /// Removes the rendered world.
        ///
        /// Drawn at the back of the band rather than in front of the camera, so it is the first
        /// billboard the renderer reaches and everything thermal lands on top of it. Sized to fill
        /// the field of view at that distance.
        /// </summary>
        private static void Blackout(ref MatrixD view, ref Vector3D eye, IMyCamera camera, Settings settings)
        {
            float dimming = settings.ThermalVisionDimming;
            if (dimming <= 0f) return;
            if (dimming > 1f) dimming = 1f;

            float halfHeight = (float)(BlackoutDistance * Math.Tan(camera.FovWithZoom * 0.5f));

            Vector2 viewport = camera.ViewportSize;
            float aspect = viewport.Y > 0f ? viewport.X / viewport.Y : 1.78f;

            MyTransparentGeometry.AddBillboardOriented(
                Flat,
                new Color(0f, 0f, 0f, dimming),
                eye + (view.Forward * BlackoutDistance),
                view.Left,
                view.Up,
                halfHeight * aspect * 1.1f,
                halfHeight * 1.1f,
                Vector2.Zero,
                Layer);
        }

        // ---- ground ------------------------------------------------------------------------

        private static void DrawTerrain(ref MatrixD view, ref Vector3D eye, Settings settings)
        {
            PlanetManager.Planet planet = PlanetManager.GetClosestPlanet(eye);
            PlanetDefinition definition = planet == null ? null : planet.Definition();

            float day = definition == null ? 300f : definition.DayTemperature;
            float night = definition == null ? 280f : definition.NightTemperature;

            ThermalTerrain.Update(eye, day, night);

            IList<ThermalTerrain.Patch> patches = ThermalTerrain.Current;
            if (patches.Count == 0) return;

            float min = settings.ThermalVisionMinKelvin;
            float max = settings.ThermalVisionMaxKelvin;
            bool greyscale = settings.ThermalVisionGreyscale;
            float intensity = settings.ThermalVisionIntensity;

            for (int i = 0; i < patches.Count; i++)
            {
                ThermalTerrain.Patch patch = patches[i];
                if (Vector3D.Dot(patch.Position - eye, view.Forward) <= 0) continue;

                Color colour = ThermalPalette.Colour(patch.Temperature, min, max, greyscale, intensity);

                // Laid flat against the ground rather than turned to the camera, so slope reads as
                // slope. Two axes in the surface plane are all that needs.
                Vector3 normal = patch.Normal;
                Vector3 tangent = Vector3.Normalize(Vector3.Cross(
                    normal, Vector3.Up.Equals(normal) ? Vector3.Right : Vector3.Up));
                Vector3 bitangent = Vector3.Cross(normal, tangent);

                EmitOriented(Flat, colour, ref eye, patch.Position, tangent, bitangent,
                    patch.Radius, patch.Radius);
            }
        }

        /// <summary>
        /// Asteroids and other voxel bodies, as discs at ambient.
        ///
        /// A voxel body has no shape a mod can draw and no temperature the mod simulates, but a hole
        /// in a thermal view reads as open space, which an asteroid is not. The list is rebuilt every
        /// few seconds: enumerating voxel maps is not cheap and asteroids do not move.
        /// </summary>
        private static void DrawVoxelBodies(ref MatrixD view, ref Vector3D eye, Settings settings)
        {
            if (voxelRefresh-- <= 0)
            {
                voxelRefresh = 300;
                Voxels.Clear();
                MyAPIGateway.Session.VoxelMaps.GetInstances(Voxels, voxel => !(voxel is MyPlanet));
            }

            double rangeSquared = settings.ThermalVisionRange * (double)settings.ThermalVisionRange;

            Color colour = ThermalPalette.Colour(
                settings.VacuumTemperature + 40f,
                settings.ThermalVisionMinKelvin, settings.ThermalVisionMaxKelvin,
                settings.ThermalVisionGreyscale, settings.ThermalVisionIntensity * 0.6f);

            for (int i = 0; i < Voxels.Count; i++)
            {
                IMyVoxelBase voxel = Voxels[i];
                if (voxel == null || voxel.Closed) continue;

                BoundingBoxD box = voxel.PositionComp.WorldAABB;
                if (Vector3D.DistanceSquared(box.Center, eye) > rangeSquared) continue;
                if (Vector3D.Dot(box.Center - eye, view.Forward) <= 0) continue;

                Emit(Disc, colour, ref view, ref eye, box.Center,
                    (float)box.HalfExtents.Length() * 0.7f);
            }
        }

        // ---- sky ---------------------------------------------------------------------------

        private static void DrawSun(ref MatrixD view, ref Vector3D eye, Settings settings)
        {
            if (!settings.EnableSolarHeat) return;

            Vector3D direction = MyVisualScriptLogicProvider.GetSunDirection();
            if (direction.LengthSquared() <= 0) return;
            if (Vector3D.Dot(direction, view.Forward) <= 0) return;

            Color hot = ThermalPalette.Colour(
                float.MaxValue, settings.ThermalVisionMinKelvin, settings.ThermalVisionMaxKelvin,
                settings.ThermalVisionGreyscale, 1f);

            // The sun is effectively at infinity, so it is placed at the back of the band directly
            // and sized from its angular radius. Everything else in the sky is in front of it.
            float depth = ScreenNear + ScreenSpan;
            Vector3D position = eye + (direction * depth);
            float radius = depth * SunAngularRadius;

            MyTransparentGeometry.AddBillboardOriented(
                Glow, new Color(hot.R, hot.G, hot.B, (byte)90), position,
                view.Left, view.Up, radius * 14f, Layer);

            MyTransparentGeometry.AddBillboardOriented(
                Disc, hot, position, view.Left, view.Up, radius, Layer);
        }

        private static void DrawPlanets(ref MatrixD view, ref Vector3D eye, Settings settings)
        {
            if (!settings.EnablePlanets) return;

            PlanetManager.Planet planet = PlanetManager.GetClosestPlanet(eye);
            if (planet == null || planet.Entity == null) return;

            double radius = planet.Entity.AverageRadius;
            if (Vector3D.Distance(eye, planet.Position) < radius + 2000.0) return;
            if (Vector3D.Dot(planet.Position - eye, view.Forward) <= 0) return;

            PlanetDefinition definition = planet.Definition();
            float temperature = definition == null
                ? 280f
                : (definition.DayTemperature + definition.NightTemperature) * 0.5f;

            Color colour = ThermalPalette.Colour(
                temperature, settings.ThermalVisionMinKelvin, settings.ThermalVisionMaxKelvin,
                settings.ThermalVisionGreyscale, settings.ThermalVisionIntensity);

            Emit(Disc, colour, ref view, ref eye, planet.Position, (float)radius);
        }

        // ---- grids -------------------------------------------------------------------------

        private static void DrawGrids(ref MatrixD view, ref Vector3D eye, Settings settings)
        {
            CollectGrids(ref eye, settings.ThermalVisionRange);

            double detailSquared = settings.ThermalVisionDetailRange
                * (double)settings.ThermalVisionDetailRange;

            for (int i = 0; i < Targets.Count; i++)
            {
                ThermalGrid thermals = Targets[i];
                BoundingBoxD box = thermals.Grid.PositionComp.WorldAABB;

                if (box.DistanceSquared(eye) > detailSquared)
                {
                    DrawGridAsBody(thermals, ref view, ref eye, settings, box);
                    continue;
                }

                DrawGridSurface(thermals, ref view, ref eye, settings);
            }

            Targets.Clear();
        }

        /// <summary>
        /// A grid's outer skin, one quad per exposed block face.
        ///
        /// The simulation already knows which faces are open to the outside — the same figure
        /// radiation is computed from — so a block buried in the hull has no exposed face and is
        /// never drawn. There is no x-ray and no interior bleeding through, and the skin costs only
        /// the faces that can be seen. A face turned away from the camera is the far side of the
        /// ship and is dropped too.
        /// </summary>
        private static void DrawGridSurface(
            ThermalGrid thermals, ref MatrixD view, ref Vector3D eye, Settings settings)
        {
            float gridSize = thermals.Grid.GridSize;
            MatrixD gridMatrix = thermals.Grid.WorldMatrix;

            float min = settings.ThermalVisionMinKelvin;
            float max = settings.ThermalVisionMaxKelvin;
            bool greyscale = settings.ThermalVisionGreyscale;
            float intensity = settings.ThermalVisionIntensity;

            double rangeSquared = settings.ThermalVisionRange * (double)settings.ThermalVisionRange;

            foreach (ThermalBlock bound in thermals.Blocks)
            {
                ThermalNode node = bound.Node;
                if (node == null || node.TotalExposedFaces == 0) continue;

                Vector3D centre;
                bound.Block.ComputeWorldCenter(out centre);
                if (Vector3D.DistanceSquared(centre, eye) > rangeSquared) continue;

                Color colour = ThermalPalette.Colour(node.Temperature, min, max, greyscale, intensity);

                Vector3 half = ((Vector3)(bound.Block.Max - bound.Block.Min + Vector3I.One))
                    * (gridSize * 0.5f);

                for (int face = 0; face < Face.Count; face++)
                {
                    if (node.ExposedFaces[face] == 0) continue;

                    Vector3 localNormal = Face.Normals[face];
                    Vector3D normal = Vector3D.TransformNormal(localNormal, gridMatrix);

                    Vector3D position = centre + (normal * Extent(ref half, ref localNormal));
                    if (Vector3D.Dot(normal, position - eye) >= 0) continue;

                    Vector3 localLeft, localUp;
                    Tangents(face, out localLeft, out localUp);

                    Vector3 left = (Vector3)Vector3D.TransformNormal(localLeft, gridMatrix);
                    Vector3 up = (Vector3)Vector3D.TransformNormal(localUp, gridMatrix);

                    EmitOriented(Flat, colour, ref eye, position, left, up,
                        Extent(ref half, ref localLeft), Extent(ref half, ref localUp));
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

        private static void DrawGridAsBody(
            ThermalGrid thermals, ref MatrixD view, ref Vector3D eye, Settings settings, BoundingBoxD box)
        {
            if (Vector3D.Dot(box.Center - eye, view.Forward) <= 0) return;

            ThermalNode hottest = thermals.HottestNode;
            if (hottest == null) return;

            Color colour = ThermalPalette.Colour(
                hottest.Temperature, settings.ThermalVisionMinKelvin, settings.ThermalVisionMaxKelvin,
                settings.ThermalVisionGreyscale, settings.ThermalVisionIntensity);

            Emit(Disc, colour, ref view, ref eye, box.Center,
                (float)box.HalfExtents.Length() * 0.6f);
        }

        private static void CollectGrids(ref Vector3D eye, float range)
        {
            Targets.Clear();
            double rangeSquared = range * (double)range;

            IList<ThermalGrid> live = ThermalGrid.LiveGrids;
            for (int i = 0; i < live.Count; i++)
            {
                ThermalGrid thermals = live[i];
                if (thermals.Simulation == null || thermals.Grid == null) continue;
                if (thermals.Grid.Closed || thermals.Grid.PositionComp == null) continue;
                if (thermals.Grid.PositionComp.WorldAABB.DistanceSquared(eye) > rangeSquared) continue;

                Targets.Add(thermals);
            }
        }

        // ---- bodies ------------------------------------------------------------------------

        private static void DrawCharacters(ref MatrixD view, ref Vector3D eye, Settings settings)
        {
            double rangeSquared = settings.ThermalVisionRange * (double)settings.ThermalVisionRange;

            Color colour = ThermalPalette.Colour(
                settings.ThermalVisionBodyTemperature,
                settings.ThermalVisionMinKelvin, settings.ThermalVisionMaxKelvin,
                settings.ThermalVisionGreyscale, settings.ThermalVisionIntensity);

            Players.Clear();
            MyAPIGateway.Players.GetPlayers(Players);

            for (int i = 0; i < Players.Count; i++)
            {
                IMyCharacter character = Players[i].Character;
                if (character == null || character.MarkedForClose) continue;

                Vector3D position = character.WorldAABB.Center;
                Vector3D delta = position - eye;

                double distanceSquared = delta.LengthSquared();
                if (distanceSquared < 1.0 || distanceSquared > rangeSquared) continue;
                if (Vector3D.Dot(delta, view.Forward) <= 0) continue;

                Emit(Disc, colour, ref view, ref eye, position,
                    (float)character.WorldAABB.HalfExtents.Length() * 0.55f);
            }

            Players.Clear();
        }

        private static void DrawHeatSources(ref MatrixD view, ref Vector3D eye, Settings settings)
        {
            if (!settings.EnableHeatSources) return;

            IList<ThermalHeatSources.HeatSource> sources = ThermalHeatSources.All;
            if (sources.Count == 0) return;

            double rangeSquared = settings.ThermalVisionRange * (double)settings.ThermalVisionRange;

            Color hot = ThermalPalette.Colour(
                float.MaxValue, settings.ThermalVisionMinKelvin, settings.ThermalVisionMaxKelvin,
                settings.ThermalVisionGreyscale, settings.ThermalVisionIntensity);

            for (int i = 0; i < sources.Count; i++)
            {
                ThermalHeatSources.HeatSource source = sources[i];
                if (source.Watts <= 0f) continue;

                Vector3D position = source.WorldPosition;
                Vector3D delta = position - eye;

                if (delta.LengthSquared() > rangeSquared) continue;
                if (Vector3D.Dot(delta, view.Forward) <= 0) continue;

                float radius = (float)Math.Sqrt(source.Watts) * 0.002f;
                if (radius < 0.5f) radius = 0.5f;

                Emit(Glow, new Color(hot.R, hot.G, hot.B, (byte)120), ref view, ref eye,
                    position, radius * 5f);
                Emit(Disc, hot, ref view, ref eye, position, radius);
            }
        }
    }
}
