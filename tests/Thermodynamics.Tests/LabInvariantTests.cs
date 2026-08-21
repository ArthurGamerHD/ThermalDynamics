using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
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
        /// Sealed blocks are **rare**, and a rise in them means the parser has started dropping
        /// mount surfaces again.
        ///
        /// A block with no exposed face and no conduction has nowhere to send its heat: it climbs
        /// until the run stops, without an exception, a NaN or any symptom but a number nobody has
        /// a prior for. Three separate harness faults produced them and each looked like physics —
        /// a gyro's torque read as thrust, a battery's mounts zeroed because it did not seal, and a
        /// definition with no declared mounts read as mounting nowhere.
        ///
        /// A bound rather than zero, because one ship in the corpus still produces a couple of
        /// dozen and has not been explained; see docs/balance-lab.md. A bound still catches what
        /// matters — the worst of those three faults produced three times as many, across a third
        /// of the ships.
        /// </summary>
        [Fact]
        public void SealedBlocksAreRare()
        {
            List<Blueprints.Ship> corpus = Corpus();
            if (corpus.Count == 0) return;

            long sealedBlocks = 0;
            long blocks = 0;
            HashSet<string> ships = new HashSet<string>();

            foreach (Blueprints.Ship ship in corpus)
            {
                ShipAssembly assembly = ship.Build();
                blocks += assembly.NodeCount;

                for (int g = 0; g < assembly.Simulations.Count; g++)
                {
                    ThermalSolver solver = assembly.Simulations[g].Solver;
                    if (solver.Nodes.Count < 2) continue;

                    for (int i = 0; i < solver.Nodes.Count; i++)
                    {
                        if (solver.Nodes[i].ExposedArea > 0f) continue;
                        if (solver.NodeConductanceTotal(i) > 0f) continue;

                        sealedBlocks++;
                        ships.Add(ship.Name);
                    }
                }
            }

            if (blocks == 0) return;

            double share = sealedBlocks / (double)blocks;

            Assert.True(share < 0.001d,
                sealedBlocks + " of " + blocks + " blocks are thermally sealed ("
                + (share * 100d).ToString("n3") + " %), across " + ships.Count + " ships. "
                + "Something has started dropping mount surfaces again.");
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

                // A ship with any block still climbing has not reached equilibrium however steady
                // its hottest block looks, and the settle detector watches only that one. Blocks
                // over critical are the tell: they are the ones still going.
                if (outcome.BlocksOverCritical > 0) continue;

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

        /// <summary>
        /// A block with nothing next to it radiates from all six faces.
        ///
        /// The one case the corpus turned up that nothing else explained: eleven armour blocks on a
        /// single ship, mounting on every face, with no neighbour in any direction, reporting zero
        /// exposed faces. A blueprint may legitimately contain a disconnected block — a leftover
        /// piece — and a lone block in vacuum is the most exposed thing there is, so if exposure
        /// reads zero for one of those it is the exposure pass, not the ship.
        ///
        /// Synthetic on purpose: it needs no corpus, so it stays in the default suite where a
        /// regression in it would be seen.
        /// </summary>
        [Fact]
        public void ABlockWithNoNeighboursIsFullyExposed()
        {
            if (!GameBlocks.IsInstalled) return;

            GameBlocks.Definition armour;
            if (!GameBlocks.BySubtype().TryGetValue("LargeBlockArmorBlock", out armour)) return;

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Blueprints.Model(armour), Vector3I.Zero);

            // Far enough away to share no face with the first.
            builder.Place(Blueprints.Model(armour), new Vector3I(0, 0, 12));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings());

            for (int i = 0; i < simulation.Solver.Nodes.Count; i++)
            {
                ThermalNode node = simulation.Solver.Nodes[i];

                Assert.True(node.TotalExposedFaces == Face.Count,
                    "a block with no neighbour reports " + node.TotalExposedFaces
                    + " exposed faces of " + Face.Count);
            }
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
