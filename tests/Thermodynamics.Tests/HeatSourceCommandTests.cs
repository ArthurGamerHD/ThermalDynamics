using Thermodynamics;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Reading the <c>/thermal heat</c> command.
    ///
    /// <para>
    /// This is the half of the debug tool that can be quietly wrong. Registering a source is one
    /// call into a registry with its own tests; deciding that <c>5M</c> means five megawatts and
    /// <c>5</c> means five watts, that <c>pulse 5M 30</c> is watts-then-seconds and not the other
    /// way round, and that a malformed command is a usage message rather than a zero-watt source
    /// nobody can see — that is where a slip becomes a modder wondering why their bonfire does
    /// nothing.
    /// </para>
    /// </summary>
    public class HeatSourceCommandTests
    {
        // ---- magnitudes ------------------------------------------------------------------------

        /// <summary>
        /// The suffixes exist because a useful source is megawatts. Getting one wrong by a
        /// thousand is invisible in the chat reply and obvious only as a source that does nothing.
        /// </summary>
        [Theory]
        [InlineData("5", 5f)]
        [InlineData("5k", 5e3f)]
        [InlineData("5K", 5e3f)]
        [InlineData("5m", 5e6f)]
        [InlineData("5M", 5e6f)]
        [InlineData("5g", 5e9f)]
        [InlineData("5G", 5e9f)]
        [InlineData("2.5M", 2.5e6f)]
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
        public void NonsenseIsRefusedRatherThanReadAsZero(string text)
        {
            float watts;
            Assert.False(HeatSourceCommand.TryWatts(text, out watts));
        }

        // ---- verbs -----------------------------------------------------------------------------

        [Fact]
        public void ABareNumberPlacesASteadySource()
        {
            HeatSourceCommand.Parsed parsed = HeatSourceCommand.Parse("5M");

            Assert.Equal(HeatSourceCommand.Verb.Place, parsed.Verb);
            Assert.Equal(5e6f, parsed.Watts);
            Assert.Equal(HeatSourceCommand.DefaultRange, parsed.Range);
            Assert.Null(parsed.Error);
        }

        [Fact]
        public void ARangeMayFollowTheWatts()
        {
            HeatSourceCommand.Parsed parsed = HeatSourceCommand.Parse("5M 500");

            Assert.Equal(HeatSourceCommand.Verb.Place, parsed.Verb);
            Assert.Equal(5e6f, parsed.Watts);
            Assert.Equal(500f, parsed.Range);
        }

        /// <summary>
        /// A pulse is watts then seconds, in that order. Reversing them would place a 30 W source
        /// for five million seconds — which looks like a working command and does nothing visible.
        /// </summary>
        [Fact]
        public void APulseIsWattsThenSeconds()
        {
            HeatSourceCommand.Parsed parsed = HeatSourceCommand.Parse("pulse 5M 30");

            Assert.Equal(HeatSourceCommand.Verb.Pulse, parsed.Verb);
            Assert.Equal(5e6f, parsed.Watts);
            Assert.Equal(30f, parsed.Seconds);
        }

        [Fact]
        public void APulseTakesAnOptionalRange()
        {
            HeatSourceCommand.Parsed parsed = HeatSourceCommand.Parse("pulse 5M 30 750");
            Assert.Equal(750f, parsed.Range);
        }

        [Fact]
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
        public void TheOtherVerbsAreRecognised(string argument, HeatSourceCommand.Verb expected)
        {
            Assert.Equal(expected, HeatSourceCommand.Parse(argument).Verb);
        }

        // ---- refusals --------------------------------------------------------------------------

        /// <summary>
        /// **A malformed command must not place a source.** Every one of these used to be a way to
        /// register something at zero watts, or at a wattage the typist did not mean, and the only
        /// symptom would be a source in the list that never warms anything.
        /// </summary>
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
        public void AMalformedCommandFallsBackToHelpWithAReason(string argument)
        {
            HeatSourceCommand.Parsed parsed = HeatSourceCommand.Parse(argument);

            Assert.Equal(HeatSourceCommand.Verb.Help, parsed.Verb);
            Assert.False(string.IsNullOrEmpty(parsed.Error),
                "a refusal must say why: " + argument);
        }

        /// <summary>An unreadable range falls back to the default rather than to zero.</summary>
        [Fact]
        public void AnUnreadableRangeFallsBackRatherThanZeroing()
        {
            HeatSourceCommand.Parsed parsed = HeatSourceCommand.Parse("5M nearby");
            Assert.Equal(HeatSourceCommand.DefaultRange, parsed.Range);
        }

        // ---- reporting -------------------------------------------------------------------------

        [Theory]
        [InlineData(500f, "500 W")]
        [InlineData(5e3f, "5.00 kW")]
        [InlineData(5e6f, "5.00 MW")]
        [InlineData(5e9f, "5.00 GW")]
        public void WattsAreReportedInTheUnitAPersonWouldSay(float watts, string expected)
        {
            Assert.Equal(expected, HeatSourceCommand.Describe(watts));
        }
    }
}
