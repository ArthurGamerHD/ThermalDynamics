using System;
using Thermodynamics.Presentation;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Natural heat cue visibility: fixed endpoint and midpoint oracles, full distance/angle sweeps,
    /// grid-scale offsets and warmed allocation accounting. This does not exercise the SE1 renderer.
    /// </summary>
    public class HeatGlowStyleTests
    {
        [Theory]
        [InlineData(-1, 0)]
        [InlineData(0, 0)]
        [InlineData(0.125, 0.5)]
        [InlineData(0.25, 1)]
        [InlineData(1, 1)]
        public void FacingHasContinuousGrazingFade(double cosine, float expected)
        {
            Assert.Equal(expected, HeatGlowStyle.FacingFade(cosine), 6);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1600, 1)]
        [InlineData(1800, 0.5)]
        [InlineData(2000, 0)]
        [InlineData(2100, 0)]
        public void DistanceFadesBeforeTheCullBoundary(double distance, float expected)
        {
            Assert.Equal(expected, HeatGlowStyle.RangeFade(distance, 2000), 6);
        }

        [Fact]
        public void InvalidViewsDrawNothing()
        {
            Assert.Equal(0f, HeatGlowStyle.FacingFade(double.NaN));
            Assert.Equal(0f, HeatGlowStyle.RangeFade(double.NaN, 2000));
            Assert.Equal(0f, HeatGlowStyle.RangeFade(double.PositiveInfinity, 2000));
            Assert.Equal(0f, HeatGlowStyle.RangeFade(-1, 2000));
            Assert.Equal(0f, HeatGlowStyle.RangeFade(100, 0));
        }

        [Fact]
        public void FadesAreBoundedMonotoneAndDoNotPopAtTheEnds()
        {
            float previousFacing = 0f, previousRange = 1f;
            for (int i = 0; i <= 4000; i++)
            {
                float facing = HeatGlowStyle.FacingFade(i / 4000.0);
                float range = HeatGlowStyle.RangeFade(i, 2000);
                Assert.InRange(facing, previousFacing, 1f);
                Assert.InRange(range, 0f, previousRange);
                previousFacing = facing;
                previousRange = range;
            }
            Assert.InRange(HeatGlowStyle.FacingFade(0.00001), 0f, 0.000001f);
            Assert.InRange(HeatGlowStyle.RangeFade(1999.99, 2000), 0f, 0.000001f);
        }

        [Fact]
        public void SmallAndLargeGridsUseTheSameRelativeOffset()
        {
            Assert.Equal(0.004f, HeatGlowStyle.StandOff(0.5f), 6);
            Assert.Equal(0.02f, HeatGlowStyle.StandOff(2.5f), 6);
        }

        [Fact]
        public void WarmedViewShapingAllocatesNoManagedMemory()
        {
            double sum = 0;
            for (int i = 0; i < 10000; i++)
                sum += HeatGlowStyle.FacingFade(i * 0.0001) + HeatGlowStyle.RangeFade(i, 2000);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100000; i++)
                sum += HeatGlowStyle.FacingFade((i % 1000) * 0.001)
                    * HeatGlowStyle.RangeFade(i % 2500, 2000) * HeatGlowStyle.StandOff(2.5f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(sum > 0);
            Assert.Equal(0, allocated);
        }
    }
}
