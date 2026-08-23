using System;
using Thermodynamics.Core;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The climate's lag is a share of the day, and the day is measured from the sun.
    ///
    /// <para>
    /// `AmbientLagSeconds` was 45 absolute seconds against a rotation a server sets to anything: it
    /// attenuates a four-minute day to less than half and does nothing at all to a two-hour one, so
    /// one authored figure meant a different climate on every world it was applied to.
    /// [backlog](../../docs/backlog.md) `C6`.
    /// </para>
    ///
    /// <para>
    /// **Measured rather than read.** `MySectorWeatherComponent.RotationInterval` exists and would
    /// answer directly, but whether the script whitelist admits it cannot be established outside a
    /// session and a rejected type takes the mod down at world load. The sun's direction is already
    /// sampled every frame, and the angle it sweeps is the same fact.
    /// </para>
    /// </summary>
    public class DayLengthTests
    {
        /// <summary>Feeds an estimator a sun going round once every <paramref name="day"/> seconds.</summary>
        private static DayLength Spin(float day, float seconds, float step = 1f / 60f)
        {
            DayLength length = new DayLength();

            for (float t = 0f; t < seconds; t += step)
            {
                double angle = 2d * Math.PI * t / day;
                length.Observe(
                    new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle)), step);
            }

            return length;
        }

        [Fact]
        public void ItMeasuresTheDayItIsShown()
        {
            foreach (float day in new[] { 240f, 1200f, 7200f })
            {
                DayLength length = Spin(day, day * 0.3f);

                Assert.True(length.Known, "no estimate after a third of a " + day + " s day");
                Assert.Equal(day, length.Seconds, day * 0.02f);
            }
        }

        /// <summary>
        /// It says nothing until it has seen enough of a turn. A rate taken over an angle the
        /// arc-cosine can barely resolve is a number with no information in it, and reporting one
        /// would put a whole world's climate on it.
        /// </summary>
        [Fact]
        public void ItSaysNothingUntilItHasSeenEnough()
        {
            DayLength length = new DayLength();
            Assert.False(length.Known);
            Assert.True(length.Seconds < 0f);

            // A hundredth of a turn is under the threshold.
            DayLength barely = Spin(7200f, 7200f * 0.005f);
            Assert.False(barely.Known, "an estimate was offered from half a per cent of a turn");
        }

        /// <summary>
        /// A sun that is not moving is a paused world or a static one, and averaging its stillness
        /// in would report a day of no length at all.
        /// </summary>
        [Fact]
        public void AStillSunIsNotADayOfZeroLength()
        {
            DayLength length = new DayLength();
            for (int i = 0; i < 10000; i++) length.Observe(Vector3.UnitX, 1f / 60f);

            Assert.False(length.Known);
        }

        /// <summary>
        /// A frame that took a second of real time — a world load, a paste, a stall — moves the sun
        /// as far as many ordinary frames and would read as a much faster rotation. Bounding the
        /// step is cheaper than reasoning about which frames were honest.
        /// </summary>
        [Fact]
        public void AStalledFrameDoesNotShortenTheDay()
        {
            DayLength length = new DayLength();
            float day = 1200f;
            float step = 1f / 60f;
            float t = 0f;

            for (int i = 0; i < 12000; i++)
            {
                // Every hundredth sample claims to have taken far too long.
                float taken = (i % 100) == 0 ? 30f : step;
                t += step;

                double angle = 2d * Math.PI * t / day;
                length.Observe(
                    new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle)), taken);
            }

            Assert.True(length.Known);
            Assert.Equal(day, length.Seconds, day * 0.05f);
        }

        /// <summary>
        /// Reset forgets the world, so a session that loads a second one measures it rather than
        /// inheriting a rotation it does not have.
        /// </summary>
        [Fact]
        public void ResetForgetsTheWorld()
        {
            DayLength length = Spin(1200f, 600f);
            Assert.True(length.Known);

            length.Reset();
            Assert.False(length.Known);
        }

        /// <summary>
        /// **The point of measuring it.** The same authored share becomes a lag proportional to the
        /// day, where the same authored seconds did not — which is the whole of `C6`.
        /// </summary>
        [Fact]
        public void TheShareScalesWithTheDayWhereTheAbsoluteDidNot()
        {
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            float shortDay = planet.LagSecondsFor(240f);
            float longDay = planet.LagSecondsFor(7200f);

            Assert.True(longDay > shortDay * 20f,
                "a thirty-times longer day should lag proportionally: " + shortDay + " against "
                + longDay);

            Assert.Equal(planet.AmbientLagShareOfDay * 7200f, longDay, 2);

            // And with no day measured yet, the authored absolute is what runs — which is what a
            // session gets for its first few seconds.
            Assert.Equal(planet.AmbientLagSeconds, planet.LagSecondsFor(0f), 3);
            Assert.Equal(planet.AmbientLagSeconds, planet.LagSecondsFor(-1f), 3);
        }

        /// <summary>
        /// And the consequence a player would notice: the hottest moment of the day lands after
        /// noon by a share of the day rather than by a fixed handful of seconds.
        /// </summary>
        [Fact]
        public void ThePeakLandsAfterNoonByAShareOfTheDay()
        {
            Assert.True(PeakFraction(7200f) > 0.52f,
                "the peak should land after noon on a long day: " + PeakFraction(7200f));
            Assert.True(PeakFraction(240f) > 0.52f,
                "and on a short one too: " + PeakFraction(240f));

            // Within a few per cent of each other, because the lag is the same share of both.
            Assert.Equal(PeakFraction(7200f), PeakFraction(240f), 0.05f);
        }

        /// <summary>
        /// Where in the day the lagged heating peaks, as a fraction. Noon is 0.5.
        /// </summary>
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
