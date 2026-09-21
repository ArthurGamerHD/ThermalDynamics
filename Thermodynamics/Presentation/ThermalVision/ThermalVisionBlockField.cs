using System;
using System.Collections.Generic;
using VRageMath;
using Region = Thermodynamics.Presentation.ThermalVisionRegionPartition.Region;

namespace Thermodynamics.Presentation
{
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
            public List<Region> Items;
        }
        private readonly SearchNode nearest;
        public int NearestSamples { get; private set; }
/// <summary>Axis operation.</summary>
        private static double Axis(Vector3D p,int axis) { return axis==0?p.X:axis==1?p.Y:p.Z; }
/// <summary>Sets the axis.</summary>
        private static Vector3D SetAxis(Vector3D p,int axis,double v) { if(axis==0)p.X=v; else if(axis==1)p.Y=v; else p.Z=v; return p; }
/// <summary>Insert operation.</summary>
        private static void Insert(SearchNode node,Region value,int depth)
        {
            node.Min=node.HasBounds?Vector3D.Min(node.Min,value.Min):value.Min;
            node.Max=node.HasBounds?Vector3D.Max(node.Max,value.Max):value.Max; node.HasBounds=true;
            if(node.Low==null && (node.Items==null || node.Items.Count<8 || depth>=24))
            {
                if(node.Items==null) node.Items=new List<Region>(8);
                node.Items.Add(value); return;
            }
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
/// <summary>Distance operation.</summary>
        private static double Distance(Vector3D point,Vector3D min,Vector3D max)
        { return Vector3D.DistanceSquared(point,Vector3D.Clamp(point,min,max)); }
/// <summary>FindNearest operation.</summary>
        private static void FindNearest(SearchNode node,Vector3D point,double boundDistance,ref double distance,ref float kelvin)
        {
            if(node==null || !node.HasBounds || boundDistance>=distance) return;
            if(node.Low==null)
            {
                foreach(var item in node.Items)
                { double d=Distance(point,item.Min,item.Max); if(d<distance) { distance=d; kelvin=item.Kelvin; } }
                return;
            }
            double a=node.Low.HasBounds?Distance(point,node.Low.Min,node.Low.Max):double.PositiveInfinity;
            double b=node.High.HasBounds?Distance(point,node.High.Min,node.High.Max):double.PositiveInfinity;
            FindNearest(a<=b?node.Low:node.High,point,a<=b?a:b,ref distance,ref kelvin);
            FindNearest(a<=b?node.High:node.Low,point,a<=b?b:a,ref distance,ref kelvin);
        }
/// <summary>OutsideSupport operation.</summary>
        private float OutsideSupport(Vector3D point,float fallback)
        {
            if(nearest!=null && nearest.HasBounds)
            {
                double distance=double.PositiveInfinity; float temperature=fallback;
                FindNearest(nearest,point,Distance(point,nearest.Min,nearest.Max),ref distance,ref temperature); NearestSamples++; return temperature;
            }
            Fallbacks++; return fallback;
        }
        private readonly Dictionary<Vector3I, List<int>> buckets;
        private struct Kernel
        {
            public Vector3D Centre, Scale;
            public float Kelvin;
        }
        private readonly List<Kernel> kernels;
        public int Count { get; private set; }
        public int Queries { get; private set; }
        public int Fallbacks { get; private set; }
/// <summary>ThermalVisionBlockField operation.</summary>
        public ThermalVisionBlockField(double blockSize, double blur, Region? sourceBounds = null, int expectedBlocks = 0)
        {
/// <summary>List operation.</summary>
            kernels=new List<Kernel>(Math.Max(0,Math.Min(expectedBlocks,32768)));
            buckets=new Dictionary<Vector3I,List<int>>(Math.Max(0,Math.Min(expectedBlocks,8192)));
            if(sourceBounds.HasValue) nearest=new SearchNode { SpaceMin=sourceBounds.Value.Min,SpaceMax=sourceBounds.Value.Max };
            this.blur = Math.Max(blockSize * .35, blur);
            double bucketBlocks=4;
            if(sourceBounds.HasValue && expectedBlocks>0)
            {
                Vector3D extent=(sourceBounds.Value.Max-sourceBounds.Value.Min)/blockSize;
                double volume=extent.X*extent.Y*extent.Z;
                if(volume>0 && expectedBlocks/volume>=.15) bucketBlocks=2;
            }
            bucketSize = Math.Max(blockSize * bucketBlocks, this.blur * 2);
        }
/// <summary>Key operation.</summary>
        private Vector3I Key(Vector3D p)
        { return new Vector3I((int)Math.Floor(p.X/bucketSize), (int)Math.Floor(p.Y/bucketSize), (int)Math.Floor(p.Z/bucketSize)); }
        private Dictionary<Vector3D,float> sampleCache;
        private int cacheProbes;
        private bool cacheDisabled;
        public int CacheHits { get; private set; }
        public int CacheEntries { get { return sampleCache==null?0:sampleCache.Count; } }
/// <summary>SampleCached operation.</summary>
        public float SampleCached(Vector3D point,float fallback)
        {
            if(cacheDisabled || nearest==null || Count==0) return Sample(point,fallback);
            if(sampleCache==null) sampleCache=new Dictionary<Vector3D,float>();
            cacheProbes++;
            float value;
            if(sampleCache.TryGetValue(point,out value)) { CacheHits++; return value; }
            value=Sample(point,fallback);
            if(cacheProbes>=256 && CacheHits*8<cacheProbes)
            { cacheDisabled=true; sampleCache=null; }
            else
            {
                if(sampleCache.Count>=4096) sampleCache.Clear();
                sampleCache.Add(point,value);
            }
            return value;
        }
/// <summary>Adds a .</summary>
        public void Add(Region sample)
        {
            if(sampleCache!=null || cacheProbes>0)
            { sampleCache=null; cacheProbes=0; CacheHits=0; cacheDisabled=false; }
            var kernel=new Kernel { Centre=(sample.Min+sample.Max)*.5,
                Scale=(sample.Max-sample.Min)*.5+new Vector3D(blur), Kelvin=sample.Kelvin };
            int kernelIndex=kernels.Count; kernels.Add(kernel);
/// <summary>Key operation.</summary>
            Vector3I lo = Key(sample.Min-new Vector3D(blur)), hi = Key(sample.Max+new Vector3D(blur));
            for(int x=lo.X;x<=hi.X;x++) for(int y=lo.Y;y<=hi.Y;y++) for(int z=lo.Z;z<=hi.Z;z++)
            {
/// <summary>Vector3I operation.</summary>
                var key=new Vector3I(x,y,z); List<int> list;
                if(!buckets.TryGetValue(key,out list)) { list=new List<int>(); buckets.Add(key,list); }
                list.Add(kernelIndex);
            }
            if(nearest!=null) Insert(nearest,sample,0);
            Count++;
        }
/// <summary>Sample operation.</summary>
        public float Sample(Vector3D point, float fallback)
        {
            Queries++;
            List<int> list;
            if(!buckets.TryGetValue(Key(point),out list)) { return OutsideSupport(point,fallback); }
            double sum=0, total=0;
            foreach(int index in list)
            {
                Kernel sample=kernels[index];
                Vector3D q=(point-sample.Centre)/sample.Scale;
                double d=q.LengthSquared();
                if(d>=1) continue;
                double weight=(1-d)*(1-d)/Math.Max(1e-12,d);
                total+=weight; sum+=weight*sample.Kelvin;
            }
            if(total>1e-12) return (float)(sum/total);
/// <summary>OutsideSupport operation.</summary>
            return OutsideSupport(point,fallback);
        }
    }

}
