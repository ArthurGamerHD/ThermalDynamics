using Thermodynamics.Presentation;
using RichHudFramework.UI;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Thermodynamics
{
    public static partial class ThermalVisionProbe
    {
        private static bool depthMode;
        private static readonly MyStringId CompositeSurfaceMaterial = MyStringId.GetOrCompute("GaugeThermalCompositeSurface");
        private static readonly MyStringId DepthMaterial = MyStringId.GetOrCompute("GaugeThermalDepthLayer");

        // Two ordered planes replace scene colour without assigning heat from distance.
        // Native depth rejects the far black plane wherever nearer opaque geometry exists.
        // Measured model triangles follow these planes in the same unsorted PostPP bucket.
        private static void DrawCompositeContext()
        {
            var camera = MyAPIGateway.Session.Camera;
            if (frames == 0)
                RecordEvent("composite: 2 neutral PostPP planes then measured model triangles; 5000 m; "
                    + "unknown is dark, not cold; incomplete geometry remains unknown; viewport="
                    + camera.ViewportSize + "; offset=" + camera.ViewportOffset);
            for (int i = 0; i < 2; i++)
            {
                Vector3D centre;
                float width, height;
                ThermalVisionDepthLayers.Plane(i == 0 ? camera.NearPlaneDistance * 1.05 : ThermalVisionDepthLayers.Reach,
                    camera.ProjectionMatrix, camera.WorldMatrix, out centre, out width, out height);
                MyTransparentGeometry.AddBillboardOriented(DepthMaterial,
                    i == 0 ? new Vector4(.035f, .035f, .035f, 1) : new Vector4(0, 0, 0, 1), centre,
                    (Vector3)camera.WorldMatrix.Right, (Vector3)camera.WorldMatrix.Up,
                    width * 1.25f, height * 1.25f, Vector2.Zero, MyBillboard.BlendTypeEnum.PostPP);
                drawn += 2;
            }
        }

        private static void DrawDepthLayers()
        {
            var camera = MyAPIGateway.Session.Camera;
            MatrixD world = camera.WorldMatrix, projection = camera.ProjectionMatrix;
            double near = camera.NearPlaneDistance * 1.05;
            if (frames == 0)
                RecordEvent("depth compositor: 48 ordered PostPP planes, near=" + near
                    + " m, far=5000 m; NOT TEMPERATURE; CPU submission timing excludes GPU cost"
                    + "; viewport=" + camera.ViewportSize + "; offset=" + camera.ViewportOffset
                    + "; projection=" + projection.M11 + "," + projection.M22 + "," + projection.M31 + "," + projection.M32);
            // PostPP preserves submission order in the installed renderer (unlike Standard).
            // No plane writes depth: the farthest visible plane replaces earlier colours.
            for (int i = 0; i < ThermalVisionDepthLayers.Count; i++)
            {
                Vector3D centre;
                float halfWidth, halfHeight;
                ThermalVisionDepthLayers.Plane(ThermalVisionDepthLayers.Distance(i, near), projection, world,
                    out centre, out halfWidth, out halfHeight);
                MyTransparentGeometry.AddBillboardOriented(DepthMaterial,
                    ThermalVisionDepthLayers.Colour(i, State.Current), centre,
                    (Vector3)world.Right, (Vector3)world.Up, halfWidth * 1.25f, halfHeight * 1.25f, Vector2.Zero,
                    MyBillboard.BlendTypeEnum.PostPP);
                drawn += 2;
                examined += 2;
            }
            rowIdentity = "native depth layers / NOT TEMPERATURE";
            Outcome("depth-diagnostic-48-layers-5000m");
            panel.Visible = true;
            if (frames++ % 6 == 0)
                panel.Text = new RichText("DEPTH DIAGNOSTIC / NOT TEMPERATURE\nLIVE / 48 layers / 5 km / near bright, far dark\n/thermal vision off");
        }
    }
}
