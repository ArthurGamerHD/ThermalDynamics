using Thermodynamics.Presentation;
using VRageMath;
using Region=Thermodynamics.Presentation.ThermalVisionRegionPartition.Region;
namespace ThermalPerformance.Baseline;
internal static class NearPlane
{

    internal static Vector3D[] Cap(Region region,MatrixD world,MatrixD projection,double near)
    {
        ThermalVisionDepthLayers.Plane(near,projection,world,out var centre,out var width,out var height);
        var polygon=new List<Vector3D> {
            centre-world.Right*width-world.Up*height,
            centre+world.Right*width-world.Up*height,
            centre+world.Right*width+world.Up*height,
            centre-world.Right*width+world.Up*height };
        for(int axis=0;axis<3;axis++) for(int side=0;side<2;side++)
        {
            if(polygon.Count==0)return new Vector3D[0];
            double plane=Coordinate(side==0?region.Min:region.Max,axis);

            var clipped=new List<Vector3D>();
            Vector3D previous=polygon[polygon.Count-1];
            double previousDistance=(Coordinate(previous,axis)-plane)*(side==0?1:-1);
            foreach(var current in polygon)
            {
                double distance=(Coordinate(current,axis)-plane)*(side==0?1:-1);
                if((distance>=0)!=(previousDistance>=0))
                    clipped.Add(previous+(current-previous)*(previousDistance/(previousDistance-distance)));
                if(distance>=0)clipped.Add(current);
                previous=current; previousDistance=distance;
            }
            polygon=clipped;
        }
        return polygon.ToArray();
    }

    static double Coordinate(Vector3D p,int axis)=>axis==0?p.X:axis==1?p.Y:p.Z;
}
