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
        private static List<string> Corpus()
        {
            return CorpusFixture.Files();
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
        internal static void SealedBlocksAreRare()
        {
            List<string> corpus = Corpus();
            if (corpus.Count == 0) return;

            long sealedBlocks = 0;
            long blocks = 0;
            HashSet<string> ships = new HashSet<string>();

            // Building a ship is the expensive half of this test and ships share nothing, so the
            // build fans out and only the tallying is done here.
            // The population this pass measured, kept. Finding which hulls are exceptional is the
            // reason to walk ten thousand ships, and it cannot be done after the fact from a pass
            // that only recorded whether the assertion held. Written per ship so an interrupted
            // run still leaves behind everything it had reached.
            foreach (Sealed count in CorpusFixture.Sweep("sealed-blocks", CountSealed))
            {
                blocks += count.Blocks;
                sealedBlocks += count.SealedBlocks;
                if (count.SealedBlocks > 0) ships.Add(count.Ship);
            }

            if (blocks == 0) return;

            double share = sealedBlocks / (double)blocks;

            Assert.True(share < 0.001d,
                sealedBlocks + " of " + blocks + " blocks are thermally sealed ("
                + (share * 100d).ToString("n3") + " %), across " + ships.Count + " ships. "
                + "Something has started dropping mount surfaces again.");
        }

        /// <summary>One ship's scenario outcomes, carried back from a sweep worker.</summary>
        private class Balance
        {
            public readonly List<ScenarioOutcome> Outcomes = new List<ScenarioOutcome>();
        }

        /// <summary>One ship's contribution to the sealed-block tally.</summary>
        private class Sealed
        {
            public string Ship;
            public long Blocks;
            public long SealedBlocks;
        }

        /// <summary>Builds one ship and counts the blocks with nowhere to send their heat.</summary>
        private static Sealed CountSealed(Blueprints.Ship ship)
        {
            ShipAssembly assembly = ship.Build();
            Sealed count = new Sealed { Ship = ship.Name, Blocks = assembly.NodeCount };

            for (int g = 0; g < assembly.Simulations.Count; g++)
            {
                ThermalSolver solver = assembly.Simulations[g].Solver;
                if (solver.Nodes.Count < 2) continue;

                for (int i = 0; i < solver.Nodes.Count; i++)
                {
                    if (solver.Nodes[i].ExposedArea > 0f) continue;
                    if (solver.NodeConductanceTotal(i) > 0f) continue;

                    count.SealedBlocks++;
                }
            }

            if (CorpusRecord.On)
            {
                List<string> row = new List<string>();
                row.Add(CorpusRecord.ShipRow(ship, assembly.NodeCount, assembly.Bridges.Count,
                    assembly.RoomCount, count.SealedBlocks, false, assembly.NodeCount == ship.Blocks));
                CorpusRecord.Write("ships", CorpusRecord.ShipHeader, row);
            }

            return count;
        }

        /// <summary>
        /// A blueprint's blocks are all accounted for: placed, or explicitly counted as unknown.
        ///
        /// A parser that silently drops blocks yields a ship that simulates perfectly well and
        /// answers a different question from the one asked — a hull missing a third of its armour
        /// is cooler than the real thing and nothing says so.
        /// </summary>
        internal static void EveryBlockInABlueprintIsEitherPlacedOrCountedAsUnknown()
        {
            // Behind the corpus opt-in like every other test here that reads it. This one missed
            // the gate and re-read the whole corpus twice per file on every run of the suite:
            // 4 m 57 s of a 5 m 4 s suite, on any machine where the corpus exists.
            if (Environment.GetEnvironmentVariable("THERMAL_CORPUS_TESTS") == null) return;
            if (!GameBlocks.IsInstalled) return;

            string root = Blueprints.DefaultPath();
            if (root == null) return;

            // Two XML loads per file — the parse and the raw count — over every file in the corpus.
            // Serially that is twenty thousand parses on one thread; the files are independent.
            List<string> wrong = LabRun.MapMany(Blueprints.Files(root), Account, LabMode.Parallel);

            Assert.Empty(wrong);
        }

        /// <summary>Checks one file's raw block count against what the reader accounted for.</summary>
        private static List<string> Account(string file)
        {
            List<string> wrong = new List<string>();

            List<Blueprints.Ship> ships = Blueprints.Read(file);
            if (ships.Count == 0) return wrong;

            int accounted = 0;
            foreach (Blueprints.Ship ship in ships) accounted += ship.Blocks + ship.UnknownBlocks;

            int inFile = CountBlocks(file);
            if (inFile != accounted)
            {
                wrong.Add(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(file))
                    + ": " + inFile + " blocks in the file, " + accounted + " accounted for");
            }

            return wrong;
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
        /// <summary>
        /// The share of what a ship makes that its hull may still be storing or giving up before it
        /// counts as finished.
        ///
        /// Necessarily tighter than <see cref="Agreement"/>: a hull admitted while still moving at
        /// the width of the assertion could satisfy the assertion by drifting, which would be a
        /// test that measures the guard.
        /// </summary>
        private const float BulkFlat = 0.01f;

        /// <summary>How near made and vented must be, as a share of made.</summary>
        private const float Agreement = 0.05f;

        internal static void ASettledGridShedsWhatItMakes()
        {
            List<string> corpus = Corpus();
            if (corpus.Count == 0) return;

            // The ceiling, and one directional burn. all-peak is every consumer, every tool, every
            // thruster in every direction and every reactor at its plate rating, which is the
            // largest number of watts a hull can be made to put through itself: the balance is
            // hardest to satisfy there and a slip in the ledger has the most room to show. Neither
            // says much on an idle ship, whose made watts round to nothing and which the loop below
            // discards anyway.
            List<Battery.Scenario> scenarios = new List<Battery.Scenario>();
            foreach (Battery.Scenario scenario in Battery.All())
            {
                if (scenario.Name == "all-peak" || scenario.Name == "burn-forward")
                {
                    scenarios.Add(scenario);
                }
            }

            // Every ship in the corpus, one worker per ship. Ships carrying a sealed block are the
            // one exclusion: a sealed block absorbs without limit, so such a hull can never balance
            // and running it here measures the open defect above rather than the invariant.
            List<string> unbalanced = new List<string>();
            int judged = 0;
            int ran = 0;

            List<Balance> results = CorpusFixture.Sweep("settled-balance", delegate (Blueprints.Ship ship)
            {
                if (HasSealedBlock(ship)) return null;

                Balance balance = new Balance();
                foreach (Battery.Scenario scenario in scenarios)
                {
                    balance.Outcomes.Add(Battery.Run(ship, scenario));
                }

                // Recorded here rather than after the sweep. A walk over ten thousand ships runs
                // for hours, and writing the dataset only once it finishes means an interruption
                // at hour three leaves nothing behind at all.
                CorpusRecord.Outcomes("settled-balance", balance.Outcomes);
                return balance;
            });

            foreach (Balance balance in results)
            {
                ran += balance.Outcomes.Count;
                judged += Judge(balance.Outcomes, unbalanced);
            }

            Assert.Empty(unbalanced);

            // A run in which every outcome was discarded proves nothing, and every discard above
            // is a plausible thing to happen to the whole corpus at once — ships that never settle,
            // ships that melt, runs too short to measure a drift. Without this the test reports
            // success for having looked at nothing.
            Assert.True(judged > 0,
                "no outcome in the corpus reached equilibrium, so the balance was never checked. "
                + ran + " runs, all discarded.");
        }

        /// <summary>
        /// Checks one batch of outcomes and returns how many of them were actually in a position to
        /// be checked. Anything unbalanced is appended to <paramref name="unbalanced"/>.
        /// </summary>
        private static int Judge(List<ScenarioOutcome> outcomes, List<string> unbalanced)
        {
            int judged = 0;

            foreach (ScenarioOutcome outcome in outcomes)
            {
                if (outcome.SecondsToSettle < 0f) continue;       // never settled; nothing to claim
                if (outcome.MadeWatts <= 1f) continue;            // nothing being made

                // A ship with any block still climbing has not reached equilibrium however steady
                // its hottest block looks, and the settle detector watches only that one. Blocks
                // over critical are the tell: they are the ones still going.
                if (outcome.BlocksOverCritical > 0) continue;

                // Nor has one whose hull is still cooling. The run stops when the hottest block
                // stops, and on a heavy ship that is minutes into a cooldown that takes an hour:
                // the armour goes on dumping stored heat, so vented runs far above made and the
                // difference is not a fault but a hull that was never asked to finish. Ask the bulk
                // whether it has, and say nothing about the ones that have not.
                //
                // In watts rather than Kelvin, because a hundredth of a degree a minute is nothing
                // on a capital hull and most of the heat budget on an interceptor. This is not the
                // assertion in disguise: the drift is read from the stored heat, and the two watt
                // figures below are read from the solver's ledger, so a ship that is genuinely
                // still cooling is dropped here while one whose ledger has started lying keeps its
                // small drift, is admitted, and fails.
                if (float.IsNaN(outcome.BulkDriftWatts)) continue;       // too short to tell
                if (System.Math.Abs(outcome.BulkDriftWatts) > BulkFlat * outcome.MadeWatts) continue;

                judged++;

                float difference = System.Math.Abs(outcome.MadeWatts - outcome.VentedWatts);
                float share = difference / outcome.MadeWatts;

                if (share > Agreement)
                {
                    unbalanced.Add(outcome.Ship + " / " + outcome.Scenario + ": made "
                        + outcome.MadeWatts.ToString("n0") + " W, vented "
                        + outcome.VentedWatts.ToString("n0") + " W");
                }
            }

            return judged;
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
        internal static void MoreLoadIsNeverCooler()
        {
            List<string> corpus = Corpus();
            if (corpus.Count == 0) return;

            Dictionary<string, Battery.Scenario> byName = new Dictionary<string, Battery.Scenario>();
            foreach (Battery.Scenario scenario in Battery.All()) byName[scenario.Name] = scenario;

            List<Battery.Scenario> ladder = new List<Battery.Scenario>
            {
                byName["idle"], byName["full-electrical"], byName["all-peak"],
            };

            // The whole corpus rather than the largest ship in it. The claim is universal, and the
            // hull it used to be asked of was the heaviest one there — the least sensitive ship
            // available, because the most thermal mass moves least for a given change in load, and
            // a hull that happens to carry no example of a misclassified block cannot show that the
            // block was misclassified.
            List<string> wrong = new List<string>();

            // One worker per ship, running the ladder in order on a hull it owns outright.
            List<Balance> results = CorpusFixture.Sweep("load-ladder", delegate (Blueprints.Ship ship)
            {
                Balance balance = new Balance();
                foreach (Battery.Scenario rung in ladder) balance.Outcomes.Add(Battery.Run(ship, rung));
                CorpusRecord.Outcomes("load-ladder", balance.Outcomes);
                return balance;
            });

            Assert.True(results.Count > 0);

            foreach (Balance balance in results)
            {
                List<ScenarioOutcome> rungs = Ordered(balance.Outcomes, ladder);

                for (int i = 1; i < rungs.Count; i++)
                {
                    if (rungs[i].PeakKelvin >= rungs[i - 1].PeakKelvin - 1f) continue;

                    wrong.Add(rungs[i].Ship + ": " + rungs[i].Scenario + " peaks at "
                        + rungs[i].PeakKelvin.ToString("n0") + " K, below "
                        + rungs[i - 1].Scenario + "'s "
                        + rungs[i - 1].PeakKelvin.ToString("n0") + " K");
                }
            }

            Assert.Empty(wrong);
        }

        /// <summary>Outcomes grouped by the ship that produced them.</summary>
        private static Dictionary<string, List<ScenarioOutcome>> ByShip(List<ScenarioOutcome> outcomes)
        {
            Dictionary<string, List<ScenarioOutcome>> byShip =
                new Dictionary<string, List<ScenarioOutcome>>();

            foreach (ScenarioOutcome outcome in outcomes)
            {
                if (!byShip.ContainsKey(outcome.Ship))
                {
                    byShip[outcome.Ship] = new List<ScenarioOutcome>();
                }

                byShip[outcome.Ship].Add(outcome);
            }

            return byShip;
        }

        /// <summary>
        /// One ship's outcomes, put back into the order the scenarios were asked for. The lab is
        /// free to return its rows in any order, and a ladder read out of order is not a ladder.
        /// </summary>
        private static List<ScenarioOutcome> Ordered(List<ScenarioOutcome> outcomes,
            List<Battery.Scenario> scenarios)
        {
            List<ScenarioOutcome> ordered = new List<ScenarioOutcome>();

            foreach (Battery.Scenario scenario in scenarios)
            {
                foreach (ScenarioOutcome outcome in outcomes)
                {
                    if (outcome.Scenario == scenario.Name) ordered.Add(outcome);
                }
            }

            return ordered;
        }

        /// <summary>
        /// The sun only ever adds. A hull held to it cannot be cooler than the same hull in shadow,
        /// and if it is, the solar path has its sign or its facing wrong.
        /// </summary>
        internal static void SunlightNeverCools()
        {
            List<string> corpus = Corpus();
            if (corpus.Count == 0) return;

            Dictionary<string, Battery.Scenario> byName = new Dictionary<string, Battery.Scenario>();
            foreach (Battery.Scenario scenario in Battery.All()) byName[scenario.Name] = scenario;

            List<Battery.Scenario> pair = new List<Battery.Scenario>
            {
                byName["vacuum-shadow"], byName["vacuum-sunlit"],
            };

            // Every ship, not the smallest one in the corpus.
            List<string> cooled = new List<string>();
            int warmed = 0;
            int ships = 0;

            List<Balance> results = CorpusFixture.Sweep("sunlight", delegate (Blueprints.Ship ship)
            {
                Balance balance = new Balance();
                foreach (Battery.Scenario scenario in pair) balance.Outcomes.Add(Battery.Run(ship, scenario));
                CorpusRecord.Outcomes("sunlight", balance.Outcomes);
                return balance;
            });

            ships = results.Count;

            foreach (Balance balance in results)
            {
                List<ScenarioOutcome> both = Ordered(balance.Outcomes, pair);
                if (both.Count < 2) continue;

                float shadow = both[0].MeanKelvin;
                float sunlit = both[1].MeanKelvin;

                if (sunlit < shadow - 1f)
                {
                    cooled.Add(both[0].Ship + ": sunlit averaged " + sunlit.ToString("n0")
                        + " K against " + shadow.ToString("n0") + " K in shadow");
                }

                if (sunlit > shadow + 1f) warmed++;
            }

            Assert.True(ships > 0);
            Assert.Empty(cooled);

            // "Never cooler" is also satisfied by a sun that does nothing at all, which is what a
            // solar path that silently stopped contributing would look like. Somewhere in a corpus
            // this size a hull has to have been warmed by it.
            Assert.True(warmed > 0,
                "no ship in the corpus was warmed by being put in the sun, so the solar path may "
                + "not be contributing at all.");
        }

        /// <summary>
        /// The same ship in the same state twice gives the same answer.
        ///
        /// Distinct from the parallel-against-linear check: that one guards shared state between
        /// concurrent runs, this one guards state left behind *between sequential* runs — a cache
        /// that keeps a load, a model mutated in place, a static that remembers the last ship.
        /// </summary>
        internal static void RunningTheSameThingTwiceGivesTheSameAnswer()
        {
            List<string> corpus = Corpus();
            if (corpus.Count == 0) return;

            List<string> drifted = new List<string>();
            int ships = 0;

            // **Across ships, not one after another.** Each ship's four runs have to stay in order —
            // the whole point is that one scenario contaminates the *next* one — but ships are
            // independent of each other, and a plain foreach here put four full simulations of
            // every hull in the corpus onto a single thread while thirty-one workers idled. The
            // ordering that matters is inside the lambda; the concurrency is between lambdas.
            List<List<string>> reports = CorpusFixture.Sweep("determinism", Compare);
            foreach (List<string> report in reports)
            {
                ships++;
                drifted.AddRange(report);
            }

            Assert.True(ships > 0);
            Assert.Empty(drifted);
        }

        /// <summary>
        /// Runs one ship three times with another scenario in the middle, and once more against a
        /// copy read fresh off disk, and reports whatever disagreed.
        ///
        /// Three runs, because two of the same scenario back to back is the one sequence that
        /// cannot detect the fault this is here for. Every simulation built from a ship shares that
        /// ship's <c>BlockInstance</c> objects and <c>ShipLoad</c> writes the load onto them, so a
        /// scenario that leaves something behind contaminates the *next* scenario, not a repeat of
        /// itself — a repeat re-derives the same wrong state from the same mutated instances and
        /// agrees with itself perfectly.
        /// </summary>
        internal static List<string> Compare(Blueprints.Ship ship)
        {
            Dictionary<string, Battery.Scenario> byName = new Dictionary<string, Battery.Scenario>();
            foreach (Battery.Scenario candidate in Battery.All()) byName[candidate.Name] = candidate;

            Battery.Scenario scenario = byName["full-electrical"];
            Battery.Scenario between = byName["burn-forward"];

            List<string> drifted = new List<string>();

            ScenarioOutcome first = Battery.Run(ship, scenario);
            Battery.Run(ship, between);
            ScenarioOutcome third = Battery.Run(ship, scenario);

            // And against a copy read fresh off disk, which is the only version of this ship that
            // no run has touched.
            ScenarioOutcome clean = Battery.Run(ship.Reload(), scenario);

            if (Differs(first, third)) drifted.Add(ship.Name + ": changed after another scenario ran");
            if (Differs(first, clean)) drifted.Add(ship.Name + ": differs from a freshly read copy");

            return drifted;
        }

        /// <summary>Whether two runs of the same ship and scenario disagree.</summary>
        private static bool Differs(ScenarioOutcome a, ScenarioOutcome b)
        {
            if (a.HottestBlock != b.HottestBlock) return true;
            if (System.Math.Abs(a.PeakKelvin - b.PeakKelvin) > 0.001f) return true;
            return System.Math.Abs(a.MeanKelvin - b.MeanKelvin) > 0.001f;
        }
    }
}
