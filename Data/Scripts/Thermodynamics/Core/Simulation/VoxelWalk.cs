using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Walks a ray through a grid's cells and reports whether it hits anything solid.
    ///
    /// The traversal is Amanatides and Woo's: keep the distance to the next cell boundary on each
    /// axis and cross whichever is nearest. It visits every cell the ray actually passes through
    /// and no others, so a ray cannot slip diagonally between two blocks that touch along an edge.
    ///
    /// The origin is a point rather than a cell, which is what lets one walk serve two purposes. A
    /// grid asking about its own shadow starts at cell centres, on the integers. A grid asking
    /// whether the station overhead is in the way starts wherever the station's cell lattice
    /// happens to fall relative to its own — almost never on an integer, and rotated besides.
    /// </summary>
    public static class VoxelWalk
    {
        /// <summary>
        /// True when the ray from <paramref name="origin"/> along <paramref name="direction"/>
        /// crosses an occupied cell of <paramref name="grid"/>, both in that grid's cell space.
        ///
        /// The walk is bounded by the grid's own box: a ray that never reaches it, or has left it,
        /// cannot hit anything and stops rather than stepping on forever.
        /// </summary>
        public static bool Blocked(GridModel grid, Vector3D origin, Vector3D direction, double startOffset = 0d)
        {
            if (grid == null) return false;
            if (direction.LengthSquared() < 1e-12) return false;

            direction = Vector3D.Normalize(direction);

            Vector3I min = grid.Min;
            Vector3I max = grid.Max;

            double enter, exit;
            if (!BoxRange(origin, direction, min, max, out enter, out exit)) return false;
            if (exit <= startOffset) return false;

            // Start at the box rather than at the origin when the origin is outside it: the ray may
            // have kilometres to cross before the first cell it could possibly hit.
            double travelled = Math.Max(enter, startOffset);
            Vector3D point = origin + (direction * travelled);

            int x = (int)Math.Round(point.X);
            int y = (int)Math.Round(point.Y);
            int z = (int)Math.Round(point.Z);

            if (Inside(x, y, z, min, max) && grid.IsOccupied(new Vector3I(x, y, z))) return true;

            int stepX = Sign(direction.X), stepY = Sign(direction.Y), stepZ = Sign(direction.Z);

            double tMaxX = FirstBoundary(point.X, x, direction.X);
            double tMaxY = FirstBoundary(point.Y, y, direction.Y);
            double tMaxZ = FirstBoundary(point.Z, z, direction.Z);

            double tDeltaX = Delta(direction.X);
            double tDeltaY = Delta(direction.Y);
            double tDeltaZ = Delta(direction.Z);

            double span = exit - travelled;

            // Bounded twice: by the distance the ray stays in the box, and by a step count no sane
            // geometry reaches. The first is the real limit; the second stops a degenerate
            // direction spinning.
            int limit = (2 * ((max.X - min.X) + (max.Y - min.Y) + (max.Z - min.Z))) + 8;

            for (int i = 0; i < limit; i++)
            {
                double t;

                if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
                {
                    t = tMaxX;
                    x += stepX;
                    tMaxX += tDeltaX;
                }
                else if (tMaxY <= tMaxZ)
                {
                    t = tMaxY;
                    y += stepY;
                    tMaxY += tDeltaY;
                }
                else
                {
                    t = tMaxZ;
                    z += stepZ;
                    tMaxZ += tDeltaZ;
                }

                if (t > span) return false;
                if (grid.IsOccupied(new Vector3I(x, y, z))) return true;
            }

            return false;
        }

        /// <summary>
        /// Where the ray enters and leaves the grid's box, in distance along the ray. False when it
        /// misses the box altogether or leaves it behind.
        /// </summary>
        private static bool BoxRange(
            Vector3D origin, Vector3D direction, Vector3I min, Vector3I max,
            out double enter, out double exit)
        {
            enter = 0d;
            exit = double.MaxValue;

            for (int axis = 0; axis < 3; axis++)
            {
                double o = Component(origin, axis);
                double d = Component(direction, axis);

                // The box is the block bounds grown by half a cell, since cells are cubes centred
                // on integers.
                double low = BoxGeometry.Component(min, axis) - 0.5d;
                double high = BoxGeometry.Component(max, axis) + 0.5d;

                if (Math.Abs(d) < 1e-9)
                {
                    if (o < low || o > high) return false;
                    continue;
                }

                double t1 = (low - o) / d;
                double t2 = (high - o) / d;

                if (t1 > t2)
                {
                    double swap = t1;
                    t1 = t2;
                    t2 = swap;
                }

                if (t1 > enter) enter = t1;
                if (t2 < exit) exit = t2;
            }

            return exit >= enter && exit > 0d;
        }

        private static bool Inside(int x, int y, int z, Vector3I min, Vector3I max)
        {
            return x >= min.X && x <= max.X
                && y >= min.Y && y <= max.Y
                && z >= min.Z && z <= max.Z;
        }

        /// <summary>Distance to the boundary of the cell the ray is standing in.</summary>
        private static double FirstBoundary(double position, int cell, double direction)
        {
            double magnitude = Math.Abs(direction);
            if (magnitude < 1e-9) return double.MaxValue;

            double offset = position - cell;              // -0.5 .. 0.5 within the cell
            double remaining = direction > 0 ? 0.5d - offset : offset + 0.5d;

            if (remaining < 0d) remaining = 0d;
            return remaining / magnitude;
        }

        private static double Delta(double direction)
        {
            double magnitude = Math.Abs(direction);
            return magnitude < 1e-9 ? double.MaxValue : 1d / magnitude;
        }

        private static double Component(Vector3D vector, int axis)
        {
            return axis == 0 ? vector.X : axis == 1 ? vector.Y : vector.Z;
        }

        private static int Sign(double value)
        {
            if (value > 0d) return 1;
            if (value < 0d) return -1;
            return 0;
        }
    }
}
