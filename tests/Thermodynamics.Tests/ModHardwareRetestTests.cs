using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **What the real-unit conversion bought the mod's own cooling hardware**, pinned so it cannot
    /// invert quietly.
    ///
    /// <para>
    /// The retest set of workshop hulls (<c>ConductanceRetestWalk</c>) cannot answer this: the
    /// corpus filters admit vanilla ships, so not one of the forty carries a coolant pipe or a
    /// radiator, and the walk's own reach column reports that arm as reaching zero blocks. The two
    /// largest single moves the conversion made — pipes 200 → 960 W/(m·K) and radiators 200 → 568.8
    /// — are therefore measured on a rig instead, one family at a time.
    /// </para>
    ///
    /// <para>
    /// **The stand-in is held against the shipped block first.** These rigs are built from
    /// <see cref="Catalog"/>, which is known to drift from the shipped definitions (backlog `C4`),
    /// and a comparison of two worlds built from the same stand-in cancels that drift everywhere
    /// except in the one figure under test. So the conductance under test is checked against
    /// <c>Cubes.xml</c> before anything is run.
    /// </para>
    /// </summary>
    [Trait("speed", "slow")]
    public class ModHardwareRetestTests
    {
        /// <summary>
        /// The two conductances these rigs are about are the shipped ones, not Catalog's opinion of
        /// them. Without this the rigs would keep producing a clean delta while measuring a block
        /// the mod does not ship.
        /// </summary>
        [Fact]
        public void TheRigsMeasureTheShippedConductances()
        {
            Assert.Equal(
                ShippedBlocks.Get("Gauge_LG_CoolantPipe_Straight").Thermal.Conductivity,
                Catalog.CoolantThermal().Conductivity, 2);

            Assert.Equal(
                ShippedBlocks.Get("Gauge_LG_Radiator").Thermal.Conductivity,
                Catalog.RadiatorThermal().Conductivity, 2);

            // And they are the materials the conversion says they are, so a change to
            // ReferenceMaterials cannot leave this suite measuring a ratio that is no longer 4.8 or
            // 2.84.
            Assert.Equal(ReferenceMaterials.Copper.Conductivity,
                Catalog.CoolantThermal().Conductivity, 2);
            Assert.Equal(ReferenceMaterials.Aluminium.Conductivity,
                Catalog.RadiatorThermal().Conductivity, 2);
        }

        /// <summary>
        /// **A radiator stack cools a source better than it did before the conversion, and the
        /// margin is small.** Aluminium at 568.8 against the old flat 200 is 2.84× the conductance
        /// into the stack, and a stack's limit is the area it radiates from rather than the rate
        /// heat reaches it — so the move buys the join, not the cooling.
        /// </summary>
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

            // **Measured 2026-08-24 at the pace C24 ships: 21.12 K at eight radiators**, rising
            // from 4.12 K at one and flat at 24.90 K by thirty-two. It was 12.07 K at eight before
            // the retune, on the same 2.84x — four times the pace carries more of the difference
            // into the stack, which is the same fact as every other row this retune moved. Pinned
            // as a band rather than a point because the figure moves with any change to the
            // radiator's area or emissivity; what must not change quietly is that 2.84x the
            // conductance is worth a few tens of kelvin on a stack whose limit is the area it
            // radiates from rather than the rate heat reaches it.
            float saved = before.SourceKelvin - shipped.SourceKelvin;
            Assert.InRange(saved, 10f, 35f);
        }

        /// <summary>
        /// **The 4.8× on coolant pipes does not reach the coolant.** The fluid couples to its pipe
        /// through <c>LoopThermalProperties.Conductivity</c>, a 0…1 quality against
        /// <c>ThermalConstants.ReferenceConductivity</c> that the conversion never touched; the
        /// pipe's own material conductance decides only what crosses between the pipe block and
        /// what is bolted to it.
        ///
        /// <para>
        /// This is worth a test rather than a sentence because the two are easy to confuse and the
        /// confusion is silent: a loop that carried more heat because copper is a better conductor
        /// would be a balance change nobody authored.
        /// </para>
        /// </summary>
        [Fact]
        public void TheCoolantLoopDoesNotCarryMoreBecauseThePipesAreCopper()
        {
            ModHardwareRetest.Row shipped = ModHardwareRetest.CoolantRing(6, 5, false);
            ModHardwareRetest.Row before = ModHardwareRetest.CoolantRing(6, 5, true);

            Assert.Equal(ReferenceMaterials.Copper.Conductivity * ThermalConstants.ConductionScale,
                shipped.Conductance, 1);
            Assert.Equal(ConductanceRetest.PreBest, before.Conductance, 1);

            // The ring itself is the fluid's, and the fluid's coupling did not move: the far side of
            // the ring sits where it sat. A tolerance rather than equality because the pipes are
            // also bolted to each other, and that path is copper now — measured 2026-08-23 at
            // 0.13 K on a twelve-pipe ring and 0.02 K on a twenty-eight, against 5.34 to 12.4 K for
            // the radiator's smaller move. The 4.8x is the largest number the conversion produced
            // and the smallest effect it had.
            Assert.True(Math.Abs(shipped.FarKelvin - before.FarKelvin) < 1f,
                "the loop's own transport should be unmoved by the pipe material: "
                + shipped.FarKelvin + " K shipped against " + before.FarKelvin + " K before");
        }

        /// <summary>
        /// Both rigs have to have actually run: a rig whose source never heated is a rig whose
        /// comparison is two room temperatures, and it reports "no change" in the same shape as a
        /// real null result (`E8`).
        ///
        /// <para>
        /// **Read against the unloaded rig rather than against a temperature.** This asked for
        /// 310 K, which was a fact about the old conduction pace and not about the load: at the
        /// pace `C24` ships, eight radiators hold a 75 kW source at 272 K, which is the stack
        /// working rather than the rig idling. The control is the same stack with nothing making
        /// heat, which falls to the sky in shadow — so what says a rig is loaded is the distance
        /// between the two.
        /// </para>
        /// </summary>
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

        /// <summary>
        /// **The conversion's four headline moves, pinned.** The published table in
        /// <c>definitions.md</c> had four rows and they described only the families
        /// <c>Cubes.xml</c> authors; the change reached every vanilla block through derivation, and
        /// its two largest moves are on blocks that table never named.
        ///
        /// <para>
        /// Measured 2026-08-23. Armour is unmoved, which is the calibration and the only exact
        /// figure here. The ion thruster fell to **0.23×** — the largest loss the conversion caused,
        /// and the reason a burning hull is hotter than it was. The jump drive rose to **3.51×** —
        /// the largest gain, on the block type carrying 71.3 % of the population's full-load waste.
        /// Bands rather than points, because these follow from build costs the game may re-balance.
        /// </para>
        /// </summary>
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

            // The calibration: ordinary armour is exactly where it was, which is what made the
            // conversion possible without re-balancing every hull.
            Assert.Equal(1.00f, moves["LargeBlockArmorBlock"].Ratio, 2);
            Assert.Equal(1.00f, moves["LargeHeavyBlockArmorBlock"].Ratio, 2);

            Assert.InRange(moves["LargeBlockLargeThrust"].Ratio, 0.18f, 0.30f);
            Assert.InRange(moves["LargeJumpDrive"].Ratio, 3.0f, 4.0f);
            Assert.InRange(moves["LargeBlockLargeGenerator"].Ratio, 0.45f, 0.60f);
            Assert.InRange(moves["LargeBlockBatteryBlock"].Ratio, 0.45f, 0.60f);

            // And the thruster family does not move together, which is why "thrusters went 0.6x"
            // was only ever true of the hydrogen ones.
            Assert.InRange(moves["LargeBlockLargeHydrogenThrust"].Ratio, 0.55f, 0.65f);
            Assert.InRange(moves["LargeBlockLargeAtmosphericThrust"].Ratio, 1.15f, 1.40f);
        }
    }
}
