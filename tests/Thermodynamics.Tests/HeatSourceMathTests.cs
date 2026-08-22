using System;
using Thermodynamics;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The falloff a placed heat source lives or dies by.
    ///
    /// <para>
    /// A bonfire, a thermal missile, a burning wreck — every use the mechanism has is judged by
    /// whether the heat weakens correctly with distance. That arithmetic sat inside the registry's
    /// sampling loop, in a file the test project cannot compile, so the registry was covered and
    /// the physics was not: a source that delivered full output at any range, or nothing past a
    /// metre, would have passed every test that existed.
    /// </para>
    /// </summary>
    public class HeatSourceMathTests
    {
        private static Vector3D At(double metres)
        {
            return new Vector3D(metres, 0, 0);
        }

        /// <summary>
        /// **The inverse square law, against its closed form.** Not "less further away" — the
        /// actual figure, because a source that fell off linearly would satisfy every ordering
        /// check and still be wrong everywhere.
        /// </summary>
        [Theory]
        [InlineData(10.0)]
        [InlineData(25.0)]
        [InlineData(100.0)]
        public void IrradianceIsPowerOverTheAreaOfTheSphere(double distance)
        {
            const float Watts = 5e6f;

            float measured = HeatSourceMath.Irradiance(Vector3D.Zero, Watts, 1000f, At(distance));
            double expected = Watts / (4.0 * Math.PI * distance * distance);

            Assert.True(Math.Abs(measured - expected) < expected * 0.001,
                "at " + distance + " m expected " + expected.ToString("n3")
                + " W/m2, got " + measured.ToString("n3"));
        }

        /// <summary>Twice the distance is a quarter of the heat.</summary>
        [Fact]
        public void DoublingTheDistanceQuartersTheIrradiance()
        {
            float near = HeatSourceMath.Irradiance(Vector3D.Zero, 5e6f, 1000f, At(10.0));
            float far = HeatSourceMath.Irradiance(Vector3D.Zero, 5e6f, 1000f, At(20.0));

            Assert.Equal(4f, near / far, 2);
        }

        /// <summary>
        /// Inside a metre the law is clamped rather than allowed to diverge. Without this a source
        /// placed on top of a block delivers an infinity, and the solver integrates it.
        /// </summary>
        [Fact]
        public void TheNearFieldIsClampedRatherThanInfinite()
        {
            float touching = HeatSourceMath.Irradiance(Vector3D.Zero, 5e6f, 1000f, Vector3D.Zero);
            float atOneMetre = HeatSourceMath.Irradiance(Vector3D.Zero, 5e6f, 1000f, At(1.0));

            Assert.True(touching > 0f);
            Assert.False(float.IsInfinity(touching));
            Assert.Equal(atOneMetre, touching, 3);
        }

        /// <summary>
        /// **Past its range a source is not felt at all.** This is what bounds the cost of the
        /// whole mechanism — every source costs a pass over the exposed blocks of every grid within
        /// range — so a cutoff that failed would be a performance fault as much as a physical one.
        /// </summary>
        [Fact]
        public void BeyondRangeNothingArrives()
        {
            Assert.True(HeatSourceMath.Irradiance(Vector3D.Zero, 5e6f, 100f, At(99.0)) > 0f);
            Assert.Equal(0f, HeatSourceMath.Irradiance(Vector3D.Zero, 5e6f, 100f, At(101.0)));
        }

        [Theory]
        [InlineData(0f, 100f)]      // no output
        [InlineData(5e6f, 0f)]      // no range
        [InlineData(-5e6f, 100f)]   // negative output
        public void ASourceMakingNothingDeliversNothing(float watts, float range)
        {
            Assert.Equal(0f, HeatSourceMath.Irradiance(Vector3D.Zero, watts, range, At(10.0)));
        }

        // ---- direction -------------------------------------------------------------------------

        /// <summary>
        /// The direction handed to the solver points from the grid *towards* the source, in the
        /// grid's frame. Reversed, every face that should be lit would be the one in shadow.
        /// </summary>
        [Fact]
        public void TheDirectionPointsAtTheSource()
        {
            MatrixD identity = MatrixD.Identity;
            Vector3 direction = HeatSourceMath.Direction(At(10.0), Vector3D.Zero, ref identity);

            Assert.Equal(1f, direction.X, 3);
            Assert.Equal(0f, direction.Y, 3);
            Assert.Equal(0f, direction.Z, 3);
        }

        /// <summary>
        /// It is in the grid's frame, not the world's: a grid turned a quarter turn sees the same
        /// source coming from a different side of itself.
        /// </summary>
        [Fact]
        public void TheDirectionIsInTheGridsOwnFrame()
        {
            MatrixD turned = MatrixD.Transpose(MatrixD.CreateRotationZ(Math.PI / 2.0));
            Vector3 direction = HeatSourceMath.Direction(At(10.0), Vector3D.Zero, ref turned);

            // A source on the world +X axis lies along the grid's -Y once the grid is turned.
            Assert.Equal(0f, direction.X, 3);
            Assert.Equal(-1f, direction.Y, 3);
        }

        /// <summary>A source exactly on the sample point has no direction rather than a NaN.</summary>
        [Fact]
        public void ACoincidentSourceHasNoDirectionRatherThanANaN()
        {
            MatrixD identity = MatrixD.Identity;
            Vector3 direction = HeatSourceMath.Direction(Vector3D.Zero, Vector3D.Zero, ref identity);

            Assert.Equal(Vector3.Zero, direction);
            Assert.False(float.IsNaN(direction.X));
        }
    }
}
