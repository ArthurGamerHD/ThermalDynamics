using System;
using System.Collections.Generic;
using System.IO;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Reading a real ship out of a blueprint file.
    ///
    /// This is the foundation of the balance lab, and the thing most able to be quietly wrong. A
    /// blueprint that half-parses does not throw: it yields a ship with some of its blocks, which
    /// simulates perfectly well and answers a different question from the one being asked. Every
    /// test here is about the *yield* rather than about the physics.
    ///
    /// Every test here is synthetic, and deliberately: the end of the chain — a real subscribed
    /// blueprint building a simulation that steps — is `CorpusSurvey`, which builds every ship in
    /// the corpus, probes that it steps and counts joints, rooms and small grids in one pass.
    /// </summary>
    public class BlueprintTests
    {
        private static string WriteBlueprint(string blocks, bool large = true)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "thermal-bp-" + System.Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(path);

            string file = Path.Combine(path, "bp.sbc");
            File.WriteAllText(file,
                "<?xml version=\"1.0\"?>\n" +
                "<Definitions xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\n" +
                "  <ShipBlueprints>\n" +
                "    <ShipBlueprint>\n" +
                "      <Id Type=\"MyObjectBuilder_ShipBlueprintDefinition\" Subtype=\"TestShip\" />\n" +
                "      <CubeGrids>\n" +
                "        <CubeGrid>\n" +
                "          <DisplayName>Test Ship</DisplayName>\n" +
                "          <GridSizeEnum>" + (large ? "Large" : "Small") + "</GridSizeEnum>\n" +
                "          <CubeBlocks>\n" + blocks + "\n          </CubeBlocks>\n" +
                "        </CubeGrid>\n" +
                "      </CubeGrids>\n" +
                "    </ShipBlueprint>\n" +
                "  </ShipBlueprints>\n" +
                "</Definitions>\n");
            return file;
        }

        private static string Block(string subtype, int x, int y, int z, string orientation = null)
        {
            return "<MyObjectBuilder_CubeBlock xsi:type=\"MyObjectBuilder_CubeBlock\">" +
                (subtype == null ? "<SubtypeName />" : "<SubtypeName>" + subtype + "</SubtypeName>") +
                "<Min x=\"" + x + "\" y=\"" + y + "\" z=\"" + z + "\" />" +
                (orientation ?? "") +
                "</MyObjectBuilder_CubeBlock>";
        }

        [Fact]
        public void AShipIsReadWithItsNameAndItsBlocks()
        {
            if (!GameBlocks.IsInstalled) return;

            string file = WriteBlueprint(
                Block("LargeBlockArmorBlock", 0, 0, 0) +
                Block("LargeBlockArmorBlock", 1, 0, 0) +
                Block("LargeHeavyBlockArmorBlock", 2, 0, 0));

            List<Blueprints.Ship> ships = Blueprints.Read(file);

            Assert.Single(ships);

            // The blueprint's own name, not the hull grid's: a ship is the whole blueprint now.
            Assert.Equal("TestShip", ships[0].Name);
            Assert.Equal(3, ships[0].Blocks);
            Assert.Single(ships[0].Grids);
            Assert.True(ships[0].IsVanilla);
        }

        /// <summary>
        /// One block element with a stated <c>xsi:type</c>, which is how a blueprint spells
        /// anything that is not a plain cube.
        /// </summary>
        private static string Typed(string typeId, string subtype, int x, int y, int z)
        {
            return "<MyObjectBuilder_CubeBlock xsi:type=\"MyObjectBuilder_" + typeId + "\">" +
                (subtype == null ? "<SubtypeName />" : "<SubtypeName>" + subtype + "</SubtypeName>") +
                "<Min x=\"" + x + "\" y=\"" + y + "\" z=\"" + z + "\" />" +
                "</MyObjectBuilder_CubeBlock>";
        }

        /// <summary>
        /// **An empty <c>SubtypeName</c> on something that is not armour is that type's base
        /// variant, not an armour cube.** This is the defect the whole corpus was measured under
        /// until 2026-08-25.
        ///
        /// <para>
        /// The game leaves <c>SubtypeId</c> empty on thirteen definitions and eleven of them are
        /// not armour — the vanilla oxygen generator, air vent, oxygen tank, both gravity
        /// generators, the door, the hangar door, the passage, the ladder and the two large
        /// turrets. Every one of them in every corpus blueprint was built as a 500 kg armour cube
        /// with no power draw, so it made no heat, and nothing about the result looked wrong: the
        /// ship parsed, the block count was right, and `IsVanilla` stayed true.
        /// </para>
        ///
        /// <para>
        /// The check is mass rather than a name, because mass is what the wrong answer got wrong:
        /// a large-grid oxygen generator weighs 2,587 kg against light armour's 500.
        /// </para>
        /// </summary>
        [Fact]
        public void ABlockWithNoSubtypeNameIsItsOwnTypesBaseVariantRatherThanArmour()
        {
            if (!GameBlocks.IsInstalled) return;

            string file = WriteBlueprint(Typed("OxygenGenerator", null, 0, 0, 0));
            Blueprints.Ship ship = Blueprints.Read(file)[0];

            Assert.Equal(1, ship.Blocks);
            Assert.Equal(0, ship.UnknownBlocks);

            BlockInstance placed = ship.Grids[0].Builder.Placed[0];
            Assert.True(placed.Model.Mass > 2000f,
                "a vanilla oxygen generator was built weighing " + placed.Model.Mass
                + " kg; light armour is 500 and the generator is 2,587, so an empty SubtypeName is"
                + " resolving to armour again");
        }

        /// <summary>
        /// The small-grid half of the same rule. Two of the thirteen base variants are small-grid
        /// blocks, so a resolver keyed on type alone would build a small-grid gun onto a large
        /// hull — which is the failure the named-subtype path already refuses.
        /// </summary>
        [Fact]
        public void ABaseVariantOfTheWrongGridSizeIsNotBuilt()
        {
            if (!GameBlocks.IsInstalled) return;

            // SmallGatlingGun is small-grid only, so a large-grid blueprint naming it resolves to
            // nothing rather than to whatever else shares its type.
            string file = WriteBlueprint(
                Block("LargeBlockArmorBlock", 0, 0, 0) + Typed("SmallGatlingGun", null, 1, 0, 0));
            Blueprints.Ship ship = Blueprints.Read(file)[0];

            Assert.Equal(1, ship.Blocks);
            Assert.Equal(1, ship.UnknownBlocks);
            Assert.False(ship.IsVanilla);
        }

        /// <summary>
        /// The other half of the rule, and the reason the wrong version of it survived so long:
        /// **for a `CubeBlock` an empty <c>SubtypeName</c> really is the plain armour cube**, and
        /// armour is most of most hulls, so the guess looked right everywhere anyone checked.
        ///
        /// It is resolved as its own case rather than as a fallback, so that a type nobody has
        /// thought about resolves to nothing and is counted instead of quietly becoming armour.
        /// </summary>
        [Fact]
        public void ABlockWithNoSubtypeNameIsTheBaseArmourCube()
        {
            if (!GameBlocks.IsInstalled) return;

            string file = WriteBlueprint(Block(null, 0, 0, 0) + Block(null, 1, 0, 0));
            Blueprints.Ship ship = Blueprints.Read(file)[0];

            Assert.Equal(2, ship.Blocks);
            Assert.Equal(0, ship.UnknownBlocks);
        }

        /// <summary>
        /// One unresolved block disqualifies the whole ship. There is no way to tell whether the
        /// block that failed was a decorative panel or the reactor, and a modded ship measured as
        /// though it were vanilla is worse than a ship not measured at all.
        /// </summary>
        [Fact]
        public void OneModdedBlockDisqualifiesTheShip()
        {
            if (!GameBlocks.IsInstalled) return;

            string file = WriteBlueprint(
                Block("LargeBlockArmorBlock", 0, 0, 0) +
                Block("SomeoneElsesRailgunMkIV", 1, 0, 0));

            Blueprints.Ship ship = Blueprints.Read(file)[0];

            Assert.False(ship.IsVanilla);
            Assert.Equal(1, ship.UnknownBlocks);
            Assert.Contains("SomeoneElsesRailgunMkIV", ship.UnknownSubtypes);
        }

        /// <summary>
        /// A subtype that exists on the other grid size is not a match. Sharing a name across the
        /// two is common, and building a small-grid ship out of large-grid blocks would multiply
        /// every mass on it by roughly a hundred.
        /// </summary>
        [Fact]
        public void ALargeGridBlockIsNotAcceptedOnASmallGridShip()
        {
            if (!GameBlocks.IsInstalled) return;

            // One block of the right size so the ship exists at all, and one of the wrong size.
            string file = WriteBlueprint(
                Block("SmallBlockArmorBlock", 0, 0, 0) +
                Block("LargeBlockArmorBlock", 1, 0, 0), false);

            Blueprints.Ship ship = Blueprints.Read(file)[0];

            Assert.Equal(1, ship.Blocks);
            Assert.Equal(1, ship.UnknownBlocks);
            Assert.False(ship.IsVanilla);
        }

        [Fact]
        public void AnOrientedBlockKeepsItsOrientation()
        {
            if (!GameBlocks.IsInstalled) return;

            string file = WriteBlueprint(Block("LargeBlockArmorBlock", 0, 0, 0,
                "<BlockOrientation Forward=\"Right\" Up=\"Up\" />"));

            Blueprints.Ship ship = Blueprints.Read(file)[0];

            Assert.Equal(1, ship.Blocks);
            Assert.Equal(Base6Directions.Direction.Right,
                ship.Grids[0].Builder.Grid.Blocks[0].Orientation.Forward);
        }

        /// <summary>A file that is not a blueprint, or not XML, must yield nothing rather than throw.</summary>
        [Fact]
        public void AnUnreadableFileYieldsNoShips()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "thermal-bp-" + System.Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(path);

            string file = Path.Combine(path, "bp.sbc");
            File.WriteAllText(file, "this is not xml <<<");

            Assert.Empty(Blueprints.Read(file));
        }

        /// <summary>
        /// **Two blocks in one cell is a file to skip, not a corpus to abandon.**
        ///
        /// The reader promises never to throw, because a corpus of ten thousand files will contain
        /// some that no parser should die on — but the guard sat on the XML load alone, and
        /// everything after it ran bare. A real workshop blueprint puts two armour blocks in the
        /// same cell; <c>GridModel</c> refuses the second, and the exception came up through the
        /// parse and took down the scan of the whole corpus. Every corpus test failed on it, none
        /// of them anywhere near their own subject.
        ///
        /// A file that cannot be read yields no ships and is recorded as unreadable, so the skip
        /// is visible in the corpus report rather than being a silent hole in the population.
        /// </summary>
        [Fact]
        public void ABlueprintWithTwoBlocksInOneCellIsSkippedRatherThanThrown()
        {
            if (!GameBlocks.IsInstalled) return;

            string file = WriteBlueprint(
                Block("LargeBlockArmorBlock", 0, 0, 0) +
                Block("LargeHeavyBlockArmorBlock", 0, 0, 0));

            Assert.Empty(Blueprints.Read(file));
            Assert.True(Blueprints.Unreadable().ContainsKey(file),
                "the file should have been recorded as unreadable");
        }
    }
}
