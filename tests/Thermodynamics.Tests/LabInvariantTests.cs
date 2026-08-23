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
    ///
    /// <para>
    /// **What is left here is what nothing else does.** `CorpusSurvey` absorbed the walks that used
    /// to stand alone — sealed blocks are rare, a settled grid sheds what it makes, more load is
    /// never cooler — and `SunlightPanelWalk` took the sunlight claim, because all four want the
    /// same build of the same ship and doing them in one pass costs one pass. The bodies stayed
    /// behind as uncalled helpers for a while, which is the worst of both: a claim that reads as
    /// checked and runs nowhere. They are gone; the claims are in the two classes named above.
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
