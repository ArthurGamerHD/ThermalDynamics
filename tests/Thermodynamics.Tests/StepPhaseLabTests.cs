using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    [Collection("the stage lab's dials")]
    public class StepPhaseLabTests
    {
/// <summary>Hull operation.</summary>
        private static ThermalSimulation Hull(int blocks)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", blocks));

            ThermalSimulation simulation = builder.BuildSimulation(Hulls.Uncapped(), 293.15f);
            LoadBenchmarks.SeedSpread(simulation);
            Census.DriveCensus(simulation);
            return simulation;
        }

/// <summary>World operation.</summary>
        private static EnvironmentState World(ThermalSimulation simulation)
        {
            return EnvironmentSolver.Solve(simulation.Settings, simulation.Planet, Worlds.Flight(1f, 300f));
        }

        [Fact]
/// <summary>TheProfileIsOffUntilItIsAskedFor operation.</summary>
        public void TheProfileIsOffUntilItIsAskedFor()
        {
            Assert.False(Hull(600).Solver.ProfileStepPhases);
        }

        [Fact]
/// <summary>ProfilingAStepChangesNothingItMeasures operation.</summary>
        public void ProfilingAStepChangesNothingItMeasures()
        {
/// <summary>Hull operation.</summary>
            ThermalSimulation plain = Hull(1200);
/// <summary>Hull operation.</summary>
            ThermalSimulation profiled = Hull(1200);
            profiled.Solver.ProfileStepPhases = true;

/// <summary>World operation.</summary>
            EnvironmentState state = World(plain);
            float step = plain.Settings.StepSeconds;

            for (int i = 0; i < 5; i++)
            {
                plain.Solver.Step(step, state);
                profiled.Solver.Step(step, state);
            }

            IList<ThermalNode> a = plain.Solver.Nodes;
            IList<ThermalNode> b = profiled.Solver.Nodes;
            Assert.Equal(a.Count, b.Count);
            Assert.True(a.Count > 500, "only " + a.Count + " nodes, so little was compared");

            int spread = 0;
            for (int i = 0; i < a.Count; i++)
            {
                Assert.True(Bits(a[i].Temperature) == Bits(b[i].Temperature),
                    "node " + i + " is " + a[i].Temperature.ToString("R") + " unprofiled and "
                    + b[i].Temperature.ToString("R") + " profiled");

                if (a[i].Temperature != a[0].Temperature) spread++;
            }

            Assert.True(spread > 0, "every node sits at the same temperature, so the step did nothing");

            Assert.True(profiled.Solver.StepPhases.Visits[2] > 0, "the conduction stage recorded no visits");
        }

        [Fact]
/// <summary>EachStageIsChargedTheElementsItWalks operation.</summary>
        public void EachStageIsChargedTheElementsItWalks()
        {
/// <summary>Hull operation.</summary>
            ThermalSimulation simulation = Hull(2000);
/// <summary>World operation.</summary>
            EnvironmentState state = World(simulation);
            float step = simulation.Settings.StepSeconds;

            simulation.Solver.Step(step, state);

            simulation.Solver.ProfileStepPhases = true;
            simulation.Solver.StepPhases.Reset();

            long substepsBefore = simulation.Work.SolverSubsteps;
            simulation.Solver.Step(step, state);
            long substeps = simulation.Work.SolverSubsteps - substepsBefore;

            Assert.True(substeps > 1, "the step ran " + substeps + " substeps, so per-substep counts prove little");

            int nodes = simulation.Solver.Nodes.Count;
            int links = simulation.Solver.LinkCount;
            ThermalSolver.StepPhaseProfile phases = simulation.Solver.StepPhases;

            Assert.Equal(nodes, phases.Visits[ThermalSolver.StepPhaseProfile.EnvironmentFill]);
            Assert.Equal(nodes * substeps,
                phases.Visits[1] + phases.Visits[ThermalSolver.StepPhaseProfile.EnvironmentFill]);

            Assert.Equal(links * substeps, phases.Visits[2]);   // conduction
            Assert.Equal(nodes * substeps, phases.Visits[4]);   // apply
            Assert.Equal(nodes, phases.Visits[5]);              // publish, once for the step

            for (int p = 0; p < ThermalSolver.StepPhaseProfile.PhaseCount; p++)
            {
                Assert.True(phases.Slices[p] > 0,
                    ThermalSolver.StepPhaseProfile.Names[p] + " was never entered");
            }

            Assert.Equal(1, phases.Slices[ThermalSolver.StepPhaseProfile.EnvironmentFill]);
        }

        [Fact]
        [Trait("speed", "slow")]
/// <summary>TheLabReportsEveryStageOfAStep operation.</summary>
        public void TheLabReportsEveryStageOfAStep()
        {
            int repeats = StageLab.Repeats;
            StageLab.Repeats = 3;
            try
            {
                List<StageLab.Row> rows = StageLab.StepPhases("ship", 2000);

                Assert.Equal(ThermalSolver.StepPhaseProfile.PhaseCount, rows.Count);

                int withWork = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    Assert.Equal(ThermalSolver.StepPhaseProfile.Names[i], rows[i].Stage);
                    Assert.True(rows[i].BestMs >= 0d && rows[i].BestMs <= rows[i].WorstMs,
                        rows[i].Stage + " has no usable time");
                    if (rows[i].Work > 0) withWork++;
                }

                Assert.True(withWork >= 4,
                    "only " + withWork + " stages charged any work, so the split is not a split");
            }
            finally
            {
                StageLab.Repeats = repeats;
            }
        }

/// <summary>Bits operation.</summary>
        private static int Bits(float value)
        {
            return System.BitConverter.ToInt32(System.BitConverter.GetBytes(value), 0);
        }
    }
}
