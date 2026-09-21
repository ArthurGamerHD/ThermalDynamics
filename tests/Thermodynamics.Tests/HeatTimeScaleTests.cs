using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    public class HeatTimeScaleTests
    {
/// <summary>Sets the tings.</summary>
        private static ThermalSettings Settings(float scale, int frequency = 4)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.HeatTimeScale = scale;
            settings.Frequency = frequency;
            return settings.Derive();
        }

/// <summary>Cube operation.</summary>
        private static ThermalSimulation Cube(ThermalSettings settings, float temperature)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));
            ThermalSimulation simulation = builder.BuildSimulation(settings, temperature);
            simulation.RebuildAll();
            return simulation;
        }

        [Fact]
/// <summary>CapacityIsDividedByTheScale operation.</summary>
        public void CapacityIsDividedByTheScale()
        {
/// <summary>Cube operation.</summary>
            ThermalSimulation physical = Cube(Settings(1f), 300f);
/// <summary>Cube operation.</summary>
            ThermalSimulation accelerated = Cube(Settings(225f), 300f);

            float a = physical.Solver.Nodes[0].ThermalMass;
            float b = accelerated.Solver.Nodes[0].ThermalMass;

            Assert.Equal(225f, a / b, 2);
        }

        [Fact]
/// <summary>ScalingCapacityIsTheSameAsRunningTimeFaster operation.</summary>
        public void ScalingCapacityIsTheSameAsRunningTimeFaster()
        {
            const int scale = 20;

/// <summary>Cube operation.</summary>
            ThermalSimulation fast = Cube(Settings(scale), 800f);
/// <summary>Cube operation.</summary>
            ThermalSimulation slow = Cube(Settings(1f), 800f);

            fast.StepExact(60 * 4, Worlds.Shadow());
            slow.StepExact(60 * 4 * scale, Worlds.Shadow());

            IList<ThermalNode> a = fast.Solver.Nodes;
            IList<ThermalNode> b = slow.Solver.Nodes;

            for (int i = 0; i < a.Count; i++)
            {
                float relative = Math.Abs(a[i].Temperature - b[i].Temperature) / b[i].Temperature;
                Assert.True(relative < 0.005f,
                    "node " + i + ": " + a[i].Temperature + " vs " + b[i].Temperature);
            }
        }

        [Fact]
/// <summary>EquilibriumDoesNotDependOnTheScale operation.</summary>
        public void EquilibriumDoesNotDependOnTheScale()
        {
            float[] scales = new float[] { 50f, 225f, 1000f };
            float first = 0f;

            for (int s = 0; s < scales.Length; s++)
            {
                GridBuilder builder = GridBuilder.Large();
                builder.Fill(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(3, 3, 3));

                BlockInstance centre = builder.Grid.GetAtCell(Vector3I.Zero);
                builder.Grid.Remove(centre);
                builder.Placed.Remove(centre);
                builder.Place(Catalog.Reactor(), Vector3I.Zero)
                       .Producing(5f * ThermalConstants.MegawattsToWatts);

                ThermalSimulation simulation = builder.BuildSimulation(Settings(scales[s]), 293.15f);
                simulation.RebuildAll();

                simulation.StepExact(30000, Worlds.Shadow());

                float reactor = simulation.Solver.GetNodeAt(Vector3I.Zero).Temperature;

                if (s == 0) first = reactor;
                else Assert.Equal(first, reactor, 0);
            }
        }

        [Fact]
/// <summary>AHigherScaleCostsSubstepsNotAccuracy operation.</summary>
        public void AHigherScaleCostsSubstepsNotAccuracy()
        {
/// <summary>Cube operation.</summary>
            ThermalSimulation gentle = Cube(Settings(1f), 800f);
/// <summary>Cube operation.</summary>
            ThermalSimulation harsh = Cube(Settings(2000f), 800f);

            gentle.StepExact(40, Worlds.Shadow());
            harsh.StepExact(40, Worlds.Shadow());

            Assert.True(harsh.Solver.LastSubsteps >= gentle.Solver.LastSubsteps);

            foreach (ThermalNode node in harsh.Solver.Nodes)
            {
                Assert.False(float.IsNaN(node.Temperature));
                Assert.True(node.Temperature >= ThermalConstants.MinimumTemperature);
                Assert.True(node.Temperature <= 800f, "nothing may heat itself by being accelerated");
            }
        }

        [Fact]
/// <summary>CoolantRunsOnTheSameClockAsTheBlocksItCools operation.</summary>
        public void CoolantRunsOnTheSameClockAsTheBlocksItCools()
        {
            LoopThermalProperties properties = LoopThermalProperties.Default();
            properties.SpecificHeat = 3400f;
            properties.CoolantMassPerPipe = 500f;

/// <summary>CoolantLoop operation.</summary>
            CoolantLoop physical = new CoolantLoop(properties, 300f, 1f);
/// <summary>CoolantLoop operation.</summary>
            CoolantLoop accelerated = new CoolantLoop(properties, 300f, new ThermalSettings().HeatTimeScale);

            Assert.Equal(new ThermalSettings().HeatTimeScale,
                physical.ThermalMass / accelerated.ThermalMass, 2);
        }

        [Fact]
/// <summary>AnInvalidScaleFallsBackToPhysical operation.</summary>
        public void AnInvalidScaleFallsBackToPhysical()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.HeatTimeScale = 0f;
            settings.Derive();

            Assert.Equal(1f, settings.HeatTimeScale);

/// <summary>ThermalSettings operation.</summary>
            ThermalSettings negative = new ThermalSettings();
            negative.HeatTimeScale = -5f;
            negative.Derive();

            Assert.Equal(1f, negative.HeatTimeScale);
        }

        [Fact]
/// <summary>TheShippedClockIsSlowerThanTheOneTheConversionReproduced operation.</summary>
        public void TheShippedClockIsSlowerThanTheOneTheConversionReproduced()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();

            const float steel = 450f;         // Data/Cubes.xml, DefaultThermodynamics
            const float legacyValue = 2f;     // what that definition used to carry
            const float conversionClock = 225f;

            Assert.Equal(legacyValue, steel / conversionClock, 3);
            Assert.Equal(90f, settings.HeatTimeScale, 3);
            Assert.Equal(2.5f, conversionClock / settings.HeatTimeScale, 3);
        }
    }
}
