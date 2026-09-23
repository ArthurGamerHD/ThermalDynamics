using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    [Trait("speed", "slow")]
    public class ModHardwareRetestTests
    {
        [Fact]

        public void TheRigsMeasureTheShippedConductances()
        {
            Assert.Equal(
                ShippedBlocks.Get("Gauge_LG_CoolantPipe_Straight").Thermal.Conductivity,
                Catalog.CoolantThermal().Conductivity, 2);

            Assert.Equal(
                ShippedBlocks.Get("Gauge_LG_Radiator").Thermal.Conductivity,
                Catalog.RadiatorThermal().Conductivity, 2);

            Assert.Equal(ReferenceMaterials.Copper.Conductivity,
                Catalog.CoolantThermal().Conductivity, 2);
            Assert.Equal(ReferenceMaterials.Aluminium.Conductivity,
                Catalog.RadiatorThermal().Conductivity, 2);
        }

        [Fact]

        public void TheRadiatorConversionHelpsAndOnlyALittle()
        {
            ModHardwareRetest.Row shipped = ModHardwareRetest.RadiatorStack(8, false);
            ModHardwareRetest.Row before = ModHardwareRetest.RadiatorStack(8, true);

            Assert.Equal(ReferenceMaterials.Aluminium.Conductivity * ThermalConstants.ConductionScale,
                shipped.Conductance, 1);
            Assert.Equal(ConductanceRetest.PreBest, before.Conductance, 1);

            Assert.True(shipped.SourceKelvin < before.SourceKelvin,
                "the stiffer radiator should not leave the source hotter: "
                + shipped.SourceKelvin + " K shipped against " + before.SourceKelvin + " K before");

            float saved = before.SourceKelvin - shipped.SourceKelvin;
            Assert.InRange(saved, 10f, 35f);
        }

        [Fact]

        public void TheCoolantLoopDoesNotCarryMoreBecauseThePipesAreCopper()
        {
            ModHardwareRetest.Row shipped = ModHardwareRetest.CoolantRing(6, 5, false);
            ModHardwareRetest.Row before = ModHardwareRetest.CoolantRing(6, 5, true);

            Assert.Equal(ReferenceMaterials.Copper.Conductivity * ThermalConstants.ConductionScale,
                shipped.Conductance, 1);
            Assert.Equal(ConductanceRetest.PreBest, before.Conductance, 1);

            Assert.True(Math.Abs(shipped.FarKelvin - before.FarKelvin) < 1f,
                "the loop's own transport should be unmoved by the pipe material: "
                + shipped.FarKelvin + " K shipped against " + before.FarKelvin + " K before");
        }

        [Fact]

        public void BothRigsHeatTheirSource()
        {
            ModHardwareRetest.Row stack = ModHardwareRetest.RadiatorStack(8, false);
            ModHardwareRetest.Row ring = ModHardwareRetest.CoolantRing(6, 5, false);
            ModHardwareRetest.Row unloaded = ModHardwareRetest.RadiatorStack(8, false, 0f);

            Assert.True(unloaded.SourceKelvin < 100f,
                "the unloaded stack settled at " + unloaded.SourceKelvin + " K, so it is not the"
                + " control this reads the loaded rigs against");

            Assert.True(stack.SourceKelvin > unloaded.SourceKelvin + 150f,
                "the radiator rig's source reached " + stack.SourceKelvin + " K against "
                + unloaded.SourceKelvin + " K with no load, so it is not under load and the"
                + " comparison means nothing");
            Assert.True(ring.SourceKelvin > unloaded.SourceKelvin + 150f,
                "the ring rig's source reached " + ring.SourceKelvin + " K against "
                + unloaded.SourceKelvin + " K with no load, so it is not under load and the"
                + " comparison means nothing");
        }

        [Fact]

        public void TheConversionsHeadlineMovesAreWhereTheyWereMeasured()
        {
            if (!GameBlocks.IsInstalled) return;

            Dictionary<string, ConductanceRetest.Move> moves =
                new Dictionary<string, ConductanceRetest.Move>(StringComparer.Ordinal);
            foreach (ConductanceRetest.Move move in ConductanceRetest.Moves())
            {
                moves[move.Subtype] = move;
            }

            Assert.Empty(ConductanceRetest.Missing());
            Assert.True(moves.Count >= 13, "the conversion table lost rows: " + moves.Count);

            Assert.Equal(1.00f, moves["LargeBlockArmorBlock"].Ratio, 2);
            Assert.Equal(1.00f, moves["LargeHeavyBlockArmorBlock"].Ratio, 2);

            Assert.InRange(moves["LargeBlockLargeThrust"].Ratio, 0.18f, 0.30f);
            Assert.InRange(moves["LargeJumpDrive"].Ratio, 3.0f, 4.0f);
            Assert.InRange(moves["LargeBlockLargeGenerator"].Ratio, 0.45f, 0.60f);
            Assert.InRange(moves["LargeBlockBatteryBlock"].Ratio, 0.45f, 0.60f);

            Assert.InRange(moves["LargeBlockLargeHydrogenThrust"].Ratio, 0.55f, 0.65f);
            Assert.InRange(moves["LargeBlockLargeAtmosphericThrust"].Ratio, 1.15f, 1.40f);
        }
    }
}
