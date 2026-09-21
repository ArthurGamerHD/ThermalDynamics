using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    public class ConductionClampGateTests
    {
/// <summary>Builds the API method table.</summary>
        private static ThermalSimulation Build(bool gate, int maxSubsteps)
        {
            ThermalSimulation simulation = Hulls.Driven(Hulls.Uncapped(maxSubsteps));
            simulation.Solver.GateConductionClamp = gate;
            return simulation;
        }

/// <summary>AssertIdentical operation.</summary>
        private static void AssertIdentical(ThermalSimulation always, ThermalSimulation gated, string what)
        {
            SolverAb.AssertIdentical(
                SolverAb.Temperatures(always), SolverAb.Temperatures(gated), what,
                "with the clamp always on", "with it gated");
        }

        [Fact]
/// <summary>SkippingTheClampIsBitIdenticalWhenTheGridIsResolved operation.</summary>
        public void SkippingTheClampIsBitIdenticalWhenTheGridIsResolved()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation always = Build(gate: false, maxSubsteps: 4096);
/// <summary>Builds the method table.</summary>
            ThermalSimulation gated = Build(gate: true, maxSubsteps: 4096);

            EnvironmentSample sample = Worlds.Ab.MildAtmosphere();

            always.StepExact(40, sample);
            gated.StepExact(40, sample);

            Assert.False(gated.Solver.ConductionClampLive);
            AssertIdentical(always, gated, "resolved");
        }

        [Fact]
/// <summary>TheClampStillRunsWhenTheSubstepCountWasRefused operation.</summary>
        public void TheClampStillRunsWhenTheSubstepCountWasRefused()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation always = Build(gate: false, maxSubsteps: 1);
/// <summary>Builds the method table.</summary>
            ThermalSimulation gated = Build(gate: true, maxSubsteps: 1);

            EnvironmentSample sample = Worlds.Ab.MildAtmosphere();

            always.StepExact(40, sample);
            gated.StepExact(40, sample);

            Assert.True(gated.Solver.ConductionClampLive);
            AssertIdentical(always, gated, "clamped");
        }

        [Fact]
/// <summary>SkippingTheClampIsBitIdenticalInVacuum operation.</summary>
        public void SkippingTheClampIsBitIdenticalInVacuum()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation always = Build(gate: false, maxSubsteps: 4096);
/// <summary>Builds the method table.</summary>
            ThermalSimulation gated = Build(gate: true, maxSubsteps: 4096);

            EnvironmentSample sample = Worlds.Ab.SunlitVacuum();

            always.StepExact(40, sample);
            gated.StepExact(40, sample);

            AssertIdentical(always, gated, "vacuum");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(8)]
        [InlineData(16)]
        [InlineData(24)]
        [InlineData(64)]
/// <summary>TheGateNeverChangesTheAnswerAtAnySubstepCap operation.</summary>
        public void TheGateNeverChangesTheAnswerAtAnySubstepCap(int cap)
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation always = Build(gate: false, maxSubsteps: cap);
/// <summary>Builds the method table.</summary>
            ThermalSimulation gated = Build(gate: true, maxSubsteps: cap);

            EnvironmentSample sample = Worlds.Ab.MildAtmosphere();

            always.StepExact(20, sample);
            gated.StepExact(20, sample);

            AssertIdentical(always, gated, "cap " + cap);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(97)]
        [InlineData(5000)]
/// <summary>TheGateSurvivesTheStepBeingSpread operation.</summary>
        public void TheGateSurvivesTheStepBeingSpread(int budget)
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation whole = Build(gate: false, maxSubsteps: 4096);
/// <summary>Builds the method table.</summary>
            ThermalSimulation spread = Build(gate: true, maxSubsteps: 4096);

            EnvironmentState state = EnvironmentSolver.Solve(
                whole.Settings, whole.Planet,
                Worlds.Ab.MildAtmosphere());

            for (int step = 0; step < 8; step++)
            {
                whole.Solver.Step(whole.Settings.StepSeconds, state);

                spread.Solver.BeginStep(spread.Settings.StepSeconds, state);
                while (!spread.Solver.AdvanceStep(budget)) { }
            }

            AssertIdentical(whole, spread, "spread over " + budget);
        }

        [Fact]
/// <summary>TheGateEngagesOnAResolvedGridAndNotOnAStiffOne operation.</summary>
        public void TheGateEngagesOnAResolvedGridAndNotOnAStiffOne()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation resolved = Build(gate: true, maxSubsteps: 4096);
/// <summary>Builds the method table.</summary>
            ThermalSimulation stiff = Build(gate: true, maxSubsteps: 1);

            EnvironmentSample sample = Worlds.Ab.MildAtmosphere();

            resolved.StepExact(4, sample);
            stiff.StepExact(4, sample);

            Assert.False(resolved.Solver.ConductionClampLive);
            Assert.True(stiff.Solver.ConductionClampLive);
        }

        [Fact]
/// <summary>TheSettingStillWins operation.</summary>
        public void TheSettingStillWins()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubsteps = 1;
            settings.MaxElementVisitsPerStep = 0;
            settings.ClampConductionOvershoot = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", 500));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.RebuildAll();
            Census.DriveCensus(simulation);
            LoadBenchmarks.SeedSpread(simulation);

            simulation.StepExact(4, Worlds.Ab.MildAtmosphere());

            Assert.False(simulation.Solver.ConductionClampLive);
        }
    }
}
