using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    public class SettleExtrapolationTests
    {
        [Fact]

        public void AHalvingApproachInsideTheToleranceIsDone()
        {
            Assert.True(Battery.Converged(-0.2f, -0.4f));
        }

        [Fact]

        public void AHalvingApproachWithFurtherToGoIsNotDone()
        {
            Assert.False(Battery.Converged(-2f, -4f));
        }

        [Theory]
        [InlineData(0.1f)]
        [InlineData(0.25f)]
        [InlineData(0.5f)]
        [InlineData(0.75f)]
        [InlineData(0.89f)]

        public void WhatIsSkippedIsInsideTheSameTolerance(float ratio)
        {
            for (float step = 0.01f; step < 50f; step *= 1.3f)
            {
                float previous = step / ratio;
                if (!Battery.Converged(step, previous)) continue;

                float remaining = step * ratio / (1f - ratio);
                Assert.True(remaining <= Battery.SettleWithin,
                    "accepted a stop with " + remaining + " K still to go");
            }
        }

        [Fact]

        public void ACreepIsNotAConvergence()
        {
            Assert.False(Battery.Converged(0.99f, 1.0f));
            Assert.False(Battery.Converged(0.0999f, 0.1f));
        }

        [Fact]

        public void AnAcceleratingRunIsNotExtrapolated()
        {
            Assert.False(Battery.Converged(-0.4f, -0.2f));
            Assert.False(Battery.Converged(4f, 2f));
        }

        [Fact]

        public void AnOscillationIsNotExtrapolated()
        {
            Assert.False(Battery.Converged(0.1f, -0.2f));
            Assert.False(Battery.Converged(-0.1f, 0.2f));
        }

        [Fact]

        public void NothingIsExtrapolatedFromOneSample()
        {
            Assert.False(Battery.Converged(-0.1f, float.NaN));
            Assert.False(Battery.Converged(-0.1f, 0f));
            Assert.False(Battery.Converged(0f, -0.1f));
        }

        [Fact]

        public void ItIsOffUnlessTheWalkAsksForIt()
        {
            Assert.Equal(
                (System.Environment.GetEnvironmentVariable("THERMAL_SETTLE_EXTRAPOLATE") ?? "") == "1",
                Battery.Extrapolates);
        }
    }
}
