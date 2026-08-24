using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Whether the synthetic ship still resembles the real one — **tests of the measuring instrument
    /// rather than of the simulation**. They hold <see cref="Census"/> against the field observations
    /// recorded beside it and fail when the two drift apart; refresh both together when a dump arrives
    /// (`M11`). See stiffness.md, How close the synthetic tests are to a real ship.
    /// </summary>
    public class CensusFidelityTests
    {
        private static ThermalSimulation Hull(int blocks, int cap = 0)
        {
            // Pinned: this compares a hull's stiffness against a field measurement taken at
            // four steps a second, so the rate is part of the measurement rather than a default.
            ThermalSettings settings = new ThermalSettings { Frequency = 4 };
            settings.MaxSubstepsPerBlock = cap;
            settings.MaxSubsteps = 4096;
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", blocks));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.RebuildAll();
            return simulation;
        }

        [Fact]
        public void TheTiersAreAWholePopulation()
        {
            float total = 0f;
            for (int i = 0; i < Census.Tiers.Length; i++)
            {
                Assert.True(Census.Tiers[i].Share > 0f, Census.Tiers[i].Name + " has no population");
                Assert.True(Census.Tiers[i].Mass > 0f, Census.Tiers[i].Name + " is massless");
                total += Census.Tiers[i].Share;
            }

            Assert.Equal(1f, total, 2);
        }

        /// <summary>
        /// The headline property: a census hull must ask a step for about what a real hull asks
        /// it for. This is the assertion that would have caught the original catalogue.
        /// </summary>
        [Fact]
        public void ACensusHullIsAsStiffAsAFieldShip()
        {
            ThermalSimulation simulation = Hull(4000);
            float demand = simulation.Solver.RequiredSubsteps(simulation.Settings.StepSeconds);

            // Generous either side of the observed range: the point is the order of magnitude,
            // and one ship is one ship.
            Assert.True(demand > Census.Field.LeastDemand * 0.6f && demand < Census.Field.MostDemand * 1.6f,
                "the census hull asks for " + demand.ToString("n2")
                + " substeps against a field range of " + Census.Field.LeastDemand
                + " to " + Census.Field.MostDemand
                + "; the harness has drifted away from the ships it is meant to describe");
        }

        /// <summary>
        /// The subtler property, and the one that decides which cap is safe to ship: not how
        /// stiff the ship is, but how the stiffness is <em>distributed</em>. A hull with one very
        /// light block and nothing else in the tail would reproduce the substep count above and
        /// still be useless for choosing a cap, because every cap would touch one block.
        /// </summary>
        /// <remarks>
        /// The shares come from <c>Census.Field</c> rather than being written out again here. They
        /// were written out again here, and that is the whole failure mode this class exists to
        /// prevent one level down: a field observation recorded in two places, one of which is
        /// updated when a dump arrives.
        /// </remarks>
        [Theory]
        [InlineData(8, Census.Field.RaisedAtCap8)]
        [InlineData(4, Census.Field.RaisedAtCap4)]
        [InlineData(2, Census.Field.RaisedAtCap2)]
        [InlineData(1, Census.Field.RaisedAtCap1)]
        public void ACapReachesAboutAsMuchOfTheHullAsItReachesOfAFieldShip(int cap, float fieldShare)
        {
            ThermalSimulation simulation = Hull(4000, cap);
            ThermalSolver.SubstepProfile profile = simulation.Solver.ProfileSubsteps();

            int index = Array.IndexOf(ThermalSolver.SubstepProfile.ProjectedCaps, cap);
            Assert.True(index >= 0, "cap " + cap + " is no longer projected");

            double share = (double)profile.CapNodesFloored[index] / profile.Nodes;

            // Wide, because the census has eight discrete tiers where a ship has a continuum, so
            // blocks bunch at tier boundaries. What is being caught is a harness that has stopped
            // resembling a ship at all, not one that is a few per cent out.
            Assert.True(share > fieldShare * 0.25 && share < fieldShare * 4.0,
                "cap " + cap + " raises " + (100 * share).ToString("n1")
                + "% of the census hull against " + (100 * fieldShare).ToString("n1")
                + "% of the field ship");
        }

        // ---- against the corpus, rather than against two ships ---------------------------------

        /// <summary>
        /// The census hull, measured the way a real workshop ship is measured.
        ///
        /// <para>
        /// <see cref="StiffnessLab"/> asks one question of a built grid — what does its stiffest
        /// block demand of a step, in this world — and asks it of the census hull and of a
        /// blueprint by the same code. A comparison whose two sides are measured by different code
        /// is not a comparison.
        /// </para>
        /// </summary>
        private static StiffnessLab.Row CensusHull()
        {
            return StiffnessLab.Census(4000);
        }

        /// <summary>
        /// The hull lands inside the population it is meant to represent.
        ///
        /// <para>
        /// This is the assertion `Census.Field` could not make. Two ships from two live sessions
        /// can say "a real ship demands about twenty"; they cannot say whether twenty is ordinary,
        /// and the answer turns out to be that it is neither ordinary nor extreme — it is between
        /// the two things real ships do.
        /// </para>
        /// </summary>
        [Fact]
        public void TheCensusHullIsAsStiffAsRealShipsAreInAir()
        {
            StiffnessLab.Row hull = CensusHull();

            Assert.True(hull.Air > Census.Corpus.AirP10 && hull.Air < Census.Corpus.AirMax,
                "the census hull demands " + hull.Air.ToString("n2")
                + " substeps of a quarter-second step in air, against a corpus of "
                + Census.Corpus.Ships.ToString("n0") + " real ships spanning "
                + Census.Corpus.AirP10 + " to " + Census.Corpus.AirMax
                + "; the harness has left the population it is meant to describe");
        }

        /// <summary>
        /// The census hull feels air the way a real hull does — less than most, and inside the
        /// range.
        ///
        /// <para>
        /// <b>This test replaces one that asserted the opposite, on a broken measurement.</b> The
        /// first version divided the hull's air peak by its vacuum peak and got 1.02, which looked
        /// like a hull that does not notice air at all. Those are two different blocks: the air
        /// peak is an exposed fitting and the vacuum peak is a buried heavy block that conducts
        /// hard. Asked of the same block, the census hull is 1.20 to 1.50 against a real median of
        /// 2.34 — low, because its stiffest block has one or two exposed faces where a real ship's
        /// has 5.26, and well inside the population.
        /// </para>
        /// </summary>
        [Fact]
        public void TheCensusHullFeelsAirLikeARealHullDoes()
        {
            StiffnessLab.Row hull = CensusHull();
            Assert.True(hull.StiffestInVacuum > 0f, "the stiffest block has no vacuum demand");

            float ratio = StiffnessLab.Ratio(hull);

            Assert.True(ratio > Census.Corpus.AirRatioP10,
                "the census hull's stiffest block is " + ratio.ToString("n2")
                + " times stiffer in air than out of it, below the tenth percentile of "
                + Census.Corpus.AirRatioP10 + " over " + Census.Corpus.Ships.ToString("n0")
                + " real ships; the hull has stopped responding to the air");

            Assert.True(ratio < Census.Corpus.AirRatioP90,
                "the census hull is now " + ratio.ToString("n2")
                + " times stiffer in air, past the ninetieth percentile of "
                + Census.Corpus.AirRatioP90 + "; it has become one of the extreme hulls rather"
                + " than an ordinary one");
        }

        /// <summary>
        /// The census hull runs far hotter than a real ship, and the arithmetic of that stays put.
        /// Pinned as a **characterisation, not a target**: whether the benchmark hull should be a worst
        /// case or a typical ship is a decision, and this fails if the answer changes without anyone
        /// saying so. See stiffness.md, The census hull is a 96th-percentile ship for heat.
        /// </summary>
        [Fact]
        public void TheCensusHullMakesFarMoreHeatThanARealShip()
        {
            float perBlock = Census.ProducerShare * Census.ProducerWatts;

            Assert.Equal(Census.Corpus.CensusWastePerBlock, perBlock, 0);

            Assert.True(perBlock > Census.Corpus.WastePerBlockP90,
                "the census hull now makes " + perBlock.ToString("n0")
                + " W of waste per block, below the ninetieth percentile of real ships ("
                + Census.Corpus.WastePerBlockP90.ToString("n0")
                + "). If the tiers have been softened toward a typical ship, every temperature"
                + " figure in this repository describes something different and the pages quoting"
                + " them need re-reading.");

            Assert.True(Census.Corpus.WastePerBlockP50 < Census.Corpus.WastePerBlockP90,
                "the recorded population figures are no longer ordered");
        }

        /// <summary>
        /// The two figures a live session reported are ordinary ships, which is what makes them
        /// usable evidence at all.
        ///
        /// <para>
        /// They were the only evidence for a long time. Now they can be placed: 21.35 at the 63rd
        /// percentile of the corpus in air and 31.25 at the 85th. Had either landed in the last
        /// percent, everything built on them would have been built on an outlier.
        /// </para>
        /// </summary>
        [Fact]
        public void TheFieldObservationsAreOrdinaryShips()
        {
            Assert.True(Census.Field.LeastDemand > Census.Corpus.AirP10
                        && Census.Field.LeastDemand < Census.Corpus.AirMax,
                "the quieter field ship at " + Census.Field.LeastDemand
                + " is outside the corpus range " + Census.Corpus.AirP10
                + " to " + Census.Corpus.AirMax);

            Assert.True(Census.Field.MostDemand > Census.Corpus.AirP10
                        && Census.Field.MostDemand < Census.Corpus.AirMax,
                "the stiffer field ship at " + Census.Field.MostDemand
                + " is outside the corpus range " + Census.Corpus.AirP10
                + " to " + Census.Corpus.AirMax);
        }

        /// <summary>
        /// The population's two modes are far enough apart that a single number describes neither.
        ///
        /// <para>
        /// Guards the claim the constants encode rather than the constants themselves: if a
        /// re-measurement ever brings the two modes together, every statement in this repository
        /// about "a typical ship's stiffness" becomes sayable and several of them should be
        /// rewritten.
        /// </para>
        /// </summary>
        [Fact]
        public void TheTwoModesOfTheCorpusAreFarApart()
        {
            Assert.True(Census.Corpus.LitP50 > Census.Corpus.StructuralP50 * 4f,
                "a light-limited ship demands " + Census.Corpus.LitP50 + " against a structure"
                + "-limited ship's " + Census.Corpus.StructuralP50
                + "; the population is no longer two groups and a median now means something");

            Assert.True(Census.Corpus.LitShare > 0.2f && Census.Corpus.LitShare < 0.8f,
                "a light sets the substep count on " + (100f * Census.Corpus.LitShare).ToString("n0")
                + "% of ships, which is no longer a split population");

            // The median sits with the structural mode, so quoting it as "a typical ship" describes
            // the softer half only.
            Assert.True(Census.Corpus.AirP50 < Census.Corpus.LitP50 * 0.5f,
                "the corpus median has moved into the lit mode");
        }

        /// <summary>
        /// The cap curve a single save reported is the curve real ships have, **where it matters**:
        /// agreement is asserted at the caps anyone would ship and divergence below them, because both
        /// are findings. If the top ever parts company, the shipped cap was chosen against a population
        /// it does not describe. See stiffness.md, The cap curve holds where the cap is actually set.
        /// </summary>
        [Fact]
        public void TheFieldCapCurveMatchesTheCorpusWhereTheCapIsActuallySet()
        {
            Assert.True(Census.Corpus.FlooredAtCap8 < Census.Field.RaisedAtCap8 * 2f,
                "a cap of 8 holds back " + (100f * Census.Corpus.FlooredAtCap8).ToString("n2")
                + " % of real blocks against the dump's "
                + (100f * Census.Field.RaisedAtCap8).ToString("n2")
                + " %; the shipped cap was chosen on a curve the population no longer has");

            Assert.True(Census.Corpus.FlooredAtCap4 < Census.Field.RaisedAtCap4 * 2f,
                "a cap of 4 holds back " + (100f * Census.Corpus.FlooredAtCap4).ToString("n2")
                + " % of real blocks against the dump's "
                + (100f * Census.Field.RaisedAtCap4).ToString("n2") + " %");

            // The curve is monotonic in the cap, which is the one thing about it that is arithmetic
            // rather than measurement.
            Assert.True(Census.Corpus.FlooredAtCap1 > Census.Corpus.FlooredAtCap2);
            Assert.True(Census.Corpus.FlooredAtCap2 > Census.Corpus.FlooredAtCap4);
            Assert.True(Census.Corpus.FlooredAtCap4 > Census.Corpus.FlooredAtCap8);
        }

        /// <summary>
        /// The recorded corpus figures are a coherent set of measurements, which is the one check a
        /// transcription can fail on its own: quantiles out of order, a percentile outside 0..100, a
        /// census figure not where the prose beside it says. **It cannot tell a mistyped digit from a
        /// measurement**; it can tell a set no dataset could have produced (`E5`, `D3`).
        /// </summary>
        [Fact]
        public void TheRecordedCorpusFiguresAreInternallyConsistent()
        {
            // Distributions run the way distributions run.
            Assert.True(Census.Corpus.AirP10 < Census.Corpus.AirP50);
            Assert.True(Census.Corpus.AirP50 < Census.Corpus.AirP90);
            Assert.True(Census.Corpus.AirP90 < Census.Corpus.AirMax);

            Assert.True(Census.Corpus.VacuumP50 < Census.Corpus.VacuumP90);
            Assert.True(Census.Corpus.VacuumP90 < Census.Corpus.VacuumMax);

            Assert.True(Census.Corpus.AirRatioP10 < Census.Corpus.AirRatioP50);
            Assert.True(Census.Corpus.AirRatioP50 < Census.Corpus.AirRatioP90);

            // Air makes a ship stiffer, never softer, at every point of the distribution.
            Assert.True(Census.Corpus.AirRatioP10 >= 1f,
                "a ratio below one would mean air made a block softer");
            Assert.True(Census.Corpus.AirP50 > Census.Corpus.VacuumP50,
                "the corpus is no longer stiffer in air than in vacuum at the median");

            // The census hull's own air sensitivity is where the prose beside it says: inside the
            // population and below its median.
            Assert.InRange(Census.Corpus.CensusAirRatio,
                Census.Corpus.AirRatioP10, Census.Corpus.AirRatioP50);

            // The two modes, and the median sitting with the softer one.
            Assert.True(Census.Corpus.StructuralP50 < Census.Corpus.AirP50);
            Assert.True(Census.Corpus.LitP50 > Census.Corpus.AirP50);

            // A real ship's stiffest block is exposed; six faces is all there are.
            Assert.InRange(Census.Corpus.StiffestFacesMean, 1f, 6f);

            // Percentiles are percentiles, and the compound one is above both of its factors
            // because they multiply.
            Assert.InRange(Census.Corpus.ProducerSharePercentile, 0f, 100f);
            Assert.InRange(Census.Corpus.ProducerWattsPercentile, 0f, 100f);
            Assert.InRange(Census.Corpus.WastePerBlockPercentile, 0f, 100f);
            Assert.True(Census.Corpus.WastePerBlockPercentile > Census.Corpus.ProducerSharePercentile);
            Assert.True(Census.Corpus.WastePerBlockPercentile > Census.Corpus.ProducerWattsPercentile);

            // Cap shares fall as the cap rises, and none is a share outside 0..1.
            Assert.InRange(Census.Corpus.FlooredAtCap8, 0f, 1f);
            Assert.InRange(Census.Corpus.FlooredAtCap1, 0f, 1f);

            Assert.True(Census.Corpus.Ships > 1000,
                "only " + Census.Corpus.Ships + " ships are recorded, which is not a population");
        }

        /// <summary>
        /// The tail is what sets the substep count, so a hull that is too small to contain any of
        /// it measures the wrong thing. The picker walks the distribution rather than sampling it
        /// for exactly this reason.
        /// </summary>
        [Fact]
        public void EvenASmallHullCarriesSomeOfTheTail()
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();

            for (int i = 0; i < 200; i++)
            {
                string name = Census.Tiers[Census.TierAt(i)].Name;
                counts[name] = counts.ContainsKey(name) ? counts[name] + 1 : 1;
            }

            Assert.True(counts.ContainsKey("light fitting"),
                "two hundred blocks contained no light fitting, so a small hull would measure a"
                + " ship with no tail and no substep problem");
        }

        [Fact]
        public void ProducersAreTheMeasuredShareAndRatedHotterThanTheStructure()
        {
            ThermalSimulation simulation = Hull(4000);
            int producers = Census.DriveCensus(simulation);

            double share = (double)producers / simulation.Solver.Nodes.Count;
            Assert.True(share > Census.ProducerShare * 0.5 && share < Census.ProducerShare * 1.5,
                producers + " producers of " + simulation.Solver.Nodes.Count
                + " blocks, against a measured share of " + Census.ProducerShare);

            for (int i = 0; i < Census.Tiers.Length; i++)
            {
                Assert.True(Census.ProducerCriticalTemperature >= Census.Tiers[i].CriticalTemperature,
                    "the " + Census.Tiers[i].Name + " tier is rated hotter than the blocks that"
                    + " make the heat, which would make structure the thing that burns first");
            }
        }

        /// <summary>
        /// **The placement rule and the identity agree on a hull built in the order it was laid
        /// out**, which is what lets one of them be replaced by the other.
        ///
        /// <para>
        /// `ProducesHeatAt(index)` decides which *cell* of a hull being laid out gets a producer;
        /// `IsProducer(node)` reads the model the cell actually holds. Every hull in this
        /// repository is built in placement order, so node index equals placement index and the two
        /// answer identically — which is a coincidence of construction, not a property, and it is
        /// exactly the assumption `F22` degrades. Driving a hull now reads the model, and this pins
        /// that the change moved no block on any hull that is built normally.
        /// </para>
        /// </summary>
        [Fact]
        public void TheProducerPlacementRuleAndTheProducerIdentityPickTheSameBlocks()
        {
            ThermalSimulation simulation = Hull(2000);
            IList<ThermalNode> nodes = simulation.Solver.Nodes;

            int disagreements = 0;
            int producers = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                bool byPlacement = Census.ProducesHeatAt(i);
                bool byIdentity = Census.IsProducer(nodes[i]);

                if (byIdentity) producers++;
                if (byPlacement != byIdentity) disagreements++;
            }

            Assert.True(producers > 0, "the hull has no producer at all, so this judges nothing");
            Assert.Equal(0, disagreements);
        }

        /// <summary>
        /// The census reproduces what the field ships did rather than what would be convenient:
        /// they got hot, and they did not burn. If a change to the tiers makes a census ship burn
        /// at its own rated power, the harness has stopped describing the game.
        /// </summary>
        [Fact]
        public void ACensusShipRunsHotWithoutBurning()
        {
            ThermalSimulation simulation = Hull(2000);
            Census.DriveCensus(simulation);

            EnvironmentSample sample = Worlds.Shadow();
            simulation.StepExact(400, sample);

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            float peak = 0f;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Temperature > peak) peak = nodes[i].Temperature;
            }

            Assert.True(peak > 400f, "the census ship barely warmed, peak " + peak.ToString("n0")
                + " K, against " + Census.Field.HottestObserved + " K a field ship reached");
            Assert.True(peak < Census.Field.HottestRating,
                "the census ship reached " + peak.ToString("n0")
                + " K, past the " + Census.Field.HottestRating
                + " K rating that two field sessions never crossed");
        }
    }
}
