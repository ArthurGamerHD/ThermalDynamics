using System;
using Thermodynamics.Core;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class DayLengthTests
    {
/// <summary>Spin operation.</summary>
        private static DayLength Spin(float day, float seconds, float step = 1f / 60f)
        {
/// <summary>DayLength operation.</summary>
            DayLength length = new DayLength();

            for (float t = 0f; t < seconds; t += step)
            {
                double angle = 2d * Math.PI * t / day;
                length.Observe(
/// <summary>Vector3 operation.</summary>
                    new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle)), step);
            }

            return length;
        }

        [Fact]
/// <summary>ItMeasuresTheDayItIsShown operation.</summary>
        public void ItMeasuresTheDayItIsShown()
        {
            foreach (float day in new[] { 240f, 1200f, 7200f })
            {
/// <summary>Spin operation.</summary>
                DayLength length = Spin(day, day * 0.3f);

                Assert.True(length.Known, "no estimate after a third of a " + day + " s day");
                Assert.Equal(day, length.Seconds, day * 0.02f);
            }
        }

        [Fact]
/// <summary>ItSaysNothingUntilItHasSeenEnough operation.</summary>
        public void ItSaysNothingUntilItHasSeenEnough()
        {
/// <summary>DayLength operation.</summary>
            DayLength length = new DayLength();
            Assert.False(length.Known);
            Assert.True(length.Seconds < 0f);

/// <summary>Spin operation.</summary>
            DayLength barely = Spin(7200f, 7200f * 0.005f);
            Assert.False(barely.Known, "an estimate was offered from half a per cent of a turn");
        }

        [Fact]
/// <summary>AStillSunIsNotADayOfZeroLength operation.</summary>
        public void AStillSunIsNotADayOfZeroLength()
        {
/// <summary>DayLength operation.</summary>
            DayLength length = new DayLength();
            for (int i = 0; i < 10000; i++) length.Observe(Vector3.UnitX, 1f / 60f);

            Assert.False(length.Known);
        }

        [Fact]
/// <summary>AStalledFrameDoesNotShortenTheDay operation.</summary>
        public void AStalledFrameDoesNotShortenTheDay()
        {
/// <summary>DayLength operation.</summary>
            DayLength length = new DayLength();
            float day = 1200f;
            float step = 1f / 60f;
            float t = 0f;

            for (int i = 0; i < 12000; i++)
            {
                float taken = (i % 100) == 0 ? 30f : step;
                t += step;

                double angle = 2d * Math.PI * t / day;
                length.Observe(
/// <summary>Vector3 operation.</summary>
                    new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle)), taken);
            }

            Assert.True(length.Known);
            Assert.Equal(day, length.Seconds, day * 0.05f);
        }

        [Fact]
/// <summary>ResetForgetsTheWorld operation.</summary>
        public void ResetForgetsTheWorld()
        {
/// <summary>Spin operation.</summary>
            DayLength length = Spin(1200f, 600f);
            Assert.True(length.Known);

            length.Reset();
            Assert.False(length.Known);
        }

        [Fact]
/// <summary>TheShareScalesWithTheDayWhereTheAbsoluteDidNot operation.</summary>
        public void TheShareScalesWithTheDayWhereTheAbsoluteDidNot()
        {
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            float shortDay = planet.LagSecondsFor(240f);
            float longDay = planet.LagSecondsFor(7200f);

            Assert.True(longDay > shortDay * 20f,
                "a thirty-times longer day should lag proportionally: " + shortDay + " against "
                + longDay);

            Assert.Equal(planet.AmbientLagShareOfDay * 7200f, longDay, 2);

            Assert.Equal(planet.AmbientLagSeconds, planet.LagSecondsFor(0f), 3);
            Assert.Equal(planet.AmbientLagSeconds, planet.LagSecondsFor(-1f), 3);
        }

        [Fact]
/// <summary>ThePeakLandsAfterNoonByAShareOfTheDay operation.</summary>
        public void ThePeakLandsAfterNoonByAShareOfTheDay()
        {
            Assert.True(PeakFraction(7200f) > 0.52f,
/// <summary>PeakFraction operation.</summary>
                "the peak should land after noon on a long day: " + PeakFraction(7200f));
            Assert.True(PeakFraction(240f) > 0.52f,
/// <summary>PeakFraction operation.</summary>
                "and on a short one too: " + PeakFraction(240f));

            Assert.Equal(PeakFraction(7200f), PeakFraction(240f), 0.05f);
        }

/// <summary>PeakFraction operation.</summary>
        private static float PeakFraction(float day)
        {
            PlanetThermalProperties planet = PlanetThermalProperties.Default();
            float lag = planet.LagSecondsFor(day);

            const int Steps = 2000;
            float step = day / Steps;
            float heating = -1f;
            float best = 0f;
            float at = 0f;

            for (int i = 0; i < Steps; i++)
            {
                float fraction = i / (float)Steps;
                float sine = (float)Math.Sin(2d * Math.PI * (fraction - 0.25d));

                heating = WindProfile.Heating(heating, sine, step, lag);
                if (heating > best) { best = heating; at = fraction; }
            }

            return at;
        }
    }
}
