using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using System.Xml.Linq;
using Xunit;

namespace Thermodynamics.Tests
{
    public class CustomHeatSourceTests
    {
/// <summary>Isolated operation.</summary>
        private static ThermalSettings Isolated()
        {
            return Isolation.DeadWorld();
        }

/// <summary>Smouldering operation.</summary>
        private static BlockModel Smouldering(float watts)
        {
            BlockThermalProperties thermal = Catalog.DefaultThermal();
            thermal.HeatSourceWatts = watts;
            return BlockModel.Solid("Smoulder", Vector3I.One, 1000f, thermal);
        }

/// <summary>Rig operation.</summary>
        private static ThermalSimulation Rig(float watts, int steps)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Smouldering(watts), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated(), 293.15f);
            simulation.Solver.CollectDiagnostics = true;
            simulation.StepExact(steps, Worlds.Shadow());
            return simulation;
        }


        [Fact]
/// <summary>DeclaredWattsHeatTheBlockAndNoneLeavesItAlone operation.</summary>
        public void DeclaredWattsHeatTheBlockAndNoneLeavesItAlone()
        {
            Assert.True(Rig(50000f, 200).Solver.Nodes[0].Temperature > 293.15f);
            Assert.Equal(293.15f, Rig(0f, 200).Solver.Nodes[0].Temperature, 3);
        }

        [Fact]
/// <summary>TheWattsAreTakenLiterallyRatherThanAsAFraction operation.</summary>
        public void TheWattsAreTakenLiterallyRatherThanAsAFraction()
        {
/// <summary>Rig operation.</summary>
            ThermalNode node = Rig(50000f, 1).Solver.Nodes[0];
            Assert.Equal(50000f, node.HeatGenerationWatts, 1);
        }


        [Fact]
/// <summary>TheEnergyDeliveredIsWattsTimesSeconds operation.</summary>
        public void TheEnergyDeliveredIsWattsTimesSeconds()
        {
            const float Watts = 50000f;
            const int Steps = 100;

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Smouldering(Watts), Vector3I.Zero);

/// <summary>Isolated operation.</summary>
            ThermalSettings settings = Isolated();
            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);

            ThermalNode node = simulation.Solver.Nodes[0];
            float before = node.Temperature;
            float mass = node.ThermalMass;

            simulation.StepExact(Steps, Worlds.Shadow());

            float seconds = Steps * simulation.Settings.StepSeconds;
            float expected = before + (Watts * seconds) / mass;

            Assert.True(Math.Abs(node.Temperature - expected) < expected * 0.001f,
                "expected " + expected.ToString("n2") + " K from " + Watts + " W over "
                + seconds.ToString("n1") + " s into " + mass.ToString("n0")
                + " J/K, got " + node.Temperature.ToString("n2"));
        }

        [Fact]
/// <summary>TwiceTheWattsIsTwiceTheRise operation.</summary>
        public void TwiceTheWattsIsTwiceTheRise()
        {
/// <summary>Rig operation.</summary>
            float one = Rig(25000f, 100).Solver.Nodes[0].Temperature - 293.15f;
/// <summary>Rig operation.</summary>
            float two = Rig(50000f, 100).Solver.Nodes[0].Temperature - 293.15f;

            Assert.True(one > 0f);
            Assert.Equal(2f, two / one, 2);
        }


        [Fact]
/// <summary>IntrinsicHeatAddsToWasteHeatRatherThanReplacingIt operation.</summary>
        public void IntrinsicHeatAddsToWasteHeatRatherThanReplacingIt()
        {
            BlockThermalProperties thermal = Catalog.DefaultThermal();
            thermal.HeatSourceWatts = 10000f;
            thermal.ConsumerWasteEnergy = 0.5f;

            GridBuilder builder = GridBuilder.Large();
            builder.Place(BlockModel.Solid("Both", Vector3I.One, 1000f, thermal), Vector3I.Zero);
            builder.Last.PowerConsumedWatts = 20000f;

            ThermalSimulation simulation = builder.BuildSimulation(Isolated(), 293.15f);
            simulation.StepExact(1, Worlds.Shadow());

            Assert.Equal(20000f, simulation.Solver.Nodes[0].HeatGenerationWatts, 1);
        }

        [Fact]
/// <summary>ASourceWithNoPowerAtAllStillMakesHeat operation.</summary>
        public void ASourceWithNoPowerAtAllStillMakesHeat()
        {
/// <summary>Rig operation.</summary>
            ThermalSimulation simulation = Rig(50000f, 1);
            BlockInstance block = simulation.Solver.Nodes[0].Block;

            Assert.Equal(0f, block.PowerProducedWatts);
            Assert.Equal(0f, block.PowerConsumedWatts);
            Assert.Equal(0f, block.ThrustWatts);
            Assert.True(simulation.Solver.Nodes[0].HeatGenerationWatts > 0f);
        }


        [Fact]
/// <summary>TheHeatConductsIntoNeighbouringBlocks operation.</summary>
        public void TheHeatConductsIntoNeighbouringBlocks()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Smouldering(80000f), Vector3I.Zero);
            builder.Place(Catalog.HeavyArmor(), new Vector3I(1, 0, 0));
            builder.Place(Catalog.HeavyArmor(), new Vector3I(2, 0, 0));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated(), 293.15f);
            simulation.StepExact(400, Worlds.Shadow());

            float source = simulation.Solver.GetNodeAt(Vector3I.Zero).Temperature;
            float near = simulation.Solver.GetNodeAt(new Vector3I(1, 0, 0)).Temperature;
            float far = simulation.Solver.GetNodeAt(new Vector3I(2, 0, 0)).Temperature;

            Assert.True(near > 293.15f, "the neighbour should have been warmed");
            Assert.True(source > near, "the source should be the hottest");
            Assert.True(near > far, "heat should fall off with distance from the source");
        }

        [Fact]
/// <summary>AHullCarryingASourceSettlesWhereMadeEqualsVented operation.</summary>
        public void AHullCarryingASourceSettlesWhereMadeEqualsVented()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Smouldering(20000f), Vector3I.Zero);
            for (int i = 1; i < 6; i++) builder.Place(Catalog.LightArmor(), new Vector3I(i, 0, 0));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            simulation.Solver.CollectDiagnostics = true;
            simulation.StepExact(20000, Worlds.Shadow());

            float made = simulation.HeatGainWatts;
            float vented = simulation.VentedWatts;

            Assert.True(made > 0f, "the source should be making heat");
            Assert.True(Math.Abs(made - vented) < made * 0.05f,
                "at equilibrium the hull should shed what the source makes: made "
                + made.ToString("n0") + " W, vented " + vented.ToString("n0") + " W");
        }


        [Fact]
/// <summary>TheOfflineParserReadsTheProperty operation.</summary>
        public void TheOfflineParserReadsTheProperty()
        {
            BlockThermalProperties properties = BlockThermalProperties.Default();
            Assert.Equal(0f, properties.HeatSourceWatts);

            XElement definition = XElement.Parse(
                "<Definition><Id><TypeId>CubeBlock</TypeId><SubtypeId>Smoulder</SubtypeId></Id>"
                + "<ModExtensions><Group Name=\"ThermalBlockProperties\">"
                + "<Bool Name=\"ExcludeFromSimulation\" Value=\"false\" />"
                + "<Decimal Name=\"HeatSourceWatts\" Value=\"1234\" />"
                + "</Group></ModExtensions></Definition>");

            BlockThermalProperties parsed = ShippedBlocks.ParseThermalForTest(definition);
            Assert.NotNull(parsed);
            Assert.Equal(1234f, parsed.HeatSourceWatts);
        }

        [Fact]
/// <summary>BothParsersKnowTheSamePropertyNames operation.</summary>
        public void BothParsersKnowTheSamePropertyNames()
        {
            string source = System.IO.File.ReadAllText(System.IO.Path.Combine(
                ShippedBlocks.RepoRoot(), "Thermodynamics",
                "Definitions", "ThermalCellDefinition.cs"));

/// <summary>HashSet operation.</summary>
            HashSet<string> inGame = new HashSet<string>();
            foreach (System.Text.RegularExpressions.Match match in
                System.Text.RegularExpressions.Regex.Matches(source, "GetOrCompute\\(\"(\\w+)\"\\)"))
            {
                inGame.Add(match.Groups[1].Value);
            }

            Assert.True(inGame.Count > 8,
                "only " + inGame.Count + " names were found in the in-game reader, so this test is"
                + " no longer reading it");

            inGame.Remove("ThermalBlockProperties");

            string offline = System.IO.File.ReadAllText(System.IO.Path.Combine(
                ShippedBlocks.RepoRoot(), "tests", "Thermodynamics.Harness", "ShippedBlocks.cs"));

/// <summary>List operation.</summary>
            List<string> unknown = new List<string>();
            foreach (string name in inGame)
            {
                if (offline.IndexOf("\"" + name + "\"", StringComparison.Ordinal) < 0) unknown.Add(name);
            }

            unknown.Sort(StringComparer.Ordinal);
            Assert.True(unknown.Count == 0,
                "properties the game reads and the harness does not:\n  "
                + string.Join("\n  ", unknown.ToArray()));

/// <summary>List operation.</summary>
            List<string> ungame = new List<string>();
            foreach (System.Text.RegularExpressions.Match match in
                System.Text.RegularExpressions.Regex.Matches(offline, "case \"(\\w+)\":\\s*properties\\."))
            {
                string name = match.Groups[1].Value;
                if (!inGame.Contains(name)) ungame.Add(name);
            }

            ungame.Sort(StringComparer.Ordinal);
            Assert.True(ungame.Count == 0,
                "properties the harness reads and the game does not, so they work in every test"
                + " here and in no world:\n  " + string.Join("\n  ", ungame.ToArray()));
        }

        [Fact]
/// <summary>ANegativeDeclarationCannotCoolABlock operation.</summary>
        public void ANegativeDeclarationCannotCoolABlock()
        {
            BlockThermalProperties properties = BlockThermalProperties.Default();
            properties.HeatSourceWatts = -5000f;
            properties.Clamp();

            Assert.Equal(0f, properties.HeatSourceWatts);
            Assert.Equal(293.15f, Rig(-5000f, 100).Solver.Nodes[0].Temperature, 3);
        }
    }
}
