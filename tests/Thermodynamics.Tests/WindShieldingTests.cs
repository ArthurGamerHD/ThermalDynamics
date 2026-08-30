using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **A block behind another is sheltered from the wind, for heat and for drag.**
    ///
    /// <para>
    /// The same self-shadowing pass the sun uses, aimed at the relative wind — `SunShadowMap` takes
    /// its direction as an argument, so it was always a direction pass. What differs is the
    /// cadence, and that is measured rather than inherited: see `WindShieldingCostTests` for why the
    /// threshold is 20° rather than the sun's 2°, and why a running pass is never restarted.
    /// </para>
    ///
    /// <para>
    /// **Off by default, and the default is the conservative answer.** Unshielded, every face is
    /// treated as being in the open, so a hull is heated and dragged at least as much as it should
    /// be and never less. A world that switches this on is asking for the more forgiving model, not
    /// the harsher one.
    /// </para>
    /// </summary>
    public class WindShieldingTests
    {
        private const float ThickAir = 1f;
        private const float Speed = 120f;

        private static ThermalSettings Settings(bool shielding)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableDamage = false;
            settings.EnableWindwardShielding = shielding;
            settings.Derive();
            return settings;
        }

        /// <summary>
        /// A wall of hull with a second wall directly behind it, down the wind. `Worlds.Flight`
        /// moves the ship backward, so the air comes from `+Z` and the second wall is in the lee of
        /// the first.
        /// </summary>
        private static ThermalSimulation Sheltered(bool shielding)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), new Vector3I(0, 0, 4), new Vector3I(4, 4, 6));
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(4, 4, 2));

            ThermalSimulation simulation = builder.BuildSimulation(Settings(shielding), 293.15f);
            simulation.Planet = PlanetThermalProperties.Default();
            return simulation;
        }

        private static float DragWatts(ThermalSimulation simulation)
        {
            // Several steps, because the shielding pass is sliced and only a completed one changes
            // any reading — a single step would measure the frame before it finished.
            simulation.StepExact(8, Worlds.Flight(ThickAir, Speed));
            return simulation.Solver.LastFrictionWatts;
        }

        /// <summary>
        /// **The finding this exists for**: the same hull takes less wind when what is behind
        /// counts as behind.
        /// </summary>
        [Fact]
        public void AHullBehindAnotherTakesLessWindWhenShielded()
        {
            float open = DragWatts(Sheltered(false));
            float shielded = DragWatts(Sheltered(true));

            Assert.True(open > 0f, "the hull took no wind at all, so this compares nothing");
            Assert.True(shielded < open,
                "shielding changed nothing: " + shielded + " against " + open);
        }

        /// <summary>
        /// **Unshielded is the conservative answer, and that is why it is the default.** A world
        /// that has not switched this on is heated and dragged at least as much as it should be.
        /// </summary>
        [Fact]
        public void TheDefaultIsTheHarsherAnswer()
        {
            Assert.False(new ThermalSettings().EnableWindwardShielding);
            Assert.True(DragWatts(Sheltered(false)) >= DragWatts(Sheltered(true)));
        }

        /// <summary>
        /// **A hull with nothing behind anything is unchanged**, which is the check that the
        /// shielding shelters what is sheltered rather than everything.
        /// </summary>
        [Fact]
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

        /// <summary>
        /// **A world with the shielding off allocates nothing for it.** Six floats a node is 3 MB on
        /// a 126,731-block hull, and a world not using it should not carry it.
        /// </summary>
        [Fact]
        public void TheShieldingArrayIsNotAllocatedWhenItIsOff()
        {
            ThermalSimulation off = Sheltered(false);
            off.StepExact(1, Worlds.Flight(ThickAir, Speed));

            Assert.Equal(0, off.Solver.WindLitLength);

            ThermalSimulation on = Sheltered(true);
            on.StepExact(1, Worlds.Flight(ThickAir, Speed));

            Assert.True(on.Solver.WindLitLength > 0,
                "the shielding is on and its array is empty, so it is reading nothing");
        }
        /// <summary>
        /// **The shielding reaches convection as well as friction, and this measures how far.**
        ///
        /// <para>
        /// The six-face wind sum is read by *both* the friction row and the convection factor —
        /// `windFactor = 1 + wind` — so sheltering a face reduces the forced convection over it as
        /// well as the aerodynamic heating of it. That is physically right: a face in another
        /// block's lee has less air moving across it, and forced convection is what air moving
        /// across a surface does.
        /// </para>
        ///
        /// <para>
        /// **But it means this is a change to the heat model rather than an addition to the force
        /// one**, which is backlog.md `K9` and the reason the switch ships off. Nothing has changed
        /// for any existing world; what a world turns on when it sets the switch is measured here
        /// on a hull and wants the corpus before it becomes a default.
        /// </para>
        /// </summary>
        [Fact]
        public void ShieldingChangesTemperaturesAndNotOnlyDrag()
        {
            // **The environment on**, unlike the drag rigs above: those switch it off to isolate
            // friction, and convection is the term being measured here.
            ThermalSimulation open = Convecting(false);
            ThermalSimulation shielded = Convecting(true);

            open.StepExact(120, Worlds.Flight(ThickAir, Speed));
            shielded.StepExact(120, Worlds.Flight(ThickAir, Speed));

            float openPeak = Peak(open);
            float shieldedPeak = Peak(shielded);

            Assert.True(openPeak > 0f && shieldedPeak > 0f);

            // **The shielded hull runs hotter, and that is the right direction.** A sheltered face
            // has less air moving over it, so it loses less to forced convection — and this hull
            // starts above the air, so losing less means staying warmer. Measured at **7.4 K** on
            // this rig, 316.1 K open against 323.5 K shielded, which is the size of the change
            // `K9` says has to reach the corpus before it could ever be a default.
            Assert.True(shieldedPeak > openPeak,
                "the shielded hull is not hotter, so the shielding is not reducing convection the "
                + "way it reduces friction — open " + openPeak + " K, shielded " + shieldedPeak + " K");
            Assert.True(Math.Abs(openPeak - shieldedPeak) > 0.001f,
                "shielding moved no temperature at all, so it is not reaching the convection term "
                + "that the same six-face sum feeds — open " + openPeak + " K, shielded "
                + shieldedPeak + " K");
        }

        /// <summary>The sheltered pair with the environment left on, so convection is live.</summary>
        private static ThermalSimulation Convecting(bool shielding)
        {
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
