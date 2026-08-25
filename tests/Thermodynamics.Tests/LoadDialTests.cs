using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The load axis of the pair grid reaches the blocks, and reaches nothing else.**
    ///
    /// <para>
    /// `C12`'s search moved conduction and the clock, which are both transport, and reaching the
    /// significance window that way costs three of the mod's levers. The load is the dial that is
    /// not transport: it changes how much heat a ship makes without changing how the heat moves,
    /// and measured over the retest set it reaches the same window at conductivity ×1. See
    /// balance.md.
    /// </para>
    ///
    /// <para>
    /// **A sweep dial that reaches nothing reports *no change* in exactly the shape of a dial that
    /// reached everything and changed nothing**, which is the failure this repository keeps
    /// finding — the retest set's own `reach.csv` exists for it. So the override is checked here
    /// rather than trusted: both waste fractions move, and nothing else does.
    /// </para>
    /// </summary>
    public class LoadDialTests
    {
        private static BlockThermalProperties Apply(PairLab.Cell cell, BlockThermalProperties source)
        {
            Func<string, string, BlockThermalProperties, BlockThermalProperties> material =
                cell.Material();

            return material == null ? source : material("TypeId", "Subtype", source);
        }

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
        public void TheLoadDialMovesBothWasteFractionsAndNothingElse()
        {
            PairLab.Cell cell = new PairLab.Cell { Conductivity = 1f, Clock = 100f, Waste = 0.5f };
            BlockThermalProperties source = Sample();
            BlockThermalProperties moved = Apply(cell, source);

            Assert.Equal(0.2f, moved.ProducerWasteEnergy, 4);
            Assert.Equal(0.1f, moved.ConsumerWasteEnergy, 4);

            // Everything a load dial must not touch. Conductivity above all: the whole point of
            // this axis is that it is not transport, and a dial that quietly moved it would
            // reproduce the conduction result and be read as a different finding.
            Assert.Equal(source.Conductivity, moved.Conductivity, 4);
            Assert.Equal(source.SpecificHeat, moved.SpecificHeat, 4);
            Assert.Equal(source.Emissivity, moved.Emissivity, 4);
            Assert.Equal(source.CriticalTemperature, moved.CriticalTemperature, 4);

            // And the source itself is untouched, because the catalogue hands out one instance per
            // definition and a mutating override would compound across cells.
            Assert.Equal(0.4f, source.ProducerWasteEnergy, 4);
        }

        [Fact]
        public void ACellThatMovesNothingInstallsNoOverrideAtAll()
        {
            // Null rather than an identity function, so the control shares the model cache with
            // nothing installed and cannot differ from the shipped world by a rounding.
            PairLab.Cell control = new PairLab.Cell { Conductivity = 1f, Clock = 225f, Waste = 1f };
            Assert.Null(control.Material());

            Assert.NotNull(new PairLab.Cell { Conductivity = 1f, Clock = 225f, Waste = 2f }.Material());
            Assert.NotNull(new PairLab.Cell { Conductivity = 4f, Clock = 225f, Waste = 1f }.Material());
        }

        /// <summary>
        /// **The jump drive is not a tool, and moving it off that list moved no published figure.**
        ///
        /// <para>
        /// It was filed with the drills and the turrets, so <see cref="ShipLoad.State.Tools"/>
        /// governed it and <see cref="ShipLoad.State.Consumers"/> — the dial that reads as *the
        /// ship's electrical load* — never reached the block carrying 71.3 % of the corpus's
        /// full-load waste heat. That is a dial nobody could use rather than a wrong number: in all
        /// four states that existed the two shares agree, which is what makes the reclassification
        /// safe and is what this pins.
        /// </para>
        /// </summary>
        [Fact]
        public void EveryStateThatExistedPutsTheDriveWhereItAlwaysWas()
        {
            // Idle and Burn had their tools off, Full and Everything had them on. The drive must
            // land on the same side of each, or a figure taken before this pass moves under it.
            Assert.Equal(0f, ShipLoad.State.Idle.Drives, 4);
            Assert.Equal(0f, ShipLoad.State.Burn(0).Drives, 4);
            Assert.Equal(1f, ShipLoad.State.Full.Drives, 4);
            Assert.Equal(1f, ShipLoad.State.Everything.Drives, 4);

            // And each still matches the tool share it used to be read off, which is the property
            // that makes "no figure moved" true rather than merely intended.
            Assert.Equal(ShipLoad.State.Idle.Tools, ShipLoad.State.Idle.Drives, 4);
            Assert.Equal(ShipLoad.State.Burn(0).Tools, ShipLoad.State.Burn(0).Drives, 4);
            Assert.Equal(ShipLoad.State.Full.Tools, ShipLoad.State.Full.Drives, 4);
            Assert.Equal(ShipLoad.State.Everything.Tools, ShipLoad.State.Everything.Drives, 4);

            // The new state is the only one where they differ, and that difference is its point.
            Assert.Equal(1f, ShipLoad.State.Charged.Tools, 4);
            Assert.Equal(0f, ShipLoad.State.Charged.Drives, 4);
        }

        [Fact]
        public void ADriveIsNotATool()
        {
            Assert.True(ShipLoad.IsDrive("JumpDrive"));
            Assert.False(ShipLoad.IsDrive("Refinery"));
            Assert.False(ShipLoad.IsDrive("BatteryBlock"));

            // A drive is not a store either: it draws to fill and never supplies.
            Assert.False(ShipLoad.IsStore("JumpDrive"));
        }

        /// <summary>
        /// **The charge has a length, and the game states it rather than this harness choosing it.**
        ///
        /// A large jump drive holds `PowerNeededForJump` 3 MWh, draws `RequiredPowerInput` 32 MW
        /// while filling and keeps `PowerEfficiency` 0.8 of that, so it fills in
        /// <c>3 × 3.6e9 / (32e6 × 0.8)</c> = **421.9 s**. That figure is the length of the largest
        /// thermal event most ships have, and `G8` is a claim about how it compares to 2–5 minutes
        /// — so a number invented here would be the criterion being scored against an assumption.
        /// </summary>
        [Fact]
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

            // And it is longer than G8's window, which is the fact the criterion has to be read
            // against: the event a player meets lasts seven minutes, and the question is whether a
            // hull crosses inside it rather than whether the event itself is 2 to 5 minutes long.
            Assert.True(seconds > 300f);
        }

        [Fact]
        public void ABlockThatDoesNotChargeCarriesNoChargeAtAll()
        {
            Dictionary<string, GameBlocks.Definition> byType = GameBlocks.BySubtype();
            if (byType.Count == 0) return;

            GameBlocks.Definition armour;
            if (!byType.TryGetValue("LargeBlockArmorBlock", out armour)) return;

            Assert.Equal(0f, armour.JumpEnergyJoules, 4);

            // **Zero means the definition is silent, not that the block stores none of what it
            // draws.** Reading an absent field as total loss would make every block in the game a
            // heater, which is why `WasteFromEfficiency` takes zero as silence and returns -1.
            Assert.Equal(0f, armour.PowerEfficiency, 4);
            Assert.Equal(-1f, BlockThermalDerivation.WasteFromEfficiency(armour.PowerEfficiency), 4);

            // And the drive, which does state one, comes back with the fraction it implies.
            Assert.Equal(0.2f, BlockThermalDerivation.WasteFromEfficiency(0.8f), 4);
            Assert.Equal(0.1f, BlockThermalDerivation.WasteFromEfficiency(0.9f), 4);
        }

        [Fact]
        public void TheLoadGridHasExactlyOneControlAndItRunsFirst()
        {
            // A grid is read against its control, so a grid killed early has to have written it.
            // The two earlier grids assert this in the walk; this asserts it without a corpus.
            List<PairLab.Cell> cells = PairLab.Load();

            Assert.True(cells.Count > 1);
            Assert.True(cells[0].IsShipped, "the control must run first");

            int controls = 0;
            foreach (PairLab.Cell cell in cells) if (cell.IsShipped) controls++;

            Assert.Equal(1, controls);
        }

        [Fact]
        public void TheShippedPairIsNotAWasteMultiplierAwayFromItself()
        {
            // A cell at the shipped conduction and clock but a different load is not the control,
            // and reading it as one would make the grid compare a cell against itself.
            PairLab.Cell halved = new PairLab.Cell
            { Conductivity = 1f, Clock = PairLab.ShippedClock, Waste = 0.5f };

            Assert.False(halved.IsShipped);
            Assert.Contains("w0.5", halved.Name);

            // And a cell that does not move the load keeps the name the two earlier grids wrote,
            // so their resume records still match the cells they were taken on.
            PairLab.Cell plain = new PairLab.Cell { Conductivity = 4f, Clock = 80f };
            Assert.Equal("k4-h80", plain.Name);
        }
    }
}
