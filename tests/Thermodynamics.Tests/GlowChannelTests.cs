using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **How much of the glow's colour channel a player can actually see**, which is what
    /// backlog.md `B34` needed before the intent page could say who the
    /// two-channel design is for.
    /// </summary>
    public class GlowChannelTests
    {
        private readonly ITestOutputHelper output;

        public GlowChannelTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>
        /// **The conversion is checked against colours whose L*a*b* is known**, because every
        /// number below is a distance in that space and a wrong matrix would make the whole finding
        /// an artefact of the arithmetic (`P4`).
        /// </summary>
        [Fact]
        public void TheColourConversionAgreesWithKnownValues()
        {
            double[] white = GlowChannelLab.Lab(new VRageMath.Vector3(1f, 1f, 1f));
            Assert.Equal(100d, white[0], 2);
            Assert.Equal(0d, white[1], 2);
            Assert.Equal(0d, white[2], 2);

            double[] black = GlowChannelLab.Lab(new VRageMath.Vector3(0f, 0f, 0f));
            Assert.Equal(0d, black[0], 2);

            // sRGB pure red is L* 53.24, a* 80.09, b* 67.20 — the standard value.
            double[] red = GlowChannelLab.Lab(new VRageMath.Vector3(1f, 0f, 0f));
            Assert.Equal(53.24d, red[0], 1);
            Assert.Equal(80.09d, red[1], 1);
            Assert.Equal(67.20d, red[2], 1);

            Assert.Equal(0d, GlowChannelLab.DeltaE(
                new VRageMath.Vector3(1f, 0f, 0f), new VRageMath.Vector3(1f, 0f, 0f)), 6);
        }

        /// <summary>
        /// **The colour channel is flat over every block's own band, not only over the quarter
        /// under the Draper point.**
        ///
        /// <para>
        /// The locus is steepest just above 800 K and flattens from there, and a block glows over
        /// a hundred kelvin — so the most colour any block in the game can carry across its whole
        /// glow band is a few ΔE, against a just-noticeable difference of 2.3 on flat adjacent
        /// patches. A block that changes colour by less than that between *not glowing* and
        /// *failing* has one channel, whatever the design says.
        /// </para>
        /// </summary>
        [Fact]
        public void NoBlockMovesMoreThanAFewDeltaEAcrossItsWholeGlowBand()
        {
            if (!GameBlocks.IsInstalled) return;

            List<GlowChannelLab.Band> bands = GlowChannelLab.Bands();
            Assert.NotEmpty(bands);

            int visible = 0;
            int pinned = 0;

            foreach (GlowChannelLab.Band band in bands)
            {
                if (band.Visible) visible++;
                if (band.BelowDraper) pinned++;
            }

            output.WriteLine("{0} block types, {1} under the Draper point, {2} whose colour moves "
                + "a visible amount over their own band", bands.Count, pinned, visible);

            for (int i = 0; i < bands.Count && i < 5; i++)
            {
                output.WriteLine("most colour: {0} rated {1:n0} K, band {2:n0}-{1:n0}, dE {3:n2}",
                    bands[i].TypeId, bands[i].CriticalKelvin, bands[i].StartKelvin,
                    bands[i].DeltaE);
            }

            float atKelvin;
            double best = GlowChannelLab.BestBand(out atKelvin);
            output.WriteLine("the best hundred kelvin anywhere in the table: dE {0:n2} from {1:n0} K",
                best, atKelvin);

            // **Sanity first**: the measured share under the Draper point is the one already
            // published, so this catalogue is the one that finding was taken over.
            Assert.InRange(pinned / (double)bands.Count, 0.1d, 0.45d);

            // **The finding.** A band that could carry a large ΔE would make the colour channel
            // real for the blocks that have one, and this test would be the wrong argument.
            Assert.True(best < 12d,
                "the best hundred-kelvin band in the table carries dE " + best.ToString("n1")
                + ", which is not the flat channel this claim rests on");
        }

        /// <summary>
        /// **The channel is weakest at both ends of the table and best in the middle**, which is
        /// not what the first version of this test assumed and is why it is written down.
        ///
        /// <para>
        /// The guess was that the locus flattens with temperature, so the hottest-rated blocks
        /// would carry the least colour. It does flatten in *green*, and it does not flatten in
        /// ΔE: blue starts rising above 2,000 K and puts the top of the table back ahead of the
        /// bottom. The peak is a hundred kelvin from about 1,100 K, and it is still only about
        /// twice a just-noticeable difference.
        /// </para>
        /// </summary>
        [Fact]
        public void TheChannelIsBestInTheMiddleOfTheTableAndEvenThereItIsSmall()
        {
            double low = GlowChannelLab.DeltaE(
                Incandescence.Colour(800f), Incandescence.Colour(900f));

            double high = GlowChannelLab.DeltaE(
                Incandescence.Colour(2900f), Incandescence.Colour(3000f));

            float atKelvin;
            double best = GlowChannelLab.BestBand(out atKelvin);

            output.WriteLine("800-900 K: dE {0:n2}; 2,900-3,000 K: dE {1:n2}; best dE {2:n2} "
                + "from {3:n0} K", low, high, best, atKelvin);

            Assert.True(best > low && best > high,
                "the best band in the table is at one of its ends, so the shape described above "
                + "is wrong");

            Assert.InRange(atKelvin, 900f, 2000f);

            // Twice the just-noticeable difference is the whole of what the colour channel is
            // worth at its very best, on a patch of flat colour a viewer can compare side by side.
            Assert.True(best < 3d * GlowChannelLab.JustNoticeable,
                "the best band carries dE " + best.ToString("n1") + ", which is more than three "
                + "just-noticeable differences and is not the flat channel this claim rests on");
        }

        /// <summary>
        /// **Between two blocks the colour does say something, and that is the claim the design
        /// actually makes.**
        ///
        /// <para>
        /// *A decorative block dying dull red beside a thruster dying orange* is a comparison
        /// across blocks rated hundreds of kelvin apart, and over that distance the locus is walked
        /// properly. Keeping this test beside the one above is the point: the colour channel is a
        /// cross-block signal and not a within-block one, and the two had never been told apart.
        /// </para>
        /// </summary>
        [Fact]
        public void BetweenTheCoolestAndHottestRatedBlocksTheColourIsPlainlyDifferent()
        {
            if (!GameBlocks.IsInstalled) return;

            float coolest;
            float hottest;
            double across = GlowChannelLab.AcrossBlocks(out coolest, out hottest);

            output.WriteLine("{0:n0} K against {1:n0} K: dE {2:n2}", coolest, hottest, across);

            Assert.True(across > 4d * GlowChannelLab.JustNoticeable,
                "the coolest and hottest rated blocks in the game differ by dE "
                + across.ToString("n1") + " at their own ratings, so the colour channel says "
                + "nothing between blocks either and the design carries one channel outright");
        }
    }
}
