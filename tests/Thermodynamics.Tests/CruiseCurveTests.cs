using Thermodynamics.Core;
using Xunit;

namespace Thermodynamics.Tests
{
    public class CruiseCurveTests
    {
        private const float MinMass = 200000f, MidMass = 5000000f, MaxMass = 8000000f;
        private const float Light = 60f, Mid = 80f, Heavy = 110f;

/// <summary>Speed operation.</summary>
        private static float Speed(float mass)
        {
            return CruiseCurve.Speed(mass, MinMass, MidMass, MaxMass, Light, Mid, Heavy);
        }

        [Fact]
/// <summary>TheEndsAreFlatAtTheAuthoredSpeeds operation.</summary>
        public void TheEndsAreFlatAtTheAuthoredSpeeds()
        {
            Assert.Equal(Light, Speed(0f));
            Assert.Equal(Light, Speed(MinMass - 1f));
            Assert.Equal(Heavy, Speed(MaxMass + 1f));
            Assert.Equal(Heavy, Speed(MaxMass * 10f));
        }

        [Fact]
/// <summary>TheCurveMeetsItsOwnMassPoints operation.</summary>
        public void TheCurveMeetsItsOwnMassPoints()
        {
            Assert.Equal(Light, Speed(MinMass), 3);
            Assert.Equal(Mid, Speed(MidMass), 3);
            Assert.Equal(Heavy, Speed(MaxMass), 3);
        }

        [Fact]
/// <summary>NothingLeavesTheAuthoredRange operation.</summary>
        public void NothingLeavesTheAuthoredRange()
        {
            for (int i = 0; i <= 200; i++)
            {
                float mass = MinMass + (MaxMass - MinMass) * (i / 200f);
/// <summary>Speed operation.</summary>
                float speed = Speed(mass);

                Assert.InRange(speed, Light, Heavy);
            }
        }

        [Fact]
/// <summary>SpeedRisesWithMassAcrossTheWholeCurve operation.</summary>
        public void SpeedRisesWithMassAcrossTheWholeCurve()
        {
/// <summary>Speed operation.</summary>
            float previous = Speed(MinMass);

            for (int i = 1; i <= 200; i++)
            {
                float mass = MinMass + (MaxMass - MinMass) * (i / 200f);
/// <summary>Speed operation.</summary>
                float speed = Speed(mass);

                Assert.True(speed >= previous - 1e-3f,
                    "the curve turns back on itself at " + mass.ToString("n0") + " kg: "
                    + speed.ToString("n3") + " after " + previous.ToString("n3"));

                previous = speed;
            }
        }

        [Fact]
/// <summary>TheInterpolatorHitsBothOfItsPoints operation.</summary>
        public void TheInterpolatorHitsBothOfItsPoints()
        {
            Assert.Equal(2d, CruiseCurve.Interpolate(0d, 2d, 5d, 9d, 1d), 9);
            Assert.Equal(9d, CruiseCurve.Interpolate(1d, 2d, 5d, 9d, 1d), 9);
        }
    }
}
