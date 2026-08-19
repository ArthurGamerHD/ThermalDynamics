using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Points to cast from when testing whether the sun reaches a grid.
    ///
    /// One ray from the grid's centre answers for a point. A large grid crossing a planet's
    /// terminator is then fully lit or fully dark according to that ray alone, flipping the moment
    /// the centre crosses. Sampling several points spread through the grid turns that step into a
    /// ramp at a cost of one ray each.
    ///
    /// The centre comes first, so a sample count of one behaves exactly as a single centre ray. The
    /// remaining points are the corners of a box inset inside the grid's bounds; a ray from the
    /// exact corner of the bounding box would start in empty space beside the grid, where an
    /// occluder shadowing the whole hull can be missed.
    /// </summary>
    public static class SolarOcclusionSampler
    {
        /// <summary>Maximum points that can be requested: the centre and eight corners.</summary>
        public const int MaxSamples = 9;

        /// <summary>
        /// How far out the corner samples sit, as a fraction of the half-extent. Inside the hull
        /// rather than at its surface, so a sample is within the grid rather than beside it.
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
        /// Fills <paramref name="results"/> with up to <paramref name="samples"/> points to cast from.
        /// Always returns at least the centre.
        ///
        /// Opposite corners are paired in order, so any even count straddles the box rather than
        /// clustering at one end of the grid.
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
