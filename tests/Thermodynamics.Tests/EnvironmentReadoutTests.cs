using Thermodynamics.Core;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The line the mod puts on screen as a matter of course**, and the one thing about it that
    /// can be checked outside a session: what it says.
    ///
    /// <para>
    /// The intent page decides that basic environmental information should be on screen without
    /// waiting for a failure, and sets the bar at *clear* rather than *present* — somebody who has
    /// never read this repository should be able to look at the screen and tell their ship is
    /// getting hotter. What that means in code is a decision about wording and thresholds, and a
    /// decision is a thing to pin; the drawing is a shell over it that only a session can judge
    /// (`B41`).
    /// </para>
    /// </summary>
    public class EnvironmentReadoutTests
    {
        /// <summary>A rating in the middle of what the game's blocks carry.</summary>
        private const float Critical = 1000f;

        private const float Ambient = 293.15f;

        /// <summary>
        /// **The word and the glow agree about what "hot" means, by construction.**
        ///
        /// <para>
        /// This is the assertion the whole type exists for. A player sees a block start to glow and
        /// reads a line that says the hull is hot; two readouts of one quantity that disagreed
        /// would be worse than either alone, and a threshold chosen independently would drift the
        /// first time the glow band moved (`D3`). So `hot` is not 900 K or four fifths of the
        /// rating — it is exactly `Incandescence.GlowStartKelvin`, and this checks the boundary
        /// from both sides rather than at it.
        /// </para>
        /// </summary>
        [Fact]
        public void HotBeginsExactlyWhereTheGlowBegins()
        {
            float glow = Incandescence.GlowStartKelvin(Critical);

            Assert.Equal(EnvironmentReadout.Heat.Hot,
                EnvironmentReadout.State(Ambient, glow, Critical));

            Assert.Equal(EnvironmentReadout.Heat.Warm,
                EnvironmentReadout.State(Ambient, glow - 1f, Critical));

            // And the glow itself agrees: nothing is glowing a degree below, something is at it.
            Assert.Equal(0f, Incandescence.Glow(glow - 1f, Critical));
            Assert.True(Incandescence.Glow(glow + 1f, Critical) > 0f);
        }

        /// <summary>
        /// `warm` is half the climb from the air, not half the rating. A hull sitting in a furnace
        /// is not warming up, and one in the cold that has climbed halfway to glowing is.
        /// </summary>
        [Fact]
        public void WarmIsHalfTheClimbFromTheAirRatherThanHalfTheRating()
        {
            float glow = Incandescence.GlowStartKelvin(Critical);
            float half = Ambient + ((glow - Ambient) * EnvironmentReadout.WarmShare);

            // **Around the boundary rather than at it, and the asymmetry with `hot` is real.**
            // `hot` is exact by construction — the climb at the glow is one subtraction divided by
            // itself — but the half point is a multiply and a divide, and 0.49999997 is a fair
            // answer to it. A test that asserted the exact midpoint would be asserting the rounding
            // of one expression, and a caller that depended on it would be doing the same.
            Assert.Equal(EnvironmentReadout.Heat.Warm, EnvironmentReadout.State(Ambient, half + 1f, Critical));
            Assert.Equal(EnvironmentReadout.Heat.Cool, EnvironmentReadout.State(Ambient, half - 1f, Critical));

            // The same hull temperature, in a hotter world, is further along its own climb — which
            // is the whole reason the air is the baseline.
            float hotterAir = Ambient + 200f;
            Assert.Equal(EnvironmentReadout.Heat.Hot,
                EnvironmentReadout.State(hotterAir, hotterAir + ((glow - hotterAir) * 1.1f), Critical));

            // And a hull at the air is cool however hot the air is.
            Assert.Equal(EnvironmentReadout.Heat.Cool, EnvironmentReadout.State(Ambient, Ambient, Critical));
            Assert.Equal(EnvironmentReadout.Heat.Cool, EnvironmentReadout.State(hotterAir, hotterAir, Critical));
        }

        /// <summary>
        /// The climb keeps counting past the glow rather than clamping, because a caller that wants
        /// a bar can clamp and one that wants to know how far past cannot recover it.
        /// </summary>
        [Fact]
        public void TheClimbKeepsCountingPastTheGlow()
        {
            float glow = Incandescence.GlowStartKelvin(Critical);

            Assert.Equal(0f, EnvironmentReadout.Climb(Ambient, Ambient, Critical), 4);
            Assert.Equal(1f, EnvironmentReadout.Climb(Ambient, glow, Critical), 4);
            Assert.True(EnvironmentReadout.Climb(Ambient, Critical, Critical) > 1f);
        }

        /// <summary>
        /// **Every degenerate world reads as cool rather than as a number from nothing** (`E8`). The
        /// cases are the ones a session actually produces: a block with no rating, a world hotter
        /// than the glow, and a hull below the air it sits in.
        /// </summary>
        [Fact]
        public void AWorldWithNoClimbToMeasureReadsCoolRatherThanInventingOne()
        {
            Assert.Equal(0f, EnvironmentReadout.Climb(Ambient, 5000f, 0f));
            Assert.Equal(EnvironmentReadout.Heat.Cool, EnvironmentReadout.State(Ambient, 5000f, 0f));

            // Air already past the glow: nothing the hull does in it is warming.
            float furnace = Incandescence.GlowStartKelvin(Critical) + 50f;
            Assert.Equal(0f, EnvironmentReadout.Climb(furnace, furnace + 500f, Critical));

            // Colder than the air it is in.
            Assert.Equal(0f, EnvironmentReadout.Climb(Ambient, Ambient - 80f, Critical));

            Assert.Equal(0f, EnvironmentReadout.Climb(float.NaN, 400f, Critical));
            Assert.Equal(0f, EnvironmentReadout.Climb(Ambient, float.NaN, Critical));
            Assert.Equal(0f, EnvironmentReadout.Climb(Ambient, 400f, float.NaN));
        }

        /// <summary>
        /// The line carries the air always and the ship only where there is a rating to judge it
        /// against — a word derived from nothing is worse than no word.
        /// </summary>
        [Fact]
        public void TheLineDropsTheShipsHalfRatherThanGuessingIt()
        {
            string whole = EnvironmentReadout.Line(Ambient, 400f, Critical);
            Assert.Contains("hull", whole);
            Assert.Contains(TemperatureScale.ToCelsiusString(Ambient), whole);

            string airOnly = EnvironmentReadout.Line(Ambient, 400f, 0f);
            Assert.DoesNotContain("hull", airOnly);
            Assert.Contains(TemperatureScale.ToCelsiusString(Ambient), airOnly);
        }

        /// <summary>
        /// The three words are three words. A readout whose states collapsed would pass every
        /// threshold test above and say the same thing all the way up (`E8`).
        /// </summary>
        [Fact]
        public void TheThreeStatesReadDifferently()
        {
            float glow = Incandescence.GlowStartKelvin(Critical);

            string cool = EnvironmentReadout.Line(Ambient, Ambient, Critical);
            string warm = EnvironmentReadout.Line(Ambient, Ambient + ((glow - Ambient) * 0.75f), Critical);
            string hot = EnvironmentReadout.Line(Ambient, glow + 10f, Critical);

            Assert.NotEqual(cool, warm);
            Assert.NotEqual(warm, hot);
            Assert.NotEqual(cool, hot);
        }
    }
}
