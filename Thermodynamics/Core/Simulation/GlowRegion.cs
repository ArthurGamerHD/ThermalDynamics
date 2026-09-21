using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    public struct LitBlock
    {
        public Vector3I Position;

        public float Kelvin;

        public float Glow;
    }

    public struct GlowRegion
    {
        public Vector3D Centre;

        public float RadiusCells;

        public float Kelvin;

        public float Glow;

/// <summary>Reduce operation.</summary>
        public static bool Reduce(IList<LitBlock> lit, out GlowRegion region)
        {
/// <summary>GlowRegion operation.</summary>
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
