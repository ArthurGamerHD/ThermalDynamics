using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Where to look from, when asking whether the sun reaches a grid.
    ///
    /// One ray from the middle of a ship answers for a point, and a ship is not a point: a
    /// kilometre of hull crossing a planet's terminator is either fully lit or fully dark according
    /// to that one ray, and it flips the moment the centre crosses. Sampling a handful of points
    /// spread through the ship turns that step into a ramp, at a cost of one ray each.
    ///
    /// The points are the centre first, then the corners of a box inset inside the grid's bounds.
    /// Centre first because it is the one everybody gets: a sample count of one has to behave
    /// exactly as the single-ray version did. Inset because a ray from the exact corner of the
    /// bounding box starts in empty space beside the ship, where an occluder that shadows the whole
    /// hull can be missed by a metre.
    /// </summary>
    public static class SolarOcclusionSampler
    {
        /// <summary>Most points anyone can ask for: the centre and eight corners.</summary>
        public const int MaxSamples = 9;

        /// <summary>
        /// How far out the corner samples sit, as a share of the half-extent. Well inside the hull
        /// rather than at its skin, so a sample stands in the ship rather than beside it.
        /// </summary>
        public const double CornerInset = 0.6;

        private static readonly Vector3D[] Corners = new Vector3D[]
        {
            new Vector3D(-1, -1, -1),
            new Vector3D(1, 1, 1),
            new Vector3D(-1, 1, -1),
            new Vector3D(1, -1, 1),
            new Vector3D(-1, -1, 1),
            new Vector3D(1, 1, -1),
            new Vector3D(-1, 1, 1),
            new Vector3D(1, -1, -1),
        };

        /// <summary>
        /// Fills <paramref name="results"/> with up to <paramref name="samples"/> points to cast
        /// from. Always returns at least the centre.
        ///
        /// Opposite corners are paired in order, so any even count straddles the box rather than
        /// clustering down one side of it — four samples that all sit on the same end of a ship
        /// would be worse than one in the middle.
        /// </summary>
        public static void Points(BoundingBoxD bounds, int samples, List<Vector3D> results)
        {
            if (results == null) return;

            results.Clear();

            if (samples > MaxSamples) samples = MaxSamples;
            if (samples < 1) samples = 1;

            Vector3D centre = bounds.Center;
            results.Add(centre);

            Vector3D reach = bounds.HalfExtents * CornerInset;

            for (int i = 0; i < samples - 1; i++)
            {
                results.Add(centre + (Corners[i] * reach));
            }
        }
    }
}
