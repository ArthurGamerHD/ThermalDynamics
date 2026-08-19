using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Specific heat is stated in real J/(kg K), and the game's pace comes from one global
    /// factor that divides every heat capacity.
    ///
    /// The claim that makes that honest is that dividing capacity is <em>exactly</em> running
    /// thermal time faster: nothing about the physics changes, only the clock. These tests hold
    /// that claim to account, because if it is wrong the setting is not a time scale, it is a
    /// silent rebalance.
    /// </summary>
    public class HeatTimeScaleTests
    {
        private static ThermalSettings Settings(float scale, int frequency = 4)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.HeatTimeScale = scale;
            settings.Frequency = frequency;
            return settings.Derive();
        }

        private static ThermalSimulation Cube(ThermalSettings settings, float temperature)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));
            ThermalSimulation simulation = builder.BuildSimulation(settings, temperature);
            simulation.RebuildAll();
            return simulation;
        }

        [Fact]
        public void CapacityIsDividedByTheScale()
        {
            ThermalSimulation physical = Cube(Settings(1f), 300f);
            ThermalSimulation accelerated = Cube(Settings(225f), 300f);

            float a = physical.Solver.Nodes[0].ThermalMass;
            float b = accelerated.Solver.Nodes[0].ThermalMass;

            Assert.Equal(225f, a / b, 2);
        }

        /// <summary>
        /// The central claim: k× the scale for t seconds equals 1× the scale for k×t seconds.
        /// Radiation is quartic in temperature, so this is not a trivial identity — it holds
        /// because every mechanism is a rate divided by the same capacity.
        /// </summary>
        [Fact]
        public void ScalingCapacityIsTheSameAsRunningTimeFaster()
        {
            const int scale = 20;

            ThermalSimulation fast = Cube(Settings(scale), 800f);
            ThermalSimulation slow = Cube(Settings(1f), 800f);

            // one simulated minute at scale 20 against twenty simulated minutes at scale 1
            fast.StepExact(60 * 4, Worlds.Shadow());
            slow.StepExact(60 * 4 * scale, Worlds.Shadow());

            IList<ThermalNode> a = fast.Solver.Nodes;
            IList<ThermalNode> b = slow.Solver.Nodes;

            // Not bit-identical: the two runs choose different substep counts, so they
            // discretise the same curve differently. Agreement to a fraction of a percent is
            // the claim — that this is one curve, sampled twice.
            for (int i = 0; i < a.Count; i++)
            {
                float relative = Math.Abs(a[i].Temperature - b[i].Temperature) / b[i].Temperature;
                Assert.True(relative < 0.005f,
                    "node " + i + ": " + a[i].Temperature + " vs " + b[i].Temperature);
            }
        }

        /// <summary>
        /// Equilibrium is set by watts in against watts out, and the scale divides both. A
        /// player changing the pace must not find their reactor settling somewhere else.
        /// </summary>
        [Fact]
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

                // Long enough that even the slowest scale here has settled.
                simulation.StepExact(30000, Worlds.Shadow());

                float reactor = simulation.Solver.GetNodeAt(Vector3I.Zero).Temperature;

                if (s == 0) first = reactor;
                else Assert.Equal(first, reactor, 0);
            }
        }

        /// <summary>
        /// Raising the scale makes the system stiffer, which is the price of the acceleration.
        /// The solver has to absorb that in substeps rather than in wrong answers.
        /// </summary>
        [Fact]
        public void AHigherScaleCostsSubstepsNotAccuracy()
        {
            ThermalSimulation gentle = Cube(Settings(1f), 800f);
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
        public void CoolantRunsOnTheSameClockAsTheBlocksItCools()
        {
            LoopThermalProperties properties = LoopThermalProperties.Default();
            properties.SpecificHeat = 3400f;
            properties.MassPerPipe = 500f;

            CoolantLoop physical = new CoolantLoop(properties, 300f, 1f);
            CoolantLoop accelerated = new CoolantLoop(properties, 300f, 225f);

            Assert.Equal(225f, physical.ThermalMass / accelerated.ThermalMass, 2);
        }

        [Fact]
        public void AnInvalidScaleFallsBackToPhysical()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.HeatTimeScale = 0f;
            settings.Derive();

            Assert.Equal(1f, settings.HeatTimeScale);

            ThermalSettings negative = new ThermalSettings();
            negative.HeatTimeScale = -5f;
            negative.Derive();

            Assert.Equal(1f, negative.HeatTimeScale);
        }

        /// <summary>
        /// The definitions now carry real material values. This is the arithmetic that says the
        /// shipped default reproduces the pace the mod had when they were written as 1–6.
        /// </summary>
        [Fact]
        public void TheShippedDefaultReproducesTheOldEffectiveCapacity()
        {
            ThermalSettings settings = new ThermalSettings();

            const float steel = 450f;      // Data/Cubes.xml, DefaultThermodynamics
            const float legacyValue = 2f;  // what that definition used to carry

            Assert.Equal(legacyValue, steel / settings.HeatTimeScale, 3);
        }
    }
}
