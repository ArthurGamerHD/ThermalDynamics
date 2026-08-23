using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// What a storm does to a ship that is standing still.
    ///
    /// <para>
    /// The defect the wind field was written to prevent: the engine's raw figure put every parked
    /// ship in a permanent 80 m/s wind, over the friction threshold and at double the still-air
    /// convection. The composed model reopened it — a storm reached 187 m/s, 143 of that within a
    /// hundred metres of the ground — for two reasons that were faults rather than balance, and one
    /// that is not. [backlog](../../docs/backlog.md) `B17`, `B18`.
    /// </para>
    ///
    /// <para>
    /// The faults are fixed: the weather's own wind modifier moved the intensity instead of
    /// multiplying the share, so the same storm is no longer counted twice, and the composed speed
    /// is bounded by the planet's own figure again. What is left is that a storm at that figure is
    /// still over `FrictionAtSpeedsAbove`, and **that is the model working rather than failing** —
    /// `v_rel` is relative wind by design, so a ship parked in a hurricane heats like a ship flying
    /// at hurricane speed. This measures how much, because "acceptable" is an assertion until it is
    /// a number.
    /// </para>
    /// </summary>
    public class StormHeatingTests
    {
        /// <summary>
        /// Where a hull settles after a stated time in a stated wind, and where the same hull
        /// settles in still air. The difference is what the wind did.
        /// </summary>
        private static float Settled(float windSpeed, float seconds)
        {
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

        /// <summary>
        /// A hurricane warms a parked hull by a few degrees, not by a few hundred.
        ///
        /// The friction term at the ceiling is real watts, and the same wind's convection carries
        /// nearly all of them away: a fast wind is a better cooler than it is a heater until it is
        /// far faster than any weather. That is why a storm can be allowed to cross the threshold.
        /// </summary>
        [Fact]
        public void AParkedHullInAHurricaneWarmsByDegreesRatherThanHundreds()
        {
            ThermalSettings settings = new ThermalSettings();

            float still = Settled(0f, 600f);
            float storm = Settled(settings.FrictionAtSpeedsAbove * 1.4f, 600f);

            Assert.True(storm > still,
                "a wind over the friction threshold has to heat something, or the term is dead");

            float rise = storm - still;
            Assert.True(rise < 25f,
                "a parked hull in a hurricane rose " + rise.ToString("n1")
                + " K, which is not a few degrees");
        }

        /// <summary>
        /// Below the threshold there is no friction term at all — not a small one, none. That is
        /// what makes the threshold a threshold, and it is the half of `B17` that is a contract
        /// rather than a judgement.
        /// </summary>
        [Fact]
        public void BelowTheThresholdThereIsNoFrictionAtAll()
        {
            ThermalSettings settings = new ThermalSettings();

            Assert.Equal(0f, FrictionWatts(settings.FrictionAtSpeedsAbove * 0.8f), 4);
            Assert.Equal(0f, FrictionWatts(settings.FrictionAtSpeedsAbove), 4);
            Assert.True(FrictionWatts(settings.FrictionAtSpeedsAbove * 1.4f) > 0f,
                "past the threshold the term has to do something, or it is dead");
        }

        /// <summary>
        /// Watts the friction term put into a hull over one step in a wind of a given speed.
        /// </summary>
        private static float FrictionWatts(float windSpeed)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableDamage = false;
            settings.EnableSolarHeat = false;
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
