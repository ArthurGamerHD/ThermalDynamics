using Thermodynamics.Core;
using Xunit;

namespace Thermodynamics.Tests
{
    public class TopSpeedForceTests
    {
        private const float Resistance = 1.5f;
        private const float BoostCeiling = 140f;

        [Fact]
/// <summary>TurningBoostOffChangesWhereTheShipIsHeld operation.</summary>
        public void TurningBoostOffChangesWhereTheShipIsHeld()
        {
            float on = TopSpeedForce.Ceiling(true, 80f, BoostCeiling);
            float off = TopSpeedForce.Ceiling(false, 80f, BoostCeiling);

            Assert.NotEqual(on, off);

            Assert.Equal(80f, off);
            Assert.Equal(BoostCeiling, on);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(60f)]
        [InlineData(140f)]
        [InlineData(1000f)]
/// <summary>WithBoostOffTheBoostDialDoesNothing operation.</summary>
        public void WithBoostOffTheBoostDialDoesNothing(float boostCeiling)
        {
            Assert.Equal(110f, TopSpeedForce.Ceiling(false, 110f, boostCeiling));
        }

        [Fact]
/// <summary>ABoostCeilingBelowCruiseDoesNotHoldAShipUnderItsOwnCruise operation.</summary>
        public void ABoostCeilingBelowCruiseDoesNotHoldAShipUnderItsOwnCruise()
        {
            Assert.Equal(110f, TopSpeedForce.Ceiling(true, 110f, 60f));
            Assert.Equal(110f, TopSpeedForce.Ceiling(true, 110f, 0f));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(40f)]
        [InlineData(79.9f)]
        [InlineData(80f)]
/// <summary>NoForceAtOrBelowCruise operation.</summary>
        public void NoForceAtOrBelowCruise(float speed)
        {
            Assert.Equal(0f, TopSpeedForce.Newtons(Resistance, 1000000f, 80f, speed));
        }

        [Fact]
/// <summary>TheForceIsTheOriginalsAtAWorkedPoint operation.</summary>
        public void TheForceIsTheOriginalsAtAWorkedPoint()
        {
            Assert.Equal(300000f, TopSpeedForce.Newtons(1.5f, 1000000f, 80f, 100f), 1);
        }

        [Fact]
/// <summary>TheForceRisesWithTheOverspeedAndApproachesItsAsymptote operation.</summary>
        public void TheForceRisesWithTheOverspeedAndApproachesItsAsymptote()
        {
            float previous = -1f;
            for (int speed = 81; speed <= 400; speed++)
            {
                float newtons = TopSpeedForce.Newtons(Resistance, 1000f, 80f, speed);
                Assert.True(newtons > previous, "the force fell off at " + speed + " m/s");
                previous = newtons;
            }

            Assert.True(previous < Resistance * 1000f);
            Assert.Equal(Resistance * 1000f, TopSpeedForce.Newtons(Resistance, 1000f, 80f, 1e9f), 0);
        }

        [Fact]
/// <summary>GarbageInputsProduceNoForceRatherThanGarbageForce operation.</summary>
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
