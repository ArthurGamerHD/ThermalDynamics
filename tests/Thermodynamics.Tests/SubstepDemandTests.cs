using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class SubstepDemandTests
    {

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


        private static ThermalSimulation Fitting(ThermalSettings settings)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(4, 1, 1));
            builder.Place(BlockModel.Solid("Fitting", Vector3I.One, 16f, Catalog.DefaultThermal()),

                new Vector3I(4, 0, 0));

            ThermalSimulation simulation = builder.BuildSimulation(settings);
            while (simulation.HasPendingWork) simulation.Update(1f / 60f, Worlds.Shadow());
            return simulation;
        }


        private static EnvironmentState Air(ThermalSettings settings)
        {
            return EnvironmentSolver.Solve(
                settings, PlanetThermalProperties.Default(), Worlds.PlanetSurface(1f, 0.5f));
        }


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

        public void AGridThatHasNotSteppedDemandsWhatItWillDemand()
        {

            ThermalSettings settings = Settings();

            ThermalSimulation simulation = Fitting(settings);


            float before = Peak(simulation.Solver);

            simulation.StepExact(1, Worlds.Shadow());

            Assert.Equal(1d, Peak(simulation.Solver) / before, 2);
        }

        [Fact]

        public void TheAnswerAboutAnotherWorldAlsoNeedsNoStep()
        {

            ThermalSettings settings = Settings();

            ThermalSimulation simulation = Fitting(settings);

            EnvironmentState air = Air(settings);


            float before = Peak(simulation.Solver, ref air);

            simulation.StepExact(1, Worlds.Shadow());

            Assert.Equal(1d, Peak(simulation.Solver, ref air) / before, 2);
        }

        [Fact]

        public void TheSameFittingIsStifferInAirThanInVacuum()
        {

            ThermalSettings settings = Settings();

            ThermalSimulation simulation = Fitting(settings);

            EnvironmentState air = Air(settings);


            float vacuum = Peak(simulation.Solver);

            float inAir = Peak(simulation.Solver, ref air);

            Assert.True(inAir > vacuum * 2f,
                "in air " + inAir + " against vacuum " + vacuum);
        }

        [Fact]

        public void ABuriedBlockDemandsTheSameInEveryWorld()
        {

            ThermalSettings settings = Settings();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(settings);
            while (simulation.HasPendingWork) simulation.Update(1f / 60f, Worlds.Shadow());


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

        public void AShipProfileReportsBothWorlds()
        {

            ThermalSettings settings = Settings();

            ThermalSimulation simulation = Fitting(settings);

            EnvironmentState air = Air(settings);

            Assert.True(Peak(simulation.Solver, ref air) > Peak(simulation.Solver));
        }
    }
}
