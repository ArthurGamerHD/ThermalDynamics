using VRageMath;

namespace Thermodynamics.Presentation
{
    /// <summary>Conservative model-space backface rejection for ordinary affine transforms.</summary>
    public enum ThermalVisionProjection { Visible, Backface, Degenerate }

    public struct ThermalVisionWorldTriangle
    {
        public Vector3D A, B, C;
        public Vector3 Normal;
    }

    public static class ThermalVisionGeometry
    {
        /// <summary>Conservative local bounds for an axis-aligned rectangular thermal patch.
        /// Padding includes the renderer's 1 mm camera-facing displacement.</summary>
        public static BoundingBoxD PatchBounds(Vector3D a,Vector3D c)
        {
            var padding=new Vector3D(.004);
            return new BoundingBoxD(Vector3D.Min(a,c)-padding,Vector3D.Max(a,c)+padding);
        }

        /// <summary>One facing normal shared by all positive-area patches on a planar face.
        /// Untranslated tangents avoid subtracting large world coordinates.</summary>
        public static bool FaceNormal(Vector3D worldU,Vector3D worldV,Vector3D eyeFromFace,
            out Vector3 normal,out bool reverse)
        {
            Vector3D cross=Vector3D.Cross(worldU,worldV);
            normal=Vector3.Zero; reverse=false;
            if(cross.LengthSquared()<1e-20) return false;
            reverse=Vector3D.Dot(cross,eyeFromFace)<0;
            if(reverse)cross=-cross;
            normal=(Vector3)Vector3D.Normalize(cross);
            return true;
        }

        public static ThermalVisionProjection Project(ThermalVisionTriangle triangle, MatrixD world,
            Vector3D eye, bool localCull, Vector3D localEye, out ThermalVisionWorldTriangle result)
        {
            result = new ThermalVisionWorldTriangle();
            if (localCull && IsBackFacing(triangle.LocalNormal, triangle.A, localEye))
                return ThermalVisionProjection.Backface;
            Vector3D a = Vector3D.Transform(triangle.A, world), b = Vector3D.Transform(triangle.B, world), c = Vector3D.Transform(triangle.C, world);
            Vector3D normal = Vector3D.Cross(b - a, c - a);
            double length = normal.Length();
            if (length < 1e-10) return ThermalVisionProjection.Degenerate;
            normal /= length;
            if (Vector3D.Dot(normal, eye - a) <= 0) return ThermalVisionProjection.Backface;
            Vector3D offset = normal * .002;
            result.A = a + offset; result.B = b + offset; result.C = c + offset; result.Normal = (Vector3)normal;
            return ThermalVisionProjection.Visible;
        }

        public static bool TryGetLocalEye(MatrixD world, Vector3D eye, out Vector3D localEye)
        {
            localEye = Vector3D.Zero;
            // Mirrored or singular transforms must use the world-space winding check instead.
            if (!(world.Determinant() > 1e-12)) return false;
            localEye = Vector3D.Transform(eye, MatrixD.Invert(world));
            return true;
        }

        public static bool IsBackFacing(Vector3 normal, Vector3 vertex, Vector3D localEye)
        {
            // Degenerate triangles are left to the world-space degeneracy accounting.
            return normal.LengthSquared() > 1e-20f && Vector3D.Dot(normal, localEye - vertex) <= 0;
        }
    }
}
