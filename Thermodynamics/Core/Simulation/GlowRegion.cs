using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>One block a grid is currently glowing.</summary>
    public struct LitBlock
    {
        /// <summary>Its position in grid cells.</summary>
        public Vector3I Position;

        /// <summary>Its temperature, which decides the colour. See <see cref="Incandescence"/>.</summary>
        public float Kelvin;

        /// <summary>How near it is to failing, 0..1, which decides the brightness.</summary>
        public float Glow;
    }

    /// <summary>
    /// A grid's glowing blocks reduced to one light: where it sits, how far it reaches, what colour
    /// it is and how bright.
    ///
    /// <para>
    /// **One light per grid rather than one per block.** A hull losing five hundred blocks would
    /// otherwise ask the renderer for five hundred dynamic lights. Which block is hot is already
    /// carried by the glow drawn on the block itself; all a light has to do is let that fall on what
    /// is around it, and that is a property of the region rather than of the cube.
    /// </para>
    ///
    /// <para>
    /// In grid-local cell space, so it can be computed and checked without a session; the caller
    /// transforms the result. See document-of-intent.md, Natural feedback.
    /// </para>
    /// </summary>
    public struct GlowRegion
    {
        /// <summary>The glow-weighted centre of the glowing blocks, in grid cells.</summary>
        public Vector3D Centre;

        /// <summary>Cells from <see cref="Centre"/> to the furthest glowing block.</summary>
        public float RadiusCells;

        /// <summary>The hottest glowing block: the colour the region is lit in.</summary>
        public float Kelvin;

        /// <summary>The brightest: how near the grid is to losing something.</summary>
        public float Glow;

        /// <summary>
        /// Reduces a grid's glowing blocks to one region, or returns false where none is glowing.
        ///
        /// The centre is weighted by glow, so it sits on the blocks that are actually failing rather
        /// than in the middle of everything that happens to be warm.
        /// </summary>
        public static bool Reduce(IList<LitBlock> lit, out GlowRegion region)
        {
            region = new GlowRegion();
            if (lit == null || lit.Count == 0) return false;

            Vector3D sum = Vector3D.Zero;
            double weight = 0d;

            for (int i = 0; i < lit.Count; i++)
            {
                LitBlock block = lit[i];
                if (block.Glow <= 0f) continue;

                sum += (Vector3D)block.Position * block.Glow;
                weight += block.Glow;

                if (block.Kelvin > region.Kelvin) region.Kelvin = block.Kelvin;
                if (block.Glow > region.Glow) region.Glow = block.Glow;
            }

            if (weight <= 0d) return false;

            region.Centre = sum / weight;

            double furthest = 0d;
            for (int i = 0; i < lit.Count; i++)
            {
                if (lit[i].Glow <= 0f) continue;

                double distance = Vector3D.Distance((Vector3D)lit[i].Position, region.Centre);
                if (distance > furthest) furthest = distance;
            }

            region.RadiusCells = (float)furthest;
            return true;
        }
    }
}
