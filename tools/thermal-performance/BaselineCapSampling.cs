using Thermodynamics.Presentation;
using VRageMath;
using TemperatureTriangle=Thermodynamics.Presentation.ThermalVisionSurfaceField.TemperatureTriangle;
namespace ThermalPerformance;
public sealed class BaselineCapSampling
{
    private readonly ThermalVisionSurfaceField field;
/// <summary>BaselineCapSampling operation.</summary>
    public BaselineCapSampling(ThermalVisionSurfaceField field) { this.field=field; }
/// <summary>BlendSample operation.</summary>
    private float BlendSample(ThermalVisionSurfaceField previous,Vector3D point,float blend)
    {
        float current=field.Sample(point);
        if(previous==null)return current;
        blend=Math.Max(0f,Math.Min(1f,blend));
        return MathHelper.Lerp(previous.Sample(point),current,blend*blend*(3-2*blend));
    }
/// <summary>AppendCapTriangles operation.</summary>
        public void AppendCapTriangles(Vector3D a,Vector3D b,Vector3D c,
            ThermalVisionSurfaceField previous,float blend,float error,List<TemperatureTriangle> output)
        {
            AppendCap(a,b,c,BlendSample(previous,a,blend),BlendSample(previous,b,blend),
                BlendSample(previous,c,blend),previous,blend,error,0,output);
        }
/// <summary>AppendCap operation.</summary>
        private void AppendCap(Vector3D a,Vector3D b,Vector3D c,float ta,float tb,float tc,
            ThermalVisionSurfaceField previous,float blend,float error,int depth,List<TemperatureTriangle> output)
        {
            if(depth<3)
            {
                var ab=(a+b)*.5;var bc=(b+c)*.5;var ca=(c+a)*.5;
                float tab=BlendSample(previous,ab,blend),tbc=BlendSample(previous,bc,blend),tca=BlendSample(previous,ca,blend);
                float centre=BlendSample(previous,(a+b+c)/3,blend);
                if(Math.Abs(tab-(ta+tb)*.5f)>error || Math.Abs(tbc-(tb+tc)*.5f)>error
                    || Math.Abs(tca-(tc+ta)*.5f)>error || Math.Abs(centre-(ta+tb+tc)/3)>error)
                {
                    AppendCap(a,ab,ca,ta,tab,tca,previous,blend,error,depth+1,output);
                    AppendCap(ab,b,bc,tab,tb,tbc,previous,blend,error,depth+1,output);
                    AppendCap(ca,bc,c,tca,tbc,tc,previous,blend,error,depth+1,output);
                    AppendCap(ab,bc,ca,tab,tbc,tca,previous,blend,error,depth+1,output);
                    return;
                }
            }
            output.Add(new TemperatureTriangle { A=a,B=b,C=c,Temperatures=new Vector3(ta,tb,tc) });
        }
}
