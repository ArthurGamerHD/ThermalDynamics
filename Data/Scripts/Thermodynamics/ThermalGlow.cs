using System;
using System.Collections.Generic;
using Sandbox.Game.Lights;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using Thermodynamics.Presentation;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using static VRageRender.MyBillboard;

namespace Thermodynamics
{
    /// <summary>Soft additive heat patches and bounded nearby heat lights; see thermal-glow.md.</summary>
    public static class ThermalGlow
    {
        private static readonly MyStringId GlowMaterial = MyStringId.GetOrCompute("GaugeHeatGlow");

        /// <summary>Quads submitted during the most recent draw.</summary>
        public static int LastQuads { get; private set; }

        /// <summary>
        /// Brightest a grid's heat light gets, at the moment its hottest block reaches its rating.
        /// Chosen against the game's own interior light rather than derived: a block about to fail
        /// should light the compartment it is in, not wash it out.
        /// </summary>
        private const float LightIntensity = 2.5f;

        /// <summary>
        /// Metres the light reaches past the glowing blocks themselves, and the least it reaches at
        /// all. A single cooking block still has to light the corridor it sits in.
        /// </summary>
        private const float LightReach = 6f;

        private const float LightRangeCap = 120f;

        /// <summary>
        /// One light per grid, held for as long as that grid has anything glowing.
        ///
        /// **Per grid rather than per block, deliberately.** A hull losing five hundred blocks would
        /// otherwise ask the renderer for five hundred dynamic lights. The block glow above already
        /// carries which block is hot; all the light has to do is let it fall on what is around it,
        /// and that is a property of the region rather than of the cube.
        /// </summary>
        private static readonly Dictionary<long, MyLight> Lights = new Dictionary<long, MyLight>();

        private static readonly List<long> Extinguished = new List<long>();

        public static void Draw()
        {
            LastQuads = 0;

            Settings settings = Settings.Instance;
            if (settings == null || !settings.HeatGlow)
            {
                Clear();
                return;
            }

            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated
                || MyAPIGateway.Session == null || MyAPIGateway.Session.Camera == null)
            {
                Clear();
                return;
            }

            Vector3D eye = MyAPIGateway.Session.Camera.WorldMatrix.Translation;

            IList<ThermalGrid> grids = ThermalGrid.LiveGrids;
            int quads = 0;

            Extinguished.Clear();
            foreach (KeyValuePair<long, MyLight> entry in Lights) Extinguished.Add(entry.Key);

            for (int i = 0; i < grids.Count; i++)
            {
                ThermalGrid thermals = grids[i];
                if (thermals == null || thermals.LitBlocks.Count == 0) continue;
                if (thermals.Grid == null || thermals.Grid.MarkedForClose) continue;

                BoundingBoxD bounds = thermals.Grid.PositionComp.WorldAABB;
                double distance = Math.Max(0.0, Vector3D.Distance(bounds.Center, eye) - bounds.HalfExtents.Length());
                if (distance >= HeatGlowStyle.DrawRange) continue;

                Extinguished.Remove(thermals.Grid.EntityId);
                UpdateLight(thermals, ref eye);

                if (quads < HeatGlowStyle.MaxQuads)
                    quads += DrawGrid(thermals, ref eye, HeatGlowStyle.MaxQuads - quads);
            }

            for (int i = 0; i < Extinguished.Count; i++) Extinguish(Extinguished[i]);
            Extinguished.Clear();

            LastQuads = quads;
        }

        /// <summary>
        /// Puts every heat light out. Called when the feature is switched off or the world ends: a
        /// light this mod created and forgot outlives the grid it was lighting.
        /// </summary>
        public static void Clear()
        {
            LastQuads = 0;
            Extinguished.Clear();
            foreach (KeyValuePair<long, MyLight> entry in Lights) Extinguished.Add(entry.Key);
            for (int i = 0; i < Extinguished.Count; i++) Extinguish(Extinguished[i]);
            Extinguished.Clear();
        }

        private static void Extinguish(long entityId)
        {
            MyLight light;
            if (!Lights.TryGetValue(entityId, out light)) return;

            Lights.Remove(entityId);
            if (light == null) return;

            light.LightOn = false;
            light.GlareOn = false;
            light.UpdateLight();
            MyLights.RemoveLight(light);
        }

        /// <summary>
        /// Sets one grid's heat light from the blocks it has glowing: the glow-weighted centre of
        /// them, the colour of its hottest, and a range that covers the set.
        /// </summary>
        private static void UpdateLight(ThermalGrid thermals, ref Vector3D eye)
        {
            GlowRegion region;
            Vector3D centre;
            float radius;
            if (!Region(thermals, out region, out centre, out radius))
            {
                Extinguish(thermals.Grid.EntityId);
                return;
            }

            float lightRange = Math.Min(LightRangeCap, radius + LightReach);
            float fade = HeatGlowStyle.RangeFade(Math.Max(0.0,
                Vector3D.Distance(centre, eye) - lightRange), 200.0);
            if (fade <= 0f)
            {
                Extinguish(thermals.Grid.EntityId);
                return;
            }

            MyLight light;
            if (!Lights.TryGetValue(thermals.Grid.EntityId, out light) || light == null)
            {
                if (Lights.Count >= HeatGlowStyle.MaxLights) return;
                light = MyLights.AddLight();
                if (light == null) return;

                light.Start("ThermodynamicsHeat");
                light.LightType = MyLightType.DEFAULT;
                light.CastShadows = false;
                light.Falloff = 1.5f;
                Lights[thermals.Grid.EntityId] = light;
            }

            Vector3 locus = Incandescence.Colour(region.Kelvin);

            light.Position = centre;
            light.Color = new Color(locus);
            light.Intensity = region.Glow * LightIntensity * fade;
            light.Range = lightRange;
            light.LightOn = true;
            light.MarkPositionDirty();
            light.UpdateLight();
        }

        /// <summary>
        /// Where one grid's glow region sits in the world. The reduction itself is
        /// <see cref="GlowRegion"/>, in grid cells; this is the transform onto the grid.
        /// </summary>
        private static bool Region(ThermalGrid thermals, out GlowRegion region, out Vector3D centre,
            out float radius)
        {
            centre = Vector3D.Zero;
            radius = 0f;

            if (!GlowRegion.Reduce(thermals.LitBlocks, out region)) return false;

            float gridSize = thermals.Grid.GridSize;
            MatrixD gridMatrix = thermals.Grid.WorldMatrix;

            centre = Vector3D.Transform(region.Centre * gridSize, gridMatrix);
            radius = region.RadiusCells * gridSize;
            return true;
        }

        /// <summary>Draws one grid's glowing blocks, and says how many quads that took.</summary>
        private static int DrawGrid(ThermalGrid thermals, ref Vector3D eye, int budget)
        {
            MatrixD gridMatrix = thermals.Grid.WorldMatrix;
            float gridSize = thermals.Grid.GridSize;

            IList<LitBlock> lit = thermals.LitBlocks;
            int quads = 0;

            for (int i = 0; i < lit.Count && quads < budget; i++)
            {
                LitBlock block = lit[i];
                if (block.Glow <= 0f) continue;

                ThermalBlock bound = thermals.Get(block.Position);
                if (bound == null || bound.Block == null || bound.Node == null) continue;

                ThermalNode node = bound.Node;
                if (node.TotalExposedFaces == 0) continue;

                Vector3D centre;
                bound.Block.ComputeWorldCenter(out centre);
                double distance = Vector3D.Distance(centre, eye);
                float fade = HeatGlowStyle.RangeFade(distance, HeatGlowStyle.DrawRange);
                if (fade <= 0f) continue;

                Vector3 half = FaceQuad.HalfExtents(bound.Block.Min, bound.Block.Max, gridSize);
                Vector4 colour = Colour(block) * (fade * HeatGlowStyle.SurfaceIntensity);

                for (int face = 0; face < Face.Count && quads < budget; face++)
                {
                    if (node.GetExposedFaces(face) == 0) continue;
                    if (DrawFace(face, ref centre, ref half, ref gridMatrix, ref eye, ref colour, gridSize))
                    {
                        quads++;
                    }
                }
            }

            return quads;
        }

        /// <summary>
        /// Draws one face, and says whether it was drawn. A face turned away from the camera is not:
        /// it is on the far side of the block that would occlude it.
        /// </summary>
        private static bool DrawFace(int face, ref Vector3D centre, ref Vector3 half,
            ref MatrixD gridMatrix, ref Vector3D eye, ref Vector4 colour, float gridSize)
        {
            Vector3 localNormal = Face.Normals[face];
            Vector3D normal = Vector3D.TransformNormal(localNormal, gridMatrix);

            float reach = FaceQuad.Extent(ref half, ref localNormal);
            Vector3D position = centre + (normal * (reach + HeatGlowStyle.StandOff(gridSize)));
            Vector3D toEye = eye - position;
            double distance = toEye.Length();
            if (distance <= 0.0001) return false;
            float facing = HeatGlowStyle.FacingFade(Vector3D.Dot(normal, toEye) / distance);
            if (facing <= 0f) return false;

            Vector3 localLeft, localUp;
            FaceQuad.Tangents(face, out localLeft, out localUp);

            Vector3 left = (Vector3)Vector3D.TransformNormal(localLeft, gridMatrix);
            Vector3 up = (Vector3)Vector3D.TransformNormal(localUp, gridMatrix);

            MyTransparentGeometry.AddBillboardOriented(
                GlowMaterial,
                colour * facing,
                position,
                left,
                up,
                FaceQuad.Extent(ref half, ref localLeft) * HeatGlowStyle.HaloScale,
                FaceQuad.Extent(ref half, ref localUp) * HeatGlowStyle.HaloScale,
                Vector2.Zero,
                BlendTypeEnum.AdditiveBottom);

            return true;
        }

        /// <summary>Premultiplied colour: one warning ramp in RGB and its matching alpha.</summary>
        private static Vector4 Colour(LitBlock block)
        {
            Vector3 locus = Incandescence.Colour(block.Kelvin);
            float glow = block.Glow;
            return new Vector4(locus * glow, glow);
        }
    }

}
