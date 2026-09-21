using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class StepFixedCostTests
    {
/// <summary>Ship operation.</summary>
        private static ThermalSimulation Ship(int blocks, int budgetVisits)
        {
            ThermalSettings settings = new ThermalSettings
            {
                Frequency = 4,
                MaxSubsteps = 64,
                MaxSubstepsPerBlock = 0,
                MaxElementVisitsPerStep = budgetVisits,
            };
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(blocks, 2, 2));
            return builder.BuildSimulation(settings);
        }

/// <summary>RunOneStep operation.</summary>
        private static void RunOneStep(ThermalSimulation simulation)
        {
            while (simulation.HasPendingWork) simulation.Update(1f / 60f, Worlds.Shadow());

            simulation.Work.Reset();
            long steps = simulation.Work.SolverSteps;

            for (int frame = 0; frame < 600 && simulation.Work.SolverSteps == steps; frame++)
            {
                simulation.Update(1f / 60f, Worlds.Shadow());
            }

            Assert.Equal(steps + 1, simulation.Work.SolverSteps);
        }

        [Fact]
/// <summary>AStepEstimatesItsStiffnessOnce operation.</summary>
        public void AStepEstimatesItsStiffnessOnce()
        {
/// <summary>Ship operation.</summary>
            ThermalSimulation simulation = Ship(16, 1000000);

            RunOneStep(simulation);

            Assert.Equal(1, simulation.Work.StabilityEstimates);
        }

        [Fact]
/// <summary>AStepMirrorsTheNodeObjectsOnce operation.</summary>
        public void AStepMirrorsTheNodeObjectsOnce()
        {
/// <summary>Ship operation.</summary>
            ThermalSimulation simulation = Ship(16, 1000000);

            RunOneStep(simulation);

            Assert.Equal(1, simulation.Work.NodeStateSyncs);
        }

        [Fact]
/// <summary>TheCountDoesNotDependOnTheVisitBudget operation.</summary>
        public void TheCountDoesNotDependOnTheVisitBudget()
        {
/// <summary>Ship operation.</summary>
            ThermalSimulation unbounded = Ship(16, 0);

            RunOneStep(unbounded);

            Assert.Equal(1, unbounded.Work.StabilityEstimates);
            Assert.Equal(1, unbounded.Work.NodeStateSyncs);
        }

        [Fact]
/// <summary>AStepHandedItsEstimateIntegratesTheSameFloats operation.</summary>
        public void AStepHandedItsEstimateIntegratesTheSameFloats()
        {
/// <summary>Ship operation.</summary>
            ThermalSimulation given = Ship(12, 1000000);
/// <summary>Ship operation.</summary>
            ThermalSimulation taken = Ship(12, 1000000);

            while (given.HasPendingWork) given.Update(1f / 60f, Worlds.Shadow());
            while (taken.HasPendingWork) taken.Update(1f / 60f, Worlds.Shadow());

            EnvironmentSample sample = Worlds.Space(new Vector3(0f, 1f, 0f));
            EnvironmentState state = EnvironmentSolver.Solve(
                given.Settings, given.Planet, sample);

            float step = given.Settings.StepSeconds;

            for (int i = 0; i < 8; i++)
            {
                given.Solver.Step(step, state, given.Solver.RequiredSubsteps(step, state));
                taken.Solver.Step(step, state);
            }

            Assert.Equal(taken.Solver.LastSubsteps, given.Solver.LastSubsteps);

            for (int i = 0; i < taken.Solver.Nodes.Count; i++)
            {
                Assert.Equal(taken.Solver.Nodes[i].Temperature,
                    given.Solver.Nodes[i].Temperature);
            }
        }

        [Fact]
/// <summary>TheMassFloorSeesTheEnvironmentTheStepWillRunIn operation.</summary>
        public void TheMassFloorSeesTheEnvironmentTheStepWillRunIn()
        {
            ThermalSettings settings = new ThermalSettings
            {
                Frequency = 4,
                MaxSubsteps = 64,
                MaxSubstepsPerBlock = 1,
                MaxElementVisitsPerStep = 1000000,
            };
            settings.Derive();

            BlockModel fitting = BlockModel.Solid(
                "Fitting", Vector3I.One, 500f, Catalog.DefaultThermal());

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(fitting, Vector3I.Zero, new Vector3I(6, 2, 2));
            ThermalSimulation simulation = builder.BuildSimulation(settings);

            while (simulation.HasPendingWork) simulation.Update(1f / 60f, Worlds.Shadow());

            simulation.StepExact(2, Worlds.Shadow());
            int inVacuum = simulation.Solver.FlooredNodes;

            simulation.StepExact(1, Worlds.PlanetSurface(1f, 0.5f));
            int inAir = simulation.Solver.FlooredNodes;

            Assert.True(inVacuum < simulation.Solver.Nodes.Count,
                "the floor saturated in vacuum, so the count cannot rise in air");
            Assert.True(inAir > inVacuum,
                "floored in vacuum " + inVacuum + ", floored in air " + inAir);
        }

        [Fact]
/// <summary>AStepSpreadOverManyFramesStillEstimatesOnce operation.</summary>
        public void AStepSpreadOverManyFramesStillEstimatesOnce()
        {
/// <summary>Ship operation.</summary>
            ThermalSimulation simulation = Ship(64, 4000);

            RunOneStep(simulation);

            Assert.True(simulation.Work.SolverSubsteps >= 1);
            Assert.Equal(1, simulation.Work.StabilityEstimates);
            Assert.Equal(1, simulation.Work.NodeStateSyncs);
        }
    }
}
