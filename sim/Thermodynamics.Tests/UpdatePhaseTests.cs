using System.Collections.Generic;
using Thermodynamics.Core;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Which frame of the ten-frame cycle a grid works on.
    ///
    /// The field run this was written for measured a 203-grid world doing all of its thermal work
    /// on one frame in ten: 1,392 of 13,915 frames did anything, and those averaged 117 ms with
    /// two in three over a 60 fps frame. The total was the same either way — what differed was
    /// whether it arrived in a lump.
    ///
    /// The property that matters is not "phases have equal counts". It is that the heaviest frame
    /// is close to the average one, measured in blocks, because grids differ in size by three
    /// orders of magnitude and a balance by count would put a capital ship and two hundred
    /// fighters on the same frame.
    /// </summary>
    public class UpdatePhaseTests
    {
        [Fact]
        public void OneGridTakesThefirstPhaseAndCarriesItsBlocks()
        {
            UpdatePhases phases = new UpdatePhases();

            int phase = phases.Claim(500);

            Assert.Equal(0, phase);
            Assert.Equal(500, phases.LoadOf(0));
            Assert.Equal(1, phases.MembersOf(0));
            Assert.Equal(500, phases.TotalLoad);
        }

        /// <summary>
        /// Equal grids must land one per phase before any phase takes a second — the check that
        /// the emptiest phase is really being chosen.
        /// </summary>
        [Fact]
        public void EqualGridsFillEveryPhaseBeforeDoublingUp()
        {
            UpdatePhases phases = new UpdatePhases();

            for (int i = 0; i < UpdatePhases.Count; i++)
            {
                Assert.Equal(i, phases.Claim(100));
            }

            for (int i = 0; i < UpdatePhases.Count; i++)
            {
                Assert.Equal(1, phases.MembersOf(i));
                Assert.Equal(100, phases.LoadOf(i));
            }

            // The eleventh goes back to the front.
            Assert.Equal(0, phases.Claim(100));
        }

        /// <summary>
        /// The shape of the world that motivated this: one capital ship and two hundred small
        /// craft. Balancing by count would put the fighters evenly across the phases and leave
        /// the capital's frame carrying it plus its share of them.
        /// </summary>
        [Fact]
        public void ALargeGridDoesNotAlsoCollectAShareOfTheSmallOnes()
        {
            UpdatePhases phases = new UpdatePhases();

            int capital = phases.Claim(42051);
            for (int i = 0; i < 200; i++) phases.Claim(400);

            // Everything else went elsewhere until the rest caught up with the capital.
            Assert.Equal(42051, phases.LoadOf(capital));

            // And the other phases carry the fighters between them, evenly.
            for (int i = 0; i < UpdatePhases.Count; i++)
            {
                if (i == capital) continue;
                Assert.True(phases.LoadOf(i) <= 42051,
                    "phase " + i + " carries " + phases.LoadOf(i) + ", more than the capital ship");
            }
        }

        /// <summary>
        /// The measurement the whole change is for: the heaviest frame against the average one.
        /// Ten is everything on a single frame, which is what the engine's arrangement produced.
        ///
        /// The bar is 1.4 rather than 1.0 because the decision is online — a grid has to be given
        /// a frame when it appears, without knowing what will be built later — and putting each
        /// arrival on the emptiest frame is bounded at about 4/3 of the best arrangement possible
        /// in hindsight. This mix lands on 1.25, which is essentially that bound. Getting nearer
        /// to 1 would mean moving grids between frames after the fact, and a grid that changes
        /// phase either skips a tick or takes two close together.
        /// </summary>
        [Fact]
        public void ManySimilarGridsSpreadNearlyEvenly()
        {
            UpdatePhases phases = new UpdatePhases();

            // Roughly the field world: two hundred grids, most small, a few large.
            for (int i = 0; i < 200; i++)
            {
                phases.Claim(i % 25 == 0 ? 4000 : 300);
            }

            Assert.True(phases.Imbalance < 1.4d,
                "heaviest phase is " + phases.Imbalance.ToString("n2")
                + "x the average; everything on one frame is " + UpdatePhases.Count + "x");
        }

        /// <summary>
        /// A single grid larger than everything else sets a floor no arrangement can beat, because
        /// a grid cannot be split across frames. The imbalance figure has to say so rather than
        /// pretending, or it would read as a scheduling failure when it is a fact about the world.
        /// </summary>
        [Fact]
        public void OneDominantGridSetsAFloorTheSpreadCannotBeat()
        {
            UpdatePhases phases = new UpdatePhases();

            phases.Claim(100000);
            for (int i = 0; i < 50; i++) phases.Claim(100);

            Assert.True(phases.Imbalance > 5d,
                "one grid holding nearly all the blocks cannot be spread, and the figure should "
                + "report that honestly; got " + phases.Imbalance.ToString("n2"));
        }

        [Fact]
        public void ReleasingAGridGivesItsWeightBack()
        {
            UpdatePhases phases = new UpdatePhases();

            int a = phases.Claim(1000);
            int b = phases.Claim(2000);

            phases.Release(a, 1000);

            Assert.Equal(0, phases.LoadOf(a));
            Assert.Equal(0, phases.MembersOf(a));
            Assert.Equal(2000, phases.LoadOf(b));
            Assert.Equal(2000, phases.TotalLoad);
        }

        /// <summary>
        /// A projector's output goes from one block to forty thousand without re-registering. If
        /// the balance never noticed, it would reflect what every grid looked like when it first
        /// appeared — which for a ship being built is nothing at all.
        /// </summary>
        [Fact]
        public void AGridThatGrowsIsReweighed()
        {
            UpdatePhases phases = new UpdatePhases();

            int growing = phases.Claim(1);
            phases.Reweigh(growing, 1, 40000);

            Assert.Equal(40000, phases.LoadOf(growing));

            // And the next grid avoids it, which it would not have done before the reweigh.
            Assert.NotEqual(growing, phases.Claim(100));
        }

        [Fact]
        public void AGridGroundDownIsReweighedToo()
        {
            UpdatePhases phases = new UpdatePhases();

            int shrinking = phases.Claim(40000);
            phases.Reweigh(shrinking, 40000, 12);

            Assert.Equal(12, phases.LoadOf(shrinking));
        }

        /// <summary>
        /// Loads must never go negative, whatever order the bookkeeping arrives in — a grid closing
        /// after its blocks have already been counted away, a release for a weight that was never
        /// claimed. A negative load would make its phase permanently the emptiest and collect
        /// every grid built afterwards.
        /// </summary>
        [Fact]
        public void LoadsNeverGoNegativeHoweverTheBookkeepingArrives()
        {
            UpdatePhases phases = new UpdatePhases();

            int phase = phases.Claim(100);
            phases.Release(phase, 5000);
            phases.Reweigh(phase, 5000, 0);
            phases.Release(phase, 100);

            for (int i = 0; i < UpdatePhases.Count; i++)
            {
                Assert.True(phases.LoadOf(i) >= 0, "phase " + i + " went negative");
                Assert.True(phases.MembersOf(i) >= 0, "phase " + i + " has negative members");
            }
        }

        /// <summary>
        /// Out-of-range phases are ignored rather than throwing. A grid that never claimed one
        /// carries -1, and it must be able to go through close without taking the session with it
        /// — IndexOutOfRangeException is not on the game's whitelist, so it would not be catchable.
        /// </summary>
        [Fact]
        public void AnUnclaimedPhaseIsHarmless()
        {
            UpdatePhases phases = new UpdatePhases();

            phases.Release(-1, 500);
            phases.Reweigh(-1, 0, 500);
            phases.Release(UpdatePhases.Count, 500);
            phases.Reweigh(UpdatePhases.Count + 3, 0, 500);

            Assert.Equal(0, phases.TotalLoad);
            Assert.Equal(0, phases.LoadOf(-1));
            Assert.Equal(0, phases.MembersOf(UpdatePhases.Count));
        }

        /// <summary>
        /// Every grid must end up on exactly one phase, so the cycle covers all of them once and
        /// none of them twice. This is the property a stagger exists to provide, and it is easy to
        /// lose while making the balance cleverer.
        /// </summary>
        [Fact]
        public void EveryGridLandsOnExactlyOnePhase()
        {
            UpdatePhases phases = new UpdatePhases();

            List<int> assigned = new List<int>();
            for (int i = 0; i < 203; i++)
            {
                assigned.Add(phases.Claim(100 + (i * 37) % 900));
            }

            int[] counted = new int[UpdatePhases.Count];
            for (int i = 0; i < assigned.Count; i++)
            {
                Assert.InRange(assigned[i], 0, UpdatePhases.Count - 1);
                counted[assigned[i]]++;
            }

            int total = 0;
            for (int i = 0; i < UpdatePhases.Count; i++)
            {
                Assert.Equal(counted[i], phases.MembersOf(i));
                total += counted[i];
            }

            Assert.Equal(assigned.Count, total);
        }
    }
}
