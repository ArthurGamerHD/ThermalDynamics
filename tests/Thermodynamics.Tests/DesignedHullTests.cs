using System.Collections.Generic;
using Thermodynamics.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The mod's design thesis, which had never been measured.**
    ///
    /// <para>
    /// `C40` bounded every path that moves heat *inside* a grid at about 9 % of the peak, and closed
    /// the transport line for retrofits with it. What it could not settle is the claim
    /// document-of-intent.md actually makes — *cooling designed
    /// in* — because every rig this repository owned had an **exposed** source. A panel bolted to an
    /// exposed source radiates wherever it is put, so no rig could see the difference between
    /// reaching the sky and not reaching it.
    /// </para>
    ///
    /// <para>
    /// <see cref="DesignedHullLab"/> buries the source in solid armour, which is how one is
    /// installed, and the difference appears: a bolted panel is buried too.
    /// </para>
    /// </summary>
    public class DesignedHullTests
    {
        private readonly ITestOutputHelper output;

        public DesignedHullTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static DesignedHullLab.Row Find(List<DesignedHullLab.Row> rows, string fit)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Fit == fit) return rows[i];
            }

            Assert.True(false, "no row for " + fit);
            return null;
        }

        /// <summary>
        /// **A loop to the skin beats a bolt joint on a buried source, carrying the same panels.**
        ///
        /// <para>
        /// Direction and reach rather than a figure, so it survives every retune: the sizes belong
        /// to balance.md and will move. What must not move is the sign, because the whole mod is
        /// built on it.
        /// </para>
        /// </summary>
        [Fact]
        public void PlumbingToTheSkinBeatsBoltingIntoTheHull()
        {
            List<DesignedHullLab.Row> rows = DesignedHullLab.Run(200000f, 4);

            DesignedHullLab.Row bare = Find(rows, "buried, bare");
            DesignedHullLab.Row bolted = Find(rows, "bolted, buried");
            DesignedHullLab.Row plumbed = Find(rows, "plumbed, to the skin");

            foreach (DesignedHullLab.Row row in rows)
            {
                output.WriteLine("{0,-22} {1,8:n1} K  saved {2,7:n1} K  panels {3}  on the skin {4}",
                    row.Fit, row.SourceKelvin, row.SavedKelvin, row.Radiators, row.RadiatorsOnTheSkin);
            }

            Assert.True(bare.SourceKelvin > 340f,
                "the buried source settled at " + bare.SourceKelvin.ToString("n1")
                + " K, which is not a cooling problem, so nothing below is being measured");

            // **The arms must carry the same radiators**, or the comparison is about how much panel
            // each got rather than where it went.
            Assert.Equal(bolted.Radiators, plumbed.Radiators);
            Assert.True(bolted.Radiators > 0,
                "no panels were fitted in either arm, so this test would pass on a lab that measured"
                + " nothing — which is exactly how the first run of it behaved");

            // And they must differ in the one way the claim is about.
            Assert.Equal(0, bolted.RadiatorsOnTheSkin);
            Assert.True(plumbed.RadiatorsOnTheSkin > 0,
                "the plumbed arm put no panel on the skin, so it is not testing the claim");

            Assert.True(plumbed.SavedKelvin > bolted.SavedKelvin,
                "plumbing to the skin saved " + plumbed.SavedKelvin.ToString("n1")
                + " K against bolting's " + bolted.SavedKelvin.ToString("n1")
                + " K on the same hull with the same panels; if this is real then the mod's design"
                + " thesis is wrong and document-of-intent.md needs rewriting");
        }

        /// <summary>
        /// **A panel bolted to a buried source is worse than no panel at all**, which is the sharp
        /// half of the finding. It displaces armour that was conducting heat away and puts in its
        /// place a block whose one talent — radiating — it cannot use, having no face on the sky.
        /// </summary>
        [Fact]
        public void BoltingAPanelIntoASolidHullIsWorseThanFittingNothing()
        {
            List<DesignedHullLab.Row> rows = DesignedHullLab.Run(200000f, 4);
            DesignedHullLab.Row bolted = Find(rows, "bolted, buried");

            Assert.True(bolted.Radiators > 0, "no panels were fitted, so nothing is being measured");
            Assert.Equal(0, bolted.RadiatorsOnTheSkin);

            Assert.True(bolted.SavedKelvin < 0f,
                "a buried panel took " + bolted.SavedKelvin.ToString("n1")
                + " K off a buried source; a block that cannot radiate and displaces armour that"
                + " conducts was expected to cost rather than pay");
        }

        /// <summary>
        /// **The pickup decides whether a block has an answer, and the panels cannot make up for
        /// it.** Watts over the sink conductance is the gradient a buried source is forced to sit
        /// at whatever is hung off the far end, so a fit whose forced gradient exceeds the rating
        /// saturates: the first panel does most of what the ladder will ever do and the rest do
        /// almost nothing.
        ///
        /// <para>
        /// Asserted as a shape rather than a figure — the first rung dwarfing the rest — because the
        /// figures are balance.md's and will move. What must not change is that a starved pickup
        /// cannot be fixed downstream, which is `C42` and the reason `C38`'s package is worth
        /// nothing on a fitted hull and decisive on a buried source.
        /// </para>
        /// </summary>
        [Fact]
        public void PanelsCannotMakeUpForAStarvedPickup()
        {
            List<DesignedHullLab.Row> rows = new List<DesignedHullLab.Row>();
            float bare = 0f;

            for (int panels = 1; panels <= 4; panels++)
            {
                List<DesignedHullLab.Row> run = DesignedHullLab.Run(6400000f, panels);
                DesignedHullLab.Row plumbed = Find(run, "plumbed, to the skin");

                if (bare == 0f) bare = Find(run, "buried, bare").SourceKelvin;
                rows.Add(plumbed);

                output.WriteLine("{0} panel(s): {1,8:n1} K   sink {2,8:n0} W/K   needs {3,7:n0} K",
                    panels, plumbed.SourceKelvin, plumbed.SinkWattsPerKelvin,
                    plumbed.SinkWattsPerKelvin > 0f ? 6400000f / plumbed.SinkWattsPerKelvin : 0f);
            }

            Assert.True(rows[0].SinkWattsPerKelvin > 0f,
                "the ring had no sink onto the source, so nothing here is being measured");

            // The pickup forces a gradient far above any rating this mod authors.
            float forced = 6400000f / rows[0].SinkWattsPerKelvin;
            Assert.True(forced > 2000f,
                "the shipped pickup forced only " + forced.ToString("n0")
                + " K on 6.4 MW, so this test is no longer describing a starved pickup");

            float first = bare - rows[0].SourceKelvin;
            float rest = rows[0].SourceKelvin - rows[rows.Count - 1].SourceKelvin;

            Assert.True(first > rest * 3f,
                "the first panel took " + first.ToString("n1") + " K and the next three took "
                + rest.ToString("n1") + " K between them; a ladder that keeps paying means the"
                + " pickup is no longer what binds, and C42 needs re-reading");
        }
    }
}
