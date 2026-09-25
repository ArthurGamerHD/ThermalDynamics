using VRageMath;

namespace Thermodynamics.Presentation
{
    public static class ThermalVisionHudProjection
    {

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
