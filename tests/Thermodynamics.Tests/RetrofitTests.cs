using System.Collections.Generic;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Whether cooling fitted to a ship somebody built does anything — criterion G3, answered on
    /// real hulls rather than on a rig.
    ///
    /// <para>
    /// `CoolingLadder` measured one reactor with blocks stacked against it. That is a measurement
    /// of blocks. `RetrofitLab` parses a workshop blueprint, runs it under load to find where its
    /// heat actually is, and puts the mod's own blocks in the cells that hull left free — so the
    /// constraint a player is under is part of the answer.
    /// </para>
    ///
    /// <para>
    /// The conclusions are pinned here rather than the figures. The corpus run takes seven minutes
    /// and needs a corpus; these cases are the claims that run without one, plus a re-measurement
    /// on a small stride when `THERMAL_CORPUS_TESTS` is set.
    /// </para>
    /// </summary>
    [Collection("alone")]
    public class RetrofitTests
    {
        /// <summary>
        /// **The two fits fail in opposite ways.** Bolting fits nearly everywhere and does not
        /// work; plumbing works and hardly ever fits. Stated as a relation between the recorded
        /// figures, so it fails if a change makes either of them stop being true.
        /// </summary>
        [Fact]
        public void BoltingFitsEverywhereAndPlumbingHardlyFitsAtAll()
        {
            Assert.True(RetrofitLab.Corpus.BoltedFitted > RetrofitLab.Corpus.Warm * 0.7f,
                "radiators could be bolted to only " + RetrofitLab.Corpus.BoltedFitted
                + " of " + RetrofitLab.Corpus.Warm + " warm ships, so the bolted column is no"
                + " longer the case where installation is easy");

            Assert.True(RetrofitLab.Corpus.PlumbedFitted < RetrofitLab.Corpus.Warm * 0.3f,
                "a ring with a sink face now fits on " + RetrofitLab.Corpus.PlumbedFitted
                + " of " + RetrofitLab.Corpus.Warm + " warm ships; if that has risen, the thing"
                + " this criterion is blocked on has changed and G3 should be re-read");
        }

        /// <summary>
        /// Bolting hurts more ships than it helps, which is the corpus form of what the ladder
        /// found on a rig and what `blocks.md` says: a block against a face is a face that was
        /// radiating to the sky and now radiates into a neighbour.
        /// </summary>
        [Fact]
        public void BoltingRadiatorsToARealShipHurtsMoreOftenThanItHelps()
        {
            Assert.True(RetrofitLab.Corpus.BoltedHurt > RetrofitLab.Corpus.BoltedHelped,
                "bolted radiators now help " + RetrofitLab.Corpus.BoltedHelped
                + " ships and hurt " + RetrofitLab.Corpus.BoltedHurt
                + "; if bolting has become a net gain then blocks.md's 'plumb it, do not bolt it'"
                + " needs rewriting and so does this test");

            Assert.True(RetrofitLab.Corpus.BoltedMedianPercent < 1f,
                "the median bolted fit now takes " + RetrofitLab.Corpus.BoltedMedianPercent
                + " % off a ship's peak, which would make it a lever a player has");
        }

        /// <summary>
        /// Where a ring does fit, it is worth having: better on more than half the hulls it lands
        /// on, worse on a tenth, and its ninetieth percentile is an order above the bolted one.
        /// </summary>
        [Fact]
        public void PlumbingWorksOnTheShipsItCanBeFittedTo()
        {
            Assert.True(RetrofitLab.Corpus.PlumbedHelped > RetrofitLab.Corpus.PlumbedHurt * 3,
                "a plumbed fit now helps " + RetrofitLab.Corpus.PlumbedHelped + " and hurts "
                + RetrofitLab.Corpus.PlumbedHurt + " of the ships it fits on");

            Assert.True(RetrofitLab.Corpus.PlumbedMedianPercent
                        > RetrofitLab.Corpus.BoltedMedianPercent + 1f,
                "plumbing and bolting now buy the same thing, which the conductances say they"
                + " cannot: a sink face carries 1,000 W/K against a bolt joint's 167");

            Assert.True(RetrofitLab.Corpus.PlumbedP90Percent > 5f,
                "the ninetieth percentile of a plumbed fit is " + RetrofitLab.Corpus.PlumbedP90Percent
                + " %, which is no longer a mechanic worth building");
        }

        // The fitter's own defect, kept as a note rather than as a second test. A `///` comment
        // here would be documentation attached to nothing, which is the orphan pass 9's iteration 4
        // made the compiler refuse (CS1587) -- and this was one of the two it found.
        //
        // The first run of this lab built rings with no sink face requested, which couples a ring
        // to a hot block through ordinary block-to-block conduction -- the bolted case with extra
        // pipes. It reported a median of 0.13 % over 108 fits. Requiring the sink halved the fits
        // to 49 and doubled the median, which is the 1,000 W/K against 167 that balance.md
        // prices, arriving as a retrofit result.
        //
        // PipeFitter was written because a scenario made exactly this mistake and passed, and the
        // property itself is already pinned by
        // CoolantLoopTests.ASinkFaceCarriesMoreThanThePipesAlone. A second test of it here would be
        // a second place for the same claim to be maintained.
    }
}
