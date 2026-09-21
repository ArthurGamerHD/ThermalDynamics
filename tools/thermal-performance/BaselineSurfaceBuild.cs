using Thermodynamics.Presentation;
// Represents a frozen surface builder for offline patch coordinate construction.
using System;
using System.Collections.Generic;
using VRageMath;
using Region=Thermodynamics.Presentation.ThermalVisionRegionPartition.Region;
namespace ThermalPerformance.SurfaceBuildBaseline
{
    public sealed class ThermalVisionSurfaceField
    {
        public readonly Region Bounds;
        public readonly Vector3I Steps;
        public readonly float[] Values;
        public int PairedFaceMask;
         // Initializes a new surface field with the given bounds and spacing.
         public ThermalVisionSurfaceField(Region bounds, double spacing)
         {
             Bounds=bounds;
             Vector3D size=bounds.Max-bounds.Min;
             Steps=new Vector3I(Step(size.X,spacing),Step(size.Y,spacing),Step(size.Z,spacing));
             Values=new float[(Steps.X+1)*(Steps.Y+1)*(Steps.Z+1)];
         }
         // Computes the number of steps for a given size and spacing, clamped between 1 and 4.
         private static int Step(double size,double spacing) { return Math.Max(1,Math.Min(4,(int)Math.Ceiling(size/Math.Max(.001,spacing)))); }
         // Computes the linear index for the given 3D coordinates.
         public int Index(int x,int y,int z) { return (z*(Steps.Y+1)+y)*(Steps.X+1)+x; }
         // Computes the 3D point corresponding to the given lattice coordinates.
         public Vector3D Point(int x,int y,int z)
         { return Bounds.Min+(Bounds.Max-Bounds.Min)*new Vector3D((double)x/Steps.X,(double)y/Steps.Y,(double)z/Steps.Z); }
        public struct Patch
        {
            public Vector3D A, B, C, D;
            public Vector4 Temperatures, PreviousTemperatures;
        }
        public float Minimum { get; private set; }
        public float Maximum { get; private set; }
        private readonly List<Patch>[] faces = new List<Patch>[6];
         // Returns the list of patches for the given axis and side (upper or lower).
         public IList<Patch> Face(int axis,bool upper) { return faces[axis*2+(upper?1:0)]; }
         // Builds the surface patches by splitting faces based on the error tolerance.
         public void BuildFaces(float errorKelvin)
         {
             Minimum=float.PositiveInfinity; Maximum=float.NegativeInfinity;
             foreach(float value in Values) { Minimum=Math.Min(Minimum,value); Maximum=Math.Max(Maximum,value); }
             for(int axis=0;axis<3;axis++) for(int side=0;side<2;side++)
             {
                 var list=new List<Patch>(); faces[axis*2+side]=list;
                 int nu=axis==0?Steps.Y:Steps.X, nv=axis==2?Steps.Y:Steps.Z;
                 SplitFace(axis,side==1,0,nu,0,nv,errorKelvin,list);
             }
         }
         // Computes the 3D point for a given face coordinates (axis, upper, u, v).
         private Vector3D FacePoint(int axis,bool upper,int u,int v)
         {
             if(axis==0) return Point(upper?Steps.X:0,u,v);
             if(axis==1) return Point(u,upper?Steps.Y:0,v);
             return Point(u,v,upper?Steps.Z:0);
         }
         // Retrieves the temperature value at the given face coordinates (axis, upper, u, v).
         private float FaceValue(int axis,bool upper,int u,int v)
         {
             if(axis==0) return Values[Index(upper?Steps.X:0,u,v)];
             if(axis==1) return Values[Index(u,upper?Steps.Y:0,v)];
             return Values[Index(u,v,upper?Steps.Z:0)];
         }
         // Recursively splits a face into patches if the error tolerance is exceeded.
         private void SplitFace(int axis,bool upper,int u0,int u1,int v0,int v1,float error,List<Patch> list)
         {
             Vector3D a=FacePoint(axis,upper,u0,v0), b=FacePoint(axis,upper,u1,v0),
                 c=FacePoint(axis,upper,u1,v1), d=FacePoint(axis,upper,u0,v1);
             float ta=FaceValue(axis,upper,u0,v0), tb=FaceValue(axis,upper,u1,v0),
                 tc=FaceValue(axis,upper,u1,v1), td=FaceValue(axis,upper,u0,v1);
             bool split=false;
             // Check lattice vertices for changes that require splitting.
             for(int v=v0;v<=v1 && !split;v++) for(int u=u0;u<=u1 && !split;u++)
             {
                 double x=(double)(u-u0)/(u1-u0), y=(double)(v-v0)/(v1-v0);
                 double estimate=x>=y ? ta*(1-x)+tb*(x-y)+tc*y : ta*(1-y)+tc*x+td*(y-x);
                 if(Math.Abs(FaceValue(axis,upper,u,v)-estimate)>error) split=true;
             }
             // Check center points for changes that require splitting.
             // Leaf patches approximate the lattice with two triangles.
             for(int v=v0;v<v1 && !split;v++) for(int u=u0;u<u1 && !split;u++)
             {
                 double x=(u+.5-u0)/(u1-u0), y=(v+.5-v0)/(v1-v0);
                 double estimate=x>=y ? ta*(1-x)+tb*(x-y)+tc*y : ta*(1-y)+tc*x+td*(y-x);
                 double midpoint=((double)FaceValue(axis,upper,u,v)+FaceValue(axis,upper,u+1,v)
                     +FaceValue(axis,upper,u,v+1)+FaceValue(axis,upper,u+1,v+1))*.25;
                 if(Math.Abs(midpoint-estimate)>error) split=true;
             }
             if(split && (u1-u0>1 || v1-v0>1))
             {
                 if(u1-u0>=v1-v0 && u1-u0>1)
                 { int m=(u0+u1)/2; SplitFace(axis,upper,u0,m,v0,v1,error,list); SplitFace(axis,upper,m,u1,v0,v1,error,list); }
                 else
                 { int m=(v0+v1)/2; SplitFace(axis,upper,u0,u1,v0,m,error,list); SplitFace(axis,upper,u0,u1,m,v1,error,list); }
             }
             else list.Add(new Patch { A=a,B=b=C=c,D=d, Temperatures=new Vector4(ta,tb,tc,td), PreviousTemperatures=new Vector4(ta,tb,tc,td) });
         }
         // Prepares the surface field for transition by storing previous temperatures from another field.
         public void PrepareTransition(ThermalVisionSurfaceField previous)
         {
             foreach(var face in faces)
                 for(int i=0;i<face.Count;i++)
                 {
                     var patch=face[i];
                     patch.PreviousTemperatures=new Vector4(previous.Sample(patch.A),previous.Sample(patch.B),previous.Sample(patch.C),previous.Sample(patch.D));
                     face[i]=patch;
                 }
         }
         // Computes the blended temperatures for a patch based on the blend factor.
         public static Vector4 PatchTemperatures(Patch patch,float blend)
         {
             if(blend>=1) return patch.Temperatures;
             blend=Math.Max(0f,blend); float weight=blend*blend*(3-2*blend);
             return patch.PreviousTemperatures+(patch.Temperatures-patch.PreviousTemperatures)*weight;
         }
         // Computes a blended sample between the current and previous field at the given point.
         public float BlendSample(ThermalVisionSurfaceField previous,Vector3D point,float blend)
         {
             float current=Sample(point);
             if(previous==null) return current;
             blend=Math.Max(0f,Math.Min(1f,blend));
             return MathHelper.Lerp(previous.Sample(point),current,blend*blend*(3-2*blend));
         }
         // Samples the temperature at the given point using trilinear interpolation.
         public float Sample(Vector3D point)
         {
             Vector3D t=Vector3D.Clamp((point-Bounds.Min)/(Bounds.Max-Bounds.Min),Vector3D.Zero,Vector3D.One)*new Vector3D(Steps.X,Steps.Y,Steps.Z);
             int x=Math.Min(Steps.X-1,(int)t.X), y=Math.Min(Steps.Y-1,(int)t.Y), z=Math.Min(Steps.Z-1,(int)t.Z);
             t-=new Vector3D(x,y,z);
             int row=Steps.X+1, layer=row*(Steps.Y+1), index=Index(x,y,z);
             double ix=1-t.X, iy=1-t.Y, iz=1-t.Z;
             // Maintain original corner summation order for consistency.
             // Corner addresses differ by row/layer strides.
             double value=0;
             value+=Values[index]*ix*iy*iz;
             value+=Values[index+1]*t.X*iy*iz;
             value+=Values[index+row]*ix*t.Y*iz;
             value+=Values[index+row+1]*t.X*t.Y*iz;
             value+=Values[index+layer]*ix*iy*t.Z;
             value+=Values[index+layer+1]*t.X*iy*t.Z;
             value+=Values[index+layer+row]*ix*t.Y*t.Z;
             value+=Values[index+layer+row+1]*t.X*t.Y*t.Z;
             return (float)value;
         }
    }
}
