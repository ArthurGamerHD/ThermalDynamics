using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Every mechanism is meant to be independently switchable, and switching one off is meant to
    /// remove exactly its own contribution and nothing else. These pin that down one mechanism at
    /// a time: build the same grid twice, change one switch, and check that the difference is the
    /// one mechanism.
    /// </summary>
    public class FeatureToggleTests
    {
        private static ThermalSimulation TwoBlocks(ThermalSettings settings, float hot, float cold)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            builder.Place(Catalog.HeavyArmor(), new Vector3I(1, 0, 0));

            ThermalSimulation simulation = builder.BuildSimulation(settings, cold);
            simulation.Solver.Nodes[0].Temperature = hot;
            return simulation;
        }

        [Fact]
        public void ConductionOffLeavesNeighboursAlone()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableConduction = false;
            settings.Derive();

            ThermalSimulation simulation = TwoBlocks(settings, 500f, 300f);
            simulation.StepExact(20, Worlds.Shadow());

            Assert.Equal(500f, simulation.Solver.Nodes[0].Temperature, 3);
            Assert.Equal(300f, simulation.Solver.Nodes[1].Temperature, 3);
        }

        [Fact]
        public void ConductionOnEqualisesThem()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            ThermalSimulation simulation = TwoBlocks(settings, 500f, 300f);
            simulation.StepExact(1000, Worlds.Shadow());

            float a = simulation.Solver.Nodes[0].Temperature;
            float b = simulation.Solver.Nodes[1].Temperature;
            Assert.True(Math.Abs(a - b) < 1f, "expected the pair to equalise, got " + a + " and " + b);
            Assert.Equal(400f, a, 0);
        }

        [Fact]
        public void RadiationOffStopsAHotBlockCoolingIntoSpace()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableRadiation = false;
            settings.Derive();

            ThermalSimulation simulation = TwoBlocks(settings, 900f, 900f);
            simulation.StepExact(50, Worlds.Shadow());

            Assert.Equal(900f, simulation.Solver.Nodes[0].Temperature, 2);
        }

        [Fact]
        public void RadiationOnCoolsIt()
        {
            ThermalSimulation simulation = TwoBlocks(new ThermalSettings(), 900f, 900f);
            simulation.StepExact(50, Worlds.Shadow());

            Assert.True(simulation.Solver.Nodes[0].Temperature < 899f);
        }

        [Fact]
        public void ConvectionOffLeavesOnlyRadiationInAtmosphere()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableConvection = false;
            settings.Derive();

            ThermalSimulation withoutConvection = TwoBlocks(settings, 600f, 600f);
            withoutConvection.StepExact(20, Worlds.PlanetSurface(1f, 0.5f));

            ThermalSimulation withConvection = TwoBlocks(new ThermalSettings(), 600f, 600f);
            withConvection.StepExact(20, Worlds.PlanetSurface(1f, 0.5f));

            // Sea level convection dwarfs radiation, so switching it off has to leave the block
            // hotter by a wide margin.
            Assert.True(withoutConvection.Solver.Nodes[0].Temperature
                > withConvection.Solver.Nodes[0].Temperature + 10f);
        }

        [Fact]
        public void WasteHeatOffStopsAReactorHeatingItself()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableWasteHeat = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Producing(15e6f);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.StepExact(50, Worlds.Shadow());

            Assert.Equal(293.15f, simulation.Solver.Nodes[0].Temperature, 3);
        }

        [Fact]
        public void SwitchingASettingTakesEffectWithoutARebuild()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Producing(15e6f);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.StepExact(10, Worlds.Shadow());
            float heated = simulation.Solver.Nodes[0].Temperature;
            Assert.True(heated > 293.15f);

            // The same settings object every grid already holds, changed mid-session.
            settings.EnableWasteHeat = false;
            settings.Derive();

            simulation.StepExact(10, Worlds.Shadow());
            Assert.Equal(heated, simulation.Solver.Nodes[0].Temperature, 3);
        }

        [Fact]
        public void ChangingHeatTimeScaleMidSessionRescalesCapacity()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.HeatTimeScale = 1f;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);
            float slow = simulation.Solver.Nodes[0].ThermalMass;

            settings.HeatTimeScale = 100f;
            settings.Derive();
            simulation.StepExact(1, Worlds.Shadow());

            Assert.Equal(slow / 100f, simulation.Solver.Nodes[0].ThermalMass, 4);
        }

        [Fact]
        public void CoolantLoopsSwitchOffMidSession()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 4, 3));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            Assert.NotEmpty(simulation.Solver.Loops);

            settings.EnableCoolantLoops = false;
            settings.Derive();
            simulation.StepExact(1, Worlds.Shadow());

            Assert.Empty(simulation.Solver.Loops);
        }
    }
}
