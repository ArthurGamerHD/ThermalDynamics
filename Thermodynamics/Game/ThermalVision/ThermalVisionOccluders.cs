using Thermodynamics.Presentation;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics
{
    public static partial class ThermalVisionProbe
    {
        private struct SolidOccluder
        {
            public ThermalGrid Owner;
            public MatrixD Inverse;
            public BoundingBoxD Bounds;
        }
/// <summary>List operation.</summary>
        private static readonly List<SolidOccluder> visionOccluders=new List<SolidOccluder>();

/// <summary>CullHiddenFleet operation.</summary>
        private static int CullHiddenFleet(Vector3D eye)
        {
            visionOccluders.Clear();
            int queries=0;
            foreach(var view in visibleViews)
            {
                VRage.Game.ModAPI.IMyCubeGrid grid=view.Grid.Grid;
                double size=grid.GridSize;
                if(view.Distance>size*3) continue;
                MatrixD inverse=MatrixD.Invert(grid.WorldMatrix);
                var centre=grid.WorldToGridInteger(eye);
                for(int x=-2;x<=2;x++) for(int y=-2;y<=2;y++) for(int z=-2;z<=2;z++)
                {
                    if(queries>=500 || visionOccluders.Count>=32) break;
                    queries++;
/// <summary>Vector3I operation.</summary>
                    var position=centre+new Vector3I(x,y,z);
                    var block=grid.GetCubeBlock(position);
                    if(block==null || !block.IsFullIntegrity || block.BuildLevelRatio<1 || block.HasDeformation) continue;
                    if(block.BlockDefinition.Context==null || !block.BlockDefinition.Context.IsBaseGame) continue;
                    string subtype=block.BlockDefinition.Id.SubtypeName;
                    if(subtype!="LargeBlockArmorBlock" && subtype!="LargeHeavyBlockArmorBlock"
                        && subtype!="SmallBlockArmorBlock" && subtype!="SmallHeavyBlockArmorBlock") continue;
                    Vector3D mid=(Vector3D)position*size;
/// <summary>Vector3D operation.</summary>
                    Vector3D half=new Vector3D(size*.4);
                    visionOccluders.Add(new SolidOccluder { Owner=view.Grid,Inverse=inverse,
/// <summary>BoundingBoxD operation.</summary>
                        Bounds=new BoundingBoxD(mid-half,mid+half) });
                }
                if(queries>=500 || visionOccluders.Count>=32) break;
            }
            int kept=0, hidden=0;
            for(int i=0;i<visibleViews.Count;i++)
            {
                var view=visibleViews[i]; bool blocked=false;
                foreach(var occluder in visionOccluders)
                {
                    if(occluder.Owner==view.Grid) continue;
                    if(ThermalVisionOcclusion.Hidden(eye,view.Grid.Grid.PositionComp.WorldAABB,
                        occluder.Inverse,occluder.Bounds)) { blocked=true; break; }
                }
                if(blocked) hidden++;
                else visibleViews[kept++]=view;
            }
            if(kept<visibleViews.Count) visibleViews.RemoveRange(kept,visibleViews.Count-kept);
            return hidden;
        }
    }
}
