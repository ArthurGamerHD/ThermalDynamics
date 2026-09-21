using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    public class StepWorkUnitTests
    {
        private const int Blocks = 2000;

/// <summary>Hull operation.</summary>
        private static WorstCases.Built Hull()
        {
            return WorstCases.Hull("ship", Blocks);
        }

        [Fact]
/// <summary>TheRecordedSubstepCostIsTheOneTheAllowanceIsDividedBy operation.</summary>
        public void TheRecordedSubstepCostIsTheOneTheAllowanceIsDividedBy()
        {
/// <summary>Hull operation.</summary>
            WorstCases.Built built = Hull();
            ThermalSimulation simulation = built.Simulation;

            Assert.Equal(simulation.Solver.LinkCount
                + (ThermalSettings.NodeCostInLinks * simulation.Solver.Nodes.Count),
                simulation.SubstepCost);
        }

        [Fact]
/// <summary>ScoringAStepWithTheJointCountUnderstatesItByTheLinkHalf operation.</summary>
        public void ScoringAStepWithTheJointCountUnderstatesItByTheLinkHalf()
        {
/// <summary>Hull operation.</summary>
            WorstCases.Built built = Hull();

/// <summary>ShipAssembly operation.</summary>
            ShipAssembly assembly = new ShipAssembly();
            assembly.Simulations.Add(built.Simulation);

            Assert.Empty(assembly.Bridges);
            Assert.True(assembly.LinkCount > 0, "a census hull of touching blocks has no links");

            long honest = assembly.WorstGridSubstepCost;
            long asScored = ThermalSettings.NodeCostInLinks * assembly.NodeCount;

            Assert.True(honest > asScored,
                "the link term is nought, so nothing was being left out and this test is not"
                + " measuring what it says it is");

            double links = assembly.LinkCount / (double)assembly.NodeCount;
            Assert.InRange(links, 0.5, 3.5);

            double understated = honest / (double)asScored;
            Assert.InRange(understated, 1.1, 1.9);
        }

        [Fact]
/// <summary>TheCostIsTheWorstGridRatherThanTheSumOfThem operation.</summary>
        public void TheCostIsTheWorstGridRatherThanTheSumOfThem()
        {
/// <summary>Hull operation.</summary>
            WorstCases.Built one = Hull();
/// <summary>Hull operation.</summary>
            WorstCases.Built two = Hull();

/// <summary>ShipAssembly operation.</summary>
            ShipAssembly assembly = new ShipAssembly();
            assembly.Simulations.Add(one.Simulation);
            assembly.Simulations.Add(two.Simulation);

            Assert.Equal(one.Simulation.SubstepCost, assembly.WorstGridSubstepCost);
            Assert.Equal(2 * one.Simulation.Solver.LinkCount, assembly.LinkCount);
        }

        [Fact]
/// <summary>AnOutcomeCarriesTheLinkCountAndTheCostItImplies operation.</summary>
        public void AnOutcomeCarriesTheLinkCountAndTheCostItImplies()
        {
/// <summary>Hull operation.</summary>
            WorstCases.Built built = Hull();

/// <summary>ShipAssembly operation.</summary>
            ShipAssembly assembly = new ShipAssembly();
            assembly.Simulations.Add(built.Simulation);

            ScenarioOutcome outcome = ScenarioOutcome.Read(assembly, "hull", "bench");

            Assert.Equal(assembly.LinkCount, outcome.Links);
            Assert.Equal(assembly.WorstGridSubstepCost, outcome.SubstepCost);

/// <summary>List operation.</summary>
            List<string> header = new List<string>(CorpusRecord.OutcomeHeader.Split(','));
            Assert.Contains("links", header);
            Assert.Contains("substep_cost", header);

            string[] cells = CorpusRecord.Row("bench", outcome).Split(',');
            Assert.Equal(header.Count, cells.Length);
            Assert.Equal(outcome.Links.ToString(), cells[header.IndexOf("links")]);
            Assert.Equal(outcome.SubstepCost.ToString(), cells[header.IndexOf("substep_cost")]);
        }
    }
}
