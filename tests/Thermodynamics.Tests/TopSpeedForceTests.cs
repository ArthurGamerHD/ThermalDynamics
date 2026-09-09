using Thermodynamics.Core;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The force that holds a ship under its cruise speed, and the switch that decides how far past
    /// it a ship may be pushed.
    ///
    /// <para>
    /// **This suite exists because `EnableSpeedBoost` shipped dead.** `ThermalGridTopSpeed` returned
    /// at <c>speed &lt;= cruise</c> and then asked, on the next line, whether boosting was off *and*
    /// the speed was at or below cruise — a condition that could never be true. The switch appeared
    /// in the settings file, in the settings menu and in a documented ladder of three rungs, and
    /// changed nothing in any world. Nothing caught it: the file needs a session, a physics
    /// component and a grid group, so every decision it made was made where no check could look.
    /// </para>
    ///
    /// <para>
    /// So the tests that matter here are the ones that ask **what the switch changes**, not whether
    /// the arithmetic is pretty. A dead switch passes any test that only exercises one side of it.
    /// </para>
    /// </summary>
    public class TopSpeedForceTests
    {
        /// <summary>The shipped large-grid figures, so the pins below are the world's own.</summary>
        private const float Resistance = 1.5f;
        private const float BoostCeiling = 140f;

        /// <summary>
        /// **The switch changes the ceiling, and that is the assertion the defect would have
        /// failed.** Written as a difference rather than as two absolute values, because the way
        /// this broke was a switch that was read and then ignored — and a test of one side alone
        /// cannot tell that from a switch that works.
        /// </summary>
        [Fact]
        public void TurningBoostOffChangesWhereTheShipIsHeld()
        {
            float on = TopSpeedForce.Ceiling(true, 80f, BoostCeiling);
            float off = TopSpeedForce.Ceiling(false, 80f, BoostCeiling);

            Assert.NotEqual(on, off);

            // And the two rungs are the ones configuration.md describes: off is a speed a ship
            // cannot pass, on is a speed it is dragged back from.
            Assert.Equal(80f, off);
            Assert.Equal(BoostCeiling, on);
        }

        /// <summary>
        /// Boost off holds a ship at exactly its own cruise speed whatever the boost dial says — the
        /// dial is the thing being switched off, so it must not leak through.
        /// </summary>
        [Theory]
        [InlineData(0f)]
        [InlineData(60f)]
        [InlineData(140f)]
        [InlineData(1000f)]
        public void WithBoostOffTheBoostDialDoesNothing(float boostCeiling)
        {
            Assert.Equal(110f, TopSpeedForce.Ceiling(false, 110f, boostCeiling));
        }

        /// <summary>
        /// A boost ceiling under the cruise speed would hold a ship below the speed its own mass
        /// earns it, which is not one of the three rungs. Cruise is the floor either way.
        /// </summary>
        [Fact]
        public void ABoostCeilingBelowCruiseDoesNotHoldAShipUnderItsOwnCruise()
        {
            Assert.Equal(110f, TopSpeedForce.Ceiling(true, 110f, 60f));
            Assert.Equal(110f, TopSpeedForce.Ceiling(true, 110f, 0f));
        }

        /// <summary>
        /// Nothing is applied at or below cruise. This is what makes the mechanism a ceiling rather
        /// than a permanent tax on flying, and it is the reason a world can leave it on.
        /// </summary>
        [Theory]
        [InlineData(0f)]
        [InlineData(40f)]
        [InlineData(79.9f)]
        [InlineData(80f)]
        public void NoForceAtOrBelowCruise(float speed)
        {
            Assert.Equal(0f, TopSpeedForce.Newtons(Resistance, 1000000f, 80f, speed));
        }

        /// <summary>
        /// The original's arithmetic, at a point worked by hand: `1.5 × 1,000,000 × (1 − 80/100)`.
        /// A port that is off by a factor here is a world that flies differently from the mod it
        /// came from, which is the one thing `K10` promised it would not be.
        /// </summary>
        [Fact]
        public void TheForceIsTheOriginalsAtAWorkedPoint()
        {
            Assert.Equal(300000f, TopSpeedForce.Newtons(1.5f, 1000000f, 80f, 100f), 1);
        }

        /// <summary>
        /// It rises with how far over the ship is and approaches `resistance × mass`, so a ship a
        /// little over is nudged and one far over is hauled back.
        /// </summary>
        [Fact]
        public void TheForceRisesWithTheOverspeedAndApproachesItsAsymptote()
        {
            float previous = -1f;
            for (int speed = 81; speed <= 400; speed++)
            {
                float newtons = TopSpeedForce.Newtons(Resistance, 1000f, 80f, speed);
                Assert.True(newtons > previous, "the force fell off at " + speed + " m/s");
                previous = newtons;
            }

            // resistance x mass is the ceiling it never reaches.
            Assert.True(previous < Resistance * 1000f);
            Assert.Equal(Resistance * 1000f, TopSpeedForce.Newtons(Resistance, 1000f, 80f, 1e9f), 0);
        }

        /// <summary>
        /// A NaN speed is a physics component read mid-teleport, and the force it produces would
        /// reach <c>AddForce</c>. Nothing downstream of this rejects it.
        /// </summary>
        [Fact]
        public void GarbageInputsProduceNoForceRatherThanGarbageForce()
        {
            Assert.Equal(0f, TopSpeedForce.Newtons(Resistance, 1000f, 80f, float.NaN));
            Assert.Equal(0f, TopSpeedForce.Newtons(Resistance, float.NaN, 80f, 200f));
            Assert.Equal(0f, TopSpeedForce.Newtons(Resistance, 0f, 80f, 200f));
            Assert.Equal(0f, TopSpeedForce.Newtons(0f, 1000f, 80f, 200f));
            Assert.Equal(0f, TopSpeedForce.Newtons(Resistance, 1000f, 80f, -200f));
        }
    }
}
