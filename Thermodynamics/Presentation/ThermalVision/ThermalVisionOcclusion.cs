using System;
using VRageMath;

namespace Thermodynamics.Presentation
{
    /// <summary>Conservative whole-box occlusion by one known solid box.</summary>
    public static class ThermalVisionOcclusion
    {
        /// <summary>Every target corner must lie strictly behind the blocker along its sight line.
        /// The shadow of one convex box is convex, so corner containment covers the entire target.
        /// Separate blockers are deliberately not combined: gaps must remain visible.</summary>
        public static bool Hidden(Vector3D eye, BoundingBoxD targetWorld, MatrixD worldToBlocker,
            BoundingBoxD solid)
        {
            Vector3D localEye=Vector3D.Transform(eye,worldToBlocker);
            if(solid.Contains(localEye)!=ContainmentType.Disjoint) return false;
            for(int i=0;i<8;i++)
            {
                var corner=new Vector3D((i&1)==0?targetWorld.Min.X:targetWorld.Max.X,
                    (i&2)==0?targetWorld.Min.Y:targetWorld.Max.Y,
                    (i&4)==0?targetWorld.Min.Z:targetWorld.Max.Z);
                Vector3D end=Vector3D.Transform(corner,worldToBlocker);
                if(solid.Contains(end)!=ContainmentType.Disjoint) return false;
                Vector3D delta=end-localEye;
                double enter=0, exit=1;
                if(!Slab(localEye.X,delta.X,solid.Min.X,solid.Max.X,ref enter,ref exit)
                    || !Slab(localEye.Y,delta.Y,solid.Min.Y,solid.Max.Y,ref enter,ref exit)
                    || !Slab(localEye.Z,delta.Z,solid.Min.Z,solid.Max.Z,ref enter,ref exit)
                    || enter<=0 || exit>=1 || exit-enter<1e-8) return false;
            }
            return true;
        }
        /// <summary>Same-space variant for region-tree traversal, without corner transforms.</summary>
        public static bool HiddenLocal(Vector3D eye,BoundingBoxD target,BoundingBoxD solid)
        {
            if(solid.Contains(eye)!=ContainmentType.Disjoint) return false;
            for(int i=0;i<8;i++)
            {
                var end=new Vector3D((i&1)==0?target.Min.X:target.Max.X,
                    (i&2)==0?target.Min.Y:target.Max.Y,(i&4)==0?target.Min.Z:target.Max.Z);
                if(solid.Contains(end)!=ContainmentType.Disjoint) return false;
                var delta=end-eye;double enter=0,exit=1;
                if(!Slab(eye.X,delta.X,solid.Min.X,solid.Max.X,ref enter,ref exit)
                    || !Slab(eye.Y,delta.Y,solid.Min.Y,solid.Max.Y,ref enter,ref exit)
                    || !Slab(eye.Z,delta.Z,solid.Min.Z,solid.Max.Z,ref enter,ref exit)
                    || enter<=0 || exit>=1 || exit-enter<1e-8)return false;
            }
            return true;
        }
        private static bool Slab(double origin,double delta,double min,double max,ref double enter,ref double exit)
        {
            if(Math.Abs(delta)<1e-12) return origin>min && origin<max;
            double a=(min-origin)/delta, b=(max-origin)/delta;
            if(a>b) { double swap=a; a=b; b=swap; }
            enter=Math.Max(enter,a); exit=Math.Min(exit,b);
            return enter<exit;
        }
    }
}
