using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The step budget bounds what one solver step may cost. It used to count link visits alone,
    /// which could not see the environment pass a substep runs once per node — so two grids with
    /// the same links and different node counts were granted the same allowance for different
    /// work, and a sparsely linked grid was under-charged worst of all.
    ///
    /// It now counts element visits: links, plus nodes weighted by
    /// <see cref="ThermalSettings.NodeCostInLinks"/>, measured in
    /// [benchmarks.md](../../docs/benchmarks.md#what-a-substep-costs).
    /// </summary>
    public class StepBudgetTests
    {
        private static ThermalSimulation Grid(int blocks, int budget)
        {
            ThermalSettings settings = new ThermalSettings
            {
                Frequency = 4,
                MaxSubsteps = 4096,
                MaxSubstepsPerBlock = 0,
                MaxElementVisitsPerStep = budget,
            };
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(blocks, 1, 1));
            return builder.BuildSimulation(settings);
        }

        [Fact]
        public void ASubstepIsChargedForItsNodesAsWellAsItsLinks()
        {
            ThermalSimulation simulation = Grid(20, 1000000);

            int nodes = simulation.Solver.Nodes.Count;
            int links = simulation.Solver.LinkCount;

            Assert.Equal(20, nodes);
            Assert.Equal(19, links);

            // A stick is the shape the old count treated most generously: barely one link a node,
            // so nearly half of what a substep does was invisible to the budget.
            Assert.Equal(links + (ThermalSettings.NodeCostInLinks * nodes), simulation.SubstepCost);
            Assert.True(simulation.SubstepCost > links * 4);
        }

        [Fact]
        public void TheBudgetIsTheAllowanceDividedByWhatOneSubstepCosts()
        {
            ThermalSimulation simulation = Grid(20, 1000);

            long expected = 1000 / simulation.SubstepCost;
            if (expected < 1) expected = 1;

            Assert.Equal((int)expected, simulation.SubstepBudget);
        }

        [Fact]
        public void ZeroRemovesTheBoundEntirely()
        {
            Assert.Equal(int.MaxValue, Grid(20, 0).SubstepBudget);
        }

        /// <summary>
        /// A grid that cannot afford one pass over its elements still gets one. A step granting
        /// zero substeps would not advance at all, and a grid frozen by its own size is a worse
        /// failure than a grid running slowly.
        /// </summary>
        [Fact]
        public void AGridTooLargeForItsAllowanceStillTakesOneSubstep()
        {
            ThermalSimulation simulation = Grid(200, 1);

            Assert.Equal(1, simulation.SubstepBudget);
            Assert.True(simulation.AffordableStepSeconds(0.25f) > 0f);
        }

        /// <summary>
        /// The shortening rule: a step too stiff for its allowance advances less simulated time at
        /// the same accuracy, rather than taking a coarser step and leaning on the overshoot
        /// clamps.
        /// </summary>
        [Fact]
        public void AStepBeyondTheBudgetIsShortenedRatherThanCoarsened()
        {
            ThermalSimulation simulation = Grid(200, 1000);

            float full = 0.25f;
            float affordable = simulation.AffordableStepSeconds(full);

            Assert.True(affordable > 0f);
            Assert.True(affordable <= full);
        }

        [Fact]
        public void AStepWithinTheBudgetIsNotShortened()
        {
            ThermalSimulation simulation = Grid(20, 100000000);

            Assert.Equal(0.25f, simulation.AffordableStepSeconds(0.25f));
        }

        /// <summary>
        /// The defect this closes, stated as a comparison the old count could not make.
        ///
        /// Two grids with the same number of links: one a chain of blocks, the other a compact
        /// slab. The chain has far more nodes per link, so a substep over it does more work — and
        /// under a link-only budget both were granted exactly the same number of substeps.
        /// </summary>
        [Fact]
        public void TwoGridsWithEqualLinksAreChargedByTheirNodeCounts()
        {
            ThermalSettings settings = new ThermalSettings
            {
                Frequency = 4,
                MaxSubsteps = 4096,
                MaxElementVisitsPerStep = 1000000,
            };
            settings.Derive();

            GridBuilder chainBuilder = GridBuilder.Large();
            chainBuilder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(41, 1, 1));
            ThermalSimulation chain = chainBuilder.BuildSimulation(settings);

            GridBuilder slabBuilder = GridBuilder.Large();
            slabBuilder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(5, 5, 1));
            ThermalSimulation slab = slabBuilder.BuildSimulation(settings);

            Assert.Equal(40, chain.Solver.LinkCount);
            Assert.Equal(40, slab.Solver.LinkCount);

            // Same links, different node counts, so different cost — which is the whole point.
            Assert.True(chain.Solver.Nodes.Count > slab.Solver.Nodes.Count);
            Assert.True(chain.SubstepCost > slab.SubstepCost);

            // And the budget follows the cost rather than the link count.
            Assert.True(chain.SubstepBudget < slab.SubstepBudget);
        }
    }
}
