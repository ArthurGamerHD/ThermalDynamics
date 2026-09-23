using System;
using System.Collections.Generic;
using VRageMath;
using Region = Thermodynamics.Presentation.ThermalVisionRegionPartition.Region;

namespace Thermodynamics.Presentation
{
    public sealed class ThermalVisionSmoothField
    {
        private readonly List<Region> samples;

        public ThermalVisionSmoothField(IList<Region> source) { samples = new List<Region>(source); }

        public float Sample(Vector3D point, float fallback)
        {
            double total = 0, sum = 0;
            foreach (Region sample in samples)
            {
                Vector3D centre = (sample.Min + sample.Max) * .5;
                double radius = Math.Max(.25, (sample.Max - sample.Min).Length());
                double d = Vector3D.DistanceSquared(point, centre) / (radius * radius);
                if (d >= 1) continue;
                double weight = (1 - d) * (1 - d);
                total += weight; sum += weight * sample.Kelvin;
            }
            return total > 1e-12 ? (float)(sum / total) : fallback;
        }

        public static Vector3D Corner(Region region, int index)
        {
            return new Vector3D((index & 1) == 0 ? region.Min.X : region.Max.X,
                (index & 2) == 0 ? region.Min.Y : region.Max.Y,
                (index & 4) == 0 ? region.Min.Z : region.Max.Z);
        }

        public static float Interpolate(Region region, float[] corners, Vector3D point)
        {
            Vector3D t = (point - region.Min) / (region.Max - region.Min);
            t = Vector3D.Clamp(t, Vector3D.Zero, Vector3D.One);
            double result = 0;
            for (int i = 0; i < 8; i++)
                result += corners[i] * ((i & 1) == 0 ? 1-t.X : t.X)
                    * ((i & 2) == 0 ? 1-t.Y : t.Y) * ((i & 4) == 0 ? 1-t.Z : t.Z);
            return (float)result;
        }
    }
}
