using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class SubgridBridgeTests
    {

        private static ShipAssembly Pair(bool bridged, out ThermalNode hot, out ThermalNode cold)
        {

            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableFriction = false;
            settings.EnableDamage = false;
            settings.Derive();

            GridBuilder first = GridBuilder.Large();
            first.Place(Catalog.HeavyArmor(), Vector3I.Zero);

            GridBuilder second = GridBuilder.Large();
            second.Place(Catalog.HeavyArmor(), Vector3I.Zero);


            ShipAssembly assembly = new ShipAssembly();
            ThermalSimulation a = first.BuildSimulation(settings, 800f);
            ThermalSimulation b = second.BuildSimulation(settings, 300f);

            assembly.Simulations.Add(a);
            assembly.Simulations.Add(b);

            hot = a.Solver.Nodes[0];
            cold = b.Solver.Nodes[0];

            if (bridged) assembly.Bridge2(a, hot, b, cold);
            return assembly;
        }

        [Fact]

        public void HeatCrossesAJointBetweenTwoGrids()
        {
            ThermalNode hot, cold;

            ShipAssembly assembly = Pair(true, out hot, out cold);

            Assert.Single(assembly.Bridges);

            float before = (hot.Temperature * hot.ThermalMass) + (cold.Temperature * cold.ThermalMass);

            for (int i = 0; i < 200; i++) assembly.Step(Worlds.Shadow(), 1f / 8f);

            Assert.True(cold.Temperature > 305f,
                "the cold grid should have warmed across the joint: " + cold.Temperature);
            Assert.True(hot.Temperature < 795f,
                "and the hot one cooled: " + hot.Temperature);

            float after = (hot.Temperature * hot.ThermalMass) + (cold.Temperature * cold.ThermalMass);
            Assert.Equal(before, after, before * 0.001f);
        }

        [Fact]

        public void NothingCrossesWithoutOne()
        {
            ThermalNode hot, cold;

            ShipAssembly assembly = Pair(false, out hot, out cold);

            Assert.Empty(assembly.Bridges);

            for (int i = 0; i < 200; i++) assembly.Step(Worlds.Shadow(), 1f / 8f);

            Assert.Equal(800f, hot.Temperature, 1);
            Assert.Equal(300f, cold.Temperature, 1);
        }

        [Fact]

        public void AJointCannotOvershootThePairItConnects()
        {
            ThermalNode hot, cold;

            ShipAssembly assembly = Pair(true, out hot, out cold);

            assembly.Step(Worlds.Shadow(), 60f);

            float low = Math.Min(300f, 800f);
            float high = Math.Max(300f, 800f);

            Assert.InRange(hot.Temperature, low, high);
            Assert.InRange(cold.Temperature, low, high);
        }
    }
}
