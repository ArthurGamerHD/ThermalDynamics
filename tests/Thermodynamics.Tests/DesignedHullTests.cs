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
    [Trait("speed", "slow")]
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

            Assert.Fail( "no row for " + fit);
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
        /// **The pickup a 6.4 MW block gets is not a starved one, which is what `C42` bought.**
        ///
        /// <para>
        /// Watts over the sink conductance is the gradient a buried source is forced to sit at
        /// whatever is hung off the far end. At the coefficient this mod shipped until `C42` that
        /// was 6,400 K on a block rated 689 K, so no radiator count could help and the ladder
        /// saturated at the first panel: **a block with no answer at any radiator count is a
        /// permanent runaway**, which is the thing the mod exists to avoid. The coefficient is the
        /// only dial on the pickup, since the sink-face count is fixed by design, so it moved.
        /// </para>
        ///
        /// <para>
        /// This asserted the starvation while it was true and now asserts it is gone — and it is
        /// the same measurement either way, which is why it is worth keeping rather than deleting.
        /// It failed on the retune with *the shipped pickup forced only 1,024 K, so this test is no
        /// longer describing a starved pickup*, which is a test reporting a fixed defect rather
        /// than a broken one (`E11`).
        /// </para>
        /// </summary>
        [Fact]
        public void TheShippedPickupIsNotStarvedOnABlockThisLarge()
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

            // **Under the rating with room to spare, on a single sink face.** Not under the block's
            // own settled temperature — the drive is not saved, and balance.md says so — but under
            // the gradient that would make saving it impossible in principle.
            float forced = 6400000f / rows[0].SinkWattsPerKelvin;

            Assert.True(forced < 2000f,
                "the shipped pickup forces " + forced.ToString("n0")
                + " K on 6.4 MW through one sink face; above the block's own rating this is a"
                + " block no radiator count can cool, which is what C42 moved the coefficient to"
                + " prevent");

            // And the ladder pays rather than saturating at the first rung, which is what a pickup
            // that is no longer binding looks like from the other end.
            float first = bare - rows[0].SourceKelvin;
            float rest = rows[0].SourceKelvin - rows[rows.Count - 1].SourceKelvin;

            Assert.True(rest > 0f,
                "panels beyond the first took " + rest.ToString("n1") + " K between them, so the"
                + " ladder is still saturating and something upstream of the panels binds");

            output.WriteLine("first panel {0:n1} K, the rest {1:n1} K, forced gradient {2:n0} K",
                first, rest, forced);
        }
    }
}
