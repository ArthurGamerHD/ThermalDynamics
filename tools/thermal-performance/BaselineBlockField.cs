using Thermodynamics.Presentation;
// Frozen pre-optimization reference; offline only.
using System;
using System.Collections.Generic;
using VRageMath;
using Region = Thermodynamics.Presentation.ThermalVisionRegionPartition.Region;

namespace ThermalPerformance.Baseline
{
    /// <summary>Original block temperatures indexed independently of the render partition.</summary>
    public sealed class ThermalVisionBlockField
    {
        private readonly double blur, bucketSize;
        private sealed class SearchNode
        {
            public Vector3D Min,Max,SpaceMin,SpaceMax;
            public bool HasBounds;
            public int Axis;
            public double Plane;
            public SearchNode Low,High;
            public List<Region> Items=new List<Region>();
        }
        private readonly SearchNode nearest;
        public int NearestSamples { get; private set; }
        private static double Axis(Vector3D p,int axis) { return axis==0?p.X:axis==1?p.Y:p.Z; }
        private static Vector3D SetAxis(Vector3D p,int axis,double v) { if(axis==0)p.X=v; else if(axis==1)p.Y=v; else p.Z=v; return p; }
        private static void Insert(SearchNode node,Region value,int depth)
        {
            node.Min=node.HasBounds?Vector3D.Min(node.Min,value.Min):value.Min;
            node.Max=node.HasBounds?Vector3D.Max(node.Max,value.Max):value.Max; node.HasBounds=true;
            if(node.Low==null && (node.Items.Count<8 || depth>=24)) { node.Items.Add(value); return; }
            if(node.Low==null)
            {
                Vector3D size=node.SpaceMax-node.SpaceMin;
                node.Axis=size.X>=size.Y&&size.X>=size.Z?0:size.Y>=size.Z?1:2;
                node.Plane=(Axis(node.SpaceMin,node.Axis)+Axis(node.SpaceMax,node.Axis))*.5;
                node.Low=new SearchNode { SpaceMin=node.SpaceMin,SpaceMax=SetAxis(node.SpaceMax,node.Axis,node.Plane) };
                node.High=new SearchNode { SpaceMin=SetAxis(node.SpaceMin,node.Axis,node.Plane),SpaceMax=node.SpaceMax };
                foreach(var item in node.Items) Insert(Axis((item.Min+item.Max)*.5,node.Axis)<node.Plane?node.Low:node.High,item,depth+1);
                node.Items=null;
            }
            Insert(Axis((value.Min+value.Max)*.5,node.Axis)<node.Plane?node.Low:node.High,value,depth+1);
        }
        private static double Distance(Vector3D point,Vector3D min,Vector3D max)
        { return Vector3D.DistanceSquared(point,Vector3D.Clamp(point,min,max)); }
        private static void FindNearest(SearchNode node,Vector3D point,ref double distance,ref float kelvin)
        {
            if(node==null || !node.HasBounds || Distance(point,node.Min,node.Max)>=distance) return;
            if(node.Low==null)
            {
                foreach(var item in node.Items)
                { double d=Distance(point,item.Min,item.Max); if(d<distance) { distance=d; kelvin=item.Kelvin; } }
                return;
            }
            double a=node.Low.HasBounds?Distance(point,node.Low.Min,node.Low.Max):double.PositiveInfinity;
            double b=node.High.HasBounds?Distance(point,node.High.Min,node.High.Max):double.PositiveInfinity;
            FindNearest(a<=b?node.Low:node.High,point,ref distance,ref kelvin);
            FindNearest(a<=b?node.High:node.Low,point,ref distance,ref kelvin);
        }
        private float OutsideSupport(Vector3D point,float fallback)
        {
            if(nearest!=null && nearest.HasBounds)
            {
                double distance=double.PositiveInfinity; float temperature=fallback;
                FindNearest(nearest,point,ref distance,ref temperature); NearestSamples++; return temperature;
            }
            Fallbacks++; return fallback;
        }
        private readonly Dictionary<Vector3I, List<Region>> buckets = new Dictionary<Vector3I, List<Region>>();
        public int Count { get; private set; }
        public int Queries { get; private set; }
        public int Fallbacks { get; private set; }
        public ThermalVisionBlockField(double blockSize, double blur, Region? sourceBounds = null)
        {
            if(sourceBounds.HasValue) nearest=new SearchNode { SpaceMin=sourceBounds.Value.Min,SpaceMax=sourceBounds.Value.Max };
            this.blur = Math.Max(blockSize * .35, blur);
            bucketSize = Math.Max(blockSize * 4, this.blur * 2);
        }
        private Vector3I Key(Vector3D p)
        { return new Vector3I((int)Math.Floor(p.X/bucketSize), (int)Math.Floor(p.Y/bucketSize), (int)Math.Floor(p.Z/bucketSize)); }
        public void Add(Region sample)
        {
            // Index the support, including large multi-cell functional blocks. Queries touch
            // one bucket rather than walking the entire ship for every surface vertex.
            Vector3I lo = Key(sample.Min-new Vector3D(blur)), hi = Key(sample.Max+new Vector3D(blur));
            for(int x=lo.X;x<=hi.X;x++) for(int y=lo.Y;y<=hi.Y;y++) for(int z=lo.Z;z<=hi.Z;z++)
            {
                var key=new Vector3I(x,y,z); List<Region> list;
                if(!buckets.TryGetValue(key,out list)) { list=new List<Region>(); buckets.Add(key,list); }
                list.Add(sample);
            }
            if(nearest!=null) Insert(nearest,sample,0);
            Count++;
        }
        public float Sample(Vector3D point, float fallback)
        {
            Queries++;
            List<Region> list;
            if(!buckets.TryGetValue(Key(point),out list)) { return OutsideSupport(point,fallback); }
            double sum=0, total=0;
            foreach(var sample in list)
            {
                Vector3D centre=(sample.Min+sample.Max)*.5;
                Vector3D scale=(sample.Max-sample.Min)*.5+new Vector3D(blur);
                Vector3D q=(point-centre)/scale;
                double d=q.LengthSquared();
                if(d>=1) continue;
                // Interpolating kernel: a block centre retains its own temperature,
                // while edges blend continuously without inventing hotter values.
                double weight=(1-d)*(1-d)/Math.Max(1e-12,d);
                total+=weight; sum+=weight*sample.Kelvin;
            }
            if(total>1e-12) return (float)(sum/total);
            return OutsideSupport(point,fallback);
        }
    }

    /// <summary>Prepared scalar lattice: no block searches or field reconstruction during draw.</summary>
    public sealed class ThermalVisionSurfaceField
    {
        public readonly Region Bounds;
        public readonly Vector3I Steps;
        public readonly float[] Values;
        public int PairedFaceMask;
        public ThermalVisionSurfaceField(Region bounds, double spacing)
        {
            Bounds=bounds;
            Vector3D size=bounds.Max-bounds.Min;
            Steps=new Vector3I(Step(size.X,spacing),Step(size.Y,spacing),Step(size.Z,spacing));
            Values=new float[(Steps.X+1)*(Steps.Y+1)*(Steps.Z+1)];
        }
        private static int Step(double size,double spacing) { return Math.Max(1,Math.Min(4,(int)Math.Ceiling(size/Math.Max(.001,spacing)))); }
        public int Index(int x,int y,int z) { return (z*(Steps.Y+1)+y)*(Steps.X+1)+x; }
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
        public IList<Patch> Face(int axis,bool upper) { return faces[axis*2+(upper?1:0)]; }
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
        private Vector3D FacePoint(int axis,bool upper,int u,int v)
        {
            if(axis==0) return Point(upper?Steps.X:0,u,v);
            if(axis==1) return Point(u,upper?Steps.Y:0,v);
            return Point(u,v,upper?Steps.Z:0);
        }
        private void SplitFace(int axis,bool upper,int u0,int u1,int v0,int v1,float error,List<Patch> list)
        {
            Vector3D a=FacePoint(axis,upper,u0,v0), b=FacePoint(axis,upper,u1,v0),
                c=FacePoint(axis,upper,u1,v1), d=FacePoint(axis,upper,u0,v1);
            float ta=Sample(a), tb=Sample(b), tc=Sample(c), td=Sample(d);
            bool split=false;
            // Preserve changes at every lattice vertex before merging surface patches.
            for(int v=v0;v<=v1 && !split;v++) for(int u=u0;u<=u1 && !split;u++)
            {
                double x=(double)(u-u0)/(u1-u0), y=(double)(v-v0)/(v1-v0);
                double estimate=x>=y ? ta*(1-x)+tb*(x-y)+tc*y : ta*(1-y)+tc*x+td*(y-x);
                if(Math.Abs(Sample(FacePoint(axis,upper,u,v))-estimate)>error) split=true;
            }
            // A bilinear saddle may differ between sampled vertices. Check centres too;
            // leaf patches are the intended two-triangle approximation of the lattice.
            for(int v=v0;v<v1 && !split;v++) for(int u=u0;u<u1 && !split;u++)
            {
                double x=(u+.5-u0)/(u1-u0), y=(v+.5-v0)/(v1-v0);
                double estimate=x>=y ? ta*(1-x)+tb*(x-y)+tc*y : ta*(1-y)+tc*x+td*(y-x);
                if(Math.Abs(Sample((FacePoint(axis,upper,u,v)+FacePoint(axis,upper,u+1,v+1))*.5)-estimate)>error) split=true;
            }
            if(split && (u1-u0>1 || v1-v0>1))
            {
                if(u1-u0>=v1-v0 && u1-u0>1)
                { int m=(u0+u1)/2; SplitFace(axis,upper,u0,m,v0,v1,error,list); SplitFace(axis,upper,m,u1,v0,v1,error,list); }
                else
                { int m=(v0+v1)/2; SplitFace(axis,upper,u0,u1,v0,m,error,list); SplitFace(axis,upper,u0,u1,m,v1,error,list); }
            }
            else list.Add(new Patch { A=a,B=b,C=c,D=d, Temperatures=new Vector4(ta,tb,tc,td), PreviousTemperatures=new Vector4(ta,tb,tc,td) });
        }
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
        public static Vector4 PatchTemperatures(Patch patch,float blend)
        {
            if(blend>=1) return patch.Temperatures;
            blend=Math.Max(0f,blend); float weight=blend*blend*(3-2*blend);
            return patch.PreviousTemperatures+(patch.Temperatures-patch.PreviousTemperatures)*weight;
        }
        public float BlendSample(ThermalVisionSurfaceField previous,Vector3D point,float blend)
        {
            float current=Sample(point);
            if(previous==null) return current;
            blend=Math.Max(0f,Math.Min(1f,blend));
            return MathHelper.Lerp(previous.Sample(point),current,blend*blend*(3-2*blend));
        }
        public float Sample(Vector3D point)
        {
            Vector3D t=Vector3D.Clamp((point-Bounds.Min)/(Bounds.Max-Bounds.Min),Vector3D.Zero,Vector3D.One)*new Vector3D(Steps.X,Steps.Y,Steps.Z);
            int x=Math.Min(Steps.X-1,(int)t.X), y=Math.Min(Steps.Y-1,(int)t.Y), z=Math.Min(Steps.Z-1,(int)t.Z);
            t-=new Vector3D(x,y,z); double value=0;
            for(int i=0;i<8;i++)
                value+=Values[Index(x+(i&1),y+((i>>1)&1),z+((i>>2)&1))]
                    *((i&1)==0?1-t.X:t.X)*((i&2)==0?1-t.Y:t.Y)*((i&4)==0?1-t.Z:t.Z);
            return (float)value;
        }
    }
}
