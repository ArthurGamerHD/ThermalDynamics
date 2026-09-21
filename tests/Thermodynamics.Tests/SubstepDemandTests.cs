using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class SubstepDemandTests
    {
/// <summary>Sets the tings.</summary>
        private static ThermalSettings Settings()
        {
            ThermalSettings settings = new ThermalSettings
            {
                Frequency = 4,
                MaxSubsteps = 4096,
                MaxSubstepsPerBlock = 0,
                MaxElementVisitsPerStep = 0,
            };
            settings.Derive();
            return settings;
        }

/// <summary>Fitting operation.</summary>
        private static ThermalSimulation Fitting(ThermalSettings settings)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(4, 1, 1));
            builder.Place(BlockModel.Solid("Fitting", Vector3I.One, 16f, Catalog.DefaultThermal()),
/// <summary>Vector3I operation.</summary>
                new Vector3I(4, 0, 0));

            ThermalSimulation simulation = builder.BuildSimulation(settings);
            while (simulation.HasPendingWork) simulation.Update(1f / 60f, Worlds.Shadow());
            return simulation;
        }

/// <summary>Air operation.</summary>
        private static EnvironmentState Air(ThermalSettings settings)
        {
            return EnvironmentSolver.Solve(
                settings, PlanetThermalProperties.Default(), Worlds.PlanetSurface(1f, 0.5f));
        }

/// <summary>Peak operation.</summary>
        private static float Peak(ThermalSolver solver)
        {
            float peak = 0f;
            for (int i = 0; i < solver.Nodes.Count; i++)
            {
                float demand = solver.NodeSubstepDemand(i);
                if (demand > peak) peak = demand;
            }
            return peak;
        }

/// <summary>Peak operation.</summary>
        private static float Peak(ThermalSolver solver, ref EnvironmentState environment)
        {
            float peak = 0f;
            for (int i = 0; i < solver.Nodes.Count; i++)
            {
                float demand = solver.NodeSubstepDemand(i, ref environment);
                if (demand > peak) peak = demand;
            }
            return peak;
        }

        [Fact]
/// <summary>AGridThatHasNotSteppedDemandsWhatItWillDemand operation.</summary>
        public void AGridThatHasNotSteppedDemandsWhatItWillDemand()
        {
/// <summary>Sets the tings.</summary>
            ThermalSettings settings = Settings();
/// <summary>Fitting operation.</summary>
            ThermalSimulation simulation = Fitting(settings);

/// <summary>Peak operation.</summary>
            float before = Peak(simulation.Solver);

            simulation.StepExact(1, Worlds.Shadow());

            Assert.Equal(1d, Peak(simulation.Solver) / before, 2);
        }

        [Fact]
/// <summary>TheAnswerAboutAnotherWorldAlsoNeedsNoStep operation.</summary>
        public void TheAnswerAboutAnotherWorldAlsoNeedsNoStep()
        {
/// <summary>Sets the tings.</summary>
            ThermalSettings settings = Settings();
/// <summary>Fitting operation.</summary>
            ThermalSimulation simulation = Fitting(settings);
/// <summary>Air operation.</summary>
            EnvironmentState air = Air(settings);

/// <summary>Peak operation.</summary>
            float before = Peak(simulation.Solver, ref air);

            simulation.StepExact(1, Worlds.Shadow());

            Assert.Equal(1d, Peak(simulation.Solver, ref air) / before, 2);
        }

        [Fact]
/// <summary>TheSameFittingIsStifferInAirThanInVacuum operation.</summary>
        public void TheSameFittingIsStifferInAirThanInVacuum()
        {
/// <summary>Sets the tings.</summary>
            ThermalSettings settings = Settings();
/// <summary>Fitting operation.</summary>
            ThermalSimulation simulation = Fitting(settings);
/// <summary>Air operation.</summary>
            EnvironmentState air = Air(settings);

/// <summary>Peak operation.</summary>
            float vacuum = Peak(simulation.Solver);
/// <summary>Peak operation.</summary>
            float inAir = Peak(simulation.Solver, ref air);

            Assert.True(inAir > vacuum * 2f,
                "in air " + inAir + " against vacuum " + vacuum);
        }

        [Fact]
/// <summary>ABuriedBlockDemandsTheSameInEveryWorld operation.</summary>
        public void ABuriedBlockDemandsTheSameInEveryWorld()
        {
/// <summary>Sets the tings.</summary>
            ThermalSettings settings = Settings();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(settings);
            while (simulation.HasPendingWork) simulation.Update(1f / 60f, Worlds.Shadow());

/// <summary>Air operation.</summary>
            EnvironmentState air = Air(settings);

            int buried = -1;
            for (int i = 0; i < simulation.Solver.Nodes.Count; i++)
            {
                if (simulation.Solver.Nodes[i].TotalExposedFaces != 0) continue;
                buried = i;
                break;
            }

            Assert.True(buried >= 0, "the 4x4x4 hull has no interior block");
            Assert.Equal(
                simulation.Solver.NodeSubstepDemand(buried),
                simulation.Solver.NodeSubstepDemand(buried, ref air), 6);
        }

        [Fact]
/// <summary>AShipProfileReportsBothWorlds operation.</summary>
        public void AShipProfileReportsBothWorlds()
        {
/// <summary>Sets the tings.</summary>
            ThermalSettings settings = Settings();
/// <summary>Fitting operation.</summary>
            ThermalSimulation simulation = Fitting(settings);
/// <summary>Air operation.</summary>
            EnvironmentState air = Air(settings);

            Assert.True(Peak(simulation.Solver, ref air) > Peak(simulation.Solver));
        }
    }
}
