using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using System.Xml.Linq;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// A block that is hot because of **what it is**, not because of the power crossing it.
    ///
    /// <para>
    /// Every other heat term in this model is a fraction of watts passing through a block:
    /// produced, consumed, or spent on thrust. That describes a reactor and a thruster and nothing
    /// else. Decay heat in spent fuel, a forge, a campfire, a wreck still burning — none of them
    /// draw power, and a mod author's only recourse was to invent a power draw the block does not
    /// have and accept whatever consumer fraction rode along with it.
    /// </para>
    ///
    /// <para>
    /// <c>HeatSourceWatts</c> is that term. These tests pin the four things it has to be: watts
    /// that arrive, watts that are *conserved*, watts independent of power, and watts that are
    /// zero unless asked for — the last mattering because the property is deliberately absent from
    /// the Cubes.xml required list, so every block in the game inherits it as an omission.
    /// </para>
    /// </summary>
    public class CustomHeatSourceTests
    {
        /// <summary>Settings with nothing but conduction and the source: no sky, no sun, no air.</summary>
        private static ThermalSettings Isolated()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableFriction = false;
            settings.EnableDamage = false;
            return settings.Derive();
        }

        private static BlockModel Smouldering(float watts)
        {
            BlockThermalProperties thermal = Catalog.DefaultThermal();
            thermal.HeatSourceWatts = watts;
            return BlockModel.Solid("Smoulder", Vector3I.One, 1000f, thermal);
        }

        /// <summary>One block, one grid, stepped for a while. Returns the simulation.</summary>
        private static ThermalSimulation Rig(float watts, int steps)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Smouldering(watts), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated(), 293.15f);
            simulation.Solver.CollectDiagnostics = true;
            simulation.StepExact(steps, Worlds.Shadow());
            return simulation;
        }

        // ---- it arrives ------------------------------------------------------------------------

        /// <summary>
        /// A block declaring intrinsic watts heats up, and one declaring none does not. The
        /// negative half is the important half: the property defaults to zero and is omitted from
        /// every shipped definition, so if omission ever started meaning "something", every block
        /// in the game would quietly become a heater.
        /// </summary>
        [Fact]
        public void DeclaredWattsHeatTheBlockAndNoneLeavesItAlone()
        {
            Assert.True(Rig(50000f, 200).Solver.Nodes[0].Temperature > 293.15f);
            Assert.Equal(293.15f, Rig(0f, 200).Solver.Nodes[0].Temperature, 3);
        }

        /// <summary>
        /// The watts reported are the watts declared — not a fraction of them.
        ///
        /// This is the distinction the property exists for. <c>ProducerWasteEnergy</c> and
        /// <c>ConsumerWasteEnergy</c> are fractions applied to power; a source that quietly ran
        /// through either of them would deliver five per cent of what its author asked for, and the
        /// only symptom would be a block that is mysteriously cold.
        /// </summary>
        [Fact]
        public void TheWattsAreTakenLiterallyRatherThanAsAFraction()
        {
            ThermalNode node = Rig(50000f, 1).Solver.Nodes[0];
            Assert.Equal(50000f, node.HeatGenerationWatts, 1);
        }

        // ---- it is conserved -------------------------------------------------------------------

        /// <summary>
        /// **The energy arrives in full.** Watts times seconds equals joules, and joules over the
        /// block's own thermal mass is the temperature rise — so the rise is predictable in
        /// closed form and can be checked against the solver rather than merely observed to be
        /// upward.
        ///
        /// Every other check here would pass on a source that delivered the wrong amount. This one
        /// is why a units slip in the new term cannot hide.
        /// </summary>
        [Fact]
        public void TheEnergyDeliveredIsWattsTimesSeconds()
        {
            const float Watts = 50000f;
            const int Steps = 100;

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Smouldering(Watts), Vector3I.Zero);

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

        /// <summary>Twice the watts is twice the rise, which no fraction or clamp survives.</summary>
        [Fact]
        public void TwiceTheWattsIsTwiceTheRise()
        {
            float one = Rig(25000f, 100).Solver.Nodes[0].Temperature - 293.15f;
            float two = Rig(50000f, 100).Solver.Nodes[0].Temperature - 293.15f;

            Assert.True(one > 0f);
            Assert.Equal(2f, two / one, 2);
        }

        // ---- it is independent of power --------------------------------------------------------

        /// <summary>
        /// Intrinsic heat adds to waste heat rather than replacing it: a block may both draw power
        /// and smoulder. A reading that replaced one with the other would make an intrinsic source
        /// silently switch off any block that was also a consumer.
        /// </summary>
        [Fact]
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

            // 10,000 intrinsic + half of 20,000 consumed.
            Assert.Equal(20000f, simulation.Solver.Nodes[0].HeatGenerationWatts, 1);
        }

        /// <summary>
        /// A source with no power figures at all still makes heat. This is the case the property
        /// was added for and the one every existing mechanism fails: a campfire draws nothing.
        /// </summary>
        [Fact]
        public void ASourceWithNoPowerAtAllStillMakesHeat()
        {
            ThermalSimulation simulation = Rig(50000f, 1);
            BlockInstance block = simulation.Solver.Nodes[0].Block;

            Assert.Equal(0f, block.PowerProducedWatts);
            Assert.Equal(0f, block.PowerConsumedWatts);
            Assert.Equal(0f, block.ThrustWatts);
            Assert.True(simulation.Solver.Nodes[0].HeatGenerationWatts > 0f);
        }

        // ---- it behaves like a source in a grid ------------------------------------------------

        /// <summary>
        /// The heat conducts away into its neighbours rather than pooling in the block that made
        /// it — the property is a *source*, not a temperature override.
        /// </summary>
        [Fact]
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

        /// <summary>
        /// **It reaches equilibrium, and the balance holds there.** A source that only ever climbs
        /// would be indistinguishable from one that leaks energy, so the check that matters is
        /// that a hull carrying one settles where what it makes equals what it sheds.
        /// </summary>
        [Fact]
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

        // ---- it is declarable the way every other property is ----------------------------------

        /// <summary>
        /// The harness's offline loader reads HeatSourceWatts out of Cubes.xml. A property the two
        /// parsers disagree about behaves differently in tests than in the game.
        /// </summary>
        [Fact]
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

        /// <summary>A negative declaration is clamped rather than cooling the block.</summary>
        [Fact]
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
