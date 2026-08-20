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
            Assert.Equal("Test Ship", ships[0].Name);
            Assert.Equal(3, ships[0].Blocks);
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

            string file = WriteBlueprint(Block("LargeBlockArmorBlock", 0, 0, 0), false);
            Blueprints.Ship ship = Blueprints.Read(file)[0];

            Assert.Equal(0, ship.Blocks);
            Assert.Equal(1, ship.UnknownBlocks);
        }

        [Fact]
        public void AnOrientedBlockKeepsItsOrientation()
        {
            if (!GameBlocks.IsInstalled) return;

            string file = WriteBlueprint(Block("LargeBlockArmorBlock", 0, 0, 0,
                "<BlockOrientation Forward=\"Right\" Up=\"Up\" />"));

            Blueprints.Ship ship = Blueprints.Read(file)[0];

            Assert.Equal(1, ship.Blocks);
            Assert.Equal(Base6Directions.Direction.Right, ship.Grid.Blocks[0].Orientation.Forward);
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
        /// The end of the chain: a ship read from a real subscribed blueprint builds a simulation
        /// that steps. Everything the lab does rests on this, and it is the one thing a synthetic
        /// blueprint cannot check — real ships have subgrids, rotors, oddly shaped blocks and
        /// twenty years of accumulated definition quirks in them.
        /// </summary>
        [Fact]
        public void ARealSubscribedShipBuildsASimulationThatSteps()
        {
            if (!GameBlocks.IsInstalled) return;

            string workshop = Blueprints.WorkshopPath();
            if (workshop == null) return;

            CorpusLab.Summary corpus = CorpusLab.Scan(workshop);
            if (corpus.Usable.Count == 0) return;

            Blueprints.Ship ship = corpus.Usable[0];

            ThermalSimulation simulation = ship.Build();

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Run(60f, 30f);

            Assert.True(simulation.Solver.Nodes.Count > 0,
                ship.Name + " built no nodes from " + ship.Blocks + " blocks");
            Assert.True(runner.Final.HottestTemperature > 0f,
                ship.Name + " reports no temperature after stepping");
        }
    }
}
