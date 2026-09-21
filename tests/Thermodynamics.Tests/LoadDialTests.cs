using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    public class LoadDialTests
    {
/// <summary>Applies the .</summary>
        private static BlockThermalProperties Apply(PairLab.Cell cell, BlockThermalProperties source)
        {
            Func<string, string, BlockThermalProperties, BlockThermalProperties> material =
                cell.Material();

            return material == null ? source : material("TypeId", "Subtype", source);
        }

/// <summary>Sample operation.</summary>
        private static BlockThermalProperties Sample()
        {
            BlockThermalProperties properties = BlockThermalProperties.Default();
            properties.Conductivity = 50f;
            properties.ProducerWasteEnergy = 0.4f;
            properties.ConsumerWasteEnergy = 0.2f;
            properties.SpecificHeat = 500f;
            properties.Emissivity = 0.6f;
            properties.CriticalTemperature = 900f;
            return properties;
        }

        [Fact]
/// <summary>TheLoadDialMovesBothWasteFractionsAndNothingElse operation.</summary>
        public void TheLoadDialMovesBothWasteFractionsAndNothingElse()
        {
            PairLab.Cell cell = new PairLab.Cell { Conductivity = 1f, Clock = 100f, Waste = 0.5f };
/// <summary>Sample operation.</summary>
            BlockThermalProperties source = Sample();
/// <summary>Applies the .</summary>
            BlockThermalProperties moved = Apply(cell, source);

            Assert.Equal(0.2f, moved.ProducerWasteEnergy, 4);
            Assert.Equal(0.1f, moved.ConsumerWasteEnergy, 4);

            Assert.Equal(source.Conductivity, moved.Conductivity, 4);
            Assert.Equal(source.SpecificHeat, moved.SpecificHeat, 4);
            Assert.Equal(source.Emissivity, moved.Emissivity, 4);
            Assert.Equal(source.CriticalTemperature, moved.CriticalTemperature, 4);

            Assert.Equal(0.4f, source.ProducerWasteEnergy, 4);
        }

        [Fact]
/// <summary>ACellThatMovesNothingInstallsNoOverrideAtAll operation.</summary>
        public void ACellThatMovesNothingInstallsNoOverrideAtAll()
        {
            PairLab.Cell control = new PairLab.Cell { Conductivity = 1f, Clock = 225f, Waste = 1f };
            Assert.Null(control.Material());

            Assert.NotNull(new PairLab.Cell { Conductivity = 1f, Clock = 225f, Waste = 2f }.Material());
            Assert.NotNull(new PairLab.Cell { Conductivity = 4f, Clock = 225f, Waste = 1f }.Material());
        }

        [Fact]
/// <summary>EveryStateThatExistedPutsTheDriveWhereItAlwaysWas operation.</summary>
        public void EveryStateThatExistedPutsTheDriveWhereItAlwaysWas()
        {
            Assert.Equal(0f, ShipLoad.State.Idle.Drives, 4);
            Assert.Equal(0f, ShipLoad.State.Burn(0).Drives, 4);
            Assert.Equal(1f, ShipLoad.State.Full.Drives, 4);
            Assert.Equal(1f, ShipLoad.State.Everything.Drives, 4);

            Assert.Equal(ShipLoad.State.Idle.Tools, ShipLoad.State.Idle.Drives, 4);
            Assert.Equal(ShipLoad.State.Burn(0).Tools, ShipLoad.State.Burn(0).Drives, 4);
            Assert.Equal(ShipLoad.State.Full.Tools, ShipLoad.State.Full.Drives, 4);
            Assert.Equal(ShipLoad.State.Everything.Tools, ShipLoad.State.Everything.Drives, 4);

            Assert.Equal(1f, ShipLoad.State.Charged.Tools, 4);
            Assert.Equal(0f, ShipLoad.State.Charged.Drives, 4);
        }

        [Fact]
/// <summary>ADriveIsNotATool operation.</summary>
        public void ADriveIsNotATool()
        {
            Assert.True(ShipLoad.IsDrive("JumpDrive"));
            Assert.False(ShipLoad.IsDrive("Refinery"));
            Assert.False(ShipLoad.IsDrive("BatteryBlock"));

            Assert.False(ShipLoad.IsStore("JumpDrive"));
        }

        [Fact]
/// <summary>ADriveChargesForAsLongAsItsOwnDefinitionSays operation.</summary>
        public void ADriveChargesForAsLongAsItsOwnDefinitionSays()
        {
            Dictionary<string, GameBlocks.Definition> byType = GameBlocks.BySubtype();
            if (byType.Count == 0) return;

            GameBlocks.Definition drive;
            Assert.True(byType.TryGetValue("LargeJumpDrive", out drive),
                "the game's own LargeJumpDrive definition was not read, so this cannot check it");

            Assert.Equal(32000000f, drive.PowerDrawWatts, 0);
            Assert.Equal(0.8f, drive.PowerEfficiency, 3);
            Assert.Equal(3f * 3.6e9f, drive.JumpEnergyJoules, 0);

            float seconds = drive.JumpEnergyJoules / (drive.PowerDrawWatts * drive.PowerEfficiency);
            Assert.Equal(421.9f, seconds, 1);

            Assert.True(seconds > 300f);
        }

        [Fact]
/// <summary>ABlockThatDoesNotChargeCarriesNoChargeAtAll operation.</summary>
        public void ABlockThatDoesNotChargeCarriesNoChargeAtAll()
        {
            Dictionary<string, GameBlocks.Definition> byType = GameBlocks.BySubtype();
            if (byType.Count == 0) return;

            GameBlocks.Definition armour;
            if (!byType.TryGetValue("LargeBlockArmorBlock", out armour)) return;

            Assert.Equal(0f, armour.JumpEnergyJoules, 4);

            Assert.Equal(0f, armour.PowerEfficiency, 4);
            Assert.Equal(-1f, BlockThermalDerivation.WasteFromEfficiency(armour.PowerEfficiency), 4);

            Assert.Equal(0.2f, BlockThermalDerivation.WasteFromEfficiency(0.8f), 4);
            Assert.Equal(0.1f, BlockThermalDerivation.WasteFromEfficiency(0.9f), 4);
        }

        [Fact]
/// <summary>TheLoadGridHasExactlyOneControlAndItRunsFirst operation.</summary>
        public void TheLoadGridHasExactlyOneControlAndItRunsFirst()
        {
            List<PairLab.Cell> cells = PairLab.Load();

            Assert.True(cells.Count > 1);
            Assert.True(cells[0].IsShipped, "the control must run first");

            int controls = 0;
            foreach (PairLab.Cell cell in cells) if (cell.IsShipped) controls++;

            Assert.Equal(1, controls);
        }

        [Fact]
/// <summary>TheShippedPairIsNotAWasteMultiplierAwayFromItself operation.</summary>
        public void TheShippedPairIsNotAWasteMultiplierAwayFromItself()
        {
            PairLab.Cell halved = new PairLab.Cell
            { Conductivity = 1f, Clock = PairLab.ShippedClock, Waste = 0.5f };

            Assert.False(halved.IsShipped);
            Assert.Contains("w0.5", halved.Name);

            PairLab.Cell plain = new PairLab.Cell { Conductivity = 4f, Clock = 80f };
            Assert.Equal("k4-h80", plain.Name);
        }
    }
}
