using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class GridHeatBalanceTests
    {
/// <summary>Generation operation.</summary>
        private static float Generation(ThermalSimulation simulation)
        {
            float total = 0f;
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++) total += nodes[i].HeatGenerationWatts;
            return total;
        }

/// <summary>Rig operation.</summary>
        private static ThermalSimulation Rig(
            float watts, out EnvironmentSample sample, bool solar = false, int blocks = 1)
        {
            ThermalSettings settings = new ThermalSettings
            {
                MaxSubsteps = 64,
                MaxElementVisitsPerStep = 0,
                EnableSolarHeat = solar,
            };
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(blocks, 1, 1));
            builder.Place(Catalog.Reactor(), new Vector3I(-1, 0, 0)).Producing(watts);

            ThermalSimulation simulation = builder.BuildSimulation(settings);
            sample = Worlds.Space(Vector3.Up);
            return simulation;
        }

        [Fact]
/// <summary>AGridMakingNoHeatShedsWhatItHasAndSlowsAsItCools operation.</summary>
        public void AGridMakingNoHeatShedsWhatItHasAndSlowsAsItCools()
        {
            EnvironmentSample sample;
/// <summary>Rig operation.</summary>
            ThermalSimulation simulation = Rig(0f, out sample);

            simulation.StepExact(1, sample);

            float first = simulation.VentedWatts;
            Assert.True(first > 0f);
            Assert.Equal(0f, simulation.HeatGainWatts, 3);

            for (int step = 0; step < 1000; step++) simulation.StepExact(1, sample);
            float later = simulation.VentedWatts;

            for (int step = 0; step < 3000; step++) simulation.StepExact(1, sample);
            float latest = simulation.VentedWatts;

            Assert.True(later < first);
            Assert.True(latest < later);
            Assert.True(latest > 0f);

            Assert.Equal(0f, simulation.HeatGainWatts, 3);
        }

        [Fact]
/// <summary>AtEquilibriumVentingEqualsGeneration operation.</summary>
        public void AtEquilibriumVentingEqualsGeneration()
        {
            EnvironmentSample sample;
/// <summary>Rig operation.</summary>
            ThermalSimulation simulation = Rig(50000f, out sample);

            for (int step = 0; step < LabClock.Steps(20000); step++) simulation.StepExact(1, sample);

/// <summary>Generation operation.</summary>
            float made = Generation(simulation);
            Assert.True(made > 0f);

            Assert.Equal(made, simulation.HeatGainWatts, 0);
            Assert.True(simulation.VentedWatts > made * 0.98f);
            Assert.True(simulation.VentedWatts < made * 1.02f);
        }

        [Fact]
/// <summary>HeatMadeInsideAHullIsStillCounted operation.</summary>
        public void HeatMadeInsideAHullIsStillCounted()
        {
            ThermalSettings settings = new ThermalSettings
            {
                MaxSubsteps = 64,
                MaxElementVisitsPerStep = 0,
            };
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Producing(80000f);

            ThermalSimulation simulation = builder.BuildSimulation(settings);
            simulation.RebuildAll();

            simulation.StepExact(1, Worlds.Space(Vector3.Up));

            ThermalNode reactor = simulation.Solver.GetNode(simulation.Grid.GetAtCell(Vector3I.Zero));
            Assert.Equal(0, reactor.TotalExposedFaces);
            Assert.True(reactor.HeatGenerationWatts > 0f);
            Assert.True(simulation.HeatGainWatts >= reactor.HeatGenerationWatts);
        }

        [Fact]
/// <summary>GenerationIsCountedWithTheEnvironmentSwitchedOff operation.</summary>
        public void GenerationIsCountedWithTheEnvironmentSwitchedOff()
        {
            ThermalSettings settings = new ThermalSettings
            {
                MaxSubsteps = 64,
                MaxElementVisitsPerStep = 0,
                EnableEnvironment = false,

                EnableSolarHeat = false,
            };
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Producing(30000f);

            ThermalSimulation simulation = builder.BuildSimulation(settings);
            simulation.StepExact(1, Worlds.Space(Vector3.Up));

            Assert.Equal(Generation(simulation), simulation.HeatGainWatts, 0);
            Assert.Equal(0f, simulation.VentedWatts, 3);
        }

        [Fact]
/// <summary>SunlightCountsAsHeatGained operation.</summary>
        public void SunlightCountsAsHeatGained()
        {
            EnvironmentSample sample;
/// <summary>Rig operation.</summary>
            ThermalSimulation simulation = Rig(0f, out sample, true, 4);

            simulation.StepExact(1, sample);

            Assert.True(simulation.HeatGainWatts > 0f);
        }

        [Fact]
/// <summary>AGridAbsorbingFromWarmAirVentsNothing operation.</summary>
        public void AGridAbsorbingFromWarmAirVentsNothing()
        {
            ThermalSettings settings = new ThermalSettings
            {
                MaxSubsteps = 64,
                MaxElementVisitsPerStep = 0,
                EnableSolarHeat = false,
            };
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 200f);
            simulation.Planet = PlanetThermalProperties.Default();

            simulation.StepExact(1, Worlds.PlanetSurface(1f, 0.5f));

            Assert.True(simulation.EnvironmentWatts > 0f);
            Assert.Equal(0f, simulation.VentedWatts, 3);
        }
    }
}
