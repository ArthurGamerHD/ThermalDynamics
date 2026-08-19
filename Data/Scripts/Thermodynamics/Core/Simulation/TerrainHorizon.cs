using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Whether planetary terrain stands between a point and the sun.
    ///
    /// The planet test treats a world as a smooth sphere, which covers night and orbit but not local
    /// relief: a base in a canyon stays in shadow for some time after the sphere test reports it
    /// lit, and a grid parked against a cliff can be shaded all day.
    ///
    /// This walks out along the sun ray and tests at each step whether the ray is still above the
    /// terrain. A standard horizon walk, and cheap for the same reason the sphere test is: it reads
    /// only the ground height at a point and never touches physics.
    ///
    /// Samples are spaced geometrically, since nearby terrain accounts for almost all real shadowing
    /// and geometric spacing concentrates the samples there.
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
