using Thermodynamics;
using Xunit;

namespace Thermodynamics.Tests
{
    public class HeatSourceCommandTests
    {

        [Theory]
        [InlineData("5", 5f)]
        [InlineData("5k", 5e3f)]
        [InlineData("5K", 5e3f)]
        [InlineData("5m", 5e6f)]
        [InlineData("5M", 5e6f)]
        [InlineData("5g", 5e9f)]
        [InlineData("5G", 5e9f)]
        [InlineData("2.5M", 2.5e6f)]
/// <summary>SuffixesScaleTheNumber operation.</summary>
        public void SuffixesScaleTheNumber(string text, float expected)
        {
            float watts;
            Assert.True(HeatSourceCommand.TryWatts(text, out watts));
            Assert.Equal(expected, watts);
        }

        [Theory]
        [InlineData("")]
        [InlineData("M")]
        [InlineData("bonfire")]
        [InlineData("5x")]
/// <summary>NonsenseIsRefusedRatherThanReadAsZero operation.</summary>
        public void NonsenseIsRefusedRatherThanReadAsZero(string text)
        {
            float watts;
            Assert.False(HeatSourceCommand.TryWatts(text, out watts));
        }


        [Fact]
/// <summary>ABareNumberPlacesASteadySource operation.</summary>
        public void ABareNumberPlacesASteadySource()
        {
            HeatSourceCommand.Parsed parsed = HeatSourceCommand.Parse("5M");

            Assert.Equal(HeatSourceCommand.Verb.Place, parsed.Verb);
            Assert.Equal(5e6f, parsed.Watts);
            Assert.Equal(HeatSourceCommand.DefaultRange, parsed.Range);
            Assert.Null(parsed.Error);
        }

        [Fact]
/// <summary>ARangeMayFollowTheWatts operation.</summary>
        public void ARangeMayFollowTheWatts()
        {
            HeatSourceCommand.Parsed parsed = HeatSourceCommand.Parse("5M 500");

            Assert.Equal(HeatSourceCommand.Verb.Place, parsed.Verb);
            Assert.Equal(5e6f, parsed.Watts);
            Assert.Equal(500f, parsed.Range);
        }

        [Fact]
/// <summary>APulseIsWattsThenSeconds operation.</summary>
        public void APulseIsWattsThenSeconds()
        {
            HeatSourceCommand.Parsed parsed = HeatSourceCommand.Parse("pulse 5M 30");

            Assert.Equal(HeatSourceCommand.Verb.Pulse, parsed.Verb);
            Assert.Equal(5e6f, parsed.Watts);
            Assert.Equal(30f, parsed.Seconds);
        }

        [Fact]
/// <summary>APulseTakesAnOptionalRange operation.</summary>
        public void APulseTakesAnOptionalRange()
        {
            HeatSourceCommand.Parsed parsed = HeatSourceCommand.Parse("pulse 5M 30 750");
            Assert.Equal(750f, parsed.Range);
        }

        [Fact]
/// <summary>Sets the carriesanidandawattage.</summary>
        public void SetCarriesAnIdAndAWattage()
        {
            HeatSourceCommand.Parsed parsed = HeatSourceCommand.Parse("set 3 250k");

            Assert.Equal(HeatSourceCommand.Verb.Set, parsed.Verb);
            Assert.Equal(3, parsed.Id);
            Assert.Equal(250e3f, parsed.Watts);
        }

        [Theory]
        [InlineData("remove 7", HeatSourceCommand.Verb.Remove)]
        [InlineData("list", HeatSourceCommand.Verb.List)]
        [InlineData("clear", HeatSourceCommand.Verb.Clear)]
        [InlineData("", HeatSourceCommand.Verb.Help)]
        [InlineData("help", HeatSourceCommand.Verb.Help)]
/// <summary>TheOtherVerbsAreRecognised operation.</summary>
        public void TheOtherVerbsAreRecognised(string argument, HeatSourceCommand.Verb expected)
        {
            Assert.Equal(expected, HeatSourceCommand.Parse(argument).Verb);
        }


        [Theory]
        [InlineData("set")]
        [InlineData("set 3")]
        [InlineData("set three 5M")]
        [InlineData("remove")]
        [InlineData("remove all")]
        [InlineData("pulse")]
        [InlineData("pulse 5M")]
        [InlineData("pulse 5M forever")]
        [InlineData("pulse 5M 0")]
        [InlineData("0")]
        [InlineData("-5M")]
/// <summary>AMalformedCommandFallsBackToHelpWithAReason operation.</summary>
        public void AMalformedCommandFallsBackToHelpWithAReason(string argument)
        {
            HeatSourceCommand.Parsed parsed = HeatSourceCommand.Parse(argument);

            Assert.Equal(HeatSourceCommand.Verb.Help, parsed.Verb);
            Assert.False(string.IsNullOrEmpty(parsed.Error),
                "a refusal must say why: " + argument);
        }

        [Fact]
/// <summary>AnUnreadableRangeFallsBackRatherThanZeroing operation.</summary>
        public void AnUnreadableRangeFallsBackRatherThanZeroing()
        {
            HeatSourceCommand.Parsed parsed = HeatSourceCommand.Parse("5M nearby");
            Assert.Equal(HeatSourceCommand.DefaultRange, parsed.Range);
        }


        [Theory]
        [InlineData(500f, "500 W")]
        [InlineData(5e3f, "5.00 kW")]
        [InlineData(5e6f, "5.00 MW")]
        [InlineData(5e9f, "5.00 GW")]
/// <summary>WattsAreReportedInTheUnitAPersonWouldSay operation.</summary>
        public void WattsAreReportedInTheUnitAPersonWouldSay(float watts, string expected)
        {
            Assert.Equal(expected, HeatSourceCommand.Describe(watts));
        }
    }
}
