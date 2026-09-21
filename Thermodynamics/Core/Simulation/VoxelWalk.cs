using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public static class VoxelWalk
    {
/// <summary>Blocked operation.</summary>
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

            double travelled = Math.Max(enter, startOffset);
            Vector3D point = origin + (direction * travelled);

            int x = (int)Math.Round(point.X);
            int y = (int)Math.Round(point.Y);
            int z = (int)Math.Round(point.Z);

            if (Inside(x, y, z, min, max) && grid.IsOccupied(new Vector3I(x, y, z))) return true;

/// <summary>Sign operation.</summary>
            int stepX = Sign(direction.X), stepY = Sign(direction.Y), stepZ = Sign(direction.Z);

/// <summary>FirstBoundary operation.</summary>
            double tMaxX = FirstBoundary(point.X, x, direction.X);
/// <summary>FirstBoundary operation.</summary>
            double tMaxY = FirstBoundary(point.Y, y, direction.Y);
/// <summary>FirstBoundary operation.</summary>
            double tMaxZ = FirstBoundary(point.Z, z, direction.Z);

/// <summary>Delta operation.</summary>
            double tDeltaX = Delta(direction.X);
/// <summary>Delta operation.</summary>
            double tDeltaY = Delta(direction.Y);
/// <summary>Delta operation.</summary>
            double tDeltaZ = Delta(direction.Z);

            double span = exit - travelled;

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
/// <summary>if operation.</summary>
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

/// <summary>BoxRange operation.</summary>
        private static bool BoxRange(
            Vector3D origin, Vector3D direction, Vector3I min, Vector3I max,
            out double enter, out double exit)
        {
            enter = 0d;
            exit = double.MaxValue;

            for (int axis = 0; axis < 3; axis++)
            {
/// <summary>Component operation.</summary>
                double o = Component(origin, axis);
/// <summary>Component operation.</summary>
                double d = Component(direction, axis);

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

/// <summary>Inside operation.</summary>
        private static bool Inside(int x, int y, int z, Vector3I min, Vector3I max)
        {
            return x >= min.X && x <= max.X
                && y >= min.Y && y <= max.Y
                && z >= min.Z && z <= max.Z;
        }

/// <summary>FirstBoundary operation.</summary>
        private static double FirstBoundary(double position, int cell, double direction)
        {
            double magnitude = Math.Abs(direction);
            if (magnitude < 1e-9) return double.MaxValue;

            double offset = position - cell;              // -0.5 .. 0.5 within the cell
            double remaining = direction > 0 ? 0.5d - offset : offset + 0.5d;

            if (remaining < 0d) remaining = 0d;
            return remaining / magnitude;
        }

/// <summary>Delta operation.</summary>
        private static double Delta(double direction)
        {
            double magnitude = Math.Abs(direction);
            return magnitude < 1e-9 ? double.MaxValue : 1d / magnitude;
        }

/// <summary>Component operation.</summary>
        private static double Component(Vector3D vector, int axis)
        {
            return axis == 0 ? vector.X : axis == 1 ? vector.Y : vector.Z;
        }

/// <summary>Sign operation.</summary>
        private static int Sign(double value)
        {
            if (value > 0d) return 1;
            if (value < 0d) return -1;
            return 0;
        }
    }
}
