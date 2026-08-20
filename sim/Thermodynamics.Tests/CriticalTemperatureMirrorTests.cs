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

        /// <summary>Armour at 900 K, a thruster at 1,050 K and a reactor at 1,200 K, in that order.</summary>
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

            // Above the armour, below the other two.
            Assert.Equal(new[] { "LightArmorBlock" }, Burned(simulation, 1000f).ToArray());

            // Above the thruster as well.
            Assert.Equal(new[] { "LargeThruster", "LightArmorBlock" }, Burned(simulation, 1100f).ToArray());

            // Above all three.
            Assert.Equal(new[] { "LargeThruster", "LightArmorBlock", "SmallReactor" }, Burned(simulation, 1300f).ToArray());
        }

        /// <summary>
        /// The failure a stale mirror produces. Removing a node moves the last node into its slot,
        /// so the reactor ends up holding the index the armour had. If the row does not follow it,
        /// the reactor is judged at 900 K and burns two hundred kelvin early.
        /// </summary>
        [Fact]
        public void ARatingFollowsItsBlockWhenARemovalMovesTheIndex()
        {
            GridBuilder builder = ThreeRatings();
            ThermalSimulation simulation = builder.BuildSimulation(Settings(), 293.15f);
            simulation.RebuildAll();

            // Settle the mirrored rows before the removal, so a stale row is a row that was right.
            Burned(simulation, 293.15f);

            BlockInstance armour = builder.Grid.GetAtCell(new Vector3I(0, 0, 0));
            Assert.NotNull(armour);
            simulation.RemoveBlock(armour);

            // Between the thruster's rating and the reactor's: only the thruster may burn.
            Assert.Equal(new[] { "LargeThruster" }, Burned(simulation, 1100f).ToArray());

            // And the reactor still burns when it should.
            Assert.Equal(new[] { "LargeThruster", "SmallReactor" }, Burned(simulation, 1300f).ToArray());
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

            // The reactor's 1,200 K rating, not the armour's 900 K.
            Assert.Equal(new[] { "LightArmorBlock" }, Burned(simulation, 1000f).ToArray());
            Assert.Equal(new[] { "LightArmorBlock", "SmallReactor" }, Burned(simulation, 1300f).ToArray());
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
