using System.Collections.Generic;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// What bolting a cooling block to a reactor actually buys, pinned so the answer cannot
    /// quietly invert.
    ///
    /// <para>
    /// `blocks.md` says the radiator is a block you **plumb**, not one you bolt, and `balance.md`
    /// prices a coolant sink face at six times a bolt joint. Both were argued from conductances.
    /// The ladder measures the end result instead — the largest reactor the game ships, at its
    /// plate rating, in shadow, with every block that has a plausible claim to being the best
    /// cooling in the game stacked against it — and the end result is starker than the argument:
    /// the mod's own radiator takes **two kelvin off eight hundred and ninety**, and a stack of
    /// thirty-two takes no more off than a stack of eight.
    /// </para>
    ///
    /// <para>
    /// These assertions are on directions and orders of magnitude rather than on figures, because
    /// the figures move with any definition change and the conclusions are what a reader is
    /// entitled to rely on. The lab prints the figures; this says they still mean what the pages
    /// claim they mean.
    /// </para>
    /// </summary>
    [Trait("speed", "slow")]
    public class CoolingLadderTests
    {
        private static readonly object Gate = new object();
        private static List<CoolingLadder.Row> rows;
        private static float bareKelvin;

        /// <summary>
        /// The ladder, run once for the whole class. About a second and a half, and every case
        /// below reads the same table — running it per case would be six times that for six
        /// identical answers.
        /// </summary>
        private static List<CoolingLadder.Row> Rows(out float bare)
        {
            lock (Gate)
            {
                if (rows == null) rows = CoolingLadder.Run(out bareKelvin);
                bare = bareKelvin;
                return rows;
            }
        }

        private static List<CoolingLadder.Row> Of(string block, out float bare)
        {
            List<CoolingLadder.Row> mine = new List<CoolingLadder.Row>();
            foreach (CoolingLadder.Row row in Rows(out bare))
            {
                if (row.Block == block) mine.Add(row);
            }
            return mine;
        }

        [Fact]
        public void TheBareReactorRunsHotEnoughForTheComparisonToMeanAnything()
        {
            if (!GameBlocks.IsInstalled) return;

            float bare;
            List<CoolingLadder.Row> all = Rows(out bare);

            Assert.NotEmpty(all);
            Assert.True(bare > 700f,
                "the bare reactor settled at " + bare.ToString("n0")
                + " K, which is not a cooling problem, so nothing below is being measured");
        }

        /// <summary>
        /// The headline. A bolted radiator is not a cooling solution and the table has to keep
        /// saying so, because it is the claim two pages of documentation rest on.
        ///
        /// <para>
        /// **The figure moved with `C24` and the claim did not.** This asked for under one per
        /// cent, measured at 0.5 % when the conduction pace was 2.4; at the 9.6 that ships, a bolt
        /// joint carries four times what it did and thirty-two radiators take **25.6 K off 890 K**,
        /// which is 2.9 %. A reactor cooled by 2.9 % is a reactor that is still going to lose
        /// itself, so what the page rests on is unchanged — but the bound is now three per cent and
        /// says why, rather than reading as though nothing had moved (`E11`).
        /// </para>
        /// </summary>
        [Fact]
        public void BoltingTheRadiatorToAReactorSavesAFewPercentOfIt()
        {
            if (!GameBlocks.IsInstalled) return;

            float bare;
            List<CoolingLadder.Row> radiator = Of("Gauge_LG_Radiator", out bare);
            Assert.NotEmpty(radiator);

            float best = 0f;
            foreach (CoolingLadder.Row row in radiator)
            {
                if (row.Saved > best) best = row.Saved;
            }

            Assert.True(best > 0f, "bolting radiators to the reactor did not cool it at all");
            Assert.True(best < bare * 0.03f,
                "thirty-two bolted radiators took " + best.ToString("n1") + " K off "
                + bare.ToString("n0") + " K, which is more than a bolt joint should buy; if this is"
                + " real then blocks.md's 'plumb it, do not bolt it' needs rewriting");
        }

        /// <summary>
        /// Nothing in the game turns the reactor into a solved problem by being bolted to it,
        /// which is what makes coolant loops worth building at all.
        /// </summary>
        [Fact]
        public void NoBoltedBlockInTheGameSolvesTheReactor()
        {
            if (!GameBlocks.IsInstalled) return;

            float bare;
            List<CoolingLadder.Row> all = Rows(out bare);

            foreach (CoolingLadder.Row row in all)
            {
                Assert.True(row.Saved < bare * 0.10f,
                    row.Count + " x " + row.Block + " took " + row.Saved.ToString("n1")
                    + " K off " + bare.ToString("n0") + " K by being bolted on, which would make it"
                    + " a cooling solution the mod's own blocks are not");
            }
        }

        /// <summary>
        /// **The control stopped losing, and that is `C24` rather than a rig fault.**
        ///
        /// <para>
        /// A block bolted to a face is a face that was radiating to the sky and now radiates into a
        /// neighbour, and at the conduction pace the conversion calibrated to that trade was a net
        /// loss for plain armour: the control lost, which is what made it a control. At four times
        /// that pace the joint carries more heat out of the reactor than the buried face was
        /// shedding, so a plain armour block bolted on now *saves* **2.42 K of 890 K**.
        /// </para>
        ///
        /// <para>
        /// **What the ladder is for survives it**, and that is what this now asserts: the block
        /// with no surface advantage is worth a fraction of the one built to have it — 2.42 K
        /// against a radiator's 20.9 K on the same mounting, which is the eight-to-one that says
        /// area is what a cooler is for. A control that has stopped losing is worth keeping while
        /// it still loses to everything with a surface, and worth removing when it does not.
        /// </para>
        /// </summary>
        [Fact]
        public void TheBlockWithNoSurfaceAdvantageIsWorthAFractionOfOneBuiltForIt()
        {
            if (!GameBlocks.IsInstalled) return;

            float bare;
            List<CoolingLadder.Row> armour = Of("LargeBlockArmorBlock", out bare);
            List<CoolingLadder.Row> radiator = Of("Gauge_LG_Radiator", out bare);

            Assert.NotEmpty(armour);
            Assert.NotEmpty(radiator);

            // Measured 2026-08-24: +2.42 K for armour against +20.90 K for the radiator, one of
            // each, on the same face of the same reactor.
            Assert.True(armour[0].Saved < radiator[0].Saved * 0.25f,
                "a plain armour block bolted to the reactor saved " + armour[0].Saved.ToString("n2")
                + " K against a radiator's " + radiator[0].Saved.ToString("n2")
                + " K, so the block built to shed heat is no longer worth building");

            // And it is small in its own right rather than only small beside a radiator.
            Assert.True(armour[0].Saved < bare * 0.005f,
                "a plain armour block took " + armour[0].Saved.ToString("n2") + " K off "
                + bare.ToString("n0") + " K, which is a cooling solution made of hull");
        }

        /// <summary>
        /// A stack is a fin, so the saving flattens. Asserted as a shape rather than a number: the
        /// marginal saving of the last rung is a small fraction of the first's.
        /// </summary>
        [Fact]
        public void AStackOfCoolersSaturates()
        {
            if (!GameBlocks.IsInstalled) return;

            float bare;
            List<CoolingLadder.Row> radiator = Of("Gauge_LG_Radiator", out bare);
            Assert.True(radiator.Count >= 3);

            CoolingLadder.Row first = radiator[0];
            CoolingLadder.Row last = radiator[radiator.Count - 1];

            Assert.True(first.Marginal > 0f, "the first radiator saved nothing");
            Assert.True(last.Marginal < first.Marginal * 0.05f,
                "the last rung still saved " + last.Marginal.ToString("n3")
                + " K against the first's " + first.Marginal.ToString("n3")
                + ", so the ladder has not reached the flat it exists to find");
        }

        /// <summary>
        /// The far-end column separates the two ways a ladder goes flat, and both kinds are
        /// present: a stack that saturated is hot at the top, and a stack the heat never reached
        /// is colder than the temperature it was built at, having radiated to the sky instead.
        ///
        /// <para>
        /// The second is a fact about the block rather than a fault in the rig — an exhaust pipe
        /// and a wind turbine cannot be stacked on each other in a way that conducts — and without
        /// this column it reads in the saving column exactly like saturation.
        /// </para>
        /// </summary>
        [Fact]
        public void TheFarEndSaysWhetherHeatEverReachedIt()
        {
            if (!GameBlocks.IsInstalled) return;

            float bare;
            List<CoolingLadder.Row> radiator = Of("Gauge_LG_Radiator", out bare);
            Assert.NotEmpty(radiator);

            // A conducting stack: warmer than the sky at the far end, and cooling as it lengthens.
            Assert.True(radiator[0].TopKelvin > 293.15f,
                "the block bolted straight to the reactor came back at "
                + radiator[0].TopKelvin.ToString("n1") + " K, so the joint carried nothing");

            CoolingLadder.Row last = radiator[radiator.Count - 1];
            Assert.True(last.TopKelvin < radiator[0].TopKelvin,
                "the far end of a long stack is no cooler than the near end, so the column is not"
                + " measuring a gradient");
        }
    }
}
