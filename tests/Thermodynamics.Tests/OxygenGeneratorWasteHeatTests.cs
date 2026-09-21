using System.Collections.Generic;
using System.Linq;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    [Trait("speed", "slow")]
    public class OxygenGeneratorWasteHeatTests
    {
        [Fact]
/// <summary>TheShippedFractionIsInsideTheBandElectrolysisSources operation.</summary>
        public void TheShippedFractionIsInsideTheBandElectrolysisSources()
        {
            float shipped = ShippedBlocks.FunctionOf("OxygenGenerator").ConsumerWasteEnergy;

            Assert.InRange(shipped, OxygenGeneratorLab.SourcedLow, OxygenGeneratorLab.SourcedHigh);
        }

        [Fact]
/// <summary>NoOxygenGeneratorDestroysItselfWithEveryFaceOnOpenSpace operation.</summary>
        public void NoOxygenGeneratorDestroysItselfWithEveryFaceOnOpenSpace()
        {
            foreach (OxygenGeneratorLab.Row row in Shipped())
            {
                Assert.True(row.BareMarginKelvin > 0f,
                    row.Name + " at its " + row.DrawWatts.ToString("n0") + " W draw settles at "
                    + row.BareKelvin.ToString("n1") + " K bare, past its critical "
                    + row.CriticalKelvin.ToString("n1")
                    + " K. Nothing a player builds is cooler than bare.");
            }
        }

        [Fact]
/// <summary>TheFractionThisReplacedStillCooksTwoGeneratorsBare operation.</summary>
        public void TheFractionThisReplacedStillCooksTwoGeneratorsBare()
        {
            List<OxygenGeneratorLab.Row> cooked = OxygenGeneratorLab.Run(0.6f)
                .Where(r => r.Load == OxygenGeneratorLab.Draw.Operational && r.BareMarginKelvin < 0f)
                .ToList();

            Assert.True(cooked.Count == 2,
                "at the 0.6 that was shipped, " + cooked.Count + " generators are past critical"
                + " bare at their rated draw, where two were: "
                + string.Join(", ", cooked.Select(r => r.Name).ToArray()));

            foreach (OxygenGeneratorLab.Row row in cooked) Assert.False(row.Large);
        }

        [Fact]
/// <summary>SkinningASmallHeatSourceCoolsItWhereSkinningAReactorDoesNot operation.</summary>
        public void SkinningASmallHeatSourceCoolsItWhereSkinningAReactorDoesNot()
        {
            foreach (OxygenGeneratorLab.Row row in Shipped()
                         .Where(r => r.Load == OxygenGeneratorLab.Draw.Operational))
            {
                Assert.True(row.SkinnedKelvin < row.BareKelvin,
                    row.Name + " at rated draw settles at " + row.SkinnedKelvin.ToString("n1")
                    + " K under one cell of armour against " + row.BareKelvin.ToString("n1")
                    + " K bare, so the shell has stopped being the larger radiator and the two"
                    + " rigs' roles have inverted back");
            }
        }

        [Fact]
/// <summary>AGeneratorAtStandbyOrTheObservedDutyNeedsNoCooling operation.</summary>
        public void AGeneratorAtStandbyOrTheObservedDutyNeedsNoCooling()
        {
            foreach (OxygenGeneratorLab.Row row in Shipped()
                         .Where(r => r.Load != OxygenGeneratorLab.Draw.Operational))
            {
                Assert.True(row.BareMarginKelvin > 100f && row.SkinnedMarginKelvin > 100f,
                    row.Name + " at " + row.DrawWatts.ToString("n0") + " W is within 100 K of its"
                    + " critical " + row.CriticalKelvin.ToString("n1") + " K — bare "
                    + row.BareKelvin.ToString("n1") + " K, skinned "
                    + row.SkinnedKelvin.ToString("n1") + " K");
            }
        }

/// <summary>Shipped operation.</summary>
        private static List<OxygenGeneratorLab.Row> Shipped()
        {
            List<OxygenGeneratorLab.Row> rows = OxygenGeneratorLab.Shipped();

            Assert.True(rows.Count > 0, "the oxygen generator sweep produced no rows");
            return rows;
        }
    }
}
