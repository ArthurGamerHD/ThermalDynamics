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
            ThermalSettings settings = new ThermalSettings();
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
        [Theory]
        [InlineData(8, 0.0116f)]
        [InlineData(4, 0.0601f)]
        [InlineData(2, 0.2368f)]
        [InlineData(1, 0.3910f)]
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

            Assert.True(peak > 400f, "the census ship barely warmed, peak " + peak.ToString("n0") + " K");
            Assert.True(peak < Census.Field.HottestRating,
                "the census ship reached " + peak.ToString("n0")
                + " K, past the " + Census.Field.HottestRating
                + " K rating that two field sessions never crossed");
        }
    }
}
