using System.Collections.Generic;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    [Collection("alone")]
    public class RetrofitTests
    {
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

    }
}
