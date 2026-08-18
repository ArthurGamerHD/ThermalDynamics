using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Whether the ground itself is in the way of the sun.
    ///
    /// The planet test treats a world as a smooth ball, which answers for night and for orbit and
    /// says nothing about the mountain to the east. A base in a canyon at sunrise is in shadow for
    /// an hour after the ball says it is lit, and a ship parked against a cliff can sit in shade all
    /// day — both look wrong in exactly the way a player notices, because they can see the shadow
    /// on the ground around them.
    ///
    /// The test walks out along the sun ray and asks, at each step, whether the ray is still above
    /// the terrain there. It is the standard horizon walk, and it is cheap for the same reason the
    /// planet test is: it never touches physics, only the height of the ground at a point.
    ///
    /// Samples are spaced geometrically. Near ground matters far more than far ground — the cliff
    /// two hundred metres away shadows you, the hill ten kilometres off almost never does — and
    /// geometric spacing puts most of the samples where most of the answers are.
    /// </summary>
    public static class TerrainHorizon
    {
        /// <summary>Where the walk starts, in metres. Closer than this is the grid's own hull.</summary>
        public const double NearDistance = 50d;

        /// <summary>
        /// How far above the ray's own height the terrain must stand before it counts as blocking,
        /// in metres. Absorbs the difference between the height field and the voxels actually
        /// rendered, which disagree by a metre or two — without it, flat ground shadows itself.
        /// </summary>
        public const double Tolerance = 2d;

        /// <summary>
        /// True when terrain stands between <paramref name="origin"/> and the sun.
        ///
        /// <paramref name="surfaceRadius"/> gives the distance from the planet's centre to the
        /// ground under a point — the one thing this needs from the world, and the reason the walk
        /// can be tested against terrain that was never rendered.
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
