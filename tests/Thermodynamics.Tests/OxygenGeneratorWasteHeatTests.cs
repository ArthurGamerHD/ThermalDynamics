using System.Collections.Generic;
using System.Linq;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// What decided the last of the three unsourced fractions with a real figure beside them,
    /// pinned so it cannot invert quietly.
    ///
    /// <para>
    /// The oxygen generator wasted **0.6** of what it draws under a comment admitting the figure
    /// was invented, against water electrolysis sourcing 0.20–0.40. The rule for moving it was
    /// registered in balance.md, *Oxygen generator waste heat*, before
    /// <see cref="OxygenGeneratorLab"/> produced a number; the rule's first clause fired, and it
    /// fired on a finding nobody had gone looking for. **At 0.6, two of the six vanilla generators
    /// are past their own critical temperature alone in open space at their own rated draw** — a
    /// state no build can improve on, because there is nothing cooler than every face on a 2.7 K
    /// sky. 0.40 is the top of the sourced band and the highest value where all six survive both
    /// rigs.
    /// </para>
    ///
    /// <para>
    /// Figures come from <see cref="OxygenGeneratorLab"/>, which reads the shipped XML at run time,
    /// so a tuning change moves these tests rather than sliding past them.
    /// </para>
    /// </summary>
    [Trait("speed", "slow")]
    public class OxygenGeneratorWasteHeatTests
    {
        /// <summary>
        /// The shipped fraction is inside the band its own note claims.
        ///
        /// <see cref="AuthoredWasteTests"/> checks that of every fraction in the file; this says it
        /// of the one this class is about, so a reader of these tests does not have to take the
        /// provenance on trust from another suite.
        /// </summary>
        [Fact]
        public void TheShippedFractionIsInsideTheBandElectrolysisSources()
        {
            float shipped = ShippedBlocks.FunctionOf("OxygenGenerator").ConsumerWasteEnergy;

            Assert.InRange(shipped, OxygenGeneratorLab.SourcedLow, OxygenGeneratorLab.SourcedHigh);
        }

        /// <summary>
        /// The bound the decision turned on. Bare in shadow with every face on a 2.7 K sky is the
        /// most heat a block can possibly shed, so one that cooks there cooks in every build a
        /// player can make, and no plumbing reaches it.
        /// </summary>
        [Fact]
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

        /// <summary>
        /// **The finding that moved the fraction, kept runnable rather than only written down.**
        ///
        /// At the 0.6 the file shipped until 2026-08-25, the two small-grid generators are past
        /// critical bare at their rated draw — measured at 905.3 K against criticals of 862.8 K and
        /// 848.6 K. That is the *unbuildable* bound `C28` named for the reactor, reached from the
        /// other side: there, the sourced figure was the one that cooked and the invention was kept;
        /// here the invention was the one that cooked.
        ///
        /// A failure of this test means the model has moved far enough that the reason recorded in
        /// balance.md no longer reproduces, which is a page to correct rather than a bound to relax.
        /// </summary>
        [Fact]
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

        /// <summary>
        /// **A skin cools these blocks rather than cooking them, which is the opposite of a
        /// reactor and the reason the two rigs are not simply *ceiling* and *floor*.**
        ///
        /// <para>
        /// balance.md called bare the ceiling and skinned the floor, and that reading came from a
        /// 300 MW reactor: at those watts the block-to-block conductance out of the block is the
        /// bottleneck, so the armour traps more than it sheds. An oxygen generator wastes three
        /// orders of magnitude less, conduction into the shell is nowhere near binding, and the
        /// shell is a radiator with several times the block's own area. The vanilla generator at
        /// rated draw settles **267 K cooler** skinned than bare at the fraction that was shipped.
        /// </para>
        ///
        /// <para>
        /// It is pinned because a registered prediction — *at 0.6 the block is past critical
        /// skinned* — was falsified by exactly this, and a falsification that is only written down
        /// in prose is one the next reader repeats.
        /// </para>
        /// </summary>
        [Fact]
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

        /// <summary>
        /// A generator that is switched on and converting nothing is not a cooling problem, and one
        /// running at the duty the field dump observed is not either. Heat arrives when the block
        /// is worked, which is the same shape a reactor's fraction was chosen to have.
        /// </summary>
        [Fact]
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

        /// <summary>
        /// The sweep rows at the fraction Cubes.xml actually ships.
        /// <see cref="OxygenGeneratorLab"/> caches them, so the tests here cost one pass between
        /// them rather than one each.
        /// </summary>
        private static List<OxygenGeneratorLab.Row> Shipped()
        {
            List<OxygenGeneratorLab.Row> rows = OxygenGeneratorLab.Shipped();

            Assert.True(rows.Count > 0, "the oxygen generator sweep produced no rows");
            return rows;
        }
    }
}
