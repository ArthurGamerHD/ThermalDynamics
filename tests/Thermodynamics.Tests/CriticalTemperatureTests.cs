using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Overheat damage: the one output of this simulation that destroys a player's ship, and the
    /// one that two field sessions failed to exercise at all.
    ///
    /// <para>
    /// Both dumps reported <c>critical damage events 0</c> and <c>total heat damage 0.0</c>. Not
    /// because nothing got hot — a thruster reached 939 K — but because the equilibrium it
    /// reached sat under its 1,050 K rating. So the whole damage path shipped untested against
    /// anything but unit tests on a single block, and the balance question ("what should a block
    /// survive") and the correctness question ("does the threshold fire when it should") were
    /// indistinguishable from outside.
    /// </para>
    ///
    /// <para>
    /// These separate them. Every test here drives a hull to a temperature the block census says
    /// is fatal, and asserts what the simulation does about it. What temperature a real ship
    /// reaches is a balance question and belongs in a dump; whether the model acts on it when it
    /// does is a correctness question and belongs here.
    /// </para>
    /// </summary>
    [Trait("speed", "slow")]
    public class CriticalTemperatureTests
    {
        private static ThermalSettings Settings(int cap = 0)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubstepsPerBlock = cap;
            settings.MaxSubsteps = 4096;
            settings.MaxElementVisitsPerStep = 0;
            settings.EnableDamage = true;
            return settings.Derive();
        }

        /// <summary>
        /// A census hull with its heat producers running hard enough to cook itself.
        ///
        /// <paramref name="watts"/> is per producer. The census figure of 111 kW settles a ship
        /// below its rating, which is what the field measured; multiplying it is how a test gets
        /// to the other side of the threshold without changing anything else about the ship.
        /// </summary>
        private static ThermalSimulation Driven(ThermalSettings settings, int blocks, float watts)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", blocks));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.RebuildAll();

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!Census.IsProducer(nodes[i])) continue;

                float waste = nodes[i].Thermal.ProducerWasteEnergy;
                if (waste <= 0f) continue;

                nodes[i].Block.PowerProducedWatts = watts / waste;
                nodes[i].RefreshHeatGeneration();
            }

            return simulation;
        }

        /// <summary>Steps a simulation and collects every overheat event it raises.</summary>
        private static List<OverheatEvent> RunCollecting(ThermalSimulation simulation, int steps)
        {
            List<OverheatEvent> events = new List<OverheatEvent>();
            EnvironmentSample sample = Worlds.Shadow();

            for (int i = 0; i < steps; i++)
            {
                simulation.StepExact(1, sample);

                IList<OverheatEvent> raised = simulation.Solver.Overheats;
                for (int e = 0; e < raised.Count; e++) events.Add(raised[e]);
            }

            return events;
        }

        private static float Hottest(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            float peak = 0f;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Temperature > peak) peak = nodes[i].Temperature;
            }
            return peak;
        }

        /// <summary>
        /// The census ship as the field measured it settles below its rating and takes no damage.
        ///
        /// This is the calibration test: it pins that the harness reproduces what the field dumps
        /// actually showed, so that when a later change makes ships start burning, the change is
        /// visible here rather than only in someone's world.
        /// </summary>
        [Fact]
        public void ACensusShipAtMeasuredPowerSettlesBelowItsRating()
        {
            ThermalSimulation simulation = Driven(Settings(), 2000, Census.ProducerWatts);
            List<OverheatEvent> events = RunCollecting(simulation, 400);

            float peak = Hottest(simulation);

            Assert.True(peak > 400f, "the hull never warmed at all, peak " + peak + " K");
            Assert.True(peak < Census.ProducerCriticalTemperature,
                "the census ship reached " + peak + " K against a producer rating of "
                + Census.ProducerCriticalTemperature + " K; the field dumps it is built from"
                + " settled below theirs, so either the census or this expectation has moved");

            Assert.Empty(events);
        }

        [Fact]
        public void APastCriticalBlockRaisesDamageProportionalToHowFarPast()
        {
            ThermalSimulation simulation = Driven(Settings(), 2000, Census.ProducerWatts * 12f);
            List<OverheatEvent> events = RunCollecting(simulation, 400);

            Assert.NotEmpty(events);

            for (int i = 0; i < events.Count; i++)
            {
                OverheatEvent raised = events[i];

                float critical = raised.Block.Thermal.CriticalTemperature;
                Assert.True(raised.Temperature > critical,
                    "damage was raised at " + raised.Temperature + " K against a critical of " + critical);
                Assert.True(raised.Damage > 0f);
            }
        }

        /// <summary>
        /// Damage per second must not depend on how the second was divided into steps. With
        /// <c>DamageIsPerSecond</c> off it deliberately does — that is the original behaviour and
        /// the setting exists to choose between them — so this pins the half that is meant to be
        /// invariant.
        /// </summary>
        [Fact]
        public void DamagePerSecondDoesNotDependOnTheStepLength()
        {
            float[] totals = new float[2];
            int[] frequencies = { 2, 8 };

            for (int f = 0; f < frequencies.Length; f++)
            {
                ThermalSettings settings = Settings();
                settings.Frequency = frequencies[f];
                settings.DamageIsPerSecond = true;
                settings.Derive();

                ThermalSimulation simulation = Driven(settings, 2000, Census.ProducerWatts * 12f);

                // The same simulated time either way: four seconds of it.
                List<OverheatEvent> events = RunCollecting(simulation, frequencies[f] * 4);

                float total = 0f;
                for (int i = 0; i < events.Count; i++) total += events[i].Damage;
                totals[f] = total;
            }

            Assert.True(totals[0] > 0f && totals[1] > 0f,
                "no damage was dealt at either frequency, so nothing was compared");

            float ratio = totals[0] / totals[1];
            Assert.True(ratio > 0.5f && ratio < 2f,
                "four seconds of damage came to " + totals[0] + " at Frequency 2 and "
                + totals[1] + " at Frequency 8; per-second damage must not scale with the step");
        }

        /// <summary>
        /// The question <c>MaxSubstepsPerBlock</c> left open, and the reason this file exists.
        ///
        /// The cap raises the heat capacity of the lightest blocks on a grid, so those blocks
        /// climb towards their critical temperature more slowly than they should. Overheat damage
        /// is a per-node threshold crossing. The argument that this is harmless is that a block
        /// too light to hold heat for the length of a step is a reading off its neighbour rather
        /// than an independent temperature — but an argument is not a measurement, and the thing
        /// at stake is whether a player's ship survives.
        ///
        /// What must hold is that the cap does not change <em>whether</em> a hull burns, because
        /// where something settles is decided by the watts cancelling and heat capacity is not in
        /// that equation. When it burns may move; that it burns may not.
        /// </summary>
        [Fact]
        public void TheSubstepCapDoesNotChangeWhetherAHullBurns()
        {
            const int Steps = 600;

            ThermalSimulation uncapped = Driven(Settings(0), 2000, Census.ProducerWatts * 12f);
            List<OverheatEvent> uncappedEvents = RunCollecting(uncapped, Steps);

            Assert.NotEmpty(uncappedEvents);

            foreach (int cap in new int[] { 8, 4, 1 })
            {
                ThermalSimulation capped = Driven(Settings(cap), 2000, Census.ProducerWatts * 12f);
                List<OverheatEvent> cappedEvents = RunCollecting(capped, Steps);

                Assert.True(cappedEvents.Count > 0,
                    "cap " + cap + " stopped the hull burning at all, which the equilibrium"
                    + " argument says it cannot do");

                // The temperature it settles at is the claim being tested; damage totals follow
                // from it but also from the path taken, which the cap is allowed to change.
                float difference = Math.Abs(Hottest(capped) - Hottest(uncapped));
                Assert.True(difference < 5f,
                    "cap " + cap + " left the hottest block at " + Hottest(capped)
                    + " K against " + Hottest(uncapped) + " K uncapped");
            }
        }

        /// <summary>
        /// A block below its rating must never be damaged, whatever the cap does to its capacity.
        /// The failure this guards against is the opposite of the one above: not a ship that
        /// survives when it should burn, but structure that burns when it should not.
        /// </summary>
        [Fact]
        public void NothingBelowItsRatingIsEverDamaged()
        {
            foreach (int cap in new int[] { 0, 4, 1 })
            {
                ThermalSimulation simulation = Driven(Settings(cap), 2000, Census.ProducerWatts * 12f);
                List<OverheatEvent> events = RunCollecting(simulation, 400);

                for (int i = 0; i < events.Count; i++)
                {
                    OverheatEvent raised = events[i];
                    Assert.True(raised.Temperature > raised.Block.Thermal.CriticalTemperature,
                        "cap " + cap + " damaged a block at " + raised.Temperature
                        + " K, rated for " + raised.Block.Thermal.CriticalTemperature + " K");
                }
            }
        }

        /// <summary>
        /// Damage stops when the setting says so. Cheap, and the kind of switch that rots.
        /// </summary>
        [Fact]
        public void DamageDisabledRaisesNothing()
        {
            ThermalSettings settings = Settings();
            settings.EnableDamage = false;
            settings.Derive();

            ThermalSimulation simulation = Driven(settings, 2000, Census.ProducerWatts * 12f);
            List<OverheatEvent> events = RunCollecting(simulation, 400);

            Assert.Empty(events);
            Assert.True(Hottest(simulation) > Census.ProducerCriticalTemperature,
                "the hull did not reach a damaging temperature, so nothing was suppressed");
        }
    }
}
