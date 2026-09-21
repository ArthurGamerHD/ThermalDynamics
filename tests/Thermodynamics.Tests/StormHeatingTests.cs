using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class StormHeatingTests
    {
/// <summary>Sets the tled.</summary>
        private static float Settled(float windSpeed, float seconds)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableDamage = false;
            settings.EnableSolarHeat = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(4, 3, 8));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.RebuildAll();

            EnvironmentSample world = Worlds.Storm(1f, windSpeed);
            int steps = (int)Math.Round(seconds / settings.StepSeconds);
            for (int i = 0; i < steps; i++) simulation.StepExact(1, world);

            float hottest = 0f;
            for (int i = 0; i < simulation.Solver.Nodes.Count; i++)
            {
                float temperature = simulation.Solver.Nodes[i].Temperature;
                if (temperature > hottest) hottest = temperature;
            }

            return hottest;
        }

        [Fact]
/// <summary>AParkedHullInAHurricaneWarmsByDegreesRatherThanHundreds operation.</summary>
        public void AParkedHullInAHurricaneWarmsByDegreesRatherThanHundreds()
        {
/// <summary>Sets the tled.</summary>
            float still = Settled(0f, 600f);
/// <summary>Sets the tled.</summary>
            float storm = Settled(70f, 600f);

            Assert.True(storm > still,
                "a hurricane-speed wind has to heat something, or the term is dead");

            float rise = storm - still;
            Assert.True(rise < 25f,
                "a parked hull in a hurricane rose " + rise.ToString("n1")
                + " K, which is not a few degrees");
        }

        [Fact]
/// <summary>AWorldThatSetsTheFloorGetsNoFrictionBelowIt operation.</summary>
        public void AWorldThatSetsTheFloorGetsNoFrictionBelowIt()
        {
            Assert.Equal(0f, FrictionWatts(40f, 50f), 4);
            Assert.Equal(0f, FrictionWatts(50f, 50f), 4);
            Assert.True(FrictionWatts(70f, 50f) > 0f,
                "past the floor the term has to do something, or it is dead");
        }

        [Fact]
/// <summary>AtTheShippedDefaultFrictionIsLiveAtEverySpeedAndVanishesByTheCube operation.</summary>
        public void AtTheShippedDefaultFrictionIsLiveAtEverySpeedAndVanishesByTheCube()
        {
/// <summary>FrictionWatts operation.</summary>
            float slow = FrictionWatts(10f, 0f);
/// <summary>FrictionWatts operation.</summary>
            float fast = FrictionWatts(100f, 0f);

            Assert.True(slow > 0f, "friction at 10 m/s is zero, so the floor is still gating");
            Assert.True(fast > slow, "friction has to grow with speed");

            float ratio = fast / slow;
            Assert.True(ratio > 900f && ratio < 1100f,
                "10 to 100 m/s moved friction by " + ratio.ToString("n0")
                + "x where the v^3 law says 1,000x");
        }

        [Fact]
/// <summary>AirspeedCoolsAHotHullSlowAndHeatsItFast operation.</summary>
        public void AirspeedCoolsAHotHullSlowAndHeatsItFast()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings world = new ThermalSettings();
            world.EnableDamage = false;
            world.EnableSolarHeat = false;
            world.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(4, 3, 8));
            builder.Remove(new Vector3I(1, 1, 1));
            builder.Place(Catalog.Reactor(), new Vector3I(1, 1, 1)).Producing(2e6f);

/// <summary>Mean operation.</summary>
            float still = Mean(builder, world, Worlds.PlanetSurface(1f, 0.5f), 600f);
/// <summary>Mean operation.</summary>
            float breezy = Mean(builder, world, Worlds.Storm(1f, 45f), 600f);
/// <summary>Mean operation.</summary>
            float screaming = Mean(builder, world, Worlds.Storm(1f, 300f), 600f);

            Assert.True(breezy < still,
                "45 m/s left the hull at " + breezy.ToString("n1") + " K against "
                + still.ToString("n1") + " K still — the wind should net-cool a hot hull");
            Assert.True(screaming > still,
                "300 m/s left the hull at " + screaming.ToString("n1") + " K against "
                + still.ToString("n1") + " K still — the cube should have won by here");
        }

        [Fact]
/// <summary>AWindyDayCoolsAHullThatIsMakingHeat operation.</summary>
        public void AWindyDayCoolsAHullThatIsMakingHeat()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings world = new ThermalSettings();
            world.EnableDamage = false;
            world.EnableSolarHeat = false;
            world.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(4, 3, 8));
            builder.Remove(new Vector3I(1, 1, 1));
            builder.Place(Catalog.Reactor(), new Vector3I(1, 1, 1)).Producing(2e6f);

/// <summary>Mean operation.</summary>
            float still = Mean(builder, world, Worlds.PlanetSurface(1f, 0.5f), 600f);

            for (float speed = 5f; speed < 50f; speed += 5f)
            {
/// <summary>Mean operation.</summary>
                float windy = Mean(builder, world, Worlds.Storm(1f, speed), 600f);

                Assert.True(windy < still,
                    "a " + speed.ToString("n0") + " m/s wind left the hull at "
                    + windy.ToString("n1") + " K against " + still.ToString("n1")
                    + " K in still air");
            }
        }

/// <summary>FrictionWatts operation.</summary>
        private static float FrictionWatts(float windSpeed, float floor)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableDamage = false;
            settings.EnableSolarHeat = false;
            settings.FrictionAtSpeedsAbove = floor;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(4, 3, 8));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.Solver.CollectDiagnostics = true;
            simulation.RebuildAll();
            simulation.StepExact(1, Worlds.Storm(1f, windSpeed));

            float total = 0f;
            for (int i = 0; i < simulation.Solver.Nodes.Count; i++)
            {
                total += simulation.Solver.Nodes[i].LastFrictionWatts;
            }

            return total;
        }

/// <summary>Mean operation.</summary>
        private static float Mean(GridBuilder builder, ThermalSettings settings,
            EnvironmentSample world, float seconds)
        {
            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.RebuildAll();

            int steps = (int)Math.Round(seconds / settings.StepSeconds);
            for (int i = 0; i < steps; i++) simulation.StepExact(1, world);

            double total = 0d;
            int count = simulation.Solver.Nodes.Count;
            for (int i = 0; i < count; i++) total += simulation.Solver.Nodes[i].Temperature;

            return count == 0 ? 0f : (float)(total / count);
        }
    }
}
