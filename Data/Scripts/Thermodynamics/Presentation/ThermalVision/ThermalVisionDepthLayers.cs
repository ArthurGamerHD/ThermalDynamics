using System;
using VRageMath;

namespace Thermodynamics.Presentation
{
    /// <summary>Ordered camera planes for the native-depth experiment, not temperature data.</summary>
    public static class ThermalVisionDepthLayers
    {
        public const int Count = 48;
        public const double Reach = 5000;

        public static double Distance(int index, double near)
        {
            if (index < 0 || index >= Count || double.IsNaN(near) || near <= 0 || near >= Reach)
                throw new ArgumentException("Invalid depth layer or near plane.");
            return near * Math.Pow(Reach / near, (double)index / (Count - 1));
        }

        /// <summary>Perspective frustum plane, including an off-centre projection.</summary>
        public static void Plane(double distance, MatrixD projection, MatrixD world,
            out Vector3D centre, out float halfWidth, out float halfHeight)
        {
            if (distance <= 0 || double.IsInfinity(distance) || double.IsNaN(distance)
                || projection.M11 <= 0 || projection.M22 <= 0)
                throw new ArgumentException("Invalid depth plane projection.");
            centre = Vector3D.Transform(new Vector3D(distance * projection.M31 / projection.M11,
                distance * projection.M32 / projection.M22, -distance), world);
            halfWidth = (float)(distance / projection.M11);
            halfHeight = (float)(distance / projection.M22);
            if (float.IsInfinity(halfWidth) || float.IsNaN(halfWidth)
                || float.IsInfinity(halfHeight) || float.IsNaN(halfHeight)
                || double.IsNaN(centre.X + centre.Y + centre.Z)
                || double.IsInfinity(centre.X + centre.Y + centre.Z))
                throw new ArgumentException("Non-finite depth plane.");
        }

        public static Vector4 Colour(int index, ThermalVisionState.Mode mode)
        {
            // The last plane also covers no-return sky: neutral black, not measured cold.
            if (index == Count - 1) return new Vector4(0, 0, 0, 1);
            Vector3 srgb;
            if (!ThermalVisionPalette.TrySample(.35f + .65f * (1f - (float)index / (Count - 1)), mode, 0, 1, out srgb))
                throw new ArgumentException("Invalid depth palette.");
            return new Vector4(srgb, 1);
        }
    }
}
