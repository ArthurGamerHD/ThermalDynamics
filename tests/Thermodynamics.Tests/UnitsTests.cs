using System.Globalization;
using Thermodynamics.Core;

namespace Thermodynamics.Tests
{
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

        [Fact]

        public void WholeWattsStayWhole()
        {
            Assert.Equal("512 W", Units.Watts(512.4f, 2, CultureInfo.InvariantCulture));
            Assert.Equal("512 W", Units.Watts(512.4f, 0, CultureInfo.InvariantCulture));
        }

        [Fact]

        public void TheCultureAskedForIsTheCultureUsed()
        {
            CultureInfo german = CultureInfo.GetCultureInfo("de-DE");

            Assert.Equal("1.5 MW", Units.Watts(1.5e6f, 1, CultureInfo.InvariantCulture));
            Assert.Equal("1,5 MW", Units.Watts(1.5e6f, 1, german));
        }

        [Fact]

        public void ANegativePlaceCountIsTakenAsNone()
        {
            Assert.Equal("2 MW", Units.Watts(2.4e6f, -3, CultureInfo.InvariantCulture));
        }
    }
}
