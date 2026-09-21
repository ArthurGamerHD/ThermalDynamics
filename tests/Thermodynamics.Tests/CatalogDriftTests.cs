using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    public class CatalogDriftTests
    {
        private readonly ITestOutputHelper output;

/// <summary>CatalogDriftTests operation.</summary>
        public CatalogDriftTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private class Stand
        {
            public string Catalogue;
            public float CatalogueMass;
            public string Vanilla;
        }

/// <summary>Stands operation.</summary>
        private static List<Stand> Stands()
        {
            return new List<Stand>
            {
/// <summary>Mass operation.</summary>
                new Stand { Catalogue = "LightArmorBlock", CatalogueMass = Mass(Catalog.LightArmor()), Vanilla = "LargeBlockArmorBlock" },
/// <summary>Mass operation.</summary>
                new Stand { Catalogue = "HeavyArmorBlock", CatalogueMass = Mass(Catalog.HeavyArmor()), Vanilla = "LargeHeavyBlockArmorBlock" },
/// <summary>Mass operation.</summary>
                new Stand { Catalogue = "SmallReactor", CatalogueMass = Mass(Catalog.Reactor()), Vanilla = "LargeBlockSmallGenerator" },
/// <summary>Mass operation.</summary>
                new Stand { Catalogue = "LargeReactor", CatalogueMass = Mass(Catalog.LargeReactor()), Vanilla = "LargeBlockLargeGenerator" },
/// <summary>Mass operation.</summary>
                new Stand { Catalogue = "Battery", CatalogueMass = Mass(Catalog.Battery()), Vanilla = "LargeBlockBatteryBlock" },
/// <summary>Mass operation.</summary>
                new Stand { Catalogue = "LargeThruster", CatalogueMass = Mass(Catalog.Thruster()), Vanilla = "LargeBlockLargeThrust" },
            };
        }

/// <summary>Mass operation.</summary>
        private static float Mass(BlockModel model)
        {
            return model.Mass;
        }

        [Fact]
/// <summary>EveryStandInWeighsWhatItStandsInFor operation.</summary>
        public void EveryStandInWeighsWhatItStandsInFor()
        {
/// <summary>List operation.</summary>
            List<string> drifted = new List<string>();
            float worst = 1f;

            foreach (Stand stand in Stands())
            {
                Vanilla.Block real = Vanilla.Find(stand.Vanilla);
                Assert.True(real != null, "no vanilla reference for " + stand.Vanilla);

                float ratio = real.Mass <= 0f ? 1f : stand.CatalogueMass / real.Mass;
                float apart = ratio > 1f ? ratio : 1f / ratio;
                if (apart > worst) worst = apart;

                if (apart > 1.05f)
                {
                    drifted.Add(string.Format("{0,-18}{1,10:n0} kg against {2}'s {3:n0} — {4:n2}x",
                        stand.Catalogue, stand.CatalogueMass, stand.Vanilla, real.Mass, apart));
                }
            }

            foreach (string line in drifted) output.WriteLine(line);

            Assert.True(drifted.Count == 0,
                "a stand-in has stopped being the block it stands in for:\n  "
                + string.Join("\n  ", drifted.ToArray()));

            Assert.True(worst < 1.001f,
                "the worst stand-in is " + worst.ToString("n4") + "x its block, which is not derived");
        }

        [Fact]
/// <summary>AScenarioReactorWastesWhatAShippedOneWastes operation.</summary>
        public void AScenarioReactorWastesWhatAShippedOneWastes()
        {
            BlockThermalProperties catalogue = Catalog.Reactor().Thermal;

            BlockThermalProperties real =
                ShippedBlocks.DeriveWithFunction(new List<BlockComponent>(), "Reactor");

            Assert.NotNull(real);
            output.WriteLine("catalogue ProducerWasteEnergy " + catalogue.ProducerWasteEnergy
                + " against the shipped " + real.ProducerWasteEnergy);

            Assert.Equal(real.ProducerWasteEnergy, catalogue.ProducerWasteEnergy, 4);
            Assert.Equal(0.01f, catalogue.ProducerWasteEnergy, 4);
        }

        [Fact]
/// <summary>ArmourMatchesExactly operation.</summary>
        public void ArmourMatchesExactly()
        {
            Assert.Equal(Vanilla.Find("LargeBlockArmorBlock").Mass, Mass(Catalog.LightArmor()), 1);
            Assert.Equal(Vanilla.Find("LargeHeavyBlockArmorBlock").Mass, Mass(Catalog.HeavyArmor()), 1);
        }
    }
}
