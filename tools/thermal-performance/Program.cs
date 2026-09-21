using Thermodynamics.Presentation;
using System.Diagnostics;
using System.Text.Json;
using VRageMath;
using Region=Thermodynamics.Presentation.ThermalVisionRegionPartition.Region;
using Old=ThermalPerformance.Baseline.ThermalVisionBlockField;
using Current=Thermodynamics.Presentation.ThermalVisionBlockField;
if(args.Contains("--occlusion"))
{
/// <summary>List operation.</summary>
    var cells=new List<Region>();
    for(int x=-8;x<8;x++)for(int y=-8;y<8;y++)for(int z=10;z<26;z++)
/// <summary>Vector3D operation.</summary>
    {var p=new Vector3D(x,y,z);cells.Add(new Region(p,p+Vector3D.One,300));}
    if(!ThermalVisionRegionOrder.TryBuild(cells,8192,out var occlusionOrder))throw new Exception("Tree failed");
    var eye=Vector3D.Zero;
/// <summary>BoundingFrustumD operation.</summary>
    var frustum=new BoundingFrustumD(MatrixD.CreateLookAt(eye,new Vector3D(0,0,1),Vector3D.Up)*MatrixD.CreatePerspectiveFieldOfView(1.5,1.8,.1,100));
/// <summary>BoundingBoxD operation.</summary>
    var blockers=new List<BoundingBoxD>{new BoundingBoxD(new Vector3D(-1,-1,2),new Vector3D(1,1,3))};
/// <summary>List operation.</summary>
    var all=new List<Region>();var output=new List<Region>();
    occlusionOrder.WriteVisibleNearToFar(eye,all,frustum);
    var expected=all.Where(r=>!ThermalVisionOcclusion.Hidden(eye,new BoundingBoxD(r.Min,r.Max),MatrixD.Identity,blockers[0])).ToList();
    occlusionOrder.WriteVisibleNearToFar(eye,output,frustum,blockers,out int hidden);
    if(!expected.SequenceEqual(output))throw new Exception("Occlusion output mismatch");
    int kept=output.Count;
/// <summary>RunTree operation.</summary>
     double RunTree(){double n=0;for(int i=0;i<100;i++){occlusionOrder.WriteVisibleNearToFar(eye,output,frustum,blockers,out int count);n+=output.Count;}return n;}
/// <summary>RunFlat operation.</summary>
     double RunFlat(){double n=0;for(int i=0;i<100;i++){occlusionOrder.WriteVisibleNearToFar(eye,output,frustum);foreach(var r in output)if(!ThermalVisionOcclusion.HiddenLocal(eye,new BoundingBoxD(r.Min,r.Max),blockers[0]))n++;}return n;}
    Console.WriteLine(JsonSerializer.Serialize(new {regions=all.Count,visible=kept,hiddenSubtrees=hidden,exactVisibility=true,traversals=100,flat=Measure(RunFlat),hierarchical=Measure(RunTree)},new JsonSerializerOptions{WriteIndented=true}));
    return;
}
if(args.Contains("--nearest"))
{
/// <summary>Region operation.</summary>
    var bounds=new Region(Vector3D.Zero,new Vector3D(64),0);
/// <summary>Old operation.</summary>
    var old=new Old(1,.6,bounds);var current=new Current(1,.6,bounds);
    for(int x=0;x<16;x++)for(int y=0;y<16;y++)for(int z=0;z<16;z++)
    {
/// <summary>Vector3D operation.</summary>
        var p=new Vector3D(x,y,z)*4;
/// <summary>Region operation.</summary>
        var block=new Region(p,p+Vector3D.One,200+(x*31+y*19+z*7)%600);
        old.Add(block);current.Add(block);
    }
/// <summary>List operation.</summary>
    var points=new List<Vector3D>();var random=new Random(711);
    for(int i=0;i<16384;i++)points.Add(new Vector3D(random.NextDouble()*64,-5-random.NextDouble()*20,random.NextDouble()*64));
    foreach(var p in points)if(old.Sample(p,293)!=current.Sample(p,293))throw new Exception("Nearest heat changed");
/// <summary>RunNearest operation.</summary>
     double RunNearest(bool reference){double sum=0;foreach(var p in points)sum+=reference?old.Sample(p,293):current.Sample(p,293);return sum;}
    Console.WriteLine(JsonSerializer.Serialize(new {blocks=4096,queries=points.Count,exactOutput=true,baseline=Measure(()=>RunNearest(true)),optimized=Measure(()=>RunNearest(false))},new JsonSerializerOptions{WriteIndented=true}));
    return;
}
if(args.Contains("--caps"))
{
/// <summary>ThermalVisionSurfaceField operation.</summary>
    var field=new ThermalVisionSurfaceField(new Region(Vector3D.Zero,new Vector3D(4),300),1);
/// <summary>ThermalVisionSurfaceField operation.</summary>
    var previous=new ThermalVisionSurfaceField(field.Bounds,1);
/// <summary>Random operation.</summary>
    var random=new Random(872);
    for(int i=0;i<field.Values.Length;i++) { field.Values[i]=200+(float)random.NextDouble()*800;previous.Values[i]=200+(float)random.NextDouble()*800; }
    var reference=new ThermalPerformance.BaselineCapSampling(field);
/// <summary>List operation.</summary>
    var oldOutput=new List<ThermalVisionSurfaceField.TemperatureTriangle>(64);
/// <summary>List operation.</summary>
    var newOutput=new List<ThermalVisionSurfaceField.TemperatureTriangle>(64);
    var inputs=new List<(Vector3D a,Vector3D b,Vector3D c,float blend)>();
    for(int i=0;i<1024;i++)
    {
        double z=random.NextDouble()*4;
        inputs.Add((new Vector3D(0,0,z),new Vector3D(4,0,z),new Vector3D(4,4,z),(i%5)/4f));
    }
    int triangles=0;
    foreach(var input in inputs)
    {
        oldOutput.Clear();newOutput.Clear();
        reference.AppendCapTriangles(input.a,input.b,input.c,previous,input.blend,20,oldOutput);
        field.AppendCapTriangles(input.a,input.b,input.c,previous,input.blend,20,newOutput);
        if(oldOutput.Count!=newOutput.Count)throw new Exception("Cap topology changed");
        for(int i=0;i<oldOutput.Count;i++)
        {
            var a=oldOutput[i];var b=newOutput[i];
            if(a.A!=b.A || a.B!=b.B || a.C!=b.C || a.Temperatures!=b.Temperatures)throw new Exception("Cap output changed");
        }
        triangles+=newOutput.Count;
    }
/// <summary>RunCaps operation.</summary>
     double RunCaps(bool old)
     {
         double sum=0;
         foreach(var input in inputs)
         {
             newOutput.Clear();
             if(old)reference.AppendCapTriangles(input.a,input.b,input.c,previous,input.blend,20,newOutput);
             else field.AppendCapTriangles(input.a,input.b,input.c,previous,input.blend,20,newOutput);
             sum+=newOutput.Count;
         }
         return sum;
     }
    Console.WriteLine(JsonSerializer.Serialize(new {caps=inputs.Count,triangles,exactOutput=true,baseline=Measure(()=>RunCaps(true)),optimized=Measure(()=>RunCaps(false))},new JsonSerializerOptions{WriteIndented=true}));
    return;
}
/// <summary>List operation.</summary>
var rows=new List<object>();
foreach(string name in new[]{"dense","hull","sparse","multi-cell","dense-large"})
{
/// <summary>List operation.</summary>
    var blocks=new List<Region>(); var points=new List<Vector3D>();
    int nx=name=="dense-large"?40:20, ny=name=="dense-large"?24:12;
    for(int x=0;x<nx;x++) for(int y=0;y<ny;y++) for(int z=0;z<ny;z++)
    {
        if(name=="hull" && x>0&&x<19&&y>0&&y<11&&z>0&&z<11) continue;
/// <summary>Vector3D operation.</summary>
        var p=new Vector3D(x,y,z)*(name=="sparse"?4:1);
/// <summary>Vector3D operation.</summary>
        var size=name=="multi-cell"?new Vector3D(1+(x%3),1+(y%2),1):Vector3D.One;
        blocks.Add(new Region(p,p+size,230+(x*31+y*19+z*7)%600));
        for(int i=0;i<8;i++) points.Add(p+new Vector3D(i&1,(i>>1)&1,(i>>2)&1));
    }
    var boundMax=blocks.Aggregate(Vector3D.Zero,(m,b)=>Vector3D.Max(m,b.Max));
/// <summary>Region operation.</summary>
    var bounds=new Region(Vector3D.Zero,boundMax,0);
/// <summary>MakeOld operation.</summary>
     Old MakeOld() { var f=new Old(1,.6,bounds); foreach(var b in blocks)f.Add(b); return f; }
/// <summary>MakeNew operation.</summary>
     Current MakeNew() {var f=new Current(1,.6,bounds,blocks.Count); foreach(var b in blocks)f.Add(b);return f;}
    var old=MakeOld(); var current=MakeNew();
    float maxError=0;
    foreach(var p in points) maxError=Math.Max(maxError,Math.Abs(old.Sample(p,293)-current.Sample(p,293)));
    if(maxError!=0) throw new Exception(name+" changed samples: "+maxError);
/// <summary>QueryOld operation.</summary>
     double QueryOld(){double sum=0;foreach(var p in points)sum+=old.Sample(p,293);return sum;}
/// <summary>QueryNew operation.</summary>
     double QueryNew(){double sum=0;foreach(var p in points)sum+=current.Sample(p,293);return sum;}
/// <summary>QueryCached operation.</summary>
     double QueryCached(){double sum=0;var cache=new Dictionary<Vector3D,float>();foreach(var p in points){float t;if(!cache.TryGetValue(p,out t)){t=current.Sample(p,293);cache.Add(p,t);}sum+=t;}return sum;}
    var oldQuery=Measure(QueryOld);var newQuery=Measure(QueryNew);var cached=Measure(QueryCached);
/// <summary>Adaptive operation.</summary>
     double Adaptive(){var f=MakeNew(); double sum=0;foreach(var p in points)sum+=f.SampleCached(p,293);return sum;}
/// <summary>BaselineTotal operation.</summary>
     double BaselineTotal(){var f=MakeOld();double sum=0;foreach(var p in points)sum+=f.Sample(p,293);return sum;}
    foreach(var p in points)if(old.Sample(p,293)!=current.SampleCached(p,293))throw new Exception("cache changed heat");
    rows.Add(new{name,blocks=blocks.Count,queries=points.Count,unique=points.Distinct().Count(),maxError,
        oldBuild=Measure(()=>MakeOld().Count),newBuild=Measure(()=>MakeNew().Count),oldQuery,newQuery,cached,baselineTotal=Measure(BaselineTotal),adaptiveTotal=Measure(Adaptive)});
}
/// <summary>Random operation.</summary>
var differentialRandom=new Random(809);
for(int trial=0;trial<24;trial++)
{
    double size=trial%2==0?.5:2.5, blur=size*(.35+trial%6);
/// <summary>Region operation.</summary>
    var bounds=new Region(new Vector3D(-50),new Vector3D(50),0);
/// <summary>Old operation.</summary>
    var reference=new Old(size,blur,bounds);
/// <summary>Current operation.</summary>
    var candidate=new Current(size,blur,bounds,trial%2==0?1000000:100);
    for(int i=0;i<100;i++)
    {
/// <summary>Vector3D operation.</summary>
        var min=new Vector3D(differentialRandom.NextDouble()*60-30,differentialRandom.NextDouble()*60-30,differentialRandom.NextDouble()*60-30);
/// <summary>Region operation.</summary>
        var block=new Region(min,min+new Vector3D(size*(1+i%4),size,size),200+i*7);
        reference.Add(block);candidate.Add(block);
    }
    for(int i=0;i<500;i++)
    {
/// <summary>Vector3D operation.</summary>
        var p=new Vector3D(differentialRandom.NextDouble()*100-50,differentialRandom.NextDouble()*100-50,differentialRandom.NextDouble()*100-50);
        if(reference.Sample(p,293)!=candidate.SampleCached(p,293))throw new Exception("Differential heat mismatch");
    }
}
/// <summary>Random operation.</summary>
var rng=new Random(77); var surfaces=new List<(Region box,float[] values)>();
for(int i=0;i<400;i++)
{
/// <summary>Vector3D operation.</summary>
    var min=new Vector3D(rng.NextDouble()*100-50,rng.NextDouble()*100-50,rng.NextDouble()*100-50);
/// <summary>Region operation.</summary>
    var box=new Region(min,min+new Vector3D(.1+rng.NextDouble()*8,.1+rng.NextDouble()*8,.1+rng.NextDouble()*8),0);
/// <summary>ThermalVisionSurfaceField operation.</summary>
    var field=new ThermalVisionSurfaceField(box,1);
    for(int j=0;j<field.Values.Length;j++)field.Values[j]=230+(float)rng.NextDouble()*600;
    surfaces.Add((box,field.Values));
}
double maxPatchError=0;
foreach(var item in surfaces)
{
    var a=new ThermalPerformance.Baseline.ThermalVisionSurfaceField(item.box,1);
/// <summary>ThermalVisionSurfaceField operation.</summary>
    var b=new ThermalVisionSurfaceField(item.box,1);
    Array.Copy(item.values,a.Values,item.values.Length);Array.Copy(item.values,b.Values,item.values.Length);
    a.BuildFaces(20);b.BuildFaces(20);
    for(int axis=0;axis<3;axis++)foreach(bool upper in new[]{false,true})
    {
        var af=a.Face(axis,upper);var bf=b.Face(axis,upper);
        if(af.Count!=bf.Count)throw new Exception("patch topology changed");
        for(int i=0;i<af.Count;i++)
        {
            if(af[i].A!=bf[i].A||af[i].B!=bf[i].B||af[i].C!=bf[i].C||af[i].D!=bf[i].D)throw new Exception("patch geometry changed");
            maxPatchError=Math.Max(maxPatchError,(af[i].Temperatures-bf[i].Temperatures).Length());
        }
    }
}
if(maxPatchError>.001)throw new Exception("patch temperatures changed");
/// <summary>OldFaces operation.</summary>
     double OldFaces(){double sum=0;foreach(var item in surfaces){var f=new ThermalPerformance.Baseline.ThermalVisionSurfaceField(item.box,1);Array.Copy(item.values,f.Values,item.values.Length);f.BuildFaces(20);sum+=f.Minimum;}return sum;}
/// <summary>NewFaces operation.</summary>
     double NewFaces(){double sum=0;foreach(var item in surfaces){var f=new ThermalVisionSurfaceField(item.box,1);Array.Copy(item.values,f.Values,item.values.Length);f.BuildFaces(20);sum+=f.Minimum;}return sum;}
rows.Add(new{name="surface-patches",surfaces=surfaces.Count,maxPatchError,baseline=Measure(OldFaces),optimized=Measure(NewFaces)});
/// <summary>List operation.</summary>
var regions=new List<Region>();
for(int x=0;x<20;x++)for(int y=0;y<20;y++)for(int z=0;z<20;z++){var p=new Vector3D(x,y,z);regions.Add(new Region(p,p+Vector3D.One,300));}
if(!ThermalVisionRegionOrder.TryBuild(regions,8192,out var order))throw new Exception("BSP build failed");
/// <summary>List operation.</summary>
var full=new List<Region>();var filtered=new List<Region>();
foreach(var eye in new[]{new Vector3D(10,10,10),new Vector3D(10,10,40),new Vector3D(10,10,100)})
{
/// <summary>BoundingFrustumD operation.</summary>
    var frustum=new BoundingFrustumD(MatrixD.CreateLookAt(eye,new Vector3D(10,10,0),Vector3D.Up)*MatrixD.CreatePerspectiveFieldOfView(.8,1.8,.1,200));
    order.WriteNearToFar(eye,full);
    var reference=full.Where(r=>frustum.Contains(new BoundingBoxD(r.Min,r.Max))!=ContainmentType.Disjoint).ToArray();
    order.WriteVisibleNearToFar(eye,filtered,frustum);
    if(!reference.SequenceEqual(filtered))throw new Exception("Culling changed leaf order/coverage");
/// <summary>Linear operation.</summary>
    double Linear(){order.WriteNearToFar(eye,full);int count=0;foreach(var r in full)if(frustum.Contains(new BoundingBoxD(r.Min,r.Max))!=ContainmentType.Disjoint)count++;return count;}
/// <summary>Hierarchical operation.</summary>
    double Hierarchical(){order.WriteVisibleNearToFar(eye,filtered,frustum);return filtered.Count;}
    rows.Add(new{name="traversal-"+eye.Z,leaves=order.LeafCount,visible=filtered.Count,baseline=Measure(Linear),optimized=Measure(Hierarchical)});
}
var skyCentres=Enumerable.Range(0,32).Select(i=>new Vector3D(i*1400-20000,i*700-10000,-100000-i*3000)).ToArray();
var skyAngles=Enumerable.Range(0,97).Select(i=>new Vector2D(Math.Cos(i*Math.PI/48),Math.Sin(i*Math.PI/48))).ToArray();
/// <summary>SkyBaseline operation.</summary>
     double SkyBaseline()
     {
         double sum=0;
         foreach(var centre in skyCentres) for(int r=0;r<=12;r++) for(int s=0;s<=96;s++)
         {
             ThermalVisionCelestial.Sample(centre,60000,r/12d,s*Math.PI/48,out var ray,out var normal);
             sum+=ray.X+normal.Y;
         }
         return sum;
     }
/// <summary>SkyPrepared operation.</summary>
     double SkyPrepared()
     {
         double sum=0;
         foreach(var centre in skyCentres)
         {
             var disc=new ThermalVisionCelestial.Disc(centre,60000);
             for(int r=0;r<=12;r++)
             {
                 var ring=disc.PrepareRing(r/12d);
                 for(int s=0;s<=96;s++)
                 {
                     ring.Sample(skyAngles[s].X,skyAngles[s].Y,out var ray,out var normal);
                     sum+=ray.X+normal.Y;
                 }
             }
         }
         return sum;
     }
if(Math.Abs(SkyBaseline()-SkyPrepared())>1e-7)throw new Exception("Celestial sampling changed");
rows.Add(new{name="celestial-32-discs",vertices=32*13*97,baseline=Measure(SkyBaseline),optimized=Measure(SkyPrepared)});
/// <summary>List operation.</summary>
var capA=new List<Vector3D>(16); var capB=new List<Vector3D>(16);
var capProjection=MatrixD.CreatePerspectiveFieldOfView(1.2,1.6,.05,5000);
/// <summary>Region operation.</summary>
var capRegions=new[]{ new Region(new Vector3D(-1),new Vector3D(1),300),
/// <summary>Region operation.</summary>
    new Region(new Vector3D(100),new Vector3D(101),300),
/// <summary>Region operation.</summary>
    new Region(new Vector3D(-.02,-.04,-.2),new Vector3D(.08,.03,.2),600) };
var capCameras=Enumerable.Range(0,4096).Select(i=>MatrixD.CreateFromYawPitchRoll(i*.013,i*.009,i*.005)).ToArray();
for(int i=0;i<capCameras.Length;i++)
{
    var expected=ThermalPerformance.Baseline.NearPlane.Cap(capRegions[i%3],capCameras[i],capProjection,.0525);
    var actual=ThermalVisionRegionPartition.NearCap(capRegions[i%3],capCameras[i],capProjection,.0525,capA,capB);
    if(!expected.SequenceEqual(actual))throw new Exception("Near-plane cap geometry changed");
}
/// <summary>OldCaps operation.</summary>
     double OldCaps(){double sum=0;for(int i=0;i<capCameras.Length;i++)sum+=ThermalPerformance.Baseline.NearPlane.Cap(capRegions[i%3],capCameras[i],capProjection,.0525).Length;return sum;}
/// <summary>ReusedCaps operation.</summary>
     double ReusedCaps(){double sum=0;for(int i=0;i<capCameras.Length;i++)sum+=ThermalVisionRegionPartition.NearCap(capRegions[i%3],capCameras[i],capProjection,.0525,capA,capB).Count;return sum;}
rows.Add(new{name="near-plane-4096-caps",baseline=Measure(OldCaps),optimized=Measure(ReusedCaps)});
/// <summary>List operation.</summary>
var drawFields=new List<ThermalVisionSurfaceField>();
foreach(var item in surfaces)
{
/// <summary>ThermalVisionSurfaceField operation.</summary>
    var field=new ThermalVisionSurfaceField(item.box,1);
    Array.Copy(item.values,field.Values,item.values.Length);field.BuildFaces(20);drawFields.Add(field);
}
/// <summary>EnumeratedFaces operation.</summary>
     double EnumeratedFaces()
     {
         double sum=0;
         for(int repeat=0;repeat<16;repeat++) foreach(var field in drawFields)
             for(int axis=0;axis<3;axis++) for(int side=0;side<2;side++)
                 foreach(var patch in field.Face(axis,side==1))sum+=patch.Temperatures.X;
         return sum;
     }
/// <summary>IndexedFaces operation.</summary>
     double IndexedFaces()
     {
         double sum=0;
         for(int repeat=0;repeat<16;repeat++) foreach(var field in drawFields)
             for(int axis=0;axis<3;axis++) for(int side=0;side<2;side++)
             {
                 var patches=field.Face(axis,side==1);
                 for(int i=0;i<patches.Count;i++)sum+=patches[i].Temperatures.X;
             }
         return sum;
     }
if(EnumeratedFaces()!=IndexedFaces())throw new Exception("Face traversal changed");
rows.Add(new{name="face-traversal",faces=16*6*drawFields.Count,baseline=Measure(EnumeratedFaces),optimized=Measure(IndexedFaces)});
/// <summary>List operation.</summary>
var oldLattices=new List<ThermalPerformance.Baseline.ThermalVisionSurfaceField>();
foreach(var field in drawFields)
{
    var oldField=new ThermalPerformance.Baseline.ThermalVisionSurfaceField(field.Bounds,1);
    Array.Copy(field.Values,oldField.Values,field.Values.Length);oldLattices.Add(oldField);
}
/// <summary>List operation.</summary>
var latticePoints=new List<Vector3D[]>();
/// <summary>Random operation.</summary>
var sampleRandom=new Random(1817);
for(int f=0;f<drawFields.Count;f++)
{
    var field=drawFields[f]; var points=new Vector3D[256];
    for(int i=0;i<points.Length;i++)
    {
        points[i]=i<125?field.Point(i%5,(i/5)%5,i/25):field.Bounds.Min+(field.Bounds.Max-field.Bounds.Min)*new Vector3D(sampleRandom.NextDouble()*1.4-.2,sampleRandom.NextDouble()*1.4-.2,sampleRandom.NextDouble()*1.4-.2);
        if(field.Sample(points[i])!=oldLattices[f].Sample(points[i]))throw new Exception("Lattice sampling changed");
    }
    latticePoints.Add(points);
}
/// <summary>OldLatticeSamples operation.</summary>
     double OldLatticeSamples(){double sum=0;for(int f=0;f<oldLattices.Count;f++)foreach(var p in latticePoints[f])sum+=oldLattices[f].Sample(p);return sum;}
/// <summary>NewLatticeSamples operation.</summary>
     double NewLatticeSamples(){double sum=0;for(int f=0;f<drawFields.Count;f++)foreach(var p in latticePoints[f])sum+=drawFields[f].Sample(p);return sum;}
rows.Add(new{name="lattice-sampling",samples=drawFields.Count*256,baseline=Measure(OldLatticeSamples),optimized=Measure(NewLatticeSamples)});
foreach(var item in surfaces)
{
    var before=new ThermalPerformance.SurfaceBuildBaseline.ThermalVisionSurfaceField(item.box,1);
/// <summary>ThermalVisionSurfaceField operation.</summary>
    var after=new ThermalVisionSurfaceField(item.box,1);
    Array.Copy(item.values,before.Values,item.values.Length);Array.Copy(item.values,after.Values,item.values.Length);
    before.BuildFaces(20);after.BuildFaces(20);
    if(before.Minimum!=after.Minimum || before.Maximum!=after.Maximum)throw new Exception("Surface range changed");
    for(int axis=0;axis<3;axis++)for(int side=0;side<2;side++)
    {
        var oldFace=before.Face(axis,side==1);var newFace=after.Face(axis,side==1);
        if(oldFace.Count!=newFace.Count)throw new Exception("Patch count changed");
        for(int i=0;i<oldFace.Count;i++)
        {
            var a=oldFace[i];var b=newFace[i];
            if(a.A!=b.A||a.B!=b.B||a.C!=b.C||a.D!=b.D||a.Temperatures!=b.Temperatures||a.PreviousTemperatures!=b.PreviousTemperatures)
                throw new Exception("Patch coordinates or temperatures changed");
        }
    }
}
/// <summary>EagerSurfaceBuild operation.</summary>
     double EagerSurfaceBuild(){double sum=0;foreach(var item in surfaces){var f=new ThermalPerformance.SurfaceBuildBaseline.ThermalVisionSurfaceField(item.box,1);Array.Copy(item.values,f.Values,item.values.Length);f.BuildFaces(20);sum+=f.Minimum;}return sum;}
rows.Add(new{name="deferred-surface-coordinates",surfaces=surfaces.Count,baseline=Measure(EagerSurfaceBuild),optimized=Measure(NewFaces)});
/// <summary>CountPatches operation.</summary>
int CountPatches(ThermalVisionSurfaceField f){int count=0;for(int axis=0;axis<3;axis++)for(int side=0;side<2;side++)count+=f.Face(axis,side==1).Count;return count;}
int splitCount=0,plannedCount=0;
foreach(var item in surfaces)
{
/// <summary>ThermalVisionSurfaceField operation.</summary>
    var before=new ThermalVisionSurfaceField(item.box,1);var after=new ThermalVisionSurfaceField(item.box,1);
    Array.Copy(item.values,before.Values,item.values.Length);Array.Copy(item.values,after.Values,item.values.Length);
    before.BuildFaces(20);after.BuildFaces(20,true);
    int oldCount=CountPatches(before),newCount=CountPatches(after);
    if(newCount>oldCount)throw new Exception("Optimal partition increased patches");
    splitCount+=oldCount;plannedCount+=newCount;
}
/// <summary>PlannedFaces operation.</summary>
     double PlannedFaces(){double sum=0;foreach(var item in surfaces){var f=new ThermalVisionSurfaceField(item.box,1);Array.Copy(item.values,f.Values,item.values.Length);f.BuildFaces(20,true);sum+=f.Minimum;}return sum;}
rows.Add(new{name="minimum-patch-partition",surfaces=surfaces.Count,baselineTriangles=splitCount*2,optimizedTriangles=plannedCount*2,baseline=Measure(NewFaces),optimized=Measure(PlannedFaces)});
/// <summary>List operation.</summary>
var patchExamples=new List<object>();
foreach(string name in new[]{"hot-strip","cooled-patch","linear-gradient","diagonal-gradient"})
{
/// <summary>ThermalVisionSurfaceField operation.</summary>
    var a=new ThermalVisionSurfaceField(new Region(Vector3D.Zero,new Vector3D(4),300),1);
/// <summary>ThermalVisionSurfaceField operation.</summary>
    var b=new ThermalVisionSurfaceField(a.Bounds,1);
    for(int z=0;z<=4;z++)for(int y=0;y<=4;y++)for(int x=0;x<=4;x++)
    {
        float t=name=="diagonal-gradient"?300+60*Math.Max(0,x+y-4):name=="hot-strip"?(x==1?800:300):name=="cooled-patch"?(x==1&&y==2?300:800):300+x*100+y*10;
        a.Values[a.Index(x,y,z)]=b.Values[b.Index(x,y,z)]=t;
    }
    a.BuildFaces(20);b.BuildFaces(20,true);
/// <summary>Export operation.</summary>
    object Export(ThermalVisionSurfaceField f)=>f.Face(2,false).Select(p=>new{a=new[]{p.A.X,p.A.Y},b=new[]{p.B.X,p.B.Y},c=new[]{p.C.X,p.C.Y},d=new[]{p.D.X,p.D.Y},t=new[]{p.Temperatures.X,p.Temperatures.Y,p.Temperatures.Z,p.Temperatures.W},alternate=p.AlternateDiagonal}).ToArray();
    int uniform=b.Face(2,false).Count(p=>ThermalVisionSurfaceField.HasUniformEndpoints(p));
    rows.Add(new{name="quad-opportunity-"+name,patches=b.Face(2,false).Count,uniformPatches=uniform,
        currentBillboards=b.Face(2,false).Count*2,potentialBillboards=b.Face(2,false).Count*2-uniform,
        gpuTriangles=b.Face(2,false).Count*2,nativeQuadPathEnabled=false});
    patchExamples.Add(new{name,baseline=Export(a),optimized=Export(b)});
}
File.WriteAllText("/tmp/thermal-patch-examples.json",JsonSerializer.Serialize(patchExamples));
Console.WriteLine(JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
/// <summary>Measure operation.</summary>
static object Measure(Func<double> run)
{
    for(int i=0;i<4;i++)run();
/// <summary>List operation.</summary>
    var times=new List<double>();long bytes=0;double checksum=0;
    for(int i=0;i<9;i++){long before=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();checksum=run();watch.Stop();bytes+=GC.GetAllocatedBytesForCurrentThread()-before;times.Add(watch.Elapsed.TotalMilliseconds);}
    times.Sort();return new{medianMs=times[4],allocatedBytes=bytes/9,checksum};
}
