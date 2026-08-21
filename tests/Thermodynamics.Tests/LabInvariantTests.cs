using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The invariants that make the lab's output trustworthy without anyone reading it.
    ///
    /// <para>
    /// Three faults have been found in this harness so far — a gyro's torque read as thrust, a
    /// block with no declared mount points built with no mounts at all, and a battery counted as
    /// charging and discharging at once. Every one of them was a *definition read wrongly*, every
    /// one produced a plausible-looking report, and **not one was caught by a test**. They were
    /// caught by looking at a number and disbelieving it.
    /// </para>
    ///
    /// <para>
    /// That is not a method that scales to ten thousand ships. These are the standing versions of
    /// the disbelief: physical statements that must hold whatever the ship, so a harness that
    /// starts lying fails here rather than in a report someone has to read carefully.
    /// </para>
    /// </summary>
    public class LabInvariantTests
    {
        /// <summary>
        /// The blueprint corpus, or nothing.
        ///
        /// **Opt-in.** These tests read thousands of other people's ships off a corpus that is
        /// gigabytes, lives outside the repository and is fetched rather than authored, and they
        /// take minutes. A suite that everyone runs on every change cannot depend on any of that,
        /// so they stand down unless <c>THERMAL_CORPUS_TESTS</c> is set — the same shape as the
        /// guards that stand down without a game install.
        ///
        ///     THERMAL_CORPUS_TESTS=1 dotnet test --filter LabInvariantTests
        /// </summary>
        private static List<Blueprints.Ship> Corpus()
        {
            if (Environment.GetEnvironmentVariable("THERMAL_CORPUS_TESTS") == null)
            {
                return new List<Blueprints.Ship>();
            }

            if (!GameBlocks.IsInstalled) return new List<Blueprints.Ship>();

            string root = Blueprints.DefaultPath();
            if (root == null) return new List<Blueprints.Ship>();

            return CorpusLab.Scan(root).Usable;
        }

        // ---- what a definition may say ---------------------------------------------------------

        /// <summary>
        /// No block draws or delivers a preposterous number of watts.
        ///
        /// A units error is the failure mode this class exists for, and it always looks the same:
        /// a figure a million times too large, because a value in megawatts was read as watts or a
        /// field meaning something else was read as power. The largest real figure in the game is a
        /// 300 MW reactor, so a gigawatt is a comfortable ceiling that no correct reading
        /// approaches and no unit slip survives.
        /// </summary>
        [Fact]
        public void NoDefinitionClaimsAPreposterousAmountOfPower()
        {
            if (!GameBlocks.IsInstalled) return;

            const float Ceiling = 1e9f;
            List<string> wrong = new List<string>();

            foreach (GameBlocks.Definition definition in GameBlocks.All())
            {
                if (definition.PowerOutputWatts > Ceiling)
                    wrong.Add(definition + " delivers " + definition.PowerOutputWatts + " W");

                if (definition.PowerDrawWatts > Ceiling)
                    wrong.Add(definition + " draws " + definition.PowerDrawWatts + " W");
            }

            Assert.Empty(wrong);
        }

        /// <summary>
        /// Every definition has a size and, if it costs anything to build, a mass.
        ///
        /// A zero mass is the quiet catastrophe: heat capacity is mass times specific heat, so a
        /// weightless block reaches any temperature instantly and drags the substep demand of the
        /// whole grid with it.
        /// </summary>
        [Fact]
        public void EveryDefinitionHasASizeAndAMass()
        {
            if (!GameBlocks.IsInstalled) return;

            List<string> wrong = new List<string>();

            foreach (GameBlocks.Definition definition in GameBlocks.All())
            {
                if (definition.Size.X < 1 || definition.Size.Y < 1 || definition.Size.Z < 1)
                    wrong.Add(definition + " is " + definition.Size + " cells");

                if (definition.Components.Count > 0 && definition.Mass <= 0f)
                    wrong.Add(definition + " is built from components and weighs nothing");
            }

            Assert.Empty(wrong);
        }

        // ---- what a built ship must be ---------------------------------------------------------

        /// <summary>
        /// **A block with no exit is either a parse hole or a real consequence, and they must be
        /// told apart.**
        ///
        /// A block with no exposed face and no conduction is a sealed box: whatever it generates
        /// stays, and its temperature climbs until the run ends. It does not throw, it does not go
        /// NaN, and nothing looks wrong except a number nobody has a prior for. That is how the
        /// battery fault survived — 51.9 kW into a block the parser had given no mounts at all.
        ///
        /// But sealing is not always the harness's fault. This model conducts across the area where
        /// *both* blocks carry a mount surface, so a definition declaring a single mount face
        /// genuinely has one joint, and a hull that leaves that face unconnected genuinely leaves
        /// the block with nowhere to send its heat. `LargeBlockGyro` declares one mount point, on
        /// its bottom, and nineteen of them across this corpus are in exactly that state.
        ///
        /// So the invariant is the one that separates the two: **a sealed block must come from a
        /// definition that barely mounts.** One that mounts on several faces and still came out
        /// isolated was isolated by the parser, which is the failure this catches.
        /// </summary>
        [Fact]
        public void ASealedBlockIsOneThatBarelyMountsRatherThanOneWeMisread()
        {
            List<Blueprints.Ship> corpus = Corpus();
            if (corpus.Count == 0) return;

            Dictionary<string, GameBlocks.Definition> definitions = GameBlocks.BySubtype();
            List<string> misread = new List<string>();

            foreach (Blueprints.Ship ship in corpus)
            {
                ShipAssembly assembly = ship.Build();

                for (int g = 0; g < assembly.Simulations.Count; g++)
                {
                    ThermalSolver solver = assembly.Simulations[g].Solver;

                    // One node is the whole grid and radiates from every face; it is not sealed.
                    if (solver.Nodes.Count < 2) continue;

                    for (int i = 0; i < solver.Nodes.Count; i++)
                    {
                        ThermalNode node = solver.Nodes[i];
                        if (node.ExposedArea > 0f) continue;
                        if (solver.NodeConductanceTotal(i) > 0f) continue;

                        GameBlocks.Definition definition;
                        if (!definitions.TryGetValue(node.Block.Name, out definition)) continue;

                        int faces = 0;
                        for (int face = 0; face < Face.Count; face++)
                        {
                            if (definition.MountFaces[face]) faces++;
                        }

                        // A definition that declares nothing gets every face, so it can never be
                        // here by parse. One or two declared faces can be here honestly — a gyro
                        // mounts only on its bottom, a conveyor tube only on its two ends — and
                        // that is the open defect pinned by the test below. Three or more and the
                        // block had places to connect and did not, which is a parse hole.
                        if (faces > 2 && misread.Count < 8)
                        {
                            misread.Add(ship.Name + " / " + node.Block.Name + " mounts on " + faces
                                + " faces and still came out with no exit");
                        }
                    }
                }
            }

            Assert.Empty(misread);
        }

        /// <summary>
        /// **Pins an open defect as present**, so that fixing it fails here rather than quietly
        /// moving a number nobody is watching.
        ///
        /// This model conducts across the area where *both* blocks carry a mount surface. A
        /// definition that mounts on one or two faces — a gyro on its bottom, a conveyor tube on
        /// its two ends — therefore has one or two joints, and a hull that leaves those faces
        /// unconnected leaves the block with nowhere at all to send its heat. Neither radiation nor
        /// conduction: the temperature climbs until the run stops.
        ///
        /// It is not rare and it is not one block type. Across a corpus of real workshop ships it
        /// turns up on gyros and conveyor tubes routinely, which makes it a consequence of the
        /// conduction rule rather than a quirk of one definition.
        ///
        /// See docs/balance-lab.md. Three shapes of fix, all of them balance decisions: conduct
        /// across any touching face, floor the conductance between touching blocks, or raise it as
        /// a telemetry fault. **Until one is chosen, every load figure the lab produces is
        /// contaminated**, because a sealed block crosses any threshold eventually.
        /// </summary>
        [Fact]
        public void SealedBlocksAreStillPresentOnRealShips()
        {
            List<Blueprints.Ship> corpus = Corpus();
            if (corpus.Count == 0) return;

            int sealedBlocks = 0;

            foreach (Blueprints.Ship ship in corpus)
            {
                ShipAssembly assembly = ship.Build();

                for (int g = 0; g < assembly.Simulations.Count; g++)
                {
                    ThermalSolver solver = assembly.Simulations[g].Solver;
                    if (solver.Nodes.Count < 2) continue;

                    for (int i = 0; i < solver.Nodes.Count; i++)
                    {
                        if (solver.Nodes[i].ExposedArea > 0f) continue;
                        if (solver.NodeConductanceTotal(i) > 0f) continue;

                        sealedBlocks++;
                    }
                }

                if (sealedBlocks > 0) break;
            }

            Assert.True(sealedBlocks > 0,
                "No block on any ship in the corpus is thermally sealed any more. If the conduction "
                + "rule was changed on purpose, delete this test and the note in balance-lab.md; if "
                + "it was not, something has stopped looking.");
        }

        /// <summary>
        /// A blueprint's blocks are all accounted for: placed, or explicitly counted as unknown.
        ///
        /// A parser that silently drops blocks yields a ship that simulates perfectly well and
        /// answers a different question from the one asked — a hull missing a third of its armour
        /// is cooler than the real thing and nothing says so.
        /// </summary>
        [Fact]
        public void EveryBlockInABlueprintIsEitherPlacedOrCountedAsUnknown()
        {
            if (!GameBlocks.IsInstalled) return;

            string root = Blueprints.DefaultPath();
            if (root == null) return;

            List<string> wrong = new List<string>();

            foreach (string file in Blueprints.Files(root))
            {
                List<Blueprints.Ship> ships = Blueprints.Read(file);
                if (ships.Count == 0) continue;

                int accounted = 0;
                foreach (Blueprints.Ship ship in ships) accounted += ship.Blocks + ship.UnknownBlocks;

                int inFile = CountBlocks(file);
                if (inFile != accounted)
                {
                    wrong.Add(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(file))
                        + ": " + inFile + " blocks in the file, " + accounted + " accounted for");
                }
            }

            Assert.Empty(wrong);
        }

        private static int CountBlocks(string file)
        {
            System.Xml.Linq.XDocument document;
            try
            {
                document = System.Xml.Linq.XDocument.Load(file);
            }
            catch
            {
                return -1;
            }

            int count = 0;
            foreach (System.Xml.Linq.XElement grid in document.Descendants("CubeGrid"))
            {
                System.Xml.Linq.XElement blocks = grid.Element("CubeBlocks");
                if (blocks == null) continue;

                foreach (System.Xml.Linq.XElement block in blocks.Elements()) count++;
            }
            return count;
        }

        // ---- what a finished run must satisfy --------------------------------------------------

        /// <summary>
        /// **At equilibrium a grid sheds exactly what it makes.**
        ///
        /// This is the strongest structural check the lab has, and the one that finally said the
        /// model was behaving: after the three faults were fixed, made and vented agreed to within
        /// a tenth of a per cent on every loaded scenario, where before they had not. A grid whose
        /// made exceeds its vented at the end of a settled run is still climbing, which means it
        /// either has not settled or has heat going somewhere it cannot leave.
        ///
        /// Restricted to shadow scenarios, where the environment is purely a sink — a sunlit hull
        /// legitimately absorbs more than it sheds, and <c>VentedWatts</c> reads zero rather than
        /// going negative while it does.
        /// </summary>
        [Fact]
        public void ASettledGridShedsWhatItMakes()
        {
            List<Blueprints.Ship> corpus = Corpus();
            if (corpus.Count == 0) return;

            List<Battery.Scenario> scenarios = new List<Battery.Scenario>();
            foreach (Battery.Scenario scenario in Battery.All())
            {
                if (scenario.Name == "full-electrical" || scenario.Name == "burn-forward")
                {
                    scenarios.Add(scenario);
                }
            }

            // A ship with no sealed block in it. One that has one can never balance — the sealed
            // block absorbs without limit — so running this on an arbitrary hull measures the open
            // defect above rather than the invariant here.
            List<Blueprints.Ship> ships = new List<Blueprints.Ship>();
            foreach (Blueprints.Ship candidate in corpus)
            {
                if (!HasSealedBlock(candidate)) { ships.Add(candidate); break; }
            }

            if (ships.Count == 0) return;

            List<ScenarioOutcome> outcomes = BatteryLab.Run(ships, scenarios);

            List<string> unbalanced = new List<string>();

            foreach (ScenarioOutcome outcome in outcomes)
            {
                if (outcome.SecondsToSettle < 0f) continue;       // never settled; nothing to claim
                if (outcome.MadeWatts <= 1f) continue;            // nothing being made

                float difference = System.Math.Abs(outcome.MadeWatts - outcome.VentedWatts);
                float share = difference / outcome.MadeWatts;

                if (share > 0.05f)
                {
                    unbalanced.Add(outcome.Ship + " / " + outcome.Scenario + ": made "
                        + outcome.MadeWatts.ToString("n0") + " W, vented "
                        + outcome.VentedWatts.ToString("n0") + " W");
                }
            }

            Assert.Empty(unbalanced);
        }

        /// <summary>Whether any block on a ship has no exit at all. See the note above.</summary>
        private static bool HasSealedBlock(Blueprints.Ship ship)
        {
            ShipAssembly assembly = ship.Build();

            for (int g = 0; g < assembly.Simulations.Count; g++)
            {
                ThermalSolver solver = assembly.Simulations[g].Solver;
                if (solver.Nodes.Count < 2) continue;

                for (int i = 0; i < solver.Nodes.Count; i++)
                {
                    if (solver.Nodes[i].ExposedArea > 0f) continue;
                    if (solver.NodeConductanceTotal(i) > 0f) continue;

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// More load cannot make a ship colder.
        ///
        /// Idle, then every consumer at rating, then everything at once in every direction: the
        /// peak has to rise, or something in the load model is cancelling rather than accumulating.
        /// It is the cheapest test of the load path there is and it needs no prior about what any
        /// particular temperature should be, which is exactly what makes it robust — a wrong
        /// constant moves every figure and leaves the ordering alone; a wrong *classification*
        /// breaks the ordering.
        /// </summary>
        [Fact]
        public void MoreLoadIsNeverCooler()
        {
            List<Blueprints.Ship> corpus = Corpus();
            if (corpus.Count == 0) return;

            Dictionary<string, Battery.Scenario> byName = new Dictionary<string, Battery.Scenario>();
            foreach (Battery.Scenario scenario in Battery.All()) byName[scenario.Name] = scenario;

            List<Battery.Scenario> ladder = new List<Battery.Scenario>
            {
                byName["idle"], byName["full-electrical"], byName["all-peak"],
            };

            List<Blueprints.Ship> ships = new List<Blueprints.Ship> { corpus[0] };
            List<ScenarioOutcome> outcomes = BatteryLab.Run(ships, ladder);

            Assert.Equal(3, outcomes.Count);

            for (int i = 1; i < outcomes.Count; i++)
            {
                Assert.True(outcomes[i].PeakKelvin >= outcomes[i - 1].PeakKelvin - 1f,
                    outcomes[i].Scenario + " peaks at " + outcomes[i].PeakKelvin.ToString("n0")
                    + " K, below " + outcomes[i - 1].Scenario + "'s "
                    + outcomes[i - 1].PeakKelvin.ToString("n0") + " K");
            }
        }

        /// <summary>
        /// The sun only ever adds. A hull held to it cannot be cooler than the same hull in shadow,
        /// and if it is, the solar path has its sign or its facing wrong.
        /// </summary>
        [Fact]
        public void SunlightNeverCools()
        {
            List<Blueprints.Ship> corpus = Corpus();
            if (corpus.Count == 0) return;

            Dictionary<string, Battery.Scenario> byName = new Dictionary<string, Battery.Scenario>();
            foreach (Battery.Scenario scenario in Battery.All()) byName[scenario.Name] = scenario;

            List<Battery.Scenario> pair = new List<Battery.Scenario>
            {
                byName["vacuum-shadow"], byName["vacuum-sunlit"],
            };

            List<Blueprints.Ship> ships = new List<Blueprints.Ship> { corpus[corpus.Count - 1] };
            List<ScenarioOutcome> outcomes = BatteryLab.Run(ships, pair);

            Assert.Equal(2, outcomes.Count);
            Assert.True(outcomes[1].MeanKelvin >= outcomes[0].MeanKelvin - 1f,
                "a sunlit hull averaged " + outcomes[1].MeanKelvin.ToString("n0")
                + " K against " + outcomes[0].MeanKelvin.ToString("n0") + " K in shadow");
        }

        /// <summary>
        /// The same ship in the same state twice gives the same answer.
        ///
        /// Distinct from the parallel-against-linear check: that one guards shared state between
        /// concurrent runs, this one guards state left behind *between sequential* runs — a cache
        /// that keeps a load, a model mutated in place, a static that remembers the last ship.
        /// </summary>
        [Fact]
        public void RunningTheSameThingTwiceGivesTheSameAnswer()
        {
            List<Blueprints.Ship> corpus = Corpus();
            if (corpus.Count == 0) return;

            Battery.Scenario scenario = null;
            foreach (Battery.Scenario candidate in Battery.All())
            {
                if (candidate.Name == "full-electrical") scenario = candidate;
            }

            Blueprints.Ship ship = corpus[corpus.Count - 1];

            ScenarioOutcome first = Battery.Run(ship, scenario);
            ScenarioOutcome second = Battery.Run(ship, scenario);

            Assert.Equal(first.PeakKelvin, second.PeakKelvin, 3);
            Assert.Equal(first.MeanKelvin, second.MeanKelvin, 3);
            Assert.Equal(first.HottestBlock, second.HottestBlock);
        }
    }
}
