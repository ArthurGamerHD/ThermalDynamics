using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    public class CriticalTemperatureMirrorTests
    {
/// <summary>Sets the tings.</summary>
        private static ThermalSettings Settings()
        {
/// <summary>ThermalSettings operation.</summary>
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

/// <summary>Burned operation.</summary>
        private static List<string> Burned(ThermalSimulation simulation, float kelvin)
        {
            simulation.Solver.SetAllTemperatures(kelvin);
            simulation.Solver.Step(simulation.Settings.StepSeconds, EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, Worlds.Space(Vector3.UnitX)));

/// <summary>List operation.</summary>
            List<string> names = new List<string>();
            IList<OverheatEvent> events = simulation.Solver.Overheats;
            for (int i = 0; i < events.Count; i++) names.Add(events[i].Block.Model.Name);

            names.Sort();
            return names;
        }

/// <summary>Between operation.</summary>
        private static float Between(float low, float high)
        {
            return 0.5f * (low + high);
        }

/// <summary>Rating operation.</summary>
        private static float Rating(ThermalSimulation simulation, string name)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Block.Model.Name == name) return nodes[i].Thermal.CriticalTemperature;
            }

            throw new InvalidOperationException("no block named " + name + " on the rig");
        }

/// <summary>ThreeRatings operation.</summary>
        private static GridBuilder ThreeRatings()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), 0, 0, 0);
            builder.Place(Catalog.Thruster(), 4, 0, 0);
            builder.Place(Catalog.Reactor(), 8, 0, 0);
            return builder;
        }

        [Fact]
/// <summary>EachBlockIsJudgedAgainstItsOwnRating operation.</summary>
        public void EachBlockIsJudgedAgainstItsOwnRating()
        {
/// <summary>ThreeRatings operation.</summary>
            ThermalSimulation simulation = ThreeRatings().BuildSimulation(Settings(), 293.15f);
            simulation.RebuildAll();

/// <summary>Rating operation.</summary>
            float armour = Rating(simulation, "LightArmorBlock");
/// <summary>Rating operation.</summary>
            float reactor = Rating(simulation, "SmallReactor");
/// <summary>Rating operation.</summary>
            float thruster = Rating(simulation, "LargeThruster");

            Assert.True(armour < reactor && reactor < thruster,
                "the three ratings are " + armour + ", " + reactor + " and " + thruster
                + ", which does not separate them");

            Assert.Equal(new[] { "LightArmorBlock" },
                Burned(simulation, Between(armour, reactor)).ToArray());

            Assert.Equal(new[] { "LightArmorBlock", "SmallReactor" },
                Burned(simulation, Between(reactor, thruster)).ToArray());

            Assert.Equal(new[] { "LargeThruster", "LightArmorBlock", "SmallReactor" },
                Burned(simulation, thruster + 100f).ToArray());
        }

        [Fact]
/// <summary>ARatingFollowsItsBlockWhenARemovalMovesTheIndex operation.</summary>
        public void ARatingFollowsItsBlockWhenARemovalMovesTheIndex()
        {
/// <summary>ThreeRatings operation.</summary>
            GridBuilder builder = ThreeRatings();
            ThermalSimulation simulation = builder.BuildSimulation(Settings(), 293.15f);
            simulation.RebuildAll();

            Burned(simulation, 293.15f);

/// <summary>Rating operation.</summary>
            float reactor = Rating(simulation, "SmallReactor");
/// <summary>Rating operation.</summary>
            float thruster = Rating(simulation, "LargeThruster");

            BlockInstance armour = builder.Grid.GetAtCell(new Vector3I(0, 0, 0));
            Assert.NotNull(armour);
            simulation.RemoveBlock(armour);

            Assert.Equal(new[] { "SmallReactor" },
                Burned(simulation, Between(reactor, thruster)).ToArray());

            Assert.Equal(new[] { "LargeThruster", "SmallReactor" },
                Burned(simulation, thruster + 100f).ToArray());
        }

        [Fact]
/// <summary>ARatingArrivesWithABlockPlacedIntoALiveGrid operation.</summary>
        public void ARatingArrivesWithABlockPlacedIntoALiveGrid()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), 0, 0, 0);

            ThermalSimulation simulation = builder.BuildSimulation(Settings(), 293.15f);
            simulation.RebuildAll();
            Burned(simulation, 293.15f);

            simulation.AddBlock(new BlockInstance(
                Catalog.Reactor(), new Vector3I(8, 0, 0), BlockOrientation.Identity), 293.15f);

/// <summary>Rating operation.</summary>
            float armour = Rating(simulation, "LightArmorBlock");
/// <summary>Rating operation.</summary>
            float reactor = Rating(simulation, "SmallReactor");
            Assert.True(armour < reactor, "the reactor must outrank the armour for this to judge anything");

            Assert.Equal(new[] { "LightArmorBlock" },
                Burned(simulation, Between(armour, reactor)).ToArray());
            Assert.Equal(new[] { "LightArmorBlock", "SmallReactor" },
                Burned(simulation, reactor + 100f).ToArray());
        }

        [Fact]
/// <summary>ABlockWithNoRatingNeverBurns operation.</summary>
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
