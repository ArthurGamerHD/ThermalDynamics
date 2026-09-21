using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class HeatGenerationTests
    {
/// <summary>Isolated operation.</summary>
        private static ThermalSettings Isolated()
        {
            ThermalSettings settings = new ThermalSettings { Frequency = 4 };
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableFriction = false;
            settings.EnableDamage = false;
            return settings.Derive();
        }

        [Fact]
/// <summary>GeneratedPowerBecomesHeatAtTheDeclaredFraction operation.</summary>
        public void GeneratedPowerBecomesHeatAtTheDeclaredFraction()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Producing(10f * ThermalConstants.MegawattsToWatts);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated(), 300f);
            ThermalNode node = simulation.Solver.Nodes[0];

            float waste = 10f * ThermalConstants.MegawattsToWatts * node.Thermal.ProducerWasteEnergy;
            Assert.True(waste > 0f, "the reactor wastes nothing, so this judges nothing");
            Assert.Equal(waste, node.HeatGenerationWatts, 0);

            float before = node.Temperature;
            simulation.StepExact(4, Worlds.Shadow());   // one simulated second

            float expected = before + (waste / node.ThermalMass);
            Assert.Equal(expected, node.Temperature, 1);
        }

        [Fact]
/// <summary>ConsumedPowerAndThrustBothCount operation.</summary>
        public void ConsumedPowerAndThrustBothCount()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.Thruster(), Vector3I.Zero)
                   .Consuming(1e6f)
                   .Thrusting(3e6f);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated(), 300f);
            ThermalNode node = simulation.Solver.Nodes[0];

            Assert.Equal(1e6f, node.HeatGenerationWatts, 0);
        }

        [Fact]
/// <summary>ProducerAndConsumerFractionsAreIndependent operation.</summary>
        public void ProducerAndConsumerFractionsAreIndependent()
        {
            BlockThermalProperties thermal = Catalog.DefaultThermal();
            thermal.ProducerWasteEnergy = 0.1f;
            thermal.ConsumerWasteEnergy = 0.5f;

            GridBuilder builder = GridBuilder.Large();
            builder.Place(BlockModel.Solid("Mixed", Vector3I.One, 1000f, thermal), Vector3I.Zero)
                   .Producing(1e6f)
                   .Consuming(1e6f);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated(), 300f);
            Assert.Equal(0.6e6f, simulation.Solver.Nodes[0].HeatGenerationWatts, 0);
        }

        [Fact]
/// <summary>ChangingPowerTakesEffectAfterARefresh operation.</summary>
        public void ChangingPowerTakesEffectAfterARefresh()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.Reactor(), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated(), 300f);
            ThermalNode node = simulation.Solver.Nodes[0];
            Assert.Equal(0f, node.HeatGenerationWatts, 3);

            node.Block.PowerProducedWatts = 4e6f;
            node.RefreshHeatGeneration();

            Assert.Equal(4e6f * node.Thermal.ProducerWasteEnergy, node.HeatGenerationWatts, 0);
        }

        [Fact]
/// <summary>HeatGenerationIsIndependentOfStepRate operation.</summary>
        public void HeatGenerationIsIndependentOfStepRate()
        {
/// <summary>TemperatureAfterOneSecond operation.</summary>
            float atFour = TemperatureAfterOneSecond(4);
/// <summary>TemperatureAfterOneSecond operation.</summary>
            float atSixteen = TemperatureAfterOneSecond(16);

            Assert.Equal(atFour, atSixteen, 2);
        }

/// <summary>TemperatureAfterOneSecond operation.</summary>
        private static float TemperatureAfterOneSecond(int frequency)
        {
/// <summary>Isolated operation.</summary>
            ThermalSettings settings = Isolated();
            settings.Frequency = frequency;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Producing(10f * ThermalConstants.MegawattsToWatts);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);
            simulation.StepExact(frequency, Worlds.Shadow());
            return simulation.Solver.Nodes[0].Temperature;
        }
    }

    public class DamageTests
    {
/// <summary>Overheated operation.</summary>
        private static ThermalSimulation Overheated(ThermalSettings settings, float temperature)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(settings, temperature);
            return simulation;
        }

/// <summary>NoTransfer operation.</summary>
        private static ThermalSettings NoTransfer(int frequency)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableFriction = false;
            settings.EnableDamage = true;
            settings.Frequency = frequency;
            return settings.Derive();
        }

        [Fact]
/// <summary>BelowCriticalNothingIsDamaged operation.</summary>
        public void BelowCriticalNothingIsDamaged()
        {
/// <summary>Overheated operation.</summary>
            ThermalSimulation simulation = Overheated(NoTransfer(4), 500f);
            simulation.StepExact(4, Worlds.Shadow());

            Assert.Empty(simulation.Overheats);
        }

        [Fact]
/// <summary>AboveCriticalTheBlockIsReported operation.</summary>
        public void AboveCriticalTheBlockIsReported()
        {
/// <summary>Overheated operation.</summary>
            ThermalSimulation simulation = Overheated(NoTransfer(4), 1000f);
            simulation.StepExact(1, Worlds.Shadow());

            Assert.Single(simulation.Overheats);
            Assert.Equal(1000f, simulation.Overheats[0].Temperature, 1);
            Assert.True(simulation.Overheats[0].Damage > 0f);
        }

        [Fact]
/// <summary>DamagePerSecondDoesNotDependOnStepRate operation.</summary>
        public void DamagePerSecondDoesNotDependOnStepRate()
        {
/// <summary>TotalDamageOverOneSecond operation.</summary>
            float atFour = TotalDamageOverOneSecond(NoTransfer(4));
/// <summary>TotalDamageOverOneSecond operation.</summary>
            float atSixteen = TotalDamageOverOneSecond(NoTransfer(16));

            Assert.Equal(100f, atFour, 1);
            Assert.Equal(100f, atSixteen, 1);
        }

        [Fact]
/// <summary>TheOldPerStepBehaviourIsStillAvailableAndScalesWithFrequency operation.</summary>
        public void TheOldPerStepBehaviourIsStillAvailableAndScalesWithFrequency()
        {
/// <summary>NoTransfer operation.</summary>
            ThermalSettings four = NoTransfer(4);
            four.DamageIsPerSecond = false;

/// <summary>NoTransfer operation.</summary>
            ThermalSettings sixteen = NoTransfer(16);
            sixteen.DamageIsPerSecond = false;

/// <summary>TotalDamageOverOneSecond operation.</summary>
            float atFour = TotalDamageOverOneSecond(four);
/// <summary>TotalDamageOverOneSecond operation.</summary>
            float atSixteen = TotalDamageOverOneSecond(sixteen);

            Assert.Equal(400f, atFour, 1);
            Assert.Equal(1600f, atSixteen, 1);
        }

/// <summary>TotalDamageOverOneSecond operation.</summary>
        private static float TotalDamageOverOneSecond(ThermalSettings settings)
        {
/// <summary>Overheated operation.</summary>
            ThermalSimulation simulation = Overheated(settings, 1000f);

            float total = 0f;
            for (int i = 0; i < settings.Frequency; i++)
            {
                simulation.StepExact(1, Worlds.Shadow());
                for (int d = 0; d < simulation.Overheats.Count; d++)
                {
                    total += simulation.Overheats[d].Damage;
                }
            }
            return total;
        }

        [Fact]
/// <summary>DamageScalesWithOvershootAndScaler operation.</summary>
        public void DamageScalesWithOvershootAndScaler()
        {
            BlockThermalProperties thermal = Catalog.DefaultThermal();
            thermal.CriticalTemperature = 500f;
            thermal.OverheatDamagePerKelvin = 2f;

            GridBuilder builder = GridBuilder.Large();
            builder.Place(BlockModel.Solid("Fragile", Vector3I.One, 500f, thermal), Vector3I.Zero);

/// <summary>NoTransfer operation.</summary>
            ThermalSettings settings = NoTransfer(1);
            ThermalSimulation simulation = builder.BuildSimulation(settings, 600f);

            simulation.StepExact(1, Worlds.Shadow());

            Assert.Equal(200f, simulation.Overheats[0].Damage, 1);
        }

        [Fact]
/// <summary>DisablingDamageSilencesTheEvents operation.</summary>
        public void DisablingDamageSilencesTheEvents()
        {
/// <summary>NoTransfer operation.</summary>
            ThermalSettings settings = NoTransfer(4);
            settings.EnableDamage = false;

/// <summary>Overheated operation.</summary>
            ThermalSimulation simulation = Overheated(settings, 2000f);
            simulation.StepExact(4, Worlds.Shadow());

            Assert.Empty(simulation.Overheats);
        }
    }
}
