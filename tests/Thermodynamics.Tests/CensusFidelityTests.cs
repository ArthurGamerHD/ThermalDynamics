using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Whether the synthetic ship still resembles the real one.
    ///
    /// <para>
    /// Every performance figure this repository publishes is measured on a hull the harness
    /// builds, and for most of the project's life that hull was heavy armour with a grating in
    /// eight. It was wrong in the one way that mattered: a step takes as many substeps as the
    /// <em>stiffest</em> block needs, so the cost of a grid is decided by its lightest block, and
    /// the lightest block in that catalogue was twelve times heavier than a real ship's. The
    /// benchmark asked for three substeps where a field ship asks for twenty-one to thirty-one.
    /// Nobody noticed for as long as there was nothing to compare against.
    /// </para>
    ///
    /// <para>
    /// So these are not tests of the simulation. They are tests of the <em>measuring
    /// instrument</em>: they hold <see cref="Census"/> against the field observations recorded in
    /// <c>Census.Field</c>, and they fail when the two drift apart. When a new dump arrives, edit
    /// the tiers to the new population and <c>Census.Field</c> to what that population was
    /// observed to do; whatever these then say is the honest state of the harness.
    /// </para>
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
        /// **The census hull is stiff for the wrong reason, and that is the finding this class
        /// exists to keep visible.**
        ///
        /// <para>
        /// A real ship's stiffness is mostly what its lightest exposed block exchanges with the
        /// air over its own area: the median hull demands 1.73 times in air what it does in
        /// vacuum, and the ninetieth percentile 5.78 times. The census hull's ratio is 1.02. It
        /// reaches a realistic air figure through conduction alone, so it will not respond to any
        /// change that touches convection, exposure or air density — and those are most of this
        /// mod.
        /// </para>
        ///
        /// <para>
        /// Pinned as *present* rather than fixed, because correcting it moves every performance
        /// figure this repository has published and that is a decision rather than a repair. It
        /// fails when someone fixes it, which is the point: the fix should be noticed.
        /// </para>
        /// </summary>
        [Fact]
        public void TheCensusHullBarelyNoticesAirAndARealShipDoes()
        {
            StiffnessLab.Row hull = CensusHull();
            Assert.True(hull.Vacuum > 0f);

            float ratio = hull.Air / hull.Vacuum;

            Assert.True(ratio < 1.2f,
                "the census hull's air-to-vacuum stiffness ratio is now " + ratio.ToString("n2")
                + ", against 1.02 when this was recorded. If it has risen toward the corpus median"
                + " of " + Census.Corpus.AirRatioP50 + " the hull has been made convectively stiff"
                + " like a real ship, which is a fix — retake the benchmark baseline and rewrite"
                + " this test as the assertion that it stays that way");
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
