using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    [Collection("alone")]
    public class LabInvariantTests
    {
/// <summary>Corpus operation.</summary>
        private static List<string> Corpus()
        {
            return CorpusFixture.Files();
        }


        [Fact]
/// <summary>NoDefinitionClaimsAPreposterousAmountOfPower operation.</summary>
        public void NoDefinitionClaimsAPreposterousAmountOfPower()
        {
            if (!GameBlocks.IsInstalled) return;

            const float Ceiling = 1e9f;
/// <summary>List operation.</summary>
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

        [Fact]
/// <summary>EveryDefinitionHasASizeAndAMass operation.</summary>
        public void EveryDefinitionHasASizeAndAMass()
        {
            if (!GameBlocks.IsInstalled) return;

/// <summary>List operation.</summary>
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
/// <summary>EveryBlockInABlueprintIsEitherPlacedOrCountedAsUnknown operation.</summary>
        internal static void EveryBlockInABlueprintIsEitherPlacedOrCountedAsUnknown()
        {
            if (!CorpusFixture.OptedIn()) return;
            if (!GameBlocks.IsInstalled) return;

            string root = Blueprints.DefaultPath();
            if (root == null) return;

            List<string> wrong = LabRun.MapMany(Blueprints.Files(root), Account, LabMode.Parallel);

            Assert.Empty(wrong);
        }

/// <summary>Account operation.</summary>
        private static List<string> Account(string file)
        {
/// <summary>List operation.</summary>
            List<string> wrong = new List<string>();

            List<Blueprints.Ship> ships = Blueprints.Read(file);
            if (ships.Count == 0) return wrong;

            int accounted = 0;
            foreach (Blueprints.Ship ship in ships) accounted += ship.Blocks + ship.UnknownBlocks;

/// <summary>CountBlocks operation.</summary>
            int inFile = CountBlocks(file);
            if (inFile != accounted)
            {
                wrong.Add(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(file))
                    + ": " + inFile + " blocks in the file, " + accounted + " accounted for");
            }

            return wrong;
        }

/// <summary>CountBlocks operation.</summary>
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

        [Fact]
/// <summary>ABlockWithNoNeighboursIsFullyExposed operation.</summary>
        public void ABlockWithNoNeighboursIsFullyExposed()
        {
            if (!GameBlocks.IsInstalled) return;

            GameBlocks.Definition armour;
            if (!GameBlocks.BySubtype().TryGetValue("LargeBlockArmorBlock", out armour)) return;

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Blueprints.Model(armour), Vector3I.Zero);

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
/// <summary>RunningTheSameThingTwiceGivesTheSameAnswer operation.</summary>
        internal static void RunningTheSameThingTwiceGivesTheSameAnswer()
        {
/// <summary>Corpus operation.</summary>
            List<string> corpus = Corpus();
            if (corpus.Count == 0) return;

/// <summary>List operation.</summary>
            List<string> drifted = new List<string>();
            int ships = 0;

            List<List<string>> reports = CorpusFixture.Sweep("determinism", Compare);
            foreach (List<string> report in reports)
            {
                ships++;
                drifted.AddRange(report);
            }

            Assert.True(ships > 0);
            Assert.Empty(drifted);
        }

/// <summary>Compare operation.</summary>
        internal static List<string> Compare(Blueprints.Ship ship)
        {
            Dictionary<string, Battery.Scenario> byName = new Dictionary<string, Battery.Scenario>();
            foreach (Battery.Scenario candidate in Battery.All()) byName[candidate.Name] = candidate;

            Battery.Scenario scenario = byName["full-electrical"];
            Battery.Scenario between = byName["burn-forward"];

/// <summary>List operation.</summary>
            List<string> drifted = new List<string>();

            ScenarioOutcome first = Battery.Run(ship, scenario);
            Battery.Run(ship, between);
            ScenarioOutcome third = Battery.Run(ship, scenario);

            ScenarioOutcome clean = Battery.Run(ship.Reload(), scenario);

            if (Differs(first, third)) drifted.Add(ship.Name + ": changed after another scenario ran");
            if (Differs(first, clean)) drifted.Add(ship.Name + ": differs from a freshly read copy");

            return drifted;
        }

/// <summary>Differs operation.</summary>
        private static bool Differs(ScenarioOutcome a, ScenarioOutcome b)
        {
            if (a.HottestBlock != b.HottestBlock) return true;
            if (System.Math.Abs(a.PeakKelvin - b.PeakKelvin) > 0.001f) return true;
            return System.Math.Abs(a.MeanKelvin - b.MeanKelvin) > 0.001f;
        }
    }
}
