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
        private static readonly List<SolidOccluder> visionOccluders=new List<SolidOccluder>();

        // Revalidate from live blocks and transforms each frame: opening/destroying a
        // wall or moving a grid must never leave a stale invisible ship behind it.
        private static int CullHiddenFleet(Vector3D eye)
        {
            visionOccluders.Clear();
            int queries=0;
            foreach(var view in visibleViews)
            {
                // The concrete grid overload returns prohibited MySlimBlock, even with var.
                VRage.Game.ModAPI.IMyCubeGrid grid=view.Grid.Grid;
                double size=grid.GridSize;
                if(view.Distance>size*3) continue;
                MatrixD inverse=MatrixD.Invert(grid.WorldMatrix);
                var centre=grid.WorldToGridInteger(eye);
                for(int x=-2;x<=2;x++) for(int y=-2;y<=2;y++) for(int z=-2;z<=2;z++)
                {
                    if(queries>=500 || visionOccluders.Count>=32) break;
                    queries++;
                    var position=centre+new Vector3I(x,y,z);
                    var block=grid.GetCubeBlock(position);
                    if(block==null || !block.IsFullIntegrity || block.BuildLevelRatio<1 || block.HasDeformation) continue;
                    // Only the four vanilla full armor cubes. Never use arbitrary
                    // block AABBs: windows, slopes, doors and machinery contain holes.
                    if(block.BlockDefinition.Context==null || !block.BlockDefinition.Context.IsBaseGame) continue;
                    string subtype=block.BlockDefinition.Id.SubtypeName;
                    if(subtype!="LargeBlockArmorBlock" && subtype!="LargeHeavyBlockArmorBlock"
                        && subtype!="SmallBlockArmorBlock" && subtype!="SmallHeavyBlockArmorBlock") continue;
                    Vector3D mid=(Vector3D)position*size;
                    // Inset edges to avoid treating bevels or silhouette boundaries as solid.
                    Vector3D half=new Vector3D(size*.4);
                    visionOccluders.Add(new SolidOccluder { Owner=view.Grid,Inverse=inverse,
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
