using System.Collections.Generic;
using System.Linq;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// That reactors heat at all, and that the fraction chosen for them still means what it was
    /// chosen to mean.
    ///
    /// The defect these were written against: the <c>Reactor</c> entry in Cubes.xml carried
    /// <c>ProducerWasteEnergy</c> 0 and <c>ConsumerWasteEnergy</c> 0.25. A reactor delivers power
    /// through the source component, so only the producer fraction can ever apply to it, and every
    /// reactor in the game made exactly no heat — the largest heat source a ship has, inert, while
    /// a suite of 1,179 tests stayed green. Nothing in the solver was wrong; the number it was
    /// handed was.
    ///
    /// Figures come from <see cref="ReactorLab"/>, which reads the shipped XML at run time, so a
    /// tuning change moves these tests rather than sliding past them.
    /// </summary>
    [Trait("speed", "slow")]
    public class ReactorWasteHeatTests
    {
        /// <summary>
        /// Block types that *convert* something into electricity — fuel, or a charge they hold —
        /// and deliver it through <c>MyResourceSourceComponent</c>. Their heat runs through the
        /// producer fraction and nothing else, so a zero there is not a cold block, it is a
        /// disconnected one.
        ///
        /// A solar panel and a wind turbine are deliberately not here. They convert energy the
        /// environment supplied, and for the panel the solar path has already put that energy into
        /// the block: charging it again through a producer fraction would count the same sunlight
        /// twice.
        /// </summary>
        private static readonly string[] ProducerTypes = { "Reactor", "HydrogenEngine", "BatteryBlock" };

        [Fact]
        public void EveryPowerProducerConvertsSomeOfItsOutputToHeat()
        {
            foreach (string typeId in ProducerTypes)
            {
                ShippedBlocks.Function function = ShippedBlocks.FunctionOf(typeId);

                Assert.True(function.ProducerWasteEnergy > 0f,
                    typeId + " carries ProducerWasteEnergy " + function.ProducerWasteEnergy
                    + ", so its output makes no heat. A producer's consumer fraction never applies.");
            }
        }

        /// <summary>
        /// The ceiling on the fraction. Bare in shadow with every face on a 2.7 K sky is the most
        /// heat a reactor can possibly shed, so a reactor that cooks there cooks in every build a
        /// player can make, and no amount of plumbing reaches it.
        /// </summary>
        [Fact]
        public void NoReactorDestroysItselfWithEveryFaceOnOpenSpace()
        {
            foreach (ReactorLab.Row row in Shipped())
            {
                Assert.True(row.BareMarginKelvin > 0f,
                    row.Subtype + " at " + (row.LoadFraction * 100f) + "% of rating settles at "
                    + row.BareKelvin.ToString("n1") + " K bare, past its critical "
                    + row.CriticalKelvin.ToString("n1") + " K. Nothing a player builds is cooler than bare.");
            }
        }

        /// <summary>
        /// **The floor on the fraction has gone, and `C24` is what removed it.**
        ///
        /// <para>
        /// The fraction was chosen against two bounds: every reactor survives at full rating with
        /// its faces on open space, which no arrangement can improve on, and the 300 MW one goes
        /// past critical once wrapped in hull, which is what makes where it is installed a
        /// decision. 0.01 was the only candidate where both held —
        /// balance.md, Reactor waste heat.
        /// </para>
        ///
        /// <para>
        /// At four times the conduction pace the armour a reactor is buried in carries its heat
        /// away and radiates from its own faces, so the skinned case fell from **1,245.0 K to
        /// 940.6 K** against a critical of 1,090.1 K. **Burying the 300 MW reactor now costs it
        /// 50.7 K rather than 355 K**, and there is no fraction that restores the old shape: at
        /// 0.02 the skinned case does cook, and so do two of the four bare, which is the bound the
        /// page calls unbuildable. So the signal is smaller rather than moved, and what to do about
        /// it is backlog.md `C28`.
        /// </para>
        ///
        /// <para>
        /// **This is a block-level signal rather than a population one.** `G2` still holds at 100 %
        /// of the retest set at this pair, because a real ship's heat is thrusters and drives
        /// rather than reactors — the sweep measured `reactor-waste` from 0.5 to 8.0 as very nearly
        /// inert on the population.
        /// </para>
        /// </summary>
        [Fact]
        public void BuryingALargeReactorCostsItRatherThanCookingIt()
        {
            ReactorLab.Row row = Shipped()
                .First(r => r.Subtype == "LargeBlockLargeGenerator" && r.LoadFraction == 1f);

            Assert.True(row.SkinnedKelvin > row.BareKelvin,
                "a 300 MW reactor under one cell of armour settles at " + row.SkinnedKelvin.ToString("n1")
                + " K against " + row.BareKelvin.ToString("n1")
                + " K bare, so burying it has stopped costing anything at all");

            // Measured 2026-08-24: 50.7 K, where it was 355 K. Bounded both ways, so a fix
            // announces itself as loudly as a further loss.
            Assert.InRange(row.SkinnedKelvin - row.BareKelvin, 20f, 150f);

            // And it survives, which is the half of this that changed.
            Assert.True(row.SkinnedMarginKelvin > 0f,
                "a 300 MW reactor under one cell of armour is past critical again at "
                + row.SkinnedKelvin.ToString("n1") + " K; if that is deliberate then C28 is closed"
                + " and this test is the one to rewrite");
        }

        /// <summary>
        /// The reason a reactor is worth cooling rather than simply throttling: the same block is
        /// safe when its faces are open and past critical when they are not. If the skin did not
        /// change the answer, the block's placement would carry no decision.
        /// </summary>
        [Fact]
        public void HowAReactorIsInstalledStillDecidesWhetherItSurvivesSomewhere()
        {
            // **Not at the shipped fraction any more.** At 0.01 both installations of the 300 MW
            // reactor survive; at 0.02 the skinned one is past critical at 1,214.9 K and the bare
            // one is inside it at 1,058.2 K, so the decision this rig is about still exists in the
            // model and it takes twice the waste heat to reach. The rig is what says so, rather
            // than an argument that it must be in there somewhere (`C28`).
            ReactorLab.Row shipped = Shipped()
                .First(r => r.Subtype == "LargeBlockLargeGenerator" && r.LoadFraction == 1f);

            Assert.True(shipped.BareMarginKelvin > 0f && shipped.SkinnedMarginKelvin > 0f,
                "bare " + shipped.BareKelvin.ToString("n1") + " K, skinned "
                + shipped.SkinnedKelvin.ToString("n1") + " K against critical "
                + shipped.CriticalKelvin.ToString("n1") + " K: one of them is past it again, so"
                + " C28 has moved and the test above needs reading with this one");

            ReactorLab.Row doubled = ReactorLab.Run(0.02f)
                .First(r => r.Subtype == "LargeBlockLargeGenerator" && r.LoadFraction == 1f);

            Assert.True(doubled.BareMarginKelvin > 0f && doubled.SkinnedMarginKelvin < 0f,
                "at twice the shipped fraction: bare " + doubled.BareKelvin.ToString("n1")
                + " K, skinned " + doubled.SkinnedKelvin.ToString("n1")
                + " K, critical " + doubled.CriticalKelvin.ToString("n1")
                + " K — both sides of critical were expected");
        }

        /// <summary>
        /// A resting ship is not a cooling problem. A reactor idling at a tenth of its rating stays
        /// well clear of critical however it is installed, so heat is something a player meets when
        /// they draw power rather than a tax on switching the lights on.
        /// </summary>
        [Fact]
        public void AnIdlingReactorNeedsNoCoolingHoweverItIsBuiltIn()
        {
            foreach (ReactorLab.Row row in Shipped().Where(r => r.LoadFraction == 0.1f))
            {
                Assert.True(row.SkinnedMarginKelvin > 0f,
                    row.Subtype + " idling at 10% of rating settles at " + row.SkinnedKelvin.ToString("n1")
                    + " K under armour, past its critical " + row.CriticalKelvin.ToString("n1") + " K");
            }
        }

        /// <summary>
        /// The sweep rows at the fraction Cubes.xml actually ships. <see cref="ReactorLab"/> caches
        /// them, so the five tests here cost one pass between them rather than five.
        /// </summary>
        private static List<ReactorLab.Row> Shipped()
        {
            List<ReactorLab.Row> rows = ReactorLab.Shipped();

            Assert.True(rows.Count > 0, "the reactor sweep produced no rows");
            return rows;
        }
    }
}
