using Thermodynamics.Presentation;
using Thermodynamics.Core;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class ThermalVisionOcclusionTests
    {
/// <summary>BoundingBoxD operation.</summary>
        private static readonly BoundingBoxD Wall=new BoundingBoxD(new Vector3D(-1,-1,2),new Vector3D(1,1,3));

        [Fact]
/// <summary>EntireShipBehindSolidCubeIsHiddenButPartialShipIsNot operation.</summary>
        public void EntireShipBehindSolidCubeIsHiddenButPartialShipIsNot()
        {
            Assert.True(Hidden(new Vector3D(-2,-2,10),new Vector3D(2,2,12)));
            Assert.False(Hidden(new Vector3D(-2,-2,10),new Vector3D(6,2,12)));
            Assert.False(Hidden(new Vector3D(-.1,-.1,1),new Vector3D(.1,.1,1.5)));
            Assert.False(Hidden(new Vector3D(-.1,-.1,2.5),new Vector3D(.1,.1,12)));
            Assert.False(Hidden(new Vector3D(-.1,-.1,-12),new Vector3D(.1,.1,-10)));
        }

        [Fact]
/// <summary>WindowBetweenSeparateSolidCubesRemainsVisible operation.</summary>
        public void WindowBetweenSeparateSolidCubesRemainsVisible()
        {
/// <summary>BoundingBoxD operation.</summary>
            var ship=new BoundingBoxD(new Vector3D(-5,-1,10),new Vector3D(5,1,12));
/// <summary>BoundingBoxD operation.</summary>
            var left=new BoundingBoxD(new Vector3D(-3,-2,2),new Vector3D(-.2,2,3));
/// <summary>BoundingBoxD operation.</summary>
            var right=new BoundingBoxD(new Vector3D(.2,-2,2),new Vector3D(3,2,3));
            Assert.False(ThermalVisionOcclusion.Hidden(Vector3D.Zero,ship,MatrixD.Identity,left));
            Assert.False(ThermalVisionOcclusion.Hidden(Vector3D.Zero,ship,MatrixD.Identity,right));
        }

        [Fact]
/// <summary>MovingCameraOrBlockerRevealsPreviouslyHiddenShip operation.</summary>
        public void MovingCameraOrBlockerRevealsPreviouslyHiddenShip()
        {
/// <summary>BoundingBoxD operation.</summary>
            var ship=new BoundingBoxD(new Vector3D(-1,-1,10),new Vector3D(1,1,12));
            Assert.True(ThermalVisionOcclusion.Hidden(Vector3D.Zero,ship,MatrixD.Identity,Wall));
            Assert.False(ThermalVisionOcclusion.Hidden(new Vector3D(8,0,0),ship,MatrixD.Identity,Wall));
            Assert.False(ThermalVisionOcclusion.Hidden(Vector3D.Zero,ship,MatrixD.CreateTranslation(-8,0,0),Wall));
            Assert.False(ThermalVisionOcclusion.Hidden(new Vector3D(0,0,2.5),ship,MatrixD.Identity,Wall));
        }

        [Fact]
/// <summary>HiddenDecisionImpliesDenseIndependentRaysHitBlocker operation.</summary>
        public void HiddenDecisionImpliesDenseIndependentRaysHitBlocker()
        {
            var random=new System.Random(436);
            int hidden=0, visible=0;
            for(int i=0;i<300;i++)
            {
/// <summary>Vector3D operation.</summary>
                var centre=new Vector3D(random.NextDouble()*12-6,random.NextDouble()*8-4,8+random.NextDouble()*20);
/// <summary>BoundingBoxD operation.</summary>
                var target=new BoundingBoxD(centre-new Vector3D(.5),centre+new Vector3D(.5));
                var transform=MatrixD.CreateRotationY(random.NextDouble()*.7-.35);
                if(!ThermalVisionOcclusion.Hidden(Vector3D.Zero,target,MatrixD.Invert(transform),Wall)) { visible++; continue; }
                hidden++;
                for(int x=0;x<=4;x++) for(int y=0;y<=4;y++) for(int z=0;z<=4;z++)
                {
/// <summary>Vector3D operation.</summary>
                    var point=target.Min+new Vector3D(x,y,z)*.25;
                    point=Vector3D.Transform(point,MatrixD.Invert(transform));
                    double length=point.Length();
/// <summary>RayD operation.</summary>
                    var ray=new RayD(Vector3D.Zero,point/length);
                    double? distance=ray.Intersects(Wall);
                    Assert.True(distance.HasValue && distance.Value>0 && distance.Value<length);
                }
            }
            Assert.True(hidden>10); Assert.True(visible>10);
        }

/// <summary>Hidden operation.</summary>
        private static bool Hidden(Vector3D min,Vector3D max)
        { return ThermalVisionOcclusion.Hidden(Vector3D.Zero,new BoundingBoxD(min,max),MatrixD.Identity,Wall); }
    }
}
