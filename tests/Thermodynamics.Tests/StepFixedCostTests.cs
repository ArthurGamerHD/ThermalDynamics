using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// A step's fixed cost — the passes over every node that run once per step rather than once
    /// per substep.
    ///
    /// There are two of them: mirroring the node objects into the flat rows, and the stability
    /// estimate that decides how many substeps the step gets. Both are O(nodes) and neither does
    /// any physics, so a step that runs either twice pays a full node walk for an answer it
    /// already holds. On a grid taking three substeps — the field average — the fixed part is
    /// roughly half the step, which is what makes the count worth pinning.
    /// </summary>
    public class StepFixedCostTests
    {
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

        /// <summary>Runs frames until exactly one solver step has completed.</summary>
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

        /// <summary>
        /// The estimate walks every node cubing a temperature. It answers two questions — how long
        /// a step this grid can afford, and how many substeps that step gets — and the second is
        /// the first scaled by the ratio of the two step lengths, so one walk serves both.
        /// </summary>
        [Fact]
        public void AStepEstimatesItsStiffnessOnce()
        {
            ThermalSimulation simulation = Ship(16, 1000000);

            RunOneStep(simulation);

            Assert.Equal(1, simulation.Work.StabilityEstimates);
        }

        /// <summary>
        /// Mirroring the node objects into the flat rows is the one pass that dereferences every
        /// <see cref="ThermalNode"/> on the grid, so it is the step's coldest walk.
        /// </summary>
        [Fact]
        public void AStepMirrorsTheNodeObjectsOnce()
        {
            ThermalSimulation simulation = Ship(16, 1000000);

            RunOneStep(simulation);

            Assert.Equal(1, simulation.Work.NodeStateSyncs);
        }

        /// <summary>
        /// The visit budget is what makes the estimate load-bearing rather than incidental: it is
        /// the figure the affordable step length is derived from. Switching the budget off must not
        /// change how many times the estimate runs, only whether its answer shortens the step.
        /// </summary>
        [Fact]
        public void TheCountDoesNotDependOnTheVisitBudget()
        {
            ThermalSimulation unbounded = Ship(16, 0);

            RunOneStep(unbounded);

            Assert.Equal(1, unbounded.Work.StabilityEstimates);
            Assert.Equal(1, unbounded.Work.NodeStateSyncs);
        }

        /// <summary>
        /// The saving is only a saving if the answer does not move. A step handed the caller's
        /// estimate must integrate exactly what a step that took its own estimate would, down to
        /// the last float — the two differ in how many times a number was computed, not in what
        /// the number is.
        /// </summary>
        [Fact]
        public void AStepHandedItsEstimateIntegratesTheSameFloats()
        {
            ThermalSimulation given = Ship(12, 1000000);
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

        /// <summary>
        /// The mass floor and the stability estimate are the same test, and both read the
        /// environment's convection coefficient. They used to read different samples: the floor
        /// ran before <c>Environment</c> was assigned and so capped against the previous step's
        /// air, while the estimate ran after it. A grid entering atmosphere floored the wrong
        /// count for one step, and a grid whose air keeps changing floored the wrong count on
        /// every step.
        /// </summary>
        [Fact]
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

            // A three-hundred-kilogram fitting rather than armour, chosen so the floor is
            // partial: heavier and nothing floors in either environment, lighter and everything
            // floors in both, and a count that saturates cannot see which sample the floor read.
            BlockModel fitting = BlockModel.Solid(
                "Fitting", Vector3I.One, 300f, Catalog.DefaultThermal());

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(fitting, Vector3I.Zero, new Vector3I(6, 2, 2));
            ThermalSimulation simulation = builder.BuildSimulation(settings);

            while (simulation.HasPendingWork) simulation.Update(1f / 60f, Worlds.Shadow());

            simulation.StepExact(2, Worlds.Shadow());
            int inVacuum = simulation.Solver.FlooredNodes;

            // Dense air raises the convection every exposed node sees, which raises the rate the
            // floor caps and so the count it floors — on this step, not the next one.
            simulation.StepExact(1, Worlds.PlanetSurface(1f, 0.5f));
            int inAir = simulation.Solver.FlooredNodes;

            Assert.True(inVacuum < simulation.Solver.Nodes.Count,
                "the floor saturated in vacuum, so the count cannot rise in air");
            Assert.True(inAir > inVacuum,
                "floored in vacuum " + inVacuum + ", floored in air " + inAir);
        }

        /// <summary>
        /// A step spread over many frames must not re-estimate on each of them. The estimate
        /// belongs to the step, not to the frame that happens to be advancing it.
        /// </summary>
        [Fact]
        public void AStepSpreadOverManyFramesStillEstimatesOnce()
        {
            ThermalSimulation simulation = Ship(64, 4000);

            RunOneStep(simulation);

            Assert.True(simulation.Work.SolverSubsteps >= 1);
            Assert.Equal(1, simulation.Work.StabilityEstimates);
            Assert.Equal(1, simulation.Work.NodeStateSyncs);
        }
    }
}
