using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// What a block demands of a step, asked of a grid that has not stepped and of a world the grid
    /// is not in.
    ///
    /// <para><c>NodeSubstepDemand</c> is the only figure the ship screening reads about stiffness,
    /// and the screening deliberately steps nothing — that is what makes it affordable over a
    /// corpus of thousands. It read the mirrored rows the step path fills, which on a grid that has
    /// never stepped are empty, so it reported conduction alone whatever world it was asked about:
    /// 0.075 against the 0.47 the same fitting demands in air, and identical figures for a vacuum
    /// and a sea-level atmosphere.</para>
    ///
    /// <para>That is not a small error in a small number. A fleet's cost is set by its stiffest
    /// block, half of that block's stiffness is what it exchanges with the world over its exposed
    /// area, and the corpus is stratified on the result.</para>
    /// </summary>
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

        /// <summary>A light fitting on the end of an armour bar: something to conduct into, and sky.</summary>
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

        /// <summary>
        /// The regression, stated as the thing it should have been all along: stepping a grid barely
        /// changes what its blocks demand, because almost nothing about the grid changed.
        ///
        /// Not exactly nothing — the radiation half of the demand goes as T³ and a step moves T — so
        /// the claim is a fraction of a per cent rather than equality. Before the fix the two
        /// differed by a factor of six.
        /// </summary>
        [Fact]
        public void AGridThatHasNotSteppedDemandsWhatItWillDemand()
        {
            ThermalSettings settings = Settings();
            ThermalSimulation simulation = Fitting(settings);

            float before = Peak(simulation.Solver);

            simulation.StepExact(1, Worlds.Shadow());

            Assert.Equal(1d, Peak(simulation.Solver) / before, 2);
        }

        /// <summary>
        /// The same, asked about a world the grid is not in. This is the call the ship screening
        /// makes, and it must not need a step either.
        /// </summary>
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

        /// <summary>
        /// Air is the whole point of asking. A fitting in a sea-level atmosphere is several times
        /// stiffer than the same fitting in vacuum, because convection over a full cell's exposed
        /// area dwarfs both its conduction and its radiation.
        /// </summary>
        [Fact]
        public void TheSameFittingIsSeveralTimesStifferInAirThanInVacuum()
        {
            ThermalSettings settings = Settings();
            ThermalSimulation simulation = Fitting(settings);
            EnvironmentState air = Air(settings);

            float vacuum = Peak(simulation.Solver);
            float inAir = Peak(simulation.Solver, ref air);

            Assert.True(inAir > vacuum * 4f,
                "in air " + inAir + " against vacuum " + vacuum);
        }

        /// <summary>
        /// A buried block exchanges with nothing, so no world changes what it demands. Without this
        /// the environment half would be charged to every block on a hull rather than to its skin.
        /// </summary>
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

        /// <summary>
        /// A ship profile carries both, and they are the same quantity measured in two worlds
        /// rather than an estimate and a correction.
        /// </summary>
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
