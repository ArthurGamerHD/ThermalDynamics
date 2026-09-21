using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class WindShieldingTests
    {
        private const float ThickAir = 1f;
        private const float Speed = 120f;

/// <summary>Sets the tings.</summary>
        private static ThermalSettings Settings(bool shielding)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableDamage = false;
            settings.EnableWindwardShielding = shielding;
            settings.Derive();
            return settings;
        }

/// <summary>Sheltered operation.</summary>
        private static ThermalSimulation Sheltered(bool shielding)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), new Vector3I(0, 0, 4), new Vector3I(4, 4, 6));
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(4, 4, 2));

            ThermalSimulation simulation = builder.BuildSimulation(Settings(shielding), 293.15f);
            simulation.Planet = PlanetThermalProperties.Default();
            return simulation;
        }

/// <summary>DragWatts operation.</summary>
        private static float DragWatts(ThermalSimulation simulation)
        {
            simulation.StepExact(8, Worlds.Flight(ThickAir, Speed));
            return simulation.Solver.LastFrictionWatts;
        }

        [Fact]
/// <summary>AHullBehindAnotherTakesLessWindWhenShielded operation.</summary>
        public void AHullBehindAnotherTakesLessWindWhenShielded()
        {
/// <summary>DragWatts operation.</summary>
            float open = DragWatts(Sheltered(false));
/// <summary>DragWatts operation.</summary>
            float shielded = DragWatts(Sheltered(true));

            Assert.True(open > 0f, "the hull took no wind at all, so this compares nothing");
            Assert.True(shielded < open,
                "shielding changed nothing: " + shielded + " against " + open);
        }

        [Fact]
/// <summary>TheDefaultIsTheHarsherAnswer operation.</summary>
        public void TheDefaultIsTheHarsherAnswer()
        {
            Assert.False(new ThermalSettings().EnableWindwardShielding);
            Assert.True(DragWatts(Sheltered(false)) >= DragWatts(Sheltered(true)));
        }

        [Fact]
/// <summary>AHullInClearAirIsUnchangedByShielding operation.</summary>
        public void AHullInClearAirIsUnchangedByShielding()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(4, 4, 1));

            ThermalSimulation open = builder.BuildSimulation(Settings(false), 293.15f);
            open.Planet = PlanetThermalProperties.Default();

            GridBuilder second = GridBuilder.Large();
            second.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(4, 4, 1));
            ThermalSimulation shielded = second.BuildSimulation(Settings(true), 293.15f);
            shielded.Planet = PlanetThermalProperties.Default();

            Assert.Equal(DragWatts(open), DragWatts(shielded), 2);
        }

        [Fact]
/// <summary>TheShieldingArrayIsNotAllocatedWhenItIsOff operation.</summary>
        public void TheShieldingArrayIsNotAllocatedWhenItIsOff()
        {
/// <summary>Sheltered operation.</summary>
            ThermalSimulation off = Sheltered(false);
            off.StepExact(1, Worlds.Flight(ThickAir, Speed));

            Assert.Equal(0, off.Solver.WindLitLength);

/// <summary>Sheltered operation.</summary>
            ThermalSimulation on = Sheltered(true);
            on.StepExact(1, Worlds.Flight(ThickAir, Speed));

            Assert.True(on.Solver.WindLitLength > 0,
                "the shielding is on and its array is empty, so it is reading nothing");
        }
        [Fact]
/// <summary>ShieldingChangesTemperaturesAndNotOnlyDrag operation.</summary>
        public void ShieldingChangesTemperaturesAndNotOnlyDrag()
        {
/// <summary>Convecting operation.</summary>
            ThermalSimulation open = Convecting(false);
/// <summary>Convecting operation.</summary>
            ThermalSimulation shielded = Convecting(true);

            open.StepExact(120, Worlds.Flight(ThickAir, Speed));
            shielded.StepExact(120, Worlds.Flight(ThickAir, Speed));

/// <summary>Peak operation.</summary>
            float openPeak = Peak(open);
/// <summary>Peak operation.</summary>
            float shieldedPeak = Peak(shielded);

            Assert.True(openPeak > 0f && shieldedPeak > 0f);

            Assert.True(shieldedPeak > openPeak,
                "the shielded hull is not hotter, so the shielding is not reducing convection the "
                + "way it reduces friction — open " + openPeak + " K, shielded " + shieldedPeak + " K");
            Assert.True(Math.Abs(openPeak - shieldedPeak) > 0.001f,
                "shielding moved no temperature at all, so it is not reaching the convection term "
                + "that the same six-face sum feeds — open " + openPeak + " K, shielded "
                + shieldedPeak + " K");
        }

/// <summary>Convecting operation.</summary>
        private static ThermalSimulation Convecting(bool shielding)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableSolarHeat = false;
            settings.EnableDamage = false;
            settings.EnableWindwardShielding = shielding;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), new Vector3I(0, 0, 4), new Vector3I(4, 4, 6));
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(4, 4, 2));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 500f);
            simulation.Planet = PlanetThermalProperties.Default();
            return simulation;
        }

/// <summary>Peak operation.</summary>
        private static float Peak(ThermalSimulation simulation)
        {
            float peak = 0f;
            for (int i = 0; i < simulation.Solver.Nodes.Count; i++)
            {
                float t = simulation.Solver.Nodes[i].Temperature;
                if (t > peak) peak = t;
            }

            return peak;
        }

    }
}
