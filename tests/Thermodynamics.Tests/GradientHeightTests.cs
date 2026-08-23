using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// A boundary layer cannot be taller than the atmosphere it is in.
    ///
    /// <para>
    /// `WindGradientHeight` ships at 600 m and several of the game's own worlds have less air than
    /// that standing over their ground — Titan's atmosphere is 285 m deep, Europa's and the Moon's
    /// are 570 and under, and Triton's peaks stand in vacuum outright. The vertical profile was
    /// being asked about heights with no air at them, and the only thing keeping the answer sane
    /// was the engine's own wind ceiling reaching zero first. [backlog](../../docs/backlog.md)
    /// `B20`.
    /// </para>
    /// </summary>
    public class GradientHeightTests
    {
        [Fact]
        public void AThickAtmosphereLeavesTheConfiguredHeightAlone()
        {
            Assert.Equal(600f, WindProfile.GradientHeightIn(600f, 12000f), 3);
            Assert.Equal(600f, WindProfile.GradientHeightIn(600f, 600f), 3);
        }

        [Fact]
        public void AThinAtmosphereCapsIt()
        {
            // Titan, whose air is 285 m deep against a 600 m boundary layer.
            Assert.Equal(285f, WindProfile.GradientHeightIn(600f, 285f), 3);
        }

        /// <summary>
        /// Never below the reference height the profile is defined against, because a gradient
        /// height under it turns the profile inside out — `Multiplier` clamps the height to the
        /// gradient, and a gradient under ten metres would make every wind weaker at the ground
        /// than at the reference it is measured from.
        /// </summary>
        [Fact]
        public void ItNeverFallsBelowTheReferenceHeight()
        {
            Assert.Equal(WindProfile.ReferenceHeight, WindProfile.GradientHeightIn(600f, 1f), 3);
            Assert.Equal(WindProfile.ReferenceHeight, WindProfile.GradientHeightIn(600f, 0.001f), 3);
        }

        /// <summary>
        /// An airless world leaves it alone rather than collapsing it. There is no wind there for a
        /// profile to shape, and a zero would read as "cap it at nothing" instead of "there is
        /// nothing to cap".
        /// </summary>
        [Fact]
        public void AnAirlessWorldLeavesItAlone()
        {
            Assert.Equal(600f, WindProfile.GradientHeightIn(600f, 0f), 3);
            Assert.Equal(600f, WindProfile.GradientHeightIn(600f, -100f), 3);
        }

        /// <summary>
        /// And the consequence on a real world: on a moon whose air is shallower than the boundary
        /// layer, the profile stops climbing where the air stops rather than going on into vacuum.
        /// </summary>
        [Fact]
        public void OnAShallowWorldTheProfileStopsWhereTheAirDoes()
        {
            const float Shallow = 285f;

            float capped = WindProfile.GradientHeightIn(600f, Shallow);
            float roughness = 0.03f;

            // At the top of the air the profile is at its maximum, and staying there above it.
            float atTop = WindProfile.Multiplier(Shallow, roughness, capped);
            float wayAbove = WindProfile.Multiplier(Shallow * 4f, roughness, capped);
            Assert.Equal(atTop, wayAbove, 4);

            // Uncapped it would have gone on climbing through vacuum, which is the defect.
            float uncapped = WindProfile.Multiplier(Shallow * 2f, roughness, 600f);
            Assert.True(uncapped > atTop,
                "the uncapped profile should have been higher, or there was nothing to fix");
        }
    }
}
