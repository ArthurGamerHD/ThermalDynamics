using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// What a block is made of, turned into what a block is thermally.
    ///
    /// Before this existed, twenty-eight block types out of a hundred and one had hand-written
    /// thermal properties and everything else in the game — and every block of every other mod —
    /// fell through to a single entry describing mild steel. A window, a battery, a medical bay and
    /// a plushie were the same object. The game already publishes what every block is built from,
    /// so none of that had to be true.
    /// </summary>
    public class BlockDerivationTests
    {
        private static List<BlockComponent> Of(params BlockComponent[] components)
        {
            return new List<BlockComponent>(components);
        }

        private static BlockComponent Part(string component, int count, float massEach)
        {
            return new BlockComponent(component, count, massEach);
        }

        /// <summary>
        /// Heat capacity is additive, so the mass-weighted mean specific heat is exactly right
        /// rather than an approximation. Half steel at 466 and half glass at 840 by mass is 653.
        /// </summary>
        [Fact]
        public void SpecificHeatIsTheMassWeightedMeanExactly()
        {
            BlockThermalProperties p = BlockThermalDerivation.Material(Of(
                Part("SteelPlate", 1, 100f),
                Part("BulletproofGlass", 1, 100f)));

            Assert.Equal((466f + 840f) / 2f, p.SpecificHeat, 1);
        }

        [Fact]
        public void AMassiveComponentDominatesALightOne()
        {
            BlockThermalProperties p = BlockThermalDerivation.Material(Of(
                Part("SteelPlate", 99, 20f),
                Part("Computer", 1, 0.2f)));

            Assert.Equal(BlockMaterials.Steel.SpecificHeat, p.SpecificHeat, 0);
            Assert.Equal(BlockMaterials.Steel.Conductivity, p.Conductivity, 0);
        }

        /// <summary>
        /// The single largest material distinction in the game. Glass conducts two orders of
        /// magnitude worse than steel and radiates six times better, and a window made of it must
        /// come out on the glass side of that gap rather than in the middle.
        /// </summary>
        [Fact]
        public void AWindowIsGlassAndAnArmourBlockIsSteel()
        {
            BlockThermalProperties window = BlockThermalDerivation.Material(Of(
                Part("BulletproofGlass", 10, 15f),
                Part("Construction", 4, 8f)));

            BlockThermalProperties armour = BlockThermalDerivation.Material(Of(
                Part("SteelPlate", 25, 20f)));

            Assert.True(window.Conductivity < armour.Conductivity / 4f,
                "window conductivity " + window.Conductivity + " is not far below steel's " + armour.Conductivity);
            Assert.True(window.Emissivity > armour.Emissivity * 3f,
                "window emissivity " + window.Emissivity + " does not beat steel's " + armour.Emissivity);
        }

        /// <summary>
        /// Emissivity is a surface property, so it comes from the cladding — the heaviest component
        /// — rather than from a blend. A blend would give a glass box the emissivity of its steel
        /// frame in proportion to a mass that is not on the outside of it.
        /// </summary>
        [Fact]
        public void EmissivityComesFromTheHeaviestComponentRatherThanTheBlend()
        {
            BlockThermalProperties p = BlockThermalDerivation.Material(Of(
                Part("BulletproofGlass", 10, 15f),
                Part("SteelPlate", 1, 20f)));

            Assert.Equal(BlockMaterials.Get("BulletproofGlass").Emissivity, p.Emissivity, 3);
        }

        /// <summary>
        /// A battery is the least heat-tolerant thing on a ship and a reactor is the most, and
        /// neither fact was written down anywhere before: both now follow from lithium cells giving
        /// out at 360 K and reactor assemblies being built to run at 1,200.
        /// </summary>
        [Fact]
        public void CriticalTemperatureSeparatesABatteryFromAReactor()
        {
            BlockThermalProperties battery = BlockThermalDerivation.Material(Of(
                Part("PowerCell", 80, 25f),
                Part("SteelPlate", 80, 20f)));

            BlockThermalProperties reactor = BlockThermalDerivation.Material(Of(
                Part("Reactor", 100, 25f),
                Part("SteelPlate", 50, 20f)));

            Assert.True(battery.CriticalTemperature < reactor.CriticalTemperature - 200f,
                "battery " + battery.CriticalTemperature + " K against reactor " + reactor.CriticalTemperature + " K");
        }

        /// <summary>
        /// The defect this fixes outright, from docs/stiffness.md: a plushie was simulated as a
        /// kilogram of steel with a heat capacity of 2 J/K, which made it the stiffest object on a
        /// fleet and set the substep count for whole capital ships. Fabric is nearly an insulator
        /// and holds a great deal of heat per kilogram, and now says so.
        /// </summary>
        [Fact]
        public void APlushieIsFabricRatherThanSteel()
        {
            BlockThermalProperties p = BlockThermalDerivation.Material(Of(
                Part("EngineerPlushie", 1, 1f)));

            Assert.True(p.Conductivity < 1f, "a plushie conducts at " + p.Conductivity);
            Assert.True(p.SpecificHeat > 1000f, "a plushie holds " + p.SpecificHeat + " J/(kg K)");
        }

        /// <summary>
        /// A definition with no priced components must come out as the steel it would have been
        /// before any of this existed, rather than as a block with no heat capacity.
        /// </summary>
        [Fact]
        public void ABlockWithNoComponentsIsSteel()
        {
            foreach (List<BlockComponent> components in new List<BlockComponent>[]
            {
                null,
                new List<BlockComponent>(),
                Of(Part("SteelPlate", 0, 20f)),
            })
            {
                BlockThermalProperties p = BlockThermalDerivation.Material(components);

                Assert.Equal(BlockMaterials.Steel.Conductivity, p.Conductivity, 2);
                Assert.Equal(BlockMaterials.Steel.SpecificHeat, p.SpecificHeat, 2);
                Assert.Equal(BlockMaterials.Steel.ServiceLimit, p.CriticalTemperature, 2);
            }
        }

        [Fact]
        public void AnUnknownComponentIsTreatedAsSteel()
        {
            BlockThermalProperties p = BlockThermalDerivation.Material(Of(
                Part("SomeOtherModsExoticAlloy", 10, 20f)));

            Assert.Equal(BlockMaterials.Steel.SpecificHeat, p.SpecificHeat, 2);
        }

        /// <summary>
        /// The functional half cannot be derived and is not: two blocks of identical construction,
        /// one a thruster and one a girder, differ entirely in what they put into the ship.
        /// </summary>
        [Fact]
        public void FunctionComesFromTheTypeAndNotFromTheComponents()
        {
            List<BlockComponent> same = Of(Part("SteelPlate", 10, 20f));

            BlockThermalProperties thruster = ShippedBlocks.DeriveWithFunction(same, "Thrust");
            BlockThermalProperties girder = ShippedBlocks.DeriveWithFunction(same, "CubeBlock");

            Assert.Equal(thruster.SpecificHeat, girder.SpecificHeat, 3);
            Assert.True(thruster.ConsumerWasteEnergy > girder.ConsumerWasteEnergy);
            Assert.True(thruster.ExposedSurfaceMultiplier > girder.ExposedSurfaceMultiplier);
        }

        /// <summary>
        /// A producer's heat runs through the producer fraction and nothing else, which is the
        /// mistake that left every reactor in the game at 0 W. Any type the table calls a producer
        /// must carry one.
        /// </summary>
        [Theory]
        [InlineData("Reactor")]
        [InlineData("HydrogenEngine")]
        [InlineData("BatteryBlock")]
        public void EveryProducerTypeConvertsSomeOfItsOutput(string typeId)
        {
            Assert.True(ShippedBlocks.FunctionOf(typeId).ProducerWasteEnergy > 0f,
                typeId + " produces power and makes no heat doing it");
        }

        /// <summary>
        /// A hydrogen engine burns fuel for electricity and should be the hottest producer in the
        /// game per watt delivered — hotter than a reactor, whose fraction is held down only
        /// because Space Engineers rates a 3x3x3 block at 300 MW.
        /// </summary>
        [Fact]
        public void ACombustionEngineRunsHotterThanAReactorPerWatt()
        {
            Assert.True(
                ShippedBlocks.FunctionOf("HydrogenEngine").ProducerWasteEnergy >
                ShippedBlocks.FunctionOf("Reactor").ProducerWasteEnergy);
        }

        /// <summary>
        /// Nothing may claim to turn more energy into heat than passed through it. The solver would
        /// not stop it and the grid would gain energy from nothing.
        /// </summary>
        [Fact]
        public void NoFunctionCreatesEnergyFromNothing()
        {
            foreach (string typeId in ShippedBlocks.FunctionTypes())
            {
                ShippedBlocks.Function f = ShippedBlocks.FunctionOf(typeId);

                Assert.True(f.ProducerWasteEnergy <= 1f, typeId + " producer fraction " + f.ProducerWasteEnergy);
                Assert.True(f.ConsumerWasteEnergy <= 1f, typeId + " consumer fraction " + f.ConsumerWasteEnergy);
                Assert.True(f.ExposedSurfaceMultiplier > 0f, typeId + " has no surface");
                Assert.True(f.OverheatDamagePerKelvin >= 0f, typeId + " has a negative damage rate");
            }
        }

        /// <summary>
        /// Every derived block must survive its own <c>Validate</c>, which is the check a mod
        /// author's hand-written definition gets. A generator that emits values the validator would
        /// complain about is worse than a hand-written one.
        /// </summary>
        [Fact]
        public void EveryMaterialProducesAValidBlock()
        {
            foreach (string component in BlockMaterials.Names)
            {
                BlockThermalProperties p = ShippedBlocks.DeriveWithFunction(
                    Of(Part(component, 10, 20f)), "CubeBlock");

                Assert.Empty(p.Validate());
                Assert.True(p.SpecificHeat > 0f, component + " has no heat capacity");
                Assert.True(p.CriticalTemperature > 0f, component + " is critical at placement");
            }
        }

        /// <summary>
        /// A third of the material table is invented, because a third of the components are. The
        /// invention is kept modest on purpose: an imaginary alloy may flavour a block but must not
        /// take it anywhere the real materials could not, or the derivation stops being a
        /// description and becomes a balance lever with a physics-shaped name.
        /// </summary>
        [Fact]
        public void EveryInventedMaterialSitsInsideTheRangeOfTheRealOnes()
        {
            CheckInsideRealRange(delegate (BlockMaterial m) { return m.Conductivity; }, "conductivity");
            CheckInsideRealRange(delegate (BlockMaterial m) { return m.SpecificHeat; }, "specific heat");
            CheckInsideRealRange(delegate (BlockMaterial m) { return m.Emissivity; }, "emissivity");
            CheckInsideRealRange(delegate (BlockMaterial m) { return m.ServiceLimit; }, "service limit");
        }

        private static void CheckInsideRealRange(System.Func<BlockMaterial, float> property, string name)
        {
            float lowest, highest;
            BlockMaterials.RealRange(property, out lowest, out highest);

            foreach (string component in BlockMaterials.Names)
            {
                BlockMaterial material = BlockMaterials.Get(component);
                if (!material.Invented) continue;

                float value = property(material);
                Assert.True(value >= lowest && value <= highest,
                    component + " invents a " + name + " of " + value
                    + ", outside the real range " + lowest + " to " + highest);
            }
        }
    }
}
