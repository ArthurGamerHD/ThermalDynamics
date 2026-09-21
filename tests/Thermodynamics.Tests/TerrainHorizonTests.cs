using System;
using Thermodynamics.Core;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class TerrainHorizonTests
    {
        private const double Radius = 60000d;
        private static readonly Vector3D Centre = Vector3D.Zero;

/// <summary>Surface operation.</summary>
        private static Vector3D Surface(double height = 2d)
        {
            return new Vector3D(0, Radius + height, 0);
        }

/// <summary>Plain operation.</summary>
        private static Func<Vector3D, double> Plain()
        {
            return point => Radius;
        }

/// <summary>Ridge operation.</summary>
        private static Func<Vector3D, double> Ridge(double from, double height)
        {
            return point => point.X >= from ? Radius + height : Radius;
        }

/// <summary>Occluded operation.</summary>
        private static bool Occluded(Vector3D origin, Vector3D sun, Func<Vector3D, double> terrain,
            double range = 4000d, int samples = 10)
        {
            return TerrainHorizon.Occluded(origin, sun, Centre, range, samples, terrain);
        }

/// <summary>Sun operation.</summary>
        private static Vector3D Sun(double degrees)
        {
            double radians = degrees * Math.PI / 180d;
            return new Vector3D(Math.Cos(radians), Math.Sin(radians), 0);
        }

        [Fact]
/// <summary>FlatGroundDoesNotShadowItself operation.</summary>
        public void FlatGroundDoesNotShadowItself()
        {
            for (int degrees = 1; degrees <= 90; degrees += 7)
            {
                Assert.False(Occluded(Surface(), Sun(degrees), Plain()),
                    "flat ground shadowed itself at " + degrees + " degrees");
            }
        }

        [Fact]
/// <summary>ARidgeShadowsALowSun operation.</summary>
        public void ARidgeShadowsALowSun()
        {
            Assert.True(Occluded(Surface(), Sun(5), Ridge(200d, 100d)));
            Assert.True(Occluded(Surface(), Sun(15), Ridge(200d, 100d)));
        }

        [Fact]
/// <summary>TheSameRidgeDoesNotShadowAHighSun operation.</summary>
        public void TheSameRidgeDoesNotShadowAHighSun()
        {
            Assert.False(Occluded(Surface(), Sun(60), Ridge(200d, 100d)));
            Assert.False(Occluded(Surface(), Sun(85), Ridge(200d, 100d)));
        }

        [Fact]
/// <summary>StandingOnTopOfTheRidgeIsNeverShadowedByIt operation.</summary>
        public void StandingOnTopOfTheRidgeIsNeverShadowedByIt()
        {
            Assert.False(Occluded(Surface(120d), Sun(5), Ridge(200d, 100d)));
        }

        [Fact]
/// <summary>ATallerRidgeShadowsAHigherSun operation.</summary>
        public void ATallerRidgeShadowsAHigherSun()
        {
            Assert.False(Occluded(Surface(), Sun(40), Ridge(200d, 100d)));
            Assert.True(Occluded(Surface(), Sun(40), Ridge(200d, 400d)));
        }

        [Fact]
/// <summary>GroundBeyondTheRangeIsNotConsulted operation.</summary>
        public void GroundBeyondTheRangeIsNotConsulted()
        {
/// <summary>Ridge operation.</summary>
            Func<Vector3D, double> distant = Ridge(8000d, 3000d);

            Assert.False(Occluded(Surface(), Sun(10), distant, 4000d));
            Assert.True(Occluded(Surface(), Sun(10), distant, 20000d));
        }

        [Fact]
/// <summary>ACanyonWallShadowsTheFloorAndNotTheRim operation.</summary>
        public void ACanyonWallShadowsTheFloorAndNotTheRim()
        {
/// <summary>Ridge operation.</summary>
            Func<Vector3D, double> canyon = Ridge(30d, 60d);

            Assert.True(Occluded(Surface(), Sun(30), canyon));
            Assert.False(Occluded(Surface(70d), Sun(30), canyon));
        }

        [Fact]
/// <summary>NoTerrainFunctionMeansNoOpinion operation.</summary>
        public void NoTerrainFunctionMeansNoOpinion()
        {
            Assert.False(TerrainHorizon.Occluded(Surface(), Sun(10), Centre, 4000d, 10, null));
        }

        [Fact]
/// <summary>ADegenerateWalkIsRefusedRatherThanGuessed operation.</summary>
        public void ADegenerateWalkIsRefusedRatherThanGuessed()
        {
/// <summary>Ridge operation.</summary>
            Func<Vector3D, double> wall = Ridge(10d, 500d);

            Assert.False(Occluded(Surface(), Sun(10), wall, 0d));
            Assert.False(Occluded(Surface(), Sun(10), wall, 4000d, 0));
            Assert.False(Occluded(Surface(), Vector3D.Zero, wall));
        }

        [Fact]
/// <summary>SamplesAreSpacedGeometricallySoNearGroundIsWatchedClosest operation.</summary>
        public void SamplesAreSpacedGeometricallySoNearGroundIsWatchedClosest()
        {
            int consulted = 0;
            Func<Vector3D, double> counting = point =>
            {
                consulted++;
                return point.X >= 60d && point.X <= 90d ? Radius + 40d : Radius;
            };

            Assert.True(Occluded(Surface(), Sun(8), counting));
            Assert.True(consulted <= 10, "should not have cost more lookups than it was allowed");
        }
    }
}
