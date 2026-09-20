using VRageMath;

namespace Thermodynamics.Presentation
{
    /// <summary>Maps centred HUD points to the current camera, independent of HUD layout timing.</summary>
    public static class ThermalVisionHudProjection
    {
        /// <summary>Creates a camera-facing plane with DPI-scaled pixel coordinates.</summary>
        public static MatrixD Create(MatrixD cameraWorld, MatrixD projection, Vector2 viewport,
            double depth, double dpi)
        {
            var scale=MatrixD.CreateScale(2*depth*dpi/(viewport.X*projection.M11),
                2*depth*dpi/(viewport.Y*projection.M22),1);
            var origin=MatrixD.CreateTranslation(depth*projection.M31/projection.M11,
                depth*projection.M32/projection.M22,-depth);
            return scale*origin*cameraWorld;
        }
    }
}
