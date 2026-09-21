using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    public class SelectiveSurfaceTests
    {
        private readonly ITestOutputHelper output;

/// <summary>SelectiveSurfaceTests operation.</summary>
        public SelectiveSurfaceTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const int Radiators = 4;

/// <summary>Find operation.</summary>
        private static SelectiveSurfaceLab.Row Find(IList<SelectiveSurfaceLab.Row> rows, string surface)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Surface == surface) return rows[i];
            }

            Assert.Fail( "no row for " + surface);
            return null;
        }

        [Fact]
/// <summary>TheShippedRadiatorDeclaresASelectiveSurface operation.</summary>
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

        [Fact]
/// <summary>ASelectiveFinishHelpsInSunlightAndNowhereElse operation.</summary>
        public void ASelectiveFinishHelpsInSunlightAndNowhereElse()
        {
            List<SelectiveSurfaceLab.Row> rows = SelectiveSurfaceLab.Run(Radiators);

/// <summary>Find operation.</summary>
            SelectiveSurfaceLab.Row shipped = Find(rows, "shipped");
/// <summary>Find operation.</summary>
            SelectiveSurfaceLab.Row selective = Find(rows, "selective");

            output.WriteLine("sunlit {0:n1} K -> {1:n1} K, shadow {2:n1} K -> {3:n1} K",
                shipped.SunlitKelvin, selective.SunlitKelvin,
                shipped.ShadowKelvin, selective.ShadowKelvin);

            Assert.Equal(shipped.ShadowKelvin, selective.ShadowKelvin, 2);
            Assert.True(shipped.SunlitKelvin - selective.SunlitKelvin > 3f,
                "the finish saved only " + (shipped.SunlitKelvin - selective.SunlitKelvin) + " K in sun");

            Assert.True(shipped.SunlitKelvin - shipped.ShadowKelvin > 10f,
                "the sun is worth only " + shipped.SunPenaltyKelvin + " K to the rig, so this judges nothing");
        }

        [Fact]
/// <summary>RaisingEmissivityAloneGivesBackMostOfWhatItGainsInSunlight operation.</summary>
        public void RaisingEmissivityAloneGivesBackMostOfWhatItGainsInSunlight()
        {
            List<SelectiveSurfaceLab.Row> rows = SelectiveSurfaceLab.Run(Radiators);

/// <summary>Find operation.</summary>
            SelectiveSurfaceLab.Row shipped = Find(rows, "shipped");
/// <summary>Find operation.</summary>
            SelectiveSurfaceLab.Row emissive = Find(rows, "emissive only");
/// <summary>Find operation.</summary>
            SelectiveSurfaceLab.Row both = Find(rows, "second-surface mirror");

            float inShadow = shipped.ShadowKelvin - emissive.ShadowKelvin;
            float inSun = shipped.SunlitKelvin - emissive.SunlitKelvin;

            output.WriteLine("emissivity alone: {0:n1} K in shadow, {1:n1} K in sun", inShadow, inSun);

            Assert.True(inShadow > 3f, "raising emissivity should help in shadow: " + inShadow + " K");
            Assert.True(inSun < inShadow * 0.5f,
                "emissivity alone bought " + inSun + " K in sun against " + inShadow + " K in shadow,"
                + " which is not the giving-back this pins");

            Assert.True(emissive.SunPenaltyKelvin > shipped.SunPenaltyKelvin,
                "a better absorber should pay more for the sun, not less");

            Assert.True(both.SunlitKelvin < emissive.SunlitKelvin,
                "the pair should beat emissivity alone in sun");
        }
    }
}
