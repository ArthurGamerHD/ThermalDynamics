using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    public class FirstStepLabTests
    {
        [Fact]

        public void ParsesMinorFaultsFromARealShapedStatLine()
        {
            string stat = "12345 (dotnet) R 100 100 100 0 -1 4194304 2926 0 0 0 5 1 0 0 20";
            Assert.Equal(2926, FirstStepLab.ParseMinorFaults(stat));
        }

        [Fact]

        public void ACommandNameWithSpacesAndParenthesesDoesNotShiftTheFields()
        {
            string stat = "999 (a (weird) name) S 1 1 1 0 -1 4194304 777 0 0 0 5 1 0 0 20";
            Assert.Equal(777, FirstStepLab.ParseMinorFaults(stat));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("no parenthesis here")]
        [InlineData("1 (short) R 1 2")]
        [InlineData("1 (bad) R 1 1 1 0 -1 4194304 notanumber 0 0 0 5 1 0 0")]

        public void ALineItCannotReadIsUnknownNotZero(string stat)
        {
            Assert.Equal(-1, FirstStepLab.ParseMinorFaults(stat));
        }

        [Fact]

        public void TheLiveCounterOnThisPlatformIsMonotonic()
        {
            long first = FirstStepLab.MinorFaults();
            if (first < 0) return;

            byte[] touch = new byte[1 << 20];
            touch[0] = 1;
            touch[touch.Length - 1] = 1;

            long second = FirstStepLab.MinorFaults();
            Assert.True(second >= first, "the counter went backwards: " + first + " then " + second);
        }
    }
}
