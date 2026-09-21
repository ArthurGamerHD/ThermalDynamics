using System;
using Thermodynamics;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class HeatSourceMathTests
    {
/// <summary>At operation.</summary>
        private static Vector3D At(double metres)
        {
            return new Vector3D(metres, 0, 0);
        }

        [Theory]
        [InlineData(10.0)]
        [InlineData(25.0)]
        [InlineData(100.0)]
/// <summary>IrradianceIsPowerOverTheAreaOfTheSphere operation.</summary>
        public void IrradianceIsPowerOverTheAreaOfTheSphere(double distance)
        {
            const float Watts = 5e6f;

            float measured = HeatSourceMath.Irradiance(Vector3D.Zero, Watts, 1000f, At(distance));
            double expected = Watts / (4.0 * Math.PI * distance * distance);

            Assert.True(Math.Abs(measured - expected) < expected * 0.001,
                "at " + distance + " m expected " + expected.ToString("n3")
                + " W/m2, got " + measured.ToString("n3"));
        }

        [Fact]
/// <summary>DoublingTheDistanceQuartersTheIrradiance operation.</summary>
        public void DoublingTheDistanceQuartersTheIrradiance()
        {
            float near = HeatSourceMath.Irradiance(Vector3D.Zero, 5e6f, 1000f, At(10.0));
            float far = HeatSourceMath.Irradiance(Vector3D.Zero, 5e6f, 1000f, At(20.0));

            Assert.Equal(4f, near / far, 2);
        }

        [Fact]
/// <summary>TheNearFieldIsClampedRatherThanInfinite operation.</summary>
        public void TheNearFieldIsClampedRatherThanInfinite()
        {
            float touching = HeatSourceMath.Irradiance(Vector3D.Zero, 5e6f, 1000f, Vector3D.Zero);
            float atOneMetre = HeatSourceMath.Irradiance(Vector3D.Zero, 5e6f, 1000f, At(1.0));

            Assert.True(touching > 0f);
            Assert.False(float.IsInfinity(touching));
            Assert.Equal(atOneMetre, touching, 3);
        }

        [Fact]
/// <summary>BeyondRangeNothingArrives operation.</summary>
        public void BeyondRangeNothingArrives()
        {
            Assert.True(HeatSourceMath.Irradiance(Vector3D.Zero, 5e6f, 100f, At(99.0)) > 0f);
            Assert.Equal(0f, HeatSourceMath.Irradiance(Vector3D.Zero, 5e6f, 100f, At(101.0)));
        }

        [Theory]
        [InlineData(0f, 100f)]      // no output
        [InlineData(5e6f, 0f)]      // no range
        [InlineData(-5e6f, 100f)]   // negative output
/// <summary>ASourceMakingNothingDeliversNothing operation.</summary>
        public void ASourceMakingNothingDeliversNothing(float watts, float range)
        {
            Assert.Equal(0f, HeatSourceMath.Irradiance(Vector3D.Zero, watts, range, At(10.0)));
        }


        [Fact]
/// <summary>TheDirectionPointsAtTheSource operation.</summary>
        public void TheDirectionPointsAtTheSource()
        {
            MatrixD identity = MatrixD.Identity;
            Vector3 direction = HeatSourceMath.Direction(At(10.0), Vector3D.Zero, ref identity);

            Assert.Equal(1f, direction.X, 3);
            Assert.Equal(0f, direction.Y, 3);
            Assert.Equal(0f, direction.Z, 3);
        }

        [Fact]
/// <summary>TheDirectionIsInTheGridsOwnFrame operation.</summary>
        public void TheDirectionIsInTheGridsOwnFrame()
        {
            MatrixD turned = MatrixD.Transpose(MatrixD.CreateRotationZ(Math.PI / 2.0));
            Vector3 direction = HeatSourceMath.Direction(At(10.0), Vector3D.Zero, ref turned);

            Assert.Equal(0f, direction.X, 3);
            Assert.Equal(-1f, direction.Y, 3);
        }

        [Fact]
/// <summary>ACoincidentSourceHasNoDirectionRatherThanANaN operation.</summary>
        public void ACoincidentSourceHasNoDirectionRatherThanANaN()
        {
            MatrixD identity = MatrixD.Identity;
            Vector3 direction = HeatSourceMath.Direction(Vector3D.Zero, Vector3D.Zero, ref identity);

            Assert.Equal(Vector3.Zero, direction);
            Assert.False(float.IsNaN(direction.X));
        }
    }
}
