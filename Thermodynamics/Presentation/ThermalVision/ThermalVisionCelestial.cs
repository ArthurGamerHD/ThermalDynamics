using System;
using VRageMath;

namespace Thermodynamics.Presentation
{
    public static class ThermalVisionCelestial
    {
        public struct Disc
        {
            private Vector3D centre, axis, right, up;
            private double distance, radius;
            public double AngularRadius;


            public Disc(Vector3D centreFromEye, double bodyRadius)
            {
                centre=centreFromEye; radius=bodyRadius; distance=centre.Length();
                axis=centre/distance;
                right=Vector3D.Normalize(Vector3D.Cross(axis,Math.Abs(axis.Y)<.9?Vector3D.Up:Vector3D.Right));
                up=Vector3D.Cross(right,axis);
                AngularRadius=Math.Asin(radius/distance);
            }


            public Ring PrepareRing(double fraction)
            {
                double phi=AngularRadius*Math.Max(0d,Math.Min(1d,fraction));
                double sine=Math.Sin(phi), cosine=Math.Cos(phi);
                double hit=distance*cosine-Math.Sqrt(Math.Max(0,radius*radius-distance*distance*sine*sine));
                return new Ring(axis*cosine,right*sine,up*sine,centre,hit);
            }
        }

        public struct Ring
        {
            private Vector3D axis, right, up, centre;
            private double hit;

            internal Ring(Vector3D a,Vector3D r,Vector3D u,Vector3D c,double h)
            { axis=a; right=r; up=u; centre=c; hit=h; }

            public void Sample(double cosine,double sine,out Vector3D ray,out Vector3D normal)
            {
                ray=axis+right*cosine+up*sine;
                normal=Vector3D.Normalize(ray*hit-centre);
            }
        }

        public static bool InViewport(Vector3D centre,double radius,MatrixD camera,MatrixD projection)
        {
            Vector3D p=Vector3D.TransformNormal(centre,MatrixD.Transpose(camera.GetOrientation()));
            double depth=-p.Z;
            if(depth+radius<=0) return false;
            return p.X*projection.M11+depth*(1-projection.M31)>=-radius*Math.Sqrt(projection.M11*projection.M11+(1-projection.M31)*(1-projection.M31))
                && -p.X*projection.M11+depth*(1+projection.M31)>=-radius*Math.Sqrt(projection.M11*projection.M11+(1+projection.M31)*(1+projection.M31))
                && p.Y*projection.M22+depth*(1-projection.M32)>=-radius*Math.Sqrt(projection.M22*projection.M22+(1-projection.M32)*(1-projection.M32))
                && -p.Y*projection.M22+depth*(1+projection.M32)>=-radius*Math.Sqrt(projection.M22*projection.M22+(1+projection.M32)*(1+projection.M32));
        }

        public static bool Sample(Vector3D centreFromEye,double radius,double ring,double angle,
            out Vector3D ray,out Vector3D normal)
        {
            ray=normal=Vector3D.Zero;
            double distance=centreFromEye.Length();
            if(radius<=0 || distance<=radius) return false;
            Vector3D axis=centreFromEye/distance;
            Vector3D right=Vector3D.Normalize(Vector3D.Cross(axis,Math.Abs(axis.Y)<.9?Vector3D.Up:Vector3D.Right));
            Vector3D up=Vector3D.Cross(right,axis);
            double phi=Math.Asin(radius/distance)*Math.Max(0d,Math.Min(1d,ring));
            ray=axis*Math.Cos(phi)+(right*Math.Cos(angle)+up*Math.Sin(angle))*Math.Sin(phi);
            double along=Vector3D.Dot(centreFromEye,ray);
            double hit=along-Math.Sqrt(Math.Max(0,radius*radius-distance*distance*Math.Sin(phi)*Math.Sin(phi)));
            normal=Vector3D.Normalize(ray*hit-centreFromEye);
            return true;
        }


        public static bool Project(Vector3D ray,MatrixD camera,double depth,out Vector3D point)
        {
            double forward=Vector3D.Dot(ray,camera.Forward);
            point=Vector3D.Zero;
            if(forward<=.0001) return false;
            point=camera.Translation+ray*(depth/forward);
            return true;
        }
    }
}
