using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class StepBudgetTests
    {
/// <summary>Grid operation.</summary>
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
/// <summary>ASubstepIsChargedForItsNodesAsWellAsItsLinks operation.</summary>
        public void ASubstepIsChargedForItsNodesAsWellAsItsLinks()
        {
/// <summary>Grid operation.</summary>
            ThermalSimulation simulation = Grid(20, 1000000);

            int nodes = simulation.Solver.Nodes.Count;
            int links = simulation.Solver.LinkCount;

            Assert.Equal(20, nodes);
            Assert.Equal(19, links);

            Assert.Equal(links + (ThermalSettings.NodeCostInLinks * nodes), simulation.SubstepCost);
            Assert.True(simulation.SubstepCost > links * 4);
        }

        [Fact]
/// <summary>TheBudgetIsTheAllowanceDividedByWhatOneSubstepCosts operation.</summary>
        public void TheBudgetIsTheAllowanceDividedByWhatOneSubstepCosts()
        {
/// <summary>Grid operation.</summary>
            ThermalSimulation simulation = Grid(20, 1000);

            long expected = 1000 / simulation.SubstepCost;
            if (expected < 1) expected = 1;

            Assert.Equal((int)expected, simulation.SubstepBudget);
        }

        [Fact]
/// <summary>ZeroRemovesTheBoundEntirely operation.</summary>
        public void ZeroRemovesTheBoundEntirely()
        {
            Assert.Equal(int.MaxValue, Grid(20, 0).SubstepBudget);
        }

        [Fact]
/// <summary>AGridTooLargeForItsAllowanceStillTakesOneSubstep operation.</summary>
        public void AGridTooLargeForItsAllowanceStillTakesOneSubstep()
        {
/// <summary>Grid operation.</summary>
            ThermalSimulation simulation = Grid(200, 1);

            Assert.Equal(1, simulation.SubstepBudget);
            Assert.True(simulation.AffordableStepSeconds(0.25f) > 0f);
        }

        [Fact]
/// <summary>AStepBeyondTheBudgetIsShortenedRatherThanCoarsened operation.</summary>
        public void AStepBeyondTheBudgetIsShortenedRatherThanCoarsened()
        {
/// <summary>Grid operation.</summary>
            ThermalSimulation simulation = Grid(200, 1000);

            float full = 0.25f;
            float affordable = simulation.AffordableStepSeconds(full);

            Assert.True(affordable > 0f);
            Assert.True(affordable <= full);
        }

        [Fact]
/// <summary>AStepWithinTheBudgetIsNotShortened operation.</summary>
        public void AStepWithinTheBudgetIsNotShortened()
        {
/// <summary>Grid operation.</summary>
            ThermalSimulation simulation = Grid(20, 100000000);

            Assert.Equal(0.25f, simulation.AffordableStepSeconds(0.25f));
        }

        [Fact]
/// <summary>TwoGridsWithEqualLinksAreChargedByTheirNodeCounts operation.</summary>
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

            Assert.True(chain.Solver.Nodes.Count > slab.Solver.Nodes.Count);
            Assert.True(chain.SubstepCost > slab.SubstepCost);

            Assert.True(chain.SubstepBudget < slab.SubstepBudget);
        }
    }
}
