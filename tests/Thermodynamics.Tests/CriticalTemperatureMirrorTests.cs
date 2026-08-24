using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Each node's critical temperature is mirrored into a flat array beside its mass and
    /// emissivity, so the apply pass can decide whether a block is burning without reaching
    /// through the node object to its block and its model.
    ///
    /// <para>
    /// That test runs once per node per substep and fires almost never — two field sessions
    /// recorded zero critical events between them — and reaching it cost three dependent loads
    /// into memory scattered across the heap. Removing them took the marginal cost of
    /// <c>EnableDamage</c> from 0.85 ms to 0.19 ms of a step, and the 125,000-block rung of the
    /// ladder from 30.8 ms to 17.1 ms.
    /// </para>
    ///
    /// <para>
    /// A mirrored row is only correct while something refreshes it, and the failure it invites is
    /// specific: a row that belongs to the block that used to hold that index. A block destroyed
    /// beside a reactor would then be judged against the reactor's 1,200 K rating, or the reactor
    /// against its 900 K one — silently, and only on grids that have lost a block. The tests below
    /// drive exactly that.
    /// </para>
    /// </summary>
    public class CriticalTemperatureMirrorTests
    {
        private static ThermalSettings Settings()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubsteps = 4096;
            settings.MaxElementVisitsPerStep = 0;
            settings.EnableDamage = true;
            settings.EnableConduction = false;
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableWasteHeat = false;
            return settings.Derive();
        }

        /// <summary>
        /// Which subtypes overheated in one step, by the temperature each node was driven to.
        ///
        /// Names rather than indices, because the point of every test here is that a node's rating
        /// must follow the block and not the slot it happens to occupy.
        /// </summary>
        private static List<string> Burned(ThermalSimulation simulation, float kelvin)
        {
            simulation.Solver.SetAllTemperatures(kelvin);
            simulation.Solver.Step(simulation.Settings.StepSeconds, EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, Worlds.Space(Vector3.UnitX)));

            List<string> names = new List<string>();
            IList<OverheatEvent> events = simulation.Solver.Overheats;
            for (int i = 0; i < events.Count; i++) names.Add(events[i].Block.Model.Name);

            names.Sort();
            return names;
        }

        /// <summary>Halfway between two ratings, which is above the first and below the second.</summary>
        private static float Between(float low, float high)
        {
            return 0.5f * (low + high);
        }

        /// <summary>The rating a block carries, so a threshold can be read rather than written.</summary>
        private static float Rating(ThermalSimulation simulation, string name)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Block.Model.Name == name) return nodes[i].Thermal.CriticalTemperature;
            }

            throw new InvalidOperationException("no block named " + name + " on the rig");
        }

        /// <summary>
        /// Three blocks with three different ratings, in ascending order.
        ///
        /// The ratings themselves are read off the blocks rather than written here: the catalogue
        /// derives its stand-ins from the blocks they stand in for, so the three figures follow the
        /// shipped definitions and a literal would be pinning those instead of the mirror.
        /// </summary>
        private static GridBuilder ThreeRatings()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), 0, 0, 0);
            builder.Place(Catalog.Thruster(), 4, 0, 0);
            builder.Place(Catalog.Reactor(), 8, 0, 0);
            return builder;
        }

        [Fact]
        public void EachBlockIsJudgedAgainstItsOwnRating()
        {
            ThermalSimulation simulation = ThreeRatings().BuildSimulation(Settings(), 293.15f);
            simulation.RebuildAll();

            float armour = Rating(simulation, "LightArmorBlock");
            float reactor = Rating(simulation, "SmallReactor");
            float thruster = Rating(simulation, "LargeThruster");

            // The rig is only a test of the mirror while the three ratings are three numbers.
            Assert.True(armour < reactor && reactor < thruster,
                "the three ratings are " + armour + ", " + reactor + " and " + thruster
                + ", which does not separate them");

            // Above the armour, below the other two.
            Assert.Equal(new[] { "LightArmorBlock" },
                Burned(simulation, Between(armour, reactor)).ToArray());

            // Above the reactor as well.
            Assert.Equal(new[] { "LightArmorBlock", "SmallReactor" },
                Burned(simulation, Between(reactor, thruster)).ToArray());

            // Above all three.
            Assert.Equal(new[] { "LargeThruster", "LightArmorBlock", "SmallReactor" },
                Burned(simulation, thruster + 100f).ToArray());
        }

        /// <summary>
        /// The failure a stale mirror produces. Removing a node moves the last node into its slot,
        /// so the last block ends up holding the index the armour had. If the row does not follow
        /// it, that block is judged at the armour's rating and burns hundreds of kelvin early.
        /// </summary>
        [Fact]
        public void ARatingFollowsItsBlockWhenARemovalMovesTheIndex()
        {
            GridBuilder builder = ThreeRatings();
            ThermalSimulation simulation = builder.BuildSimulation(Settings(), 293.15f);
            simulation.RebuildAll();

            // Settle the mirrored rows before the removal, so a stale row is a row that was right.
            Burned(simulation, 293.15f);

            float reactor = Rating(simulation, "SmallReactor");
            float thruster = Rating(simulation, "LargeThruster");

            BlockInstance armour = builder.Grid.GetAtCell(new Vector3I(0, 0, 0));
            Assert.NotNull(armour);
            simulation.RemoveBlock(armour);

            // Between the reactor's rating and the thruster's: only the reactor may burn.
            Assert.Equal(new[] { "SmallReactor" },
                Burned(simulation, Between(reactor, thruster)).ToArray());

            // And the thruster still burns when it should.
            Assert.Equal(new[] { "LargeThruster", "SmallReactor" },
                Burned(simulation, thruster + 100f).ToArray());
        }

        /// <summary>
        /// A block placed after the grid is live gets its own rating rather than whatever the
        /// buffer held at that index.
        /// </summary>
        [Fact]
        public void ARatingArrivesWithABlockPlacedIntoALiveGrid()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), 0, 0, 0);

            ThermalSimulation simulation = builder.BuildSimulation(Settings(), 293.15f);
            simulation.RebuildAll();
            Burned(simulation, 293.15f);

            simulation.AddBlock(new BlockInstance(
                Catalog.Reactor(), new Vector3I(8, 0, 0), BlockOrientation.Identity), 293.15f);

            float armour = Rating(simulation, "LightArmorBlock");
            float reactor = Rating(simulation, "SmallReactor");
            Assert.True(armour < reactor, "the reactor must outrank the armour for this to judge anything");

            // The reactor's own rating, not the armour's.
            Assert.Equal(new[] { "LightArmorBlock" },
                Burned(simulation, Between(armour, reactor)).ToArray());
            Assert.Equal(new[] { "LightArmorBlock", "SmallReactor" },
                Burned(simulation, reactor + 100f).ToArray());
        }

        /// <summary>
        /// Zero means "no rating", and a flat array full of zeros is what an unfilled row looks
        /// like. A block with no critical temperature must never raise an event, which is also
        /// what says the mirror is being read rather than ignored.
        /// </summary>
        [Fact]
        public void ABlockWithNoRatingNeverBurns()
        {
            BlockThermalProperties unrated = Catalog.ReactorThermal();
            unrated.CriticalTemperature = 0f;

            GridBuilder builder = GridBuilder.Large();
            builder.Place(BlockModel.Solid("Unrated", Vector3I.One, 500f, unrated), 0, 0, 0);
            builder.Place(Catalog.Reactor(), 4, 0, 0);

            ThermalSimulation simulation = builder.BuildSimulation(Settings(), 293.15f);
            simulation.RebuildAll();

            Assert.Equal(new[] { "SmallReactor" }, Burned(simulation, 5000f).ToArray());
        }
    }
}
