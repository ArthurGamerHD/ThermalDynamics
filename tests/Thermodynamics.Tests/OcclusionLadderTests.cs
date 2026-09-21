using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    public class OcclusionLadderTests
    {
        private readonly ITestOutputHelper output;

/// <summary>OcclusionLadderTests operation.</summary>
        public OcclusionLadderTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const int ShippedInterval = 12;
        private const float ShippedFrequency = 4f;

        [Fact]
/// <summary>MoreSamplesDoNotChangeTheEnergyAcrossATerminator operation.</summary>
        public void MoreSamplesDoNotChangeTheEnergyAcrossATerminator()
        {
            List<OcclusionLadderLab.Rung> rungs = OcclusionLadderLab.Sweep(
                OcclusionLadderLab.Lengths, new[] { 1, SolarOcclusionSampler.MaxSamples },
                ShippedInterval, ShippedFrequency);

            Assert.NotEmpty(rungs);

            int judged = 0;
/// <summary>List operation.</summary>
            List<string> moved = new List<string>();

            foreach (double length in OcclusionLadderLab.Lengths)
            {
/// <summary>Find operation.</summary>
                OcclusionLadderLab.Rung one = Find(rungs, length, 1);
/// <summary>Find operation.</summary>
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

        [Fact]
/// <summary>TheSurplusIsHalfTheIntervalWhateverTheSampleCount operation.</summary>
        public void TheSurplusIsHalfTheIntervalWhateverTheSampleCount()
        {
            int[] intervals = { 2, 4, 8, 12, 24 };
/// <summary>List operation.</summary>
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

                if (Math.Abs(measured - expected) > 0.12d)
                {
                    wrong.Add(interval + " steps: expected " + expected.ToString("n2")
                        + " s, measured " + measured.ToString("n2") + " s");
                }
            }

            Assert.True(wrong.Count == 0, string.Join("\n  ", wrong));
        }

        [Fact]
/// <summary>SamplesFixTheInstantaneousErrorAndOnlyOnLongHulls operation.</summary>
        public void SamplesFixTheInstantaneousErrorAndOnlyOnLongHulls()
        {
            List<OcclusionLadderLab.Rung> rungs = OcclusionLadderLab.Sweep(
                new[] { 25d, 600d, 2500d }, new[] { 1, SolarOcclusionSampler.MaxSamples },
                ShippedInterval, ShippedFrequency);

/// <summary>Find operation.</summary>
            OcclusionLadderLab.Rung tinyOne = Find(rungs, 25d, 1);
/// <summary>Find operation.</summary>
            OcclusionLadderLab.Rung tinyNine = Find(rungs, 25d, SolarOcclusionSampler.MaxSamples);
/// <summary>Find operation.</summary>
            OcclusionLadderLab.Rung longOne = Find(rungs, 2500d, 1);
/// <summary>Find operation.</summary>
            OcclusionLadderLab.Rung longNine = Find(rungs, 2500d, SolarOcclusionSampler.MaxSamples);

            Assert.True(tinyOne.WorstError - tinyNine.WorstError < 0.05d,
                "a 25 m grid gained " + (tinyOne.WorstError - tinyNine.WorstError).ToString("n3"));

            Assert.True(longOne.WorstError - longNine.WorstError > 0.15d,
                "a 2,500 m hull gained only "
                + (longOne.WorstError - longNine.WorstError).ToString("n3"));

            Assert.Equal(0d, tinyOne.PartialTests);
            Assert.Equal(0d, longOne.PartialTests);

/// <summary>Find operation.</summary>
            OcclusionLadderLab.Rung midNine = Find(rungs, 600d, SolarOcclusionSampler.MaxSamples);
            Assert.True(tinyNine.PartialTests < 0.25d,
                "a 25 m grid saw " + tinyNine.PartialTests.ToString("n2") + " partial tests");
            Assert.True(midNine.PartialTests > 1d,
                "a 600 m hull saw only " + midNine.PartialTests.ToString("n2") + " partial tests");
        }

        [Fact]
/// <summary>SunsetIsNotWhereGeometryPutsIt operation.</summary>
        public void SunsetIsNotWhereGeometryPutsIt()
        {
            double radius = OcclusionLadderLab.PlanetRadius + OcclusionLadderLab.Altitude;
            double terminator = OcclusionLadderLab.Terminator(radius) * 180d / Math.PI;

            output.WriteLine("sunset at " + terminator.ToString("n1") + " degrees");

            Assert.True(terminator > 100d && terminator < 140d,
                "sunset lands at " + terminator.ToString("n1") + " degrees");
        }

/// <summary>Find operation.</summary>
        private static OcclusionLadderLab.Rung Find(List<OcclusionLadderLab.Rung> rungs,
            double length, int samples)
        {
            foreach (OcclusionLadderLab.Rung rung in rungs)
            {
                if (rung.LengthMetres == length && rung.Samples == samples) return rung;
            }

            throw new InvalidOperationException("no rung at " + length + " m, " + samples + " samples");
        }

        [Fact]
/// <summary>ThePerFaceRungIsWorthAboutAKelvinEveryTwoHundredMetresOfHull operation.</summary>
        public void ThePerFaceRungIsWorthAboutAKelvinEveryTwoHundredMetresOfHull()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.Derive();

            double perSecond = OcclusionLadderLab.KelvinPerLitSecond(settings);
            Assert.InRange(perSecond, 0.2d, 0.6d);

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
/// <summary>Worst operation.</summary>
                double worst = Worst(rows[i]);
                double perMetre = worst / rows[i].LengthMetres;

                output.WriteLine("{0:n0} m: {1:n2} K geometry, {2:n2} K at the shipped cadence, "
/// <summary>Worst operation.</summary>
                    + "{3:n5} K/m", rows[i].LengthMetres, worst, Worst(shipped[i]), perMetre);

                if (rows[i].LengthMetres < 150d) continue;

                Assert.InRange(perMetre, 0.0014d, 0.0022d);
            }

/// <summary>Worst operation.</summary>
            double ordinary = Worst(rows[1]);
            Assert.True(ordinary < 1.0d,
                "a 150 m hull's worst block is out by " + ordinary.ToString("n2") + " K");
        }

        [Fact]
/// <summary>BelowThreeHundredMetresTheCadenceCostsMoreThanTheGeometry operation.</summary>
        public void BelowThreeHundredMetresTheCadenceCostsMoreThanTheGeometry()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.Derive();

            double perSecond = OcclusionLadderLab.KelvinPerLitSecond(settings);
            double[] lengths = { 150d };

/// <summary>Worst operation.</summary>
            double shipped = Worst(OcclusionLadderLab.Extremities(
                lengths, SolarOcclusionSampler.MaxSamples, 12, 4f, perSecond)[0]);

/// <summary>Worst operation.</summary>
            double spatial = Worst(OcclusionLadderLab.Extremities(
                lengths, SolarOcclusionSampler.MaxSamples, 1, 4f, perSecond)[0]);

            output.WriteLine("150 m: {0:n2} K at the shipped interval, {1:n2} K tested every step",
                shipped, spatial);

            Assert.True(spatial < shipped * 0.5d,
                "the geometry is " + spatial.ToString("n2") + " K of a shipped "
                + shipped.ToString("n2") + " K, so the cadence is not the larger half");
        }

        [Fact]
/// <summary>TheTableConfigurationPrintsIsTheOneTheLabProduces operation.</summary>
        public void TheTableConfigurationPrintsIsTheOneTheLabProduces()
        {
            string path = System.IO.Path.Combine(
                Thermodynamics.Harness.ShippedBlocks.RepoRoot(), "docs", "configuration.md");

            Assert.True(System.IO.File.Exists(path), "no configuration.md at " + path);

/// <summary>List operation.</summary>
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

            Assert.True(printed.Count == 4,
                "found " + printed.Count + " rows under configuration.md's per-face table, not 4, "
                + "so this test is reading the wrong table or none");

/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.Derive();

            double perSecond = OcclusionLadderLab.KelvinPerLitSecond(settings);
            double[] lengths = { 25d, 150d, 600d, 2500d };

            List<OcclusionLadderLab.Extremity> geometry = OcclusionLadderLab.Extremities(
                lengths, SolarOcclusionSampler.MaxSamples, 1, 4f, perSecond);

            List<OcclusionLadderLab.Extremity> cadence = OcclusionLadderLab.Extremities(
                lengths, SolarOcclusionSampler.MaxSamples, 12, 4f, perSecond);

/// <summary>List operation.</summary>
            List<string> wrong = new List<string>();

            for (int i = 0; i < lengths.Length; i++)
            {
                string[] cells = printed[i].Split('|');
                Assert.True(cells.Length >= 4, "row " + i + " of the table has no three cells");

/// <summary>Kelvin operation.</summary>
                double hull = Kelvin(cells[1].Replace("m", ""));
/// <summary>Kelvin operation.</summary>
                double statedGeometry = Kelvin(cells[2]);
/// <summary>Kelvin operation.</summary>
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

        [Fact]
/// <summary>NoPageQuotesAPerMetreFigureTheLabDoesNotProduce operation.</summary>
        public void NoPageQuotesAPerMetreFigureTheLabDoesNotProduce()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.Derive();

            double perSecond = OcclusionLadderLab.KelvinPerLitSecond(settings);
            List<OcclusionLadderLab.Extremity> rows = OcclusionLadderLab.Extremities(
                new[] { 150d, 600d, 2500d }, SolarOcclusionSampler.MaxSamples, 1, 4f, perSecond);

            double low = double.MaxValue;
            double high = 0d;

            foreach (OcclusionLadderLab.Extremity row in rows)
            {
/// <summary>Worst operation.</summary>
                double rate = Worst(row) / row.LengthMetres;
                if (rate < low) low = rate;
                if (rate > high) high = rate;
            }

            string docs = System.IO.Path.Combine(
                Thermodynamics.Harness.ShippedBlocks.RepoRoot(), "docs");

/// <summary>List operation.</summary>
            List<string> wrong = new List<string>();
            int quoted = 0;

            foreach (string path in System.IO.Directory.GetFiles(docs, "*.md"))
            {
                foreach (string line in System.IO.File.ReadAllLines(path))
                {
                    string trimmed = line.TrimStart();

                    if (trimmed.StartsWith("| 20", StringComparison.Ordinal)) continue;

                    foreach (System.Text.RegularExpressions.Match match in
                        System.Text.RegularExpressions.Regex.Matches(line,
                            @"([0-9]*\.[0-9]+) K a metre"))
                    {
                        quoted++;

                        double stated = double.Parse(match.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture);

                        double slack = Math.Pow(10d, -match.Groups[1].Value.Length + 2) / 2d;
                        if (stated >= low - slack && stated <= high + slack) continue;

                        wrong.Add(System.IO.Path.GetFileName(path) + " says "
                            + stated.ToString("n5") + " K a metre");
                    }
                }
            }

            Assert.True(quoted >= 2,
                "only " + quoted + " per-metre figures were found in the documentation, so this "
                + "check is no longer reading what it was written to read");

            wrong.Sort(StringComparer.Ordinal);
            Assert.True(wrong.Count == 0,
                "the lab measures " + low.ToString("n5") + " to " + high.ToString("n5")
                + " K a metre:\n  "
                + string.Join("\n  ", wrong.ToArray()));
        }

/// <summary>Compare operation.</summary>
        private static void Compare(List<string> wrong, double length, string column,
            double printed, double measured)
        {
            if (Math.Abs(printed - measured) <= 0.005d) return;

            wrong.Add(length.ToString("n0") + " m, " + column + ": the page says "
                + printed.ToString("n2") + " K and the lab measures " + measured.ToString("n2")
                + " K");
        }

/// <summary>Kelvin operation.</summary>
        private static double Kelvin(string cell)
        {
            string text = cell.Replace("K", "").Replace("*", "").Replace(",", "").Trim();
            return double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        }

/// <summary>Worst operation.</summary>
        private static double Worst(OcclusionLadderLab.Extremity row)
        {
            return Math.Max(row.SurplusKelvin, row.DeficitKelvin);
        }
    }
}
