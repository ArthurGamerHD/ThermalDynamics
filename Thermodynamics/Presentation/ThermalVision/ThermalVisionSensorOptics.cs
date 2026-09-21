using System;
using VRageMath;

namespace Thermodynamics.Presentation
{
    public static class ThermalVisionSensorOptics
    {
/// <summary>Layout operation.</summary>
        public static void Layout(Vector2 screen, float aspect, out Vector2 size, out Vector2 offset)
        {
            float width = Math.Min(560f, screen.X * .36f);
            float height = Math.Min(width / aspect, screen.Y * .4f);
/// <summary>Vector2 operation.</summary>
            size = new Vector2(height * aspect, height);
/// <summary>Vector2 operation.</summary>
            offset = new Vector2(screen.X * .5f - size.X * .5f - 24,
                screen.Y * .5f - height * .5f - 190);
        }

/// <summary>Ray operation.</summary>
        public static Vector3D Ray(int x, int y, int width, int height, MatrixD projection, MatrixD world)
        {
            if (width < 1 || height < 1 || x < 0 || y < 0 || x >= width || y >= height
                || projection.M11 == 0 || projection.M22 == 0)
                throw new ArgumentException("Invalid survey pixel or projection.");
            double nx = 2 * (x + .5) / width - 1, ny = 1 - 2 * (y + .5) / height;
            Vector3D ray = Vector3D.Normalize(Vector3D.TransformNormal(new Vector3D(
                (nx + projection.M31) / projection.M11, (ny + projection.M32) / projection.M22, -1), world));
            if (double.IsNaN(ray.X) || double.IsNaN(ray.Y) || double.IsNaN(ray.Z)
                || double.IsInfinity(ray.X) || double.IsInfinity(ray.Y) || double.IsInfinity(ray.Z))
                throw new ArgumentException("Non-finite survey ray.");
            return ray;
        }

/// <summary>Shade operation.</summary>
        public static Color Shade(bool hit, float kelvin, float facing, int x, int y,
            ThermalVisionState.Mode mode, float low, float high)
        {
            if (!hit) return new Color(12, 12, 12);
            Vector3 rgb;
            if (ThermalVisionPalette.TrySample(kelvin, mode, low, high, out rgb)) return new Color(rgb);
            if (float.IsNaN(facing) || float.IsInfinity(facing)) facing = 0;
            int shade = 45 + (int)(Math.Max(0, Math.Min(1, facing)) * 25) + ((x + y) % 4 == 0 ? 35 : 0);
            return new Color(shade, shade, shade);
        }
    }
}
