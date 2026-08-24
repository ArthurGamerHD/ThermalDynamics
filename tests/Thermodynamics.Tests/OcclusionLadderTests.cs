using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// What `SolarOcclusionSamples` buys, and what it does not.
    ///
    /// <para>
    /// [backlog.md](../../docs/backlog.md) `A9` ranks the shipped occlusion default second of
    /// everything open, on the ground that one ray from the grid's centre makes a ship flip between
    /// fully lit and fully dark and that *fidelity is the default* forbids shipping the cheap form of
    /// a difference a player can see. **The premise is right and the dial is the wrong one**, which
    /// is what these hold: more samples change the sunlight a hull absorbs over a terminator crossing
    /// by almost nothing, because a single centre ray is unbiased — it reports light after the
    /// leading end is dark and dark before the trailing end is, and the two cancel.
    /// </para>
    ///
    /// <para>
    /// See balance.md and configuration.md, External shadow.
    /// </para>
    /// </summary>
    public class OcclusionLadderTests
    {
        private readonly ITestOutputHelper output;

        public OcclusionLadderTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const int ShippedInterval = 12;
        private const float ShippedFrequency = 4f;

        /// <summary>
        /// Raising the sample count from one to nine does not change the energy a hull absorbs
        /// across a terminator, at any size.
        ///
        /// The claim `A9` assumed the other way round, and it is the reason the row's default change
        /// is not made: nine times the raycasts buys under a tenth of a second of sunlight on a
        /// crossing that is already a second and a half out.
        /// </summary>
        [Fact]
        public void MoreSamplesDoNotChangeTheEnergyAcrossATerminator()
        {
            List<OcclusionLadderLab.Rung> rungs = OcclusionLadderLab.Sweep(
                OcclusionLadderLab.Lengths, new[] { 1, SolarOcclusionSampler.MaxSamples },
                ShippedInterval, ShippedFrequency);

            Assert.NotEmpty(rungs);

            int judged = 0;
            List<string> moved = new List<string>();

            foreach (double length in OcclusionLadderLab.Lengths)
            {
                OcclusionLadderLab.Rung one = Find(rungs, length, 1);
                OcclusionLadderLab.Rung nine = Find(rungs, length, SolarOcclusionSampler.MaxSamples);

                judged++;
                output.WriteLine(string.Format("{0,8:n0} m   one {1,6:n2} s   nine {2,6:n2} s",
                    length, one.SurplusSeconds, nine.SurplusSeconds));

                if (Math.Abs(nine.SurplusSeconds - one.SurplusSeconds) > 0.15d) 
                {
                    moved.Add(length.ToString("n0") + " m moved from "
                        + one.SurplusSeconds.ToString("n2") + " s to "
                        + nine.SurplusSeconds.ToString("n2") + " s");
                }
            }

            Assert.True(judged > 0, "nothing was judged");
            Assert.True(moved.Count == 0, string.Join("\n  ", moved));
        }

        /// <summary>
        /// The surplus is the test cadence's own lag and is half of it, which is the dial that does
        /// move the energy.
        ///
        /// A test that ran before the crossing goes on saying *lit* until the next one, and a grid
        /// meets every phase of that schedule equally often, so the mean lag is half an interval —
        /// exactly what the sweep measures. That makes the trade a clean one: halving the interval
        /// halves the error, and no sample count does anything comparable.
        /// </summary>
        [Fact]
        public void TheSurplusIsHalfTheIntervalWhateverTheSampleCount()
        {
            int[] intervals = { 2, 4, 8, 12, 24 };
            List<string> wrong = new List<string>();

            foreach (int interval in intervals)
            {
                List<OcclusionLadderLab.Rung> rungs = OcclusionLadderLab.Sweep(
                    new[] { 600d }, new[] { SolarOcclusionSampler.MaxSamples },
                    interval, ShippedFrequency);

                double expected = 0.5d * interval / ShippedFrequency;
                double measured = rungs[0].SurplusSeconds;

                output.WriteLine(string.Format("{0,3} steps   expected {1,6:n2} s   measured {2,6:n2} s",
                    interval, expected, measured));

                // A tenth of a second, which is two ticks of the lab's own integral.
                if (Math.Abs(measured - expected) > 0.12d)
                {
                    wrong.Add(interval + " steps: expected " + expected.ToString("n2")
                        + " s, measured " + measured.ToString("n2") + " s");
                }
            }

            Assert.True(wrong.Count == 0, string.Join("\n  ", wrong));
        }

        /// <summary>
        /// What the samples *do* buy is the instantaneous error, and only on a hull long enough that
        /// a test lands while it is still straddling.
        ///
        /// This is the honest half of `A9`: a kilometre-long ship reported fully lit while half of it
        /// is in shadow is wrong in a way a player can see, and more samples fix that. The size at
        /// which it starts to matter is a measurement rather than a guess — a hull under the travel
        /// between two tests almost never gets a partial reading at all.
        /// </summary>
        [Fact]
        public void SamplesFixTheInstantaneousErrorAndOnlyOnLongHulls()
        {
            List<OcclusionLadderLab.Rung> rungs = OcclusionLadderLab.Sweep(
                new[] { 25d, 600d, 2500d }, new[] { 1, SolarOcclusionSampler.MaxSamples },
                ShippedInterval, ShippedFrequency);

            OcclusionLadderLab.Rung tinyOne = Find(rungs, 25d, 1);
            OcclusionLadderLab.Rung tinyNine = Find(rungs, 25d, SolarOcclusionSampler.MaxSamples);
            OcclusionLadderLab.Rung longOne = Find(rungs, 2500d, 1);
            OcclusionLadderLab.Rung longNine = Find(rungs, 2500d, SolarOcclusionSampler.MaxSamples);

            // A block-sized grid gains nothing: every sample point is on the same side.
            Assert.True(tinyOne.WorstError - tinyNine.WorstError < 0.05d,
                "a 25 m grid gained " + (tinyOne.WorstError - tinyNine.WorstError).ToString("n3"));

            // A 2.5 km hull gains a third of the error back.
            Assert.True(longOne.WorstError - longNine.WorstError > 0.15d,
                "a 2,500 m hull gained only "
                + (longOne.WorstError - longNine.WorstError).ToString("n3"));

            // And a single ray never reports a partial shadow, at any size, by construction.
            Assert.Equal(0d, tinyOne.PartialTests);
            Assert.Equal(0d, longOne.PartialTests);

            // The ladder only has somewhere to land once the hull is long enough to still be
            // straddling when a test falls.
            OcclusionLadderLab.Rung midNine = Find(rungs, 600d, SolarOcclusionSampler.MaxSamples);
            Assert.True(tinyNine.PartialTests < 0.25d,
                "a 25 m grid saw " + tinyNine.PartialTests.ToString("n2") + " partial tests");
            Assert.True(midNine.PartialTests > 1d,
                "a 600 m hull saw only " + midNine.PartialTests.ToString("n2") + " partial tests");
        }

        /// <summary>
        /// Sunset does not land at ninety degrees, and the lab has to fly through the crossing the
        /// code actually has rather than the one geometry would give.
        ///
        /// `OcclusionThreshold` says of itself that it is a fitted curve — *nothing about the cube or
        /// the 0.85 is physical* — and at low orbit around an Earthlike it puts sunset well past the
        /// geometric terminator. A lab that assumed ninety degrees would fly a ship through full
        /// daylight and measure nothing, which is what the first version of this one did.
        /// </summary>
        [Fact]
        public void SunsetIsNotWhereGeometryPutsIt()
        {
            double radius = OcclusionLadderLab.PlanetRadius + OcclusionLadderLab.Altitude;
            double terminator = OcclusionLadderLab.Terminator(radius) * 180d / Math.PI;

            output.WriteLine("sunset at " + terminator.ToString("n1") + " degrees");

            Assert.True(terminator > 100d && terminator < 140d,
                "sunset lands at " + terminator.ToString("n1") + " degrees");
        }

        private static OcclusionLadderLab.Rung Find(List<OcclusionLadderLab.Rung> rungs,
            double length, int samples)
        {
            foreach (OcclusionLadderLab.Rung rung in rungs)
            {
                if (rung.LengthMetres == length && rung.Samples == samples) return rung;
            }

            throw new InvalidOperationException("no rung at " + length + " m, " + samples + " samples");
        }

        /// <summary>
        /// **What the unbuilt top rung is worth, in kelvin on the block it is worst for.**
        ///
        /// <para>
        /// Resolving the planet's shadow per face is the one rung of `A9`'s ladder nothing has
        /// built, and it fixes the half neither the sample count nor the interval reaches: the two
        /// ends of a hull being told the same thing while one is dark and the other is lit. Tested
        /// every step, so the cadence contributes nothing and what is left is the spatial error
        /// alone, it comes out **linear in hull length at about a two-hundredth of a kelvin a
        /// metre** — which is what makes it a decision about how long ships are rather than about
        /// how good the model is.
        /// </para>
        /// </summary>
        [Fact]
        public void ThePerFaceRungIsWorthAboutAKelvinEveryTwoHundredMetresOfHull()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.Derive();

            // **A rate in simulated seconds, so it moves with the clock.** One lit face gains
            // 0.36 K a second at the 90 `C24` ships and gained 0.91 K at 225, because a simulated
            // second is two and a half times less thermal time. Every figure below it is seconds
            // of sunlight times this, so the whole ladder scales with it — which is why the rung's
            // worth is quoted per metre of hull rather than in kelvin.
            double perSecond = OcclusionLadderLab.KelvinPerLitSecond(settings);
            Assert.InRange(perSecond, 0.2d, 0.6d);

            double[] lengths = { 150d, 600d, 2500d };
            List<OcclusionLadderLab.Extremity> rows = OcclusionLadderLab.Extremities(
                lengths, SolarOcclusionSampler.MaxSamples, 1, 4f, perSecond);

            Assert.Equal(lengths.Length, rows.Count);

            for (int i = 0; i < rows.Count; i++)
            {
                double worst = Math.Max(rows[i].SurplusKelvin, rows[i].DeficitKelvin);
                double perMetre = worst / rows[i].LengthMetres;

                output.WriteLine("{0:n0} m: {1:n2} K, {2:n5} K/m",
                    rows[i].LengthMetres, worst, perMetre);

                // The rate is the finding, and it holds across a factor of sixteen in length.
                // 0.0018 K a metre at the shipped clock, where it was 0.0045 at 225 — the same
                // geometry costing 0.4 of the kelvin, since a slower clock covers less thermal
                // ground in the seconds a hull is told the wrong thing about.
                Assert.InRange(perMetre, 0.0014d, 0.0022d);
            }

            // An ordinary ship is a fraction of a kelvin, which is the reason the rung is not built.
            double ordinary = Math.Max(rows[0].SurplusKelvin, rows[0].DeficitKelvin);
            Assert.True(ordinary < 1.0d,
                "a 150 m hull's worst block is out by " + ordinary.ToString("n2") + " K");
        }

        /// <summary>
        /// **And below three hundred metres the cadence is the larger half**, which is why the
        /// interval is the dial to spend on first and the sample count is neither.
        /// </summary>
        [Fact]
        public void BelowThreeHundredMetresTheCadenceCostsMoreThanTheGeometry()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.Derive();

            double perSecond = OcclusionLadderLab.KelvinPerLitSecond(settings);
            double[] lengths = { 150d };

            double shipped = Worst(OcclusionLadderLab.Extremities(
                lengths, SolarOcclusionSampler.MaxSamples, 12, 4f, perSecond)[0]);

            double spatial = Worst(OcclusionLadderLab.Extremities(
                lengths, SolarOcclusionSampler.MaxSamples, 1, 4f, perSecond)[0]);

            output.WriteLine("150 m: {0:n2} K at the shipped interval, {1:n2} K tested every step",
                shipped, spatial);

            Assert.True(spatial < shipped * 0.5d,
                "the geometry is " + spatial.ToString("n2") + " K of a shipped "
                + shipped.ToString("n2") + " K, so the cadence is not the larger half");
        }

        private static double Worst(OcclusionLadderLab.Extremity row)
        {
            return Math.Max(row.SurplusKelvin, row.DeficitKelvin);
        }
    }
}
