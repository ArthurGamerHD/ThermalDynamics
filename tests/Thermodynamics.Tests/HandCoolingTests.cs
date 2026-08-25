using System;
using System.Collections.Generic;
using Thermodynamics.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **What a hand tool would have to be worth**, which is the number
    /// document-of-intent.md needed before it could say whether acting on
    /// heat by hand is in scope.
    ///
    /// <para>
    /// backlog.md `B32` was a void rather than a defect: the mod ships an
    /// extinguisher that scans and does not extinguish, and nothing said whether that was the
    /// design. A position taken without this measurement would be indistinguishable from never
    /// having asked, which is why these run before that sentence was written (`E1`).
    /// </para>
    ///
    /// <para>
    /// **The comparison is physical and the clock cannot move it.** `HeatTimeScale` divides every
    /// heat capacity, which makes a kelvin cheaper for a block's waste heat and for a tool's
    /// cooling by exactly the same factor. So every figure here is in watts and joules, and the
    /// answer holds at any value of the dial.
    /// </para>
    /// </summary>
    public class HandCoolingTests
    {
        private readonly ITestOutputHelper output;

        public HandCoolingTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>
        /// Blocks that cook themselves, cheapest to save first. Empty when the game is not
        /// installed, which every test here returns quietly on.
        /// </summary>
        private static List<HandCoolingLab.Price> Priced()
        {
            return GameBlocks.IsInstalled
                ? HandCoolingLab.All() : new List<HandCoolingLab.Price>();
        }

        /// <summary>
        /// **The heat stored in a block at the temperature it fails at is bottles of extinguisher,
        /// not one bottle.**
        ///
        /// The median block that can reach its own critical temperature holds about 86 MJ above
        /// ambient; a 5 kg CO2 bottle absorbs 3.3 MJ if every gram of it lands on the block. So
        /// undoing one crossing is twenty-six bottles at the median and four hundred and sixty at
        /// the ninetieth percentile, and a player carries one thing.
        /// </summary>
        [Fact]
        public void ReturningABlockToAmbientCostsTensOfBottlesAtTheMedian()
        {
            List<HandCoolingLab.Price> prices = Priced();
            if (prices.Count == 0) return;

            List<float> bottles = new List<float>();
            foreach (HandCoolingLab.Price price in prices) bottles.Add(price.BottlesToReturn);
            bottles.Sort();

            float cheapest = bottles[0];
            float median = HandCoolingLab.Quantile(bottles, 0.5d);

            output.WriteLine("{0} block types cross critical on their own waste", prices.Count);
            output.WriteLine("bottles to return: cheapest {0:n1}, median {1:n1}, worst {2:n0}",
                cheapest, median, bottles[bottles.Count - 1]);

            // **Not one bottle for any block in the game**, which is the claim that makes the
            // tool's size the decision rather than its existence.
            Assert.True(cheapest > 1f,
                "the cheapest block to cool by hand takes " + cheapest.ToString("n2")
                + " bottles, so a single bottle would do it and the intent's position is wrong");

            Assert.True(median > 10f,
                "the median block takes " + median.ToString("n1") + " bottles, not tens");
        }

        /// <summary>
        /// **And the window is shorter than the job.**
        ///
        /// A block does not wait: it is destroyed a median 32 s after it crosses. Doing 86 MJ in
        /// 32 s is 2.7 MW, which is twenty bottles all discharging at once — and the ninetieth
        /// percentile is seventy-seven. A tool that moves that much heat is a block, not something
        /// carried in a hand, which is the whole of the finding.
        /// </summary>
        [Fact]
        public void DoingItInsideTheWindowTakesTensOfBottlesAtOnce()
        {
            List<HandCoolingLab.Price> prices = Priced();
            if (prices.Count == 0) return;

            List<float> atOnce = new List<float>();
            List<float> windows = new List<float>();

            foreach (HandCoolingLab.Price price in prices)
            {
                atOnce.Add(price.BottlesAtOnce);
                if (!float.IsInfinity(price.WindowSeconds)) windows.Add(price.WindowSeconds);
            }

            atOnce.Sort();
            windows.Sort();

            float median = HandCoolingLab.Quantile(atOnce, 0.5d);

            output.WriteLine("window s: median {0:n0}, p90 {1:n0}",
                HandCoolingLab.Quantile(windows, 0.5d), HandCoolingLab.Quantile(windows, 0.9d));
            output.WriteLine("bottles at once: cheapest {0:n1}, median {1:n1}, p90 {2:n1}",
                atOnce[0], median, HandCoolingLab.Quantile(atOnce, 0.9d));

            Assert.True(atOnce[0] > 1f,
                "the easiest block in the game can be saved inside its own window by "
                + atOnce[0].ToString("n2") + " bottles, so one would nearly do");

            Assert.True(median > 10f,
                "the median block needs " + median.ToString("n1")
                + " bottles at once, which is not the order of magnitude this claim rests on");
        }

        /// <summary>
        /// **The blocks that cook are not blocks that cannot shed their own heat, and this is why
        /// the tool is at the wrong place rather than merely too small.**
        ///
        /// <para>
        /// Given every face radiating to deep space and every face bolted to armour held at
        /// ambient, all but one of the seventy-two shed everything they make at their own critical
        /// temperature — the surplus a tool would have to carry is *zero*. They reach critical
        /// because the hull around them is not that, and a bottle applied to the block does not
        /// change the hull. The single exception is the prototech reactor at 202 MW of surplus,
        /// which is not an argument for a hand tool either.
        /// </para>
        ///
        /// <para>
        /// This is the mod's *cooling is designed in* stated as an arithmetic property of the
        /// vanilla blocks rather than as a preference.
        /// </para>
        /// </summary>
        [Fact]
        public void AlmostEveryBlockThatCooksShedsAllOfItsOwnHeatInTheBestCase()
        {
            List<HandCoolingLab.Price> prices = Priced();
            if (prices.Count == 0) return;

            int selfSufficient = 0;
            foreach (HandCoolingLab.Price price in prices)
            {
                if (price.HoldWatts <= 0f) selfSufficient++;
            }

            output.WriteLine("{0} of {1} have no surplus at all in the best case",
                selfSufficient, prices.Count);

            Assert.True(selfSufficient * 100 >= prices.Count * 90,
                selfSufficient + " of " + prices.Count + " shed everything they make, which is "
                + "under the nine in ten the finding is stated at");

            // **A check that judged nothing would report success** (`E8`): if the index's best case
            // were generous enough to zero every block, the count above would be the whole list and
            // would say nothing about the arithmetic. One block over is what makes it a reading.
            Assert.True(selfSufficient < prices.Count,
                "every block sheds everything it makes, so this test cannot tell a best case from "
                + "a vacuous one");
        }

        /// <summary>
        /// **Pulling a block back from the brink is a different question from cooling it, and the
        /// answers differ by an order of magnitude.**
        ///
        /// <para>
        /// The tests above price *undoing a crossing* — returning a block from its rating to
        /// ambient — and it comes to twenty-six bottles at the median. That is the wrong question
        /// for a damage-mitigation tool. The solver damages an overheating block at
        /// `(T − critical) × OverheatDamagePerKelvin` a second, so what stops the damage is removing
        /// the *overshoot*, not the heat. A block fifty kelvin over its rating needs fifty kelvin
        /// taken off it, and the block's whole rise above ambient is several hundred.
        /// </para>
        ///
        /// <para>
        /// This prices that, because it is the number an ammunition design needs.
        /// </para>
        /// </summary>
        [Fact]
        public void PullingABlockBackFromItsRatingIsCheapEnoughForAHandTool()
        {
            List<HandCoolingLab.Price> prices = Priced();
            if (prices.Count == 0) return;

            foreach (double overshoot in new[] { 10d, 25d, 50d, 100d })
            {
                List<float> bottles = new List<float>();

                foreach (HandCoolingLab.Price price in prices)
                {
                    // Physical capacity, back out of the return figure: that is capacity times the
                    // whole rise from ambient to the rating.
                    float rise = price.ReturnJoules <= 0f ? 0f : price.ReturnJoules;
                    if (rise <= 0f) continue;

                    float perKelvin = rise / (price.CriticalKelvin - BlockHeatIndex.AmbientKelvin);
                    bottles.Add((float)(perKelvin * overshoot) / HandCoolingLab.BottleJoules);
                }

                bottles.Sort();

                output.WriteLine("{0:n0} K back from the rating: median {1:n1} bottles, "
                    + "p90 {2:n1}, worst {3:n0}",
                    overshoot,
                    HandCoolingLab.Quantile(bottles, 0.5d),
                    HandCoolingLab.Quantile(bottles, 0.9d),
                    bottles[bottles.Count - 1]);

                if (overshoot > 50d) continue;

                // The claim: for the overshoot a damage-mitigation tool is about, the median block
                // is within a hand tool's reach where returning it to ambient is not.
                Assert.True(HandCoolingLab.Quantile(bottles, 0.5d) < 10f,
                    "pulling the median block " + overshoot + " K back takes "
                    + HandCoolingLab.Quantile(bottles, 0.5d).ToString("n1")
                    + " bottles, which is not a hand tool");
            }
        }

        /// <summary>
        /// **The bottle is a real object and its arithmetic is checked here**, so that a change to
        /// the reference is a change somebody made rather than a number that drifted.
        /// </summary>
        [Fact]
        public void TheReferenceBottleIsFiveKilogramsOfCarbonDioxide()
        {
            Assert.Equal(3300000f, HandCoolingLab.BottleJoules);
            Assert.Equal(165000f, HandCoolingLab.BottleWatts);
        }
    }
}
