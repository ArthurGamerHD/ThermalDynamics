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
                BlockThermalDerivation.BlockFunction function = BlockThermalDerivation.FunctionOf(typeId);

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
        /// The floor on the fraction. A reactor wrapped in hull at full rating has to want cooling,
        /// or the whole mechanism is scenery: the definition would be non-zero and still make no
        /// decision for anyone.
        /// </summary>
        [Fact]
        public void ALargeReactorBuriedInHullAtFullRatingNeedsCooling()
        {
            ReactorLab.Row row = Shipped()
                .First(r => r.Subtype == "LargeBlockLargeGenerator" && r.LoadFraction == 1f);

            Assert.True(row.SkinnedMarginKelvin < 0f,
                "a 300 MW reactor under one cell of armour settles at " + row.SkinnedKelvin.ToString("n1")
                + " K, inside its critical " + row.CriticalKelvin.ToString("n1")
                + " K, so it never asks to be cooled");
        }

        /// <summary>
        /// The reason a reactor is worth cooling rather than simply throttling: the same block is
        /// safe when its faces are open and past critical when they are not. If the skin did not
        /// change the answer, the block's placement would carry no decision.
        /// </summary>
        [Fact]
        public void HowAReactorIsInstalledDecidesWhetherItSurvives()
        {
            ReactorLab.Row row = Shipped()
                .First(r => r.Subtype == "LargeBlockLargeGenerator" && r.LoadFraction == 1f);

            Assert.True(row.BareMarginKelvin > 0f && row.SkinnedMarginKelvin < 0f,
                "bare " + row.BareKelvin.ToString("n1") + " K, skinned " + row.SkinnedKelvin.ToString("n1")
                + " K: both sides of critical " + row.CriticalKelvin.ToString("n1") + " K were expected");
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
