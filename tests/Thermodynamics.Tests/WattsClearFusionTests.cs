using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    public class WattsClearFusionTests
    {
/// <summary>Builds the API method table.</summary>
        private static ThermalSimulation Build(bool fused)
        {
            ThermalSimulation simulation = Hulls.Driven();
            simulation.Solver.FuseWattsClear = fused;
            return simulation;
        }

/// <summary>AssertIdentical operation.</summary>
        private static void AssertIdentical(ThermalSimulation a, ThermalSimulation b, string what)
        {
            SolverAb.AssertIdentical(
                SolverAb.Temperatures(a), SolverAb.Temperatures(b), what,
                "with the row cleared first", "with the environment pass writing it");
        }

        [Fact]
/// <summary>WritingTheWattsRowIsBitIdenticalInFlight operation.</summary>
        public void WritingTheWattsRowIsBitIdenticalInFlight()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation cleared = Build(false);
/// <summary>Builds the method table.</summary>
            ThermalSimulation fused = Build(true);

            EnvironmentSample sample = Worlds.Ab.EveryTermLive();

            cleared.StepExact(40, sample);
            fused.StepExact(40, sample);

            AssertIdentical(cleared, fused, "flight");
        }

        [Fact]
/// <summary>WritingTheWattsRowIsBitIdenticalInAnAtmosphere operation.</summary>
        public void WritingTheWattsRowIsBitIdenticalInAnAtmosphere()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation cleared = Build(false);
/// <summary>Builds the method table.</summary>
            ThermalSimulation fused = Build(true);

            EnvironmentSample sample = Worlds.Ab.MildAtmosphere();

            cleared.StepExact(40, sample);
            fused.StepExact(40, sample);

            AssertIdentical(cleared, fused, "atmosphere");
        }

        [Fact]
/// <summary>WritingTheWattsRowIsBitIdenticalInVacuum operation.</summary>
        public void WritingTheWattsRowIsBitIdenticalInVacuum()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation cleared = Build(false);
/// <summary>Builds the method table.</summary>
            ThermalSimulation fused = Build(true);

            EnvironmentSample sample = Worlds.Ab.SunlitVacuum();

            cleared.StepExact(40, sample);
            fused.StepExact(40, sample);

            AssertIdentical(cleared, fused, "vacuum");
        }

        [Fact]
/// <summary>WritingTheWattsRowIsBitIdenticalWithTheEnvironmentOff operation.</summary>
        public void WritingTheWattsRowIsBitIdenticalWithTheEnvironmentOff()
        {
            ThermalSettings settings = Hulls.Uncapped();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.Derive();

            ThermalSimulation cleared = Hulls.Driven(settings);
            ThermalSimulation fused = Hulls.Driven(settings);
            cleared.Solver.FuseWattsClear = false;
            fused.Solver.FuseWattsClear = true;

            EnvironmentSample sample = Worlds.Ab.SunlitVacuum();

            cleared.StepExact(40, sample);
            fused.StepExact(40, sample);

            AssertIdentical(cleared, fused, "environment off");
        }

        [Fact]
/// <summary>WritingTheWattsRowIsBitIdenticalWithNothingToWrite operation.</summary>
        public void WritingTheWattsRowIsBitIdenticalWithNothingToWrite()
        {
            ThermalSettings settings = Hulls.Uncapped();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableWasteHeat = false;
            settings.Derive();

            ThermalSimulation cleared = Hulls.Driven(settings);
            ThermalSimulation fused = Hulls.Driven(settings);
            cleared.Solver.FuseWattsClear = false;
            fused.Solver.FuseWattsClear = true;

            EnvironmentSample sample = Worlds.Ab.SunlitVacuum();

            cleared.StepExact(40, sample);
            fused.StepExact(40, sample);

            AssertIdentical(cleared, fused, "nothing to write");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(97)]
        [InlineData(5000)]
/// <summary>WritingTheWattsRowSurvivesTheStepBeingSpread operation.</summary>
        public void WritingTheWattsRowSurvivesTheStepBeingSpread(int budget)
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation whole = Build(false);
/// <summary>Builds the method table.</summary>
            ThermalSimulation spread = Build(true);

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
/// <summary>TheDiagnosticsAgreeAsWell operation.</summary>
        public void TheDiagnosticsAgreeAsWell()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation cleared = Build(false);
/// <summary>Builds the method table.</summary>
            ThermalSimulation fused = Build(true);

            cleared.Solver.CollectDiagnostics = true;
            fused.Solver.CollectDiagnostics = true;

            EnvironmentSample sample = Worlds.Ab.EveryTermLive();

            cleared.StepExact(40, sample);
            fused.StepExact(40, sample);

            SolverAb.AssertIdentical(
                SolverAb.Diagnostics(cleared), SolverAb.Diagnostics(fused), "diagnostics",
                "with the row cleared first", "with the environment pass writing it");
        }

        [Fact]
/// <summary>TheGridHeatTotalsAgree operation.</summary>
        public void TheGridHeatTotalsAgree()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation cleared = Build(false);
/// <summary>Builds the method table.</summary>
            ThermalSimulation fused = Build(true);

            EnvironmentSample sample = Worlds.Ab.EveryTermLive();

            cleared.StepExact(40, sample);
            fused.StepExact(40, sample);

            Assert.Equal(cleared.Solver.LastHeatGainWatts, fused.Solver.LastHeatGainWatts);
            Assert.Equal(cleared.Solver.LastEnvironmentWatts, fused.Solver.LastEnvironmentWatts);

            Assert.True(fused.Solver.LastHeatGainWatts > 0f,
                "the driven hull generated no heat, so the totals prove nothing");
        }
    }
}
