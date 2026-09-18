using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Whether planetary terrain stands between a point and the sun — everything the smooth-sphere
    /// planet test ignores, such as the canyon a base sits in. A horizon walk out along the sun ray,
    /// reading only ground heights and never physics, sampled geometrically because nearby terrain is
    /// almost all of the real shadowing. See configuration.md, External shadow.
    /// </summary>
    public static class TerrainHorizon
    {
        /// <summary>Where the walk starts, in metres. Anything closer is the grid's own hull.</summary>
        public const double NearDistance = 50d;

        /// <summary>
        /// How far above the ray's own height terrain must stand before it counts as blocking, in
        /// metres. Absorbs the metre or two of disagreement between the height field and the
        /// rendered voxels, without which flat ground shadows itself.
        /// </summary>
        public const double Tolerance = 2d;

        /// <summary>
        /// True when terrain stands between <paramref name="origin"/> and the sun.
        ///
        /// <paramref name="surfaceRadius"/> gives the distance from the planet's centre to the ground
        /// under a point. It is the only world input, which is what allows the walk to be tested
        /// against synthetic terrain.
        /// </summary>
        public static bool Occluded(
            Vector3D origin,
            Vector3D sunDirection,
            Vector3D planetCentre,
            double range,
            int samples,
            Func<Vector3D, double> surfaceRadius)
        {
            if (surfaceRadius == null || samples < 1 || range <= NearDistance) return false;

            if (sunDirection.LengthSquared() < 1e-12) return false;
            sunDirection = Vector3D.Normalize(sunDirection);

            double ratio = Math.Pow(range / NearDistance, 1d / samples);
            double distance = NearDistance;

            for (int i = 0; i < samples; i++)
            {
                distance *= ratio;

                Vector3D point = origin + (sunDirection * distance);
                double height = (point - planetCentre).Length();

                if (height + Tolerance < surfaceRadius(point)) return true;
            }

            return false;
        }
    }
}
