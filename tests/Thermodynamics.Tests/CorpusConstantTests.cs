using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The population constants the harness reasons from have a dataset behind them, and it is
    /// checked.**
    ///
    /// <para>
    /// `Census.Corpus` states what the corpus does — substep demand in vacuum and in air, waste per
    /// block, ship count — and every one of those figures was transcribed by hand out of a walk's
    /// output. `E5` says a figure comes from the dataset it is about, and `F14` records what
    /// happens otherwise: a page said *twenty-four sealed blocks* where the dataset said **1,184
    /// across 331 ships**, a sample figure standing as a population figure with only its share
    /// right. Nothing was checking these.
    /// </para>
    ///
    /// <para>
    /// The committed summary is the source. It is kilobytes and versioned, so a constant and the
    /// walk behind it can be compared without the gigabytes the walk produced — which is the whole
    /// reason `verdict.py --csv` exists.
    /// </para>
    /// </summary>
    public class CorpusConstantTests
    {
        /// <summary>
        /// The vacuum survey re-taken through the fixed blueprint reader (`A13`), which is the
        /// dataset the vacuum constants are about.
        /// </summary>
        private const string Summary = "summary-survey-2026-08-28.csv";

        private static Dictionary<string, double> Figures(string name)
        {
            string path = Path.Combine(ShippedBlocks.RepoRoot(), "tools", "corpus", name);
            Assert.True(File.Exists(path), "no committed summary at " + path);

            Dictionary<string, double> figures = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach (string line in File.ReadAllLines(path))
            {
                string[] parts = line.Split(',');
                if (parts.Length < 2) continue;

                double value;
                if (double.TryParse(parts[1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value))
                {
                    figures[parts[0]] = value;
                }
            }

            Assert.True(figures.Count > 20,
                "only " + figures.Count + " figures were read from " + name
                + ", so this test is not reading it");

            return figures;
        }

        /// <summary>
        /// **The vacuum constants reproduce on the re-taken survey, to the digit.**
        ///
        /// <para>
        /// They were measured before `A13`'s reader fix, on a different walk and a different ship
        /// count, and they come back the same — which is the stiffness half of `A13`'s finding
        /// arriving a third time: a block-identity error worth a sixth of a population's heat is
        /// worth nothing whatever to its cost, because the blocks it corrected are heavy and heavy
        /// blocks are not stiff.
        /// </para>
        ///
        /// <para>
        /// **One per cent, and the two figures are one per cent apart in different directions.**
        /// p90 lands on 10.029 against a constant of 10.03; p50 lands on 7.386 against 7.40, which
        /// is 0.19 % out. They are two walks of the same corpus reaching different numbers of ships
        /// — 8,098 against 8,137 — and read at percentiles computed by different code, so equality
        /// is the wrong assertion and *unmoved* is the right one. A per cent is far inside what
        /// `A13` moved the heat by (15.76 %) and far outside what two walks disagree by, which is
        /// what makes it a bound rather than a fitted tolerance.
        /// </para>
        /// </summary>
        [Fact]
        public void TheVacuumConstantsMatchTheCommittedSurvey()
        {
            Dictionary<string, double> figures = Figures(Summary);

            // `idle` is the vacuum scenario the constants were taken from: no sun, no air, no load.
            Within(0.01, Census.Corpus.VacuumP50, figures["G6 idle demand p50"], "vacuum p50");
            Within(0.01, Census.Corpus.VacuumP90, figures["G6 idle demand p90"], "vacuum p90");
        }

        /// <summary>Asserts a constant is within a share of the figure the dataset states.</summary>
        private static void Within(double share, double constant, double measured, string what)
        {
            double drift = Math.Abs(measured - constant) / Math.Max(1e-9, Math.Abs(constant));

            Assert.True(drift <= share,
                what + ": the constant says " + constant.ToString("n3", CultureInfo.InvariantCulture)
                + " and the committed survey says "
                + measured.ToString("n3", CultureInfo.InvariantCulture) + ", which is "
                + (drift * 100d).ToString("n2", CultureInfo.InvariantCulture) + " % apart");
        }

        /// <summary>
        /// The ship count is a population figure like any other, and it moved: the constant is the
        /// stiffness walk's 8,098 and the survey reached 8,137. Both are *whole* walks of the same
        /// corpus, so the difference is what each walk could read rather than what the corpus holds
        /// — which is exactly why a constant naming one of them has to say which.
        /// </summary>
        [Fact]
        public void TheShipCountNamesAWalkRatherThanThePopulation()
        {
            Dictionary<string, double> figures = Figures(Summary);

            Assert.Equal(8144d, figures["dataset population"]);

            // The survey is whole and reached more ships than the stiffness walk the constant
            // comes from. Asserted rather than reconciled: they are two walks, and `M1` says
            // figures stopped different ways are not the same figure.
            Assert.True(figures["dataset ships"] > Census.Corpus.Ships,
                "the survey reached " + figures["dataset ships"] + " ships against the constant's "
                + Census.Corpus.Ships + ", so the constant is no longer the larger of the two and"
                + " the comment explaining the gap is out of date");

            Assert.True(figures["dataset ships"] - Census.Corpus.Ships < 100d,
                "the two walks are " + (figures["dataset ships"] - Census.Corpus.Ships)
                + " ships apart, which is too many to be the reader cap and the modded hulls the"
                + " constant names");
        }

        /// <summary>
        /// **The air constants are deliberately not checked here, and that is the finding.** They
        /// come from `out/air-corpus-2026-08-24`, which was collected through the broken reader and
        /// carries **no `provenance.txt` at all** — so nothing records what build it saw. The air
        /// walk is being re-taken; until it lands, this test asserts only that the air figures are
        /// still a distinct set of numbers, so that quietly copying the vacuum ones over them would
        /// fail rather than pass.
        /// </summary>
        [Fact]
        public void TheAirConstantsAreStillAwaitingTheirWalk()
        {
            Assert.True(Census.Corpus.AirP50 > Census.Corpus.VacuumP50,
                "air is not stiffer than vacuum in the constants, which is the one thing every"
                + " measurement of the pair has agreed on");

            Assert.True(Census.Corpus.AirP90 > Census.Corpus.VacuumP90);
            Assert.True(Census.Corpus.AirMax >= Census.Corpus.AirP90);
            Assert.True(Census.Corpus.AirP10 < Census.Corpus.AirP50);
        }
    }
}
