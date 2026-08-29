using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **Stopping a run when it has nowhere left to go, rather than when it last moved slowly.**
    ///
    /// <para>
    /// The settle rule asks how far the hottest block moved over the last sixty seconds and stops
    /// when that is under 0.25 K. On three of the air walk's four scenarios a hull meets that in
    /// two minutes. On `vacuum-shadow` it never does: a hull radiating into the dark decays towards
    /// its floor, so its per-chunk movement shrinks geometrically and creeps under a fixed
    /// threshold only after most of an hour. Measured on the 2026-08-28 air walk, **every ship**
    /// runs its full 1,800 s there and the scenario is **33.7 %** of the walk's cost.
    /// </para>
    ///
    /// <para>
    /// <see cref="Battery.Converged"/> asks the other question. If a chunk moved the hull `d` and
    /// the one before moved it `p`, then `r = d / p` is the decay per chunk and what is left is the
    /// rest of that geometric series, `d * r / (1 - r)`. Inside the same tolerance, the run is
    /// already where it is going. **This is a cheaper route to the same reading, not a looser
    /// reading**, which is why every test here is about the bound rather than about the saving.
    /// </para>
    ///
    /// <para>
    /// Every case below that returns false is a refusal to extrapolate something that is not
    /// decaying, because an extrapolation off a curve that is not a decay is a guess with a
    /// number attached (`E8`).
    /// </para>
    /// </summary>
    public class SettleExtrapolationTests
    {
        /// <summary>A halving approach with little left to go is finished.</summary>
        [Fact]
        public void AHalvingApproachInsideTheToleranceIsDone()
        {
            // Steps of 0.4 then 0.2: ratio 0.5, so the rest of the series is 0.2 — inside 0.25.
            Assert.True(Battery.Converged(-0.2f, -0.4f));
        }

        /// <summary>The same ratio with more left to go is not.</summary>
        [Fact]
        public void AHalvingApproachWithFurtherToGoIsNotDone()
        {
            // Steps of 4 then 2: ratio 0.5, so 2 K remains — eight times the tolerance.
            Assert.False(Battery.Converged(-2f, -4f));
        }

        /// <summary>
        /// **The bound holds: what is skipped is never more than the tolerance.**
        ///
        /// Swept over ratios and step sizes, every case this accepts has a remaining sum inside
        /// <see cref="Battery.SettleWithin"/>. That is the whole claim — a run stopped here is
        /// within the same distance of its end state as one stopped by the plain rule.
        /// </summary>
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

                // The sum of the remaining geometric series, which is what stopping here skips.
                float remaining = step * ratio / (1f - ratio);
                Assert.True(remaining <= Battery.SettleWithin,
                    "accepted a stop with " + remaining + " K still to go");
            }
        }

        /// <summary>A hull creeping at a ratio near one is not converging on any useful timescale.</summary>
        [Fact]
        public void ACreepIsNotAConvergence()
        {
            Assert.False(Battery.Converged(0.99f, 1.0f));
            Assert.False(Battery.Converged(0.0999f, 0.1f));
        }

        /// <summary>An approach that is speeding up is not a decay and is never extrapolated.</summary>
        [Fact]
        public void AnAcceleratingRunIsNotExtrapolated()
        {
            Assert.False(Battery.Converged(-0.4f, -0.2f));
            Assert.False(Battery.Converged(4f, 2f));
        }

        /// <summary>A run that changed direction is oscillating, not decaying.</summary>
        [Fact]
        public void AnOscillationIsNotExtrapolated()
        {
            Assert.False(Battery.Converged(0.1f, -0.2f));
            Assert.False(Battery.Converged(-0.1f, 0.2f));
        }

        /// <summary>
        /// With no previous step, or a step of exactly nothing, there is no ratio to take.
        ///
        /// A zero step is already caught by the plain rule one line earlier, and dividing by it
        /// here would produce an infinity that compares as *not converged* by luck rather than by
        /// intent.
        /// </summary>
        [Fact]
        public void NothingIsExtrapolatedFromOneSample()
        {
            Assert.False(Battery.Converged(-0.1f, float.NaN));
            Assert.False(Battery.Converged(-0.1f, 0f));
            Assert.False(Battery.Converged(0f, -0.1f));
        }

        /// <summary>
        /// **The rule is off unless a walk asks for it**, because turning it on moves the point at
        /// which every peak, demand and step cost is read — so a walk taken with it is not
        /// comparable with one taken without (`M1`). The default moves when a paired walk has
        /// priced it, in a commit that cites the number (`E11`).
        /// </summary>
        [Fact]
        public void ItIsOffUnlessTheWalkAsksForIt()
        {
            Assert.Equal(
                (System.Environment.GetEnvironmentVariable("THERMAL_SETTLE_EXTRAPOLATE") ?? "") == "1",
                Battery.Extrapolates);
        }
    }
}
