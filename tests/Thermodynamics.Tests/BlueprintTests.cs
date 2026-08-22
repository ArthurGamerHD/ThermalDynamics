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
        /// A blueprint spells the base variant of a type with an empty <c>SubtypeName</c>, and
        /// armour cubes are the common case — so a parser that skips them drops most of the hull
        /// of most ships and reports a plausible-looking remainder.
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

        /// <summary>
        /// The end of the chain: a ship read from a real subscribed blueprint builds a simulation
        /// that steps. Everything the lab does rests on this, and it is the one thing a synthetic
        /// blueprint cannot check — real ships have subgrids, rotors, oddly shaped blocks and
        /// twenty years of accumulated definition quirks in them.
        /// </summary>
        // Retired as a standalone corpus walk: CorpusSurvey builds every ship, checks the
        // node-count accounting, probes that it steps, and counts joints, rooms and small grids
        // as part of its single pass. The synthetic tests in this class are untouched.
        internal static void ARealSubscribedShipBuildsASimulationThatSteps()
        {
            if (CorpusFixture.Files().Count == 0) return;

            List<string> wrong = new List<string>();
            int stepped = 0;
            int withJoints = 0;
            int smallGrid = 0;
            int withRooms = 0;

            // Building a hull and stepping it a minute is the whole cost of this test, and ships
            // share nothing with each other, so it fans out and only the tallying happens here.
            foreach (Built built in CorpusFixture.Sweep("real-ships", Inspect))
            {
                if (built.Complaint != null) wrong.Add(built.Complaint);
                if (built.SmallGrid) smallGrid++;
                if (built.Stepped) stepped++;
                if (built.Joints) withJoints++;
                if (built.Rooms) withRooms++;
            }

            Assert.Empty(wrong);

            Assert.True(stepped > 0,
                "no ship in the corpus changed temperature over sixty seconds, so the step path "
                + "may not be running at all.");

            Assert.True(withJoints > 0,
                "not one ship in the corpus resolved a mechanical joint. Subgrids are common "
                + "enough that this means the joint linker has stopped finding them.");

            // Small grids multiply every mass by roughly a hundredth, so a grid-size misread is
            // one of the loudest possible faults and one nothing else on real ships would see.
            Assert.True(smallGrid > 0,
                "not one small-grid ship in the corpus. Either the corpus is unrepresentative or "
                + "small grids have stopped being read.");

            // Same argument for sealed interiors: rooms are where the air model attaches, and a
            // corpus of built ships with no room in any of them means the room mapper is silent.
            Assert.True(withRooms > 0,
                "not one ship in the corpus mapped a sealed room.");
        }

        /// <summary>What one real ship turned out to be once it was built and stepped.</summary>
        private class Built
        {
            public string Complaint;
            public bool SmallGrid;
            public bool Stepped;
            public bool Joints;
            public bool Rooms;
        }

        /// <summary>Builds one corpus ship, steps it a minute in shadow, and reports what it is.</summary>
        private static Built Inspect(Blueprints.Ship ship)
        {
            Built built = new Built { SmallGrid = !ship.Large };
            ShipAssembly assembly = ship.Build();

            // **Every block the reader counted has to have become a node.** The parser proves its
            // own count against the raw XML elsewhere, and the runner proves the nodes it has can
            // be stepped — but nobody stood between the two, and that gap is exactly where a hull
            // quietly loses a third of its armour. GridBuilder.Place returns no success signal, so
            // a rejected block is counted by the reader and absent from the solver with nothing to
            // say so.
            if (assembly.NodeCount != ship.Blocks)
            {
                built.Complaint = ship.Name + " read " + ship.Blocks + " blocks and built "
                    + assembly.NodeCount + " nodes";
                return built;
            }

            AssemblyRunner runner = new AssemblyRunner(assembly);
            runner.Environment = t => Worlds.Shadow();

            // The old version of this test asserted the hottest block was above zero Kelvin after
            // one sample, which every block satisfies at its build temperature of 293.15 K whether
            // the solver stepped or not. A temperature that moved is the thing worth asserting.
            float before = assembly.Hottest() == null ? 0f : assembly.Hottest().Temperature;
            runner.Run(60f);
            built.Stepped = runner.Hottest[runner.Hottest.Count - 1] != before;

            // Subgrids are the reason a real ship is worth testing at all — a rotor head or a
            // piston tip is a separate grid, and the bridge between them is the only path heat has
            // across the joint. The corpus is full of them; if it suddenly is not, the joint linker
            // has stopped resolving.
            built.Joints = assembly.Bridges.Count > 0;
            built.Rooms = assembly.RoomCount > 0;
            return built;
        }
    }
}
