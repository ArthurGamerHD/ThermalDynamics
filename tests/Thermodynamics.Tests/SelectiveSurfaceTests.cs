using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The mod's one selective surface, and the finding that decides how much of it to author.**
    ///
    /// <para>
    /// `SolarAbsorptivity` has been separate from `Emissivity` since the two were split, and no
    /// block declared one — so a radiator absorbed sunlight at the rate it emitted it, which is the
    /// single combination real radiators are finished to avoid.
    /// backlog.md `C15` asked whether to author one; these hold the answer
    /// and the measurement it rests on. See balance.md, What a selective surface is worth.
    /// </para>
    /// </summary>
    public class SelectiveSurfaceTests
    {
        private readonly ITestOutputHelper output;

        public SelectiveSurfaceTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>Small enough for the fast lane; the finding does not depend on stack size.</summary>
        private const int Radiators = 4;

        private static SelectiveSurfaceLab.Row Find(IList<SelectiveSurfaceLab.Row> rows, string surface)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Surface == surface) return rows[i];
            }

            Assert.Fail( "no row for " + surface);
            return null;
        }

        /// <summary>
        /// **The shipped radiator declares one**, which is the change itself: a block that no
        /// longer collects sunlight a real one would not.
        ///
        /// <para>
        /// **`C36` supplied the other half.** A selective surface is high-emissivity *and*
        /// low-absorptivity, and this block had only the low half — it shipped at the 0.35 of the
        /// plate it is welded from while its own definition named second-surface mirror and white
        /// paint, finishes measured at 0.8-0.9. Emissivity is now 0.85 and the pair finally
        /// describes one surface rather than two. It bought a third more cooling per panel (20.9 K
        /// to 27.6 K on the ladder's reactor) and moved a corpus hull by 0.1 %, which is the
        /// finding: emission was never what limited a radiator here.
        /// </para>
        /// </summary>
        [Fact]
        public void TheShippedRadiatorDeclaresASelectiveSurface()
        {
            foreach (string subtype in new[] { "Gauge_LG_Radiator", "Gauge_SG_Radiator" })
            {
                BlockThermalProperties thermal = ShippedBlocks.Get(subtype).Thermal;

                Assert.Equal(0.1f, thermal.SolarAbsorptivity, 3);
                Assert.Equal(0.85f, thermal.Emissivity, 3);
                Assert.True(thermal.EffectiveSolarAbsorptivity < thermal.Emissivity,
                    subtype + " absorbs at least as much as it emits, which is not a radiator");
            }
        }

        /// <summary>
        /// It is worth something in sunlight and **exactly nothing in shadow**, which is what says
        /// the rig measures a surface rather than a geometry (`E7`): absorptivity cannot reach a
        /// number taken where there is no sun to absorb.
        /// </summary>
        [Fact]
        public void ASelectiveFinishHelpsInSunlightAndNowhereElse()
        {
            List<SelectiveSurfaceLab.Row> rows = SelectiveSurfaceLab.Run(Radiators);

            SelectiveSurfaceLab.Row shipped = Find(rows, "shipped");
            SelectiveSurfaceLab.Row selective = Find(rows, "selective");

            output.WriteLine("sunlit {0:n1} K -> {1:n1} K, shadow {2:n1} K -> {3:n1} K",
                shipped.SunlitKelvin, selective.SunlitKelvin,
                shipped.ShadowKelvin, selective.ShadowKelvin);

            Assert.Equal(shipped.ShadowKelvin, selective.ShadowKelvin, 2);
            Assert.True(shipped.SunlitKelvin - selective.SunlitKelvin > 3f,
                "the finish saved only " + (shipped.SunlitKelvin - selective.SunlitKelvin) + " K in sun");

            // And the rig has a sun in it at all, or the equality above is two dark runs agreeing.
            Assert.True(shipped.SunlitKelvin - shipped.ShadowKelvin > 10f,
                "the sun is worth only " + shipped.SunPenaltyKelvin + " K to the rig, so this judges nothing");
        }

        /// <summary>
        /// **And the obvious change alone is nearly worthless in the sun.** Raising emissivity
        /// makes the block a better emitter *and* a better absorber, so most of what it gains it
        /// gives back — which is why the two properties had to be separated before either could be
        /// authored honestly.
        /// </summary>
        [Fact]
        public void RaisingEmissivityAloneGivesBackMostOfWhatItGainsInSunlight()
        {
            List<SelectiveSurfaceLab.Row> rows = SelectiveSurfaceLab.Run(Radiators);

            SelectiveSurfaceLab.Row shipped = Find(rows, "shipped");
            SelectiveSurfaceLab.Row emissive = Find(rows, "emissive only");
            SelectiveSurfaceLab.Row both = Find(rows, "second-surface mirror");

            float inShadow = shipped.ShadowKelvin - emissive.ShadowKelvin;
            float inSun = shipped.SunlitKelvin - emissive.SunlitKelvin;

            output.WriteLine("emissivity alone: {0:n1} K in shadow, {1:n1} K in sun", inShadow, inSun);

            Assert.True(inShadow > 3f, "raising emissivity should help in shadow: " + inShadow + " K");
            Assert.True(inSun < inShadow * 0.5f,
                "emissivity alone bought " + inSun + " K in sun against " + inShadow + " K in shadow,"
                + " which is not the giving-back this pins");

            // The sun's penalty *rises* when a block absorbs better, which is the mechanism.
            Assert.True(emissive.SunPenaltyKelvin > shipped.SunPenaltyKelvin,
                "a better absorber should pay more for the sun, not less");

            // Both together beat either, which is what makes it a pair rather than a choice.
            Assert.True(both.SunlitKelvin < emissive.SunlitKelvin,
                "the pair should beat emissivity alone in sun");
        }
    }
}
