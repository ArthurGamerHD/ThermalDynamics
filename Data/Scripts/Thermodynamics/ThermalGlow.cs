using System;
using System.Collections.Generic;
using Sandbox.Game.Lights;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using static VRageRender.MyBillboard;

namespace Thermodynamics
{
    /// <summary>
    /// A hot block drawn glowing: one additive quad over each of its exposed faces, coloured by the
    /// Planckian locus and brightened by how near the block is to failing.
    ///
    /// <para>
    /// **The emissive path could not carry this and the measurement is why.**
    /// <c>MyCubeBlock.UpdateEmissiveParts</c> writes to a named material in the block's model, and
    /// of the 1,992 base cube models the game ships **418 hold one** — of 479 armour models, four.
    /// Where the material does exist it is a status lamp rather than a skin. So the emissive write
    /// is kept, because a lamp that goes red on a hot reactor is right, and it is not the channel.
    /// See document-of-intent.md, Natural feedback.
    /// </para>
    ///
    /// <para>
    /// **Exposed faces only**, which is both the cheap answer and the correct one: a face buried
    /// inside a hull cannot be seen, and the solver already knows which faces those are.
    /// </para>
    ///
    /// <para>
    /// Client side, per frame, transparent geometry; nothing is written to the grid. A grid with no
    /// glowing block costs a count, which is the same floor the cue pass holds itself to.
    /// </para>
    /// </summary>
    public static class ThermalGlow
    {
        /// <summary>A flat white square: the face is filled, so its corners glow like its middle.</summary>
        private static readonly MyStringId GlowMaterial = MyStringId.GetOrCompute("Square");

        /// <summary>
        /// Metres the quad stands off the hull. Enough to clear the surface it covers at any range
        /// the glow is legible at, small enough not to read as a shell around the block.
        /// </summary>
        private const float StandOff = 0.03f;

        /// <summary>
        /// Metres beyond which a glowing block is not drawn. A glow is a warning to the person
        /// flying the ship, and past this it is a pixel.
        /// </summary>
        private const double DrawRange = 2000.0;

        /// <summary>
        /// Quads handed to the renderer in one frame, across every grid.
        ///
        /// A hull with a thousand blocks over their rating is the case this exists for: the glow is
        /// already the whole ship by then, and the blocks past the cap are the ones furthest away.
        /// </summary>
        private const int MaxQuads = 4000;

        /// <summary>Quads drawn last frame, for the telemetry row.</summary>
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

            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated) return;
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Camera == null) return;

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

                Extinguished.Remove(thermals.Grid.EntityId);
                UpdateLight(thermals);

                if (quads < MaxQuads) quads += DrawGrid(thermals, ref eye, MaxQuads - quads);
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
        private static void UpdateLight(ThermalGrid thermals)
        {
            GlowRegion region;
            Vector3D centre;
            float radius;
            if (!Region(thermals, out region, out centre, out radius))
            {
                Extinguish(thermals.Grid.EntityId);
                return;
            }

            MyLight light;
            if (!Lights.TryGetValue(thermals.Grid.EntityId, out light) || light == null)
            {
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
            light.Intensity = region.Glow * LightIntensity;
            light.Range = Math.Min(LightRangeCap, radius + LightReach);
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
                if (Vector3D.DistanceSquared(centre, eye) > DrawRange * DrawRange) continue;

                Vector3 half = FaceQuad.HalfExtents(bound.Block.Min, bound.Block.Max, gridSize);
                Vector4 colour = Colour(block);

                for (int face = 0; face < Face.Count && quads < budget; face++)
                {
                    if (node.ExposedFaces[face] == 0) continue;
                    if (DrawFace(face, ref centre, ref half, ref gridMatrix, ref eye, ref colour))
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
            ref MatrixD gridMatrix, ref Vector3D eye, ref Vector4 colour)
        {
            Vector3 localNormal = Face.Normals[face];
            Vector3D normal = Vector3D.TransformNormal(localNormal, gridMatrix);

            float reach = FaceQuad.Extent(ref half, ref localNormal);
            Vector3D position = centre + (normal * (reach + StandOff));
            if (Vector3D.Dot(normal, position - eye) >= 0) return false;

            Vector3 localLeft, localUp;
            FaceQuad.Tangents(face, out localLeft, out localUp);

            Vector3 left = (Vector3)Vector3D.TransformNormal(localLeft, gridMatrix);
            Vector3 up = (Vector3)Vector3D.TransformNormal(localUp, gridMatrix);

            MyTransparentGeometry.AddBillboardOriented(
                GlowMaterial,
                colour,
                position,
                left,
                up,
                FaceQuad.Extent(ref half, ref localLeft),
                FaceQuad.Extent(ref half, ref localUp),
                Vector2.Zero,
                BlendTypeEnum.AdditiveBottom);

            return true;
        }

        /// <summary>
        /// What one glowing block is drawn in.
        ///
        /// The colour carries no brightness and the brightness carries no colour — the locus is
        /// normalised so its brightest channel is full — so multiplying the two here is what keeps a
        /// dull red block dull rather than squaring its luminance. Additive blending reads the
        /// alpha as well, so the ramp is applied once in each.
        /// </summary>
        private static Vector4 Colour(LitBlock block)
        {
            Vector3 locus = Incandescence.Colour(block.Kelvin);
            float glow = block.Glow;
            return new Vector4(locus * glow, glow);
        }
    }

}
