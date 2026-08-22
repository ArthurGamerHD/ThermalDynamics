using System.Globalization;
using Thermodynamics.Core;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The one watt formatter, which used to be four.
    ///
    /// <para>
    /// The cockpit panel, the settings menu, the debug panel and the chat command each carried
    /// their own, and they had drifted into three conventions: one decimal place or two, a
    /// gigawatt tier or none, the current culture or the invariant one. A ship's heat balance was
    /// quoted at a different precision depending on which readout a player opened.
    /// </para>
    ///
    /// <para>
    /// The cases that matter are the boundaries and the sign. A figure one watt under a tier and
    /// one watt over it must not disagree about which tier it is in, and a negative figure — a
    /// grid shedding more than it makes, which is what the cockpit panel shows when cooling is
    /// working — must pick its tier by magnitude rather than falling through to raw watts.
    /// </para>
    /// </summary>
    public class UnitsTests
    {
        [Theory]
        [InlineData(0f, "0 W")]
        [InlineData(1f, "1 W")]
        [InlineData(999f, "999 W")]
        [InlineData(1000f, "1.0 kW")]
        [InlineData(999999f, "1,000.0 kW")]
        [InlineData(1000000f, "1.0 MW")]
        [InlineData(1e9f, "1.0 GW")]
        [InlineData(2.5e9f, "2.5 GW")]
        public void EachTierBeginsWhereTheOneBelowItEnds(float watts, string expected)
        {
            Assert.Equal(expected, Units.Watts(watts, 1, CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// A negative figure reads as the positive one with a sign, not as a different tier. The
        /// cockpit panel shows a grid's balance, which is negative exactly when the ship is
        /// winning.
        /// </summary>
        [Theory]
        [InlineData(-1500f, "-1.5 kW")]
        [InlineData(-2500000f, "-2.5 MW")]
        [InlineData(-500f, "-500 W")]
        public void ANegativeFigurePicksItsTierByMagnitude(float watts, string expected)
        {
            Assert.Equal(expected, Units.Watts(watts, 1, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void ThePlacesAskedForAreThePlacesGiven()
        {
            Assert.Equal("1 kW", Units.Watts(1234f, 0, CultureInfo.InvariantCulture));
            Assert.Equal("1.2 kW", Units.Watts(1234f, 1, CultureInfo.InvariantCulture));
            Assert.Equal("1.23 kW", Units.Watts(1234f, 2, CultureInfo.InvariantCulture));
            Assert.Equal("1.234 kW", Units.Watts(1234f, 3, CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Whole watts are written whole whatever is asked for. A tenth of a watt is never the
        /// interesting part of a figure small enough to be shown in watts, and a readout that put
        /// "512.00 W" beside "1.23 kW" would be spending two characters on nothing.
        /// </summary>
        [Fact]
        public void WholeWattsStayWhole()
        {
            Assert.Equal("512 W", Units.Watts(512.4f, 2, CultureInfo.InvariantCulture));
            Assert.Equal("512 W", Units.Watts(512.4f, 0, CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// A readout uses the player's own decimal separator; the chat command asks for invariant,
        /// because it echoes a figure it parsed as invariant and a player may paste it back.
        /// </summary>
        [Fact]
        public void TheCultureAskedForIsTheCultureUsed()
        {
            CultureInfo german = CultureInfo.GetCultureInfo("de-DE");

            Assert.Equal("1.5 MW", Units.Watts(1.5e6f, 1, CultureInfo.InvariantCulture));
            Assert.Equal("1,5 MW", Units.Watts(1.5e6f, 1, german));
        }

        /// <summary>
        /// A negative place count is a caller's slip rather than a request for something, so it is
        /// treated as none rather than throwing a format exception into a readout.
        /// </summary>
        [Fact]
        public void ANegativePlaceCountIsTakenAsNone()
        {
            Assert.Equal("2 MW", Units.Watts(2.4e6f, -3, CultureInfo.InvariantCulture));
        }
    }
}
