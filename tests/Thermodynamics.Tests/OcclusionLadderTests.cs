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
    /// backlog.md `A9` ranks the shipped occlusion default second of
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

            // **The table configuration.md prints, produced rather than quoted.** The shortest hull
            // is here for the page and not for the rate: 25 m is four block lengths, and over a
            // crossing that short the error is set by where the cadence falls rather than by the
            // geometry, so it sits above the band the three longer hulls hold to.
            double[] lengths = { 25d, 150d, 600d, 2500d };

            List<OcclusionLadderLab.Extremity> rows = OcclusionLadderLab.Extremities(
                lengths, SolarOcclusionSampler.MaxSamples, 1, 4f, perSecond);

            List<OcclusionLadderLab.Extremity> shipped = OcclusionLadderLab.Extremities(
                lengths, SolarOcclusionSampler.MaxSamples, 12, 4f, perSecond);

            Assert.Equal(lengths.Length, rows.Count);
            Assert.Equal(lengths.Length, shipped.Count);

            output.WriteLine("{0:n2} K a second of sunlight on one lit face", perSecond);

            for (int i = 0; i < rows.Count; i++)
            {
                double worst = Worst(rows[i]);
                double perMetre = worst / rows[i].LengthMetres;

                output.WriteLine("{0:n0} m: {1:n2} K geometry, {2:n2} K at the shipped cadence, "
                    + "{3:n5} K/m", rows[i].LengthMetres, worst, Worst(shipped[i]), perMetre);

                // The rate is the finding, and it holds across a factor of sixteen in length.
                // 0.0018 K a metre at the shipped clock, where it was 0.0045 at 225 — the same
                // geometry costing 0.4 of the kelvin, since a slower clock covers less thermal
                // ground in the seconds a hull is told the wrong thing about.
                if (rows[i].LengthMetres < 150d) continue;

                Assert.InRange(perMetre, 0.0014d, 0.0022d);
            }

            // An ordinary ship is a fraction of a kelvin, which is the reason the rung is not built.
            double ordinary = Worst(rows[1]);
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

        /// <summary>
        /// **The table configuration.md prints is the one the lab produces**, to the hundredth of
        /// a kelvin it is printed to.
        ///
        /// <para>
        /// This exists because the figures drifted and nothing said so. `C24` took `HeatTimeScale`
        /// from 225 to 90 on 2026-08-24 and every kelvin in that table is seconds of sunlight times
        /// a rate that moves with the clock, so all four rows went stale the moment the pair
        /// shipped — and they stayed stale on three pages while the test above printed the right
        /// ones, because each page had quoted the last page rather than the lab (`P1`, `E11`).
        /// **A number a page copies from another page has no source**, and a drifted copy does not
        /// throw. This one does.
        /// </para>
        ///
        /// <para>
        /// It reads the rendered table rather than a fixture, so the check fails on the thing a
        /// reader actually sees. Rewording the surrounding prose is free; changing a figure without
        /// re-measuring is not.
        /// </para>
        /// </summary>
        [Fact]
        public void TheTableConfigurationPrintsIsTheOneTheLabProduces()
        {
            string path = System.IO.Path.Combine(
                Thermodynamics.Harness.ShippedBlocks.RepoRoot(), "docs", "configuration.md");

            Assert.True(System.IO.File.Exists(path), "no configuration.md at " + path);

            List<string> printed = new List<string>();
            bool inTable = false;

            foreach (string line in System.IO.File.ReadAllLines(path))
            {
                string trimmed = line.Trim();

                if (trimmed.StartsWith("| Hull |", StringComparison.Ordinal)
                    && trimmed.Contains("shipped 12-step cadence"))
                {
                    inTable = true;
                    continue;
                }

                if (!inTable) continue;
                if (!trimmed.StartsWith("|", StringComparison.Ordinal)) break;
                if (trimmed.StartsWith("| ---", StringComparison.Ordinal)) continue;

                printed.Add(trimmed);
            }

            // **A check that found no table would report success**, which is the failure this whole
            // class of test exists to prevent (`E8`). Four rows, and the heading has to be the one
            // above — a reworded heading is a table this test is no longer reading.
            Assert.True(printed.Count == 4,
                "found " + printed.Count + " rows under configuration.md's per-face table, not 4, "
                + "so this test is reading the wrong table or none");

            ThermalSettings settings = new ThermalSettings();
            settings.Derive();

            double perSecond = OcclusionLadderLab.KelvinPerLitSecond(settings);
            double[] lengths = { 25d, 150d, 600d, 2500d };

            List<OcclusionLadderLab.Extremity> geometry = OcclusionLadderLab.Extremities(
                lengths, SolarOcclusionSampler.MaxSamples, 1, 4f, perSecond);

            List<OcclusionLadderLab.Extremity> cadence = OcclusionLadderLab.Extremities(
                lengths, SolarOcclusionSampler.MaxSamples, 12, 4f, perSecond);

            List<string> wrong = new List<string>();

            for (int i = 0; i < lengths.Length; i++)
            {
                string[] cells = printed[i].Split('|');
                Assert.True(cells.Length >= 4, "row " + i + " of the table has no three cells");

                double hull = Kelvin(cells[1].Replace("m", ""));
                double statedGeometry = Kelvin(cells[2]);
                double statedCadence = Kelvin(cells[3]);

                if (Math.Abs(hull - lengths[i]) > 0.5d)
                {
                    wrong.Add("row " + i + " is a " + hull + " m hull where the lab measured "
                        + lengths[i] + " m");
                    continue;
                }

                Compare(wrong, lengths[i], "geometry alone", statedGeometry, Worst(geometry[i]));
                Compare(wrong, lengths[i], "at the shipped cadence", statedCadence,
                    Worst(cadence[i]));
            }

            wrong.Sort(StringComparer.Ordinal);
            Assert.True(wrong.Count == 0,
                "configuration.md prints figures the lab does not produce:\n  "
                + string.Join("\n  ", wrong.ToArray()));
        }

        /// <summary>
        /// **No page states a per-metre figure the lab does not produce**, wherever it states it.
        ///
        /// <para>
        /// The check above reads the per-face *table* and could not see the same number quoted in a
        /// sentence — which is exactly where the fourth stale copy was found, hours after the other
        /// three were fixed, in configuration.md's rungs inventory.
        /// A check that catches three of four sites reads as having caught them all (`E8`).
        /// </para>
        ///
        /// <para>
        /// **Change-log rows are exempt and nothing else is.** A dated row records what a figure
        /// *was*, which is history and must not be rewritten (`R12`); a sentence in a page's body
        /// describes the present.
        /// </para>
        /// </summary>
        [Fact]
        public void NoPageQuotesAPerMetreFigureTheLabDoesNotProduce()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.Derive();

            double perSecond = OcclusionLadderLab.KelvinPerLitSecond(settings);
            List<OcclusionLadderLab.Extremity> rows = OcclusionLadderLab.Extremities(
                new[] { 150d, 600d, 2500d }, SolarOcclusionSampler.MaxSamples, 1, 4f, perSecond);

            // **A band rather than a number**, because the rate is *about* a value: it runs 0.00196
            // at 150 m to 0.00181 at 2,500 m, and a page quoting one figure for all three is
            // quoting the band. A single length would make the tolerance decide which page passes.
            double low = double.MaxValue;
            double high = 0d;

            foreach (OcclusionLadderLab.Extremity row in rows)
            {
                double rate = Worst(row) / row.LengthMetres;
                if (rate < low) low = rate;
                if (rate > high) high = rate;
            }

            string docs = System.IO.Path.Combine(
                Thermodynamics.Harness.ShippedBlocks.RepoRoot(), "docs");

            List<string> wrong = new List<string>();
            int quoted = 0;

            foreach (string path in System.IO.Directory.GetFiles(docs, "*.md"))
            {
                foreach (string line in System.IO.File.ReadAllLines(path))
                {
                    string trimmed = line.TrimStart();

                    // A change-log row. It records what a figure was, and history is not corrected.
                    if (trimmed.StartsWith("| 20", StringComparison.Ordinal)) continue;

                    foreach (System.Text.RegularExpressions.Match match in
                        System.Text.RegularExpressions.Regex.Matches(line,
                            @"([0-9]*\.[0-9]+) K a metre"))
                    {
                        quoted++;

                        double stated = double.Parse(match.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture);

                        // Half of the last printed digit outside the band at either end.
                        double slack = Math.Pow(10d, -match.Groups[1].Value.Length + 2) / 2d;
                        if (stated >= low - slack && stated <= high + slack) continue;

                        wrong.Add(System.IO.Path.GetFileName(path) + " says "
                            + stated.ToString("n5") + " K a metre");
                    }
                }
            }

            // **A scan that matched nothing would report success.** The figure is quoted in at
            // least two places by design — the backlog's ranked row and its own row — so a pattern
            // that stopped matching would pass silently.
            Assert.True(quoted >= 2,
                "only " + quoted + " per-metre figures were found in the documentation, so this "
                + "check is no longer reading what it was written to read");

            wrong.Sort(StringComparer.Ordinal);
            Assert.True(wrong.Count == 0,
                "the lab measures " + low.ToString("n5") + " to " + high.ToString("n5")
                + " K a metre:\n  "
                + string.Join("\n  ", wrong.ToArray()));
        }

        /// <summary>
        /// One printed figure against one measured one, at the precision the page prints.
        ///
        /// **Half of the last printed digit**, so rounding is allowed and re-measuring is not
        /// optional. A looser tolerance would let the whole table go stale by a clock change again,
        /// which is exactly what happened.
        /// </summary>
        private static void Compare(List<string> wrong, double length, string column,
            double printed, double measured)
        {
            if (Math.Abs(printed - measured) <= 0.005d) return;

            wrong.Add(length.ToString("n0") + " m, " + column + ": the page says "
                + printed.ToString("n2") + " K and the lab measures " + measured.ToString("n2")
                + " K");
        }

        /// <summary>A table cell as a number, with its unit and its markdown taken off.</summary>
        private static double Kelvin(string cell)
        {
            string text = cell.Replace("K", "").Replace("*", "").Replace(",", "").Trim();
            return double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static double Worst(OcclusionLadderLab.Extremity row)
        {
            return Math.Max(row.SurplusKelvin, row.DeficitKelvin);
        }
    }
}
