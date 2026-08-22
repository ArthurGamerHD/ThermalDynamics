using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// That a material override actually reaches a block built the corpus way.
    ///
    /// <para>
    /// This is the load-bearing assertion under every knob sweep. Models are built once and shared,
    /// with the derived thermal properties baked in, so an override that arrives after the cache is
    /// warm applies to nothing at all — and the failure is silent in the worst way: the sweep runs,
    /// every configuration produces numbers, and every dial reads as having no effect. A response
    /// surface of flat lines looks like a finding rather than a bug.
    /// </para>
    /// </summary>
    public class MaterialOverrideTests
    {
        [Fact]
        public void AnOverrideReachesABlockBuiltTheCorpusWay()
        {
            if (!GameBlocks.IsInstalled) return;

            GameBlocks.Definition definition = Pick();
            if (definition == null) return;

            try
            {
                Blueprints.MaterialOverride = null;
                BlockThermalProperties baseline = Blueprints.Model(definition).Thermal;
                float baseHeat = baseline.SpecificHeat;
                float baseEmissivity = baseline.Emissivity;

                Assert.True(baseHeat > 0f, "the baseline block has no specific heat to scale");

                Blueprints.MaterialOverride = source => new BlockThermalProperties
                {
                    Conductivity = source.Conductivity,
                    SpecificHeat = source.SpecificHeat * 4f,
                    Emissivity = source.Emissivity * 0.5f,
                    ExposedSurfaceMultiplier = source.ExposedSurfaceMultiplier,
                    ProducerWasteEnergy = source.ProducerWasteEnergy,
                    ConsumerWasteEnergy = source.ConsumerWasteEnergy,
                    CriticalTemperature = source.CriticalTemperature,
                    OverheatDamagePerKelvin = source.OverheatDamagePerKelvin,
                };

                BlockThermalProperties scaled = Blueprints.Model(definition).Thermal;

                Assert.Equal(baseHeat * 4f, scaled.SpecificHeat, 3);
                Assert.Equal(baseEmissivity * 0.5f, scaled.Emissivity, 4);

                // And it comes back. A sweep sets an override per configuration and clears it
                // between, so an override that stuck would contaminate every later measurement.
                Blueprints.MaterialOverride = null;
                Assert.Equal(baseHeat, Blueprints.Model(definition).Thermal.SpecificHeat, 3);
            }
            finally
            {
                Blueprints.MaterialOverride = null;
            }
        }

        /// <summary>A definition with real components, so its derived properties are not the fallback.</summary>
        private static GameBlocks.Definition Pick()
        {
            Dictionary<string, GameBlocks.Definition> definitions = GameBlocks.BySubtype();

            GameBlocks.Definition armour;
            if (definitions.TryGetValue("LargeBlockArmorBlock", out armour)
                && armour.Components.Count > 0)
            {
                return armour;
            }

            foreach (KeyValuePair<string, GameBlocks.Definition> entry in definitions)
            {
                if (entry.Value.Components.Count > 0) return entry.Value;
            }
            return null;
        }
    }
}
