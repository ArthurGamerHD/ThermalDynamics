using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class FeatureToggleTests
    {
/// <summary>TwoBlocks operation.</summary>
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
/// <summary>ConductionOffLeavesNeighboursAlone operation.</summary>
        public void ConductionOffLeavesNeighboursAlone()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableConduction = false;
            settings.Derive();

/// <summary>TwoBlocks operation.</summary>
            ThermalSimulation simulation = TwoBlocks(settings, 500f, 300f);
            simulation.StepExact(20, Worlds.Shadow());

            Assert.Equal(500f, simulation.Solver.Nodes[0].Temperature, 3);
            Assert.Equal(300f, simulation.Solver.Nodes[1].Temperature, 3);
        }

        [Fact]
/// <summary>ConductionOnEqualisesThem operation.</summary>
        public void ConductionOnEqualisesThem()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

/// <summary>TwoBlocks operation.</summary>
            ThermalSimulation simulation = TwoBlocks(settings, 500f, 300f);
            simulation.StepExact(1000, Worlds.Shadow());

            float a = simulation.Solver.Nodes[0].Temperature;
            float b = simulation.Solver.Nodes[1].Temperature;
            Assert.True(Math.Abs(a - b) < 1f, "expected the pair to equalise, got " + a + " and " + b);
            Assert.Equal(400f, a, 0);
        }

        [Fact]
/// <summary>RadiationOffStopsAHotBlockCoolingIntoSpace operation.</summary>
        public void RadiationOffStopsAHotBlockCoolingIntoSpace()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableRadiation = false;
            settings.Derive();

/// <summary>TwoBlocks operation.</summary>
            ThermalSimulation simulation = TwoBlocks(settings, 900f, 900f);
            simulation.StepExact(50, Worlds.Shadow());

            Assert.Equal(900f, simulation.Solver.Nodes[0].Temperature, 2);
        }

        [Fact]
/// <summary>RadiationOnCoolsIt operation.</summary>
        public void RadiationOnCoolsIt()
        {
/// <summary>TwoBlocks operation.</summary>
            ThermalSimulation simulation = TwoBlocks(new ThermalSettings(), 900f, 900f);
            simulation.StepExact(50, Worlds.Shadow());

            Assert.True(simulation.Solver.Nodes[0].Temperature < 899f);
        }

        [Fact]
/// <summary>RadiationShedsWhatTheLawSays operation.</summary>
        public void RadiationShedsWhatTheLawSays()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableConduction = false;
            settings.Derive();

/// <summary>TwoBlocks operation.</summary>
            ThermalSimulation simulation = TwoBlocks(settings, 900f, 900f);
            simulation.Solver.CollectDiagnostics = true;

            ThermalNode node = simulation.Solver.Nodes[0];

            Assert.Equal(node.Thermal.Emissivity * ThermalConstants.StefanBoltzmann * node.ExposedArea,
                node.RadiationCoefficient, 6);

            float temperature = node.Temperature;
            float ambient = settings.VacuumTemperature;
            simulation.StepExact(1, Worlds.Shadow());

            double expected = node.RadiationCoefficient
                * (Math.Pow(ambient, 4d) - Math.Pow(temperature, 4d));

            Assert.True(node.LastRadiationWatts < 0f);
            Assert.True(Math.Abs(node.LastRadiationWatts - expected) < Math.Abs(expected) * 0.01d,
                "expected about " + expected.ToString("n0") + " W, got " + node.LastRadiationWatts);
        }

        [Fact]
/// <summary>ConvectionOffLeavesOnlyRadiationInAtmosphere operation.</summary>
        public void ConvectionOffLeavesOnlyRadiationInAtmosphere()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableConvection = false;
            settings.Derive();

/// <summary>TwoBlocks operation.</summary>
            ThermalSimulation withoutConvection = TwoBlocks(settings, 600f, 600f);
            withoutConvection.StepExact(20, Worlds.PlanetSurface(1f, 0.5f));

/// <summary>TwoBlocks operation.</summary>
            ThermalSimulation withConvection = TwoBlocks(new ThermalSettings(), 600f, 600f);
            withConvection.StepExact(20, Worlds.PlanetSurface(1f, 0.5f));

            Assert.True(withoutConvection.Solver.Nodes[0].Temperature
                > withConvection.Solver.Nodes[0].Temperature + 10f);
        }

        [Fact]
/// <summary>ConvectionOffLeavesTheRadiativeShareUntouched operation.</summary>
        public void ConvectionOffLeavesTheRadiativeShareUntouched()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings off = new ThermalSettings();
            off.EnableConvection = false;
            off.Derive();

/// <summary>TwoBlocks operation.</summary>
            ThermalSimulation without = TwoBlocks(off, 600f, 600f);
            without.Solver.CollectDiagnostics = true;
            without.StepExact(1, ThinAir());

/// <summary>TwoBlocks operation.</summary>
            ThermalSimulation with = TwoBlocks(new ThermalSettings(), 600f, 600f);
            with.Solver.CollectDiagnostics = true;
            with.StepExact(1, ThinAir());

            Assert.Equal(0f, without.Solver.Nodes[0].LastConvectionWatts, 5);
            Assert.True(with.Solver.Nodes[0].LastConvectionWatts < 0f,
                "a 600 K block in air should be shedding convective watts");

            Assert.True(with.Solver.Nodes[0].LastRadiationWatts < 0f);
            Assert.Equal(with.Solver.Nodes[0].LastRadiationWatts,
                without.Solver.Nodes[0].LastRadiationWatts, 3);
        }

/// <summary>ThinAir operation.</summary>
        private static EnvironmentSample ThinAir()
        {
            return Worlds.PlanetSurface(0.3f, 0.5f);
        }

        [Fact]
/// <summary>TheEnvironmentSwitchRemovesRadiationAndConvectionAndLeavesSourcesAlone operation.</summary>
        public void TheEnvironmentSwitchRemovesRadiationAndConvectionAndLeavesSourcesAlone()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Producing(15e6f);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 600f);
            simulation.Solver.CollectDiagnostics = true;
            simulation.StepExact(1, ThinAir());

            ThermalNode node = simulation.Solver.Nodes[0];
            Assert.Equal(0f, node.LastRadiationWatts, 5);
            Assert.Equal(0f, node.LastConvectionWatts, 5);

            Assert.True(node.HeatGenerationWatts > 0f);
            Assert.True(node.Temperature > 600f);

            ThermalSimulation exchanging = builder.BuildSimulation(new ThermalSettings(), 600f);
            exchanging.Solver.CollectDiagnostics = true;
            exchanging.StepExact(1, ThinAir());

            Assert.True(exchanging.Solver.Nodes[0].LastRadiationWatts < 0f);
            Assert.True(exchanging.Solver.Nodes[0].LastConvectionWatts < 0f);
        }

        [Fact]
/// <summary>WasteHeatOffStopsAReactorHeatingItself operation.</summary>
        public void WasteHeatOffStopsAReactorHeatingItself()
        {
/// <summary>ThermalSettings operation.</summary>
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
/// <summary>SwitchingASettingTakesEffectWithoutARebuild operation.</summary>
        public void SwitchingASettingTakesEffectWithoutARebuild()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Producing(15e6f);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.StepExact(10, Worlds.Shadow());
            float heated = simulation.Solver.Nodes[0].Temperature;
            Assert.True(heated > 293.15f);

            settings.EnableWasteHeat = false;
            settings.Derive();

            simulation.StepExact(10, Worlds.Shadow());
            Assert.Equal(heated, simulation.Solver.Nodes[0].Temperature, 3);
        }

        [Fact]
/// <summary>ChangingHeatTimeScaleMidSessionRescalesCapacity operation.</summary>
        public void ChangingHeatTimeScaleMidSessionRescalesCapacity()
        {
/// <summary>ThermalSettings operation.</summary>
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
/// <summary>CoolantLoopsSwitchOffMidSession operation.</summary>
        public void CoolantLoopsSwitchOffMidSession()
        {
/// <summary>ThermalSettings operation.</summary>
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


        [Fact]
/// <summary>FrictionOffLeavesAHullAtSpeedCold operation.</summary>
        public void FrictionOffLeavesAHullAtSpeedCold()
        {
/// <summary>HullAtSpeed operation.</summary>
            ThermalNode cold = HullAtSpeed(false);
/// <summary>HullAtSpeed operation.</summary>
            ThermalNode hot = HullAtSpeed(true);

            Assert.Equal(0f, cold.LastFrictionWatts, 5);
            Assert.Equal(293.15f, cold.Temperature, 3);

            Assert.True(hot.LastFrictionWatts > 0f);
            Assert.True(hot.Temperature > 293.15f);
        }

/// <summary>HullAtSpeed operation.</summary>
        private static ThermalNode HullAtSpeed(bool friction)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableDamage = false;
            settings.EnableFriction = friction;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.Solver.CollectDiagnostics = true;
            simulation.Planet = PlanetThermalProperties.Default();

            simulation.StepExact(4, Worlds.Flight(0.8f, 300f));
            return simulation.Solver.Nodes[0];
        }

        [Fact]
/// <summary>SolarOffLeavesNoSolarWattsOnASunwardNode operation.</summary>
        public void SolarOffLeavesNoSolarWattsOnASunwardNode()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

/// <summary>ThermalSettings operation.</summary>
            ThermalSettings off = new ThermalSettings();
            off.EnableSolarHeat = false;
            off.Derive();

            ThermalSimulation dark = builder.BuildSimulation(off, 293.15f);
            dark.Solver.CollectDiagnostics = true;
            dark.StepExact(1, Worlds.Space(new Vector3(1f, 0f, 0f)));

/// <summary>ThermalSettings operation.</summary>
            ThermalSettings on = new ThermalSettings();
            on.EnableSolarHeat = true;
            on.Derive();

            ThermalSimulation lit = builder.BuildSimulation(on, 293.15f);
            lit.Solver.CollectDiagnostics = true;
            lit.StepExact(1, Worlds.Space(new Vector3(1f, 0f, 0f)));

            Assert.Equal(0f, dark.Solver.Nodes[0].LastSolarWatts, 5);
            Assert.True(lit.Solver.Nodes[0].LastSolarWatts > 0f,
                "a block facing the sun should be taking solar watts");
        }

        [Fact]
/// <summary>CoolantLoopsOffStopTheRingCarryingHeat operation.</summary>
        public void CoolantLoopsOffStopTheRingCarryingHeat()
        {
/// <summary>HotBlockBesideARing operation.</summary>
            float withLoops = HotBlockBesideARing(true);
/// <summary>HotBlockBesideARing operation.</summary>
            float withoutLoops = HotBlockBesideARing(false);

            Assert.True(withLoops < withoutLoops - 1f,
                "the ring should have carried heat away from the block: with loops it reached "
                + withLoops + " K, without them " + withoutLoops + " K");
        }

/// <summary>HotBlockBesideARing operation.</summary>
        private static float HotBlockBesideARing(bool loops)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableFriction = false;
            settings.EnableDamage = false;
            settings.EnableCoolantLoops = loops;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();

            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            sinks[1] = Vector3I.Down;

            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3), -1, sinks);
            builder.Place(Catalog.HeavyArmor(), new Vector3I(1, -1, 0));
            BlockInstance hot = builder.Last;

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);
            Assert.Equal(loops, simulation.Solver.Loops.Count > 0);

            ThermalNode node = simulation.Solver.GetNode(hot);
            node.Temperature = 900f;
            simulation.StepExact(20, Worlds.Shadow());

            return node.Temperature;
        }

        [Fact]
/// <summary>RoomAirOffStopsTheAirCarryingHeatAcrossARoom operation.</summary>
        public void RoomAirOffStopsTheAirCarryingHeatAcrossARoom()
        {
/// <summary>FarWallOfASealedBox operation.</summary>
            float withAir = FarWallOfASealedBox(true);
/// <summary>FarWallOfASealedBox operation.</summary>
            float withoutAir = FarWallOfASealedBox(false);

            Assert.Equal(293.15f, withoutAir, 3);
            Assert.True(withAir > withoutAir + 1f,
                "the air should have warmed the far wall, got " + withAir + " K against "
                + withoutAir + " K");
        }

/// <summary>FarWallOfASealedBox operation.</summary>
        private static float FarWallOfASealedBox(bool air)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableConduction = false;
            settings.EnableRoomAir = air;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(5, 5, 5));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.SetRoomPressure(new Vector3I(2, 2, 2), 1f);

            simulation.Solver.GetNodeAt(new Vector3I(2, 0, 2)).Temperature = 600f;
            ThermalNode far = simulation.Solver.GetNodeAt(new Vector3I(2, 4, 2));

            simulation.StepExact(400, Worlds.Shadow());
            return far.Temperature;
        }
    }
}
