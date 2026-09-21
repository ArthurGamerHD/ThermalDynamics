using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class GridFrictionTotalTests
    {
        private const float ThickAir = 1f;

/// <summary>Rig operation.</summary>
        private static ThermalSimulation Rig(bool diagnostics)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableDamage = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.Solver.CollectDiagnostics = diagnostics;
            simulation.Planet = PlanetThermalProperties.Default();
            return simulation;
        }

        [Fact]
/// <summary>TheGridTotalIsTheSumOfItsNodes operation.</summary>
        public void TheGridTotalIsTheSumOfItsNodes()
        {
/// <summary>Rig operation.</summary>
            ThermalSimulation simulation = Rig(true);
            simulation.StepExact(1, Worlds.Flight(ThickAir, 120f));

            float summed = 0f;
            for (int i = 0; i < simulation.Solver.Nodes.Count; i++)
            {
                summed += simulation.Solver.Nodes[i].LastFrictionWatts;
            }

            Assert.True(summed > 0f, "nothing was heated by friction, so this proves nothing");
            Assert.Equal(summed, simulation.Solver.LastFrictionWatts, 3);
        }

        [Fact]
/// <summary>TheTotalDoesNotDependOnDiagnosticsBeingOn operation.</summary>
        public void TheTotalDoesNotDependOnDiagnosticsBeingOn()
        {
/// <summary>Rig operation.</summary>
            ThermalSimulation watched = Rig(true);
/// <summary>Rig operation.</summary>
            ThermalSimulation unwatched = Rig(false);

            watched.StepExact(1, Worlds.Flight(ThickAir, 120f));
            unwatched.StepExact(1, Worlds.Flight(ThickAir, 120f));

            Assert.True(watched.Solver.LastFrictionWatts > 0f);
            Assert.Equal(watched.Solver.LastFrictionWatts, unwatched.Solver.LastFrictionWatts, 3);

            Assert.Equal(0f, unwatched.Solver.Nodes[0].LastFrictionWatts);
        }

        [Fact]
/// <summary>TheTotalIsARateRatherThanAnAccumulation operation.</summary>
        public void TheTotalIsARateRatherThanAnAccumulation()
        {
/// <summary>Rig operation.</summary>
            ThermalSimulation simulation = Rig(false);

            simulation.StepExact(1, Worlds.Flight(ThickAir, 120f));
            float first = simulation.Solver.LastFrictionWatts;

            simulation.StepExact(1, Worlds.Flight(ThickAir, 120f));
            float second = simulation.Solver.LastFrictionWatts;

            Assert.True(first > 0f);
            Assert.Equal(first, second, 3);
        }

        [Fact]
/// <summary>TheTotalSurvivesEverySubstepOfAStep operation.</summary>
        public void TheTotalSurvivesEverySubstepOfAStep()
        {
/// <summary>Rig operation.</summary>
            ThermalSimulation simulation = Rig(false);
            simulation.StepExact(1, Worlds.Flight(ThickAir, 120f));
            float once = simulation.Solver.LastFrictionWatts;

/// <summary>Rig operation.</summary>
            ThermalSimulation many = Rig(false);
            many.StepExact(8, Worlds.Flight(ThickAir, 120f));

            Assert.True(once > 0f);
            Assert.Equal(once, many.Solver.LastFrictionWatts, 3);
        }

        [Fact]
/// <summary>AGridThatIsNotMovingThroughAirHasNoDragPower operation.</summary>
        public void AGridThatIsNotMovingThroughAirHasNoDragPower()
        {
/// <summary>Rig operation.</summary>
            ThermalSimulation simulation = Rig(false);
            simulation.StepExact(1, Worlds.Flight(ThickAir, 0f));

            Assert.Equal(0f, simulation.Solver.LastFrictionWatts);
        }

        [Fact]
/// <summary>DragPowerRisesSteeplyWithSpeed operation.</summary>
        public void DragPowerRisesSteeplyWithSpeed()
        {
/// <summary>Rig operation.</summary>
            ThermalSimulation slow = Rig(false);
/// <summary>Rig operation.</summary>
            ThermalSimulation fast = Rig(false);

            slow.StepExact(1, Worlds.Flight(ThickAir, 60f));
            fast.StepExact(1, Worlds.Flight(ThickAir, 120f));

            float ratio = fast.Solver.LastFrictionWatts / Math.Max(1e-6f, slow.Solver.LastFrictionWatts);
            Assert.True(ratio > 4f,
                "doubling the speed multiplied the drag power by " + ratio + ", which is not a cube law");
        }

        [Fact]
/// <summary>TheFrictionTotalIsInsideTheHeatGain operation.</summary>
        public void TheFrictionTotalIsInsideTheHeatGain()
        {
/// <summary>Rig operation.</summary>
            ThermalSimulation simulation = Rig(false);
            simulation.StepExact(1, Worlds.Flight(ThickAir, 120f));

            Assert.True(simulation.Solver.LastFrictionWatts > 0f);
            Assert.True(simulation.HeatGainWatts >= simulation.Solver.LastFrictionWatts,
                "the heat gain is smaller than the friction inside it, so they are not the same sum");
            Assert.Equal(simulation.Solver.LastFrictionWatts, simulation.FrictionWatts, 3);
        }
    }
}
