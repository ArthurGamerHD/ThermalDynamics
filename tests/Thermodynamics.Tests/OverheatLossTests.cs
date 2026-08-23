using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The second event: how long a block lasts after it crosses its own critical temperature.
    ///
    /// <para>
    /// **The crossing and the loss are different events, and only the first had ever been checked.**
    /// <see cref="BlockHeatIndex.SecondsFromCriticalToLoss"/> integrates the solver's own damage
    /// rule — <c>(T - critical) x OverheatDamagePerKelvin</c> per simulated second — from the
    /// crossing until a block's hit points are gone, and it is the span a warning has to be useful
    /// in. It had no test of any kind while the balance argument was preparing to rest on it.
    /// See balance.md, How long a block has after it crosses.
    /// </para>
    /// </summary>
    public class OverheatLossTests
    {
        private const float Ambient = BlockHeatIndex.AmbientKelvin;

        /// <summary>
        /// A block behind a skin that radiates nothing climbs linearly, so its damage integrates in
        /// closed form: <c>sqrt(2 C I / (d W))</c>. The calibration every other figure in the column
        /// is read against, and arithmetic rather than a simulation, which is what lets it be
        /// checked without a game install.
        /// </summary>
        [Fact]
        public void WithNothingRadiatingTheSurvivalIsTheAdiabaticClosedForm()
        {
            const float Capacity = 5000f;         // J/K
            const float Watts = 250f;
            const float Damage = 0.5f;            // hit points per kelvin per second
            const float Integrity = 400f;
            float critical = Ambient + 100f;

            float expected = (float)Math.Sqrt(2d * Capacity * Integrity / (Damage * Watts));
            float measured = BlockHeatIndex.SecondsFromCriticalToLoss(
                Capacity, Watts, 0f, critical, Damage, Integrity);

            Assert.Equal(expected, measured, 1);

            // Four times the hit points is twice the seconds, which is the shape that decides what
            // a balance dial buys: survival goes as the square root of integrity, not linearly.
            Assert.Equal(expected * 2f, BlockHeatIndex.SecondsFromCriticalToLoss(
                Capacity, Watts, 0f, critical, Damage, Integrity * 4f), 1);
        }

        /// <summary>
        /// The quadrature agrees with the thing it stands in for: a block stepped forward in time
        /// under the same two rules reaches zero hit points at the same moment.
        ///
        /// This is the test the column actually needs. The closed form above exercises the one path
        /// with no radiation in it; every shipped block takes the other, where the temperature
        /// approaches an equilibrium and the integrator walks a geometric grid to get there.
        /// </summary>
        [Theory]
        [InlineData(120f, 5000f, 0.25f)]
        [InlineData(400f, 500f, 1f)]
        [InlineData(900f, 40000f, 2f)]
        public void TheQuadratureAgreesWithSteppingTheSameBlockForward(
            float overshoot, float integrity, float damagePerKelvin)
        {
            const float Capacity = 8000f;
            const float Watts = 900f;
            float critical = Ambient + 200f;

            // Put the equilibrium a stated distance above critical, so every case genuinely crosses.
            float equilibrium = critical + overshoot;
            double ambient4 = Math.Pow(Ambient, 4);
            float coefficient = (float)(Watts / (Math.Pow(equilibrium, 4) - ambient4));

            float measured = BlockHeatIndex.SecondsFromCriticalToLoss(
                Capacity, Watts, coefficient, critical, damagePerKelvin, integrity);
            float stepped = StepToLoss(Capacity, Watts, coefficient, critical,
                damagePerKelvin, integrity);

            Assert.True(!float.IsInfinity(measured), "the case must cross and be destroyed");
            Assert.True(Math.Abs(measured - stepped) <= 0.005f * stepped,
                "quadrature " + measured.ToString("n2") + " s against stepped "
                + stepped.ToString("n2") + " s");
        }

        /// <summary>
        /// A block whose own skin holds it under its limit never crosses, so it never takes damage
        /// and is never destroyed — reported as infinite rather than as a large number, exactly as
        /// <see cref="BlockHeatIndex.SecondsToReach"/> reports the crossing it never makes.
        /// </summary>
        [Fact]
        public void ABlockThatNeverCrossesIsNeverDestroyed()
        {
            const float Capacity = 5000f;
            const float Watts = 250f;
            float critical = Ambient + 400f;

            float equilibrium = Ambient + 200f;
            double ambient4 = Math.Pow(Ambient, 4);
            float coefficient = (float)(Watts / (Math.Pow(equilibrium, 4) - ambient4));

            Assert.True(float.IsPositiveInfinity(BlockHeatIndex.SecondsFromCriticalToLoss(
                Capacity, Watts, coefficient, critical, 1f, 500f)),
                "an equilibrium below critical is never a loss");

            // And the two columns say the same thing about the same block, which is the only
            // consistency a reader of the table can check.
            Assert.True(float.IsPositiveInfinity(
                BlockHeatIndex.SecondsToReach(Capacity, Watts, coefficient, critical)));
        }

        /// <summary>
        /// Past an hour of play the figure has stopped answering a question about a session, so it
        /// reads as never rather than as a number nobody will ever see reached.
        /// </summary>
        [Fact]
        public void DestructionBeyondTheHorizonReadsAsNever()
        {
            const float Capacity = 5000f;
            const float Watts = 250f;
            float critical = Ambient + 100f;

            // Barely over its limit, with hit points far past what that trickle can spend.
            float equilibrium = critical + 1f;
            double ambient4 = Math.Pow(Ambient, 4);
            float coefficient = (float)(Watts / (Math.Pow(equilibrium, 4) - ambient4));

            Assert.True(float.IsPositiveInfinity(BlockHeatIndex.SecondsFromCriticalToLoss(
                Capacity, Watts, coefficient, critical, 1f, 1e6f)),
                "a block an hour of overheating cannot finish is not a loss");
        }

        /// <summary>
        /// A block the install could not price reports zero rather than infinity, because the caller
        /// has to be able to tell "this block is not destroyed" from "this figure is unavailable".
        /// </summary>
        [Fact]
        public void AnUnpricedBlockIsUnavailableRatherThanIndestructible()
        {
            Assert.Equal(0f, BlockHeatIndex.SecondsFromCriticalToLoss(
                5000f, 250f, 0f, Ambient + 100f, 1f, 0f));
        }

        /// <summary>
        /// The two dials a definition has over the span move it the way a balance argument assumes:
        /// hit points lengthen it, waste heat shortens it, and damage per kelvin shortens it.
        /// </summary>
        [Fact]
        public void MoreHitPointsIsLongerAndMoreWasteHeatIsShorter()
        {
            const float Capacity = 8000f;
            float critical = Ambient + 200f;
            float equilibrium = critical + 300f;
            double ambient4 = Math.Pow(Ambient, 4);

            Func<float, float, float, float> span = delegate (float watts, float integrity, float perKelvin)
            {
                float coefficient = (float)(watts / (Math.Pow(equilibrium, 4) - ambient4));
                return BlockHeatIndex.SecondsFromCriticalToLoss(
                    Capacity, watts, coefficient, critical, perKelvin, integrity);
            };

            float baseline = span(900f, 5000f, 1f);
            Assert.True(span(900f, 10000f, 1f) > baseline, "hit points lengthen the span");
            Assert.True(span(900f, 5000f, 2f) < baseline, "damage per kelvin shortens it");

            // Waste heat is held at the same equilibrium here, so what more of it changes is the
            // pace: the same climb, reached sooner, and the damage that follows integrated faster.
            Assert.True(span(1800f, 5000f, 1f) < baseline, "waste heat shortens it");
        }

        /// <summary>
        /// Every shipped block that crosses is also destroyed, and never before it crosses.
        ///
        /// The ordering is the claim: a table that reported a loss without a crossing, or a loss at
        /// the moment of one, would be describing a different damage rule from the one the solver
        /// runs — the rate at the crossing is zero by construction.
        /// </summary>
        [Fact]
        public void NoShippedBlockIsLostBeforeItCrosses()
        {
            if (!GameBlocks.IsInstalled) return;

            List<BlockHeatIndex.Reading> readings = BlockHeatIndex.All();
            Assert.True(readings.Count > 0, "the install yielded no readings");

            int crossing = 0;
            List<string> violations = new List<string>();

            foreach (BlockHeatIndex.Reading reading in readings)
            {
                if (float.IsInfinity(reading.SecondsToCritical)) continue;
                crossing++;

                if (reading.SecondsCriticalToLoss <= 0f)
                {
                    violations.Add(reading.Subtype + " is destroyed at the moment it crosses");
                }
            }

            Assert.True(crossing > 0, "no shipped block crosses, so nothing was judged");
            Assert.True(violations.Count == 0, string.Join("\n  ", violations));
        }


        /// <summary>
        /// The closed form is a statement about the solver, so the solver is asked.
        ///
        /// <para>
        /// One generating block alone, radiation and conduction off, started exactly at its own
        /// critical temperature: the climb is linear and the damage integrates to
        /// <c>sqrt(2 C I / (d W))</c>. If <see cref="BlockHeatIndex.SecondsFromCriticalToLoss"/>
        /// and <c>ApplyNodeWattsRange</c> ever stop describing the same rule, this is what says so
        /// — every other test here checks the quadrature against arithmetic of its own.
        /// </para>
        /// </summary>
        [Fact]
        public void TheSolverDestroysTheBlockWhenTheClosedFormSaysItWill()
        {
            const float Critical = 900f;
            const float DamagePerKelvin = 2f;
            const float Integrity = 5000f;

            BlockThermalProperties thermal = Catalog.DefaultThermal();
            thermal.CriticalTemperature = Critical;
            thermal.OverheatDamagePerKelvin = DamagePerKelvin;

            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableFriction = false;
            settings.EnableDamage = true;
            settings.Frequency = 16;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(BlockModel.Solid("Overheating", Vector3I.One, 500f, thermal), Vector3I.Zero)
                   .Consuming(2e5f);

            ThermalSimulation simulation = builder.BuildSimulation(settings, Critical);
            ThermalNode node = simulation.Solver.Nodes[0];

            float watts = node.HeatGenerationWatts;
            float capacity = node.ThermalMass;
            Assert.True(watts > 0f, "the block must be making heat");

            float expected = (float)Math.Sqrt(
                2d * capacity * Integrity / (DamagePerKelvin * (double)watts));

            float damage = 0f;
            float seconds = 0f;
            float step = 1f / settings.Frequency;

            while (damage < Integrity && seconds < 4f * expected)
            {
                simulation.StepExact(1, Worlds.Shadow());
                seconds += step;
                for (int i = 0; i < simulation.Overheats.Count; i++)
                {
                    damage += simulation.Overheats[i].Damage;
                }
            }

            Assert.True(damage >= Integrity, "the block was never destroyed");
            Assert.True(Math.Abs(seconds - expected) <= 0.02f * expected + step,
                "solver " + seconds.ToString("n2") + " s against closed form "
                + expected.ToString("n2") + " s");
        }

        /// <summary>
        /// Steps the same block forward in time under the same two rules the quadrature integrates,
        /// small enough that the answer is set by the physics rather than by the step.
        /// </summary>
        private static float StepToLoss(float capacity, float watts, float coefficient,
            float critical, float damagePerKelvin, float integrity)
        {
            const double Step = 0.0005d;
            double ambient4 = Math.Pow(Ambient, 4);

            double temperature = critical;
            double damage = 0d;
            double seconds = 0d;

            while (seconds < BlockHeatIndex.LossHorizonSeconds)
            {
                double net = watts - (coefficient * (Math.Pow(temperature, 4) - ambient4));
                temperature += net * Step / capacity;
                damage += damagePerKelvin * (temperature - critical) * Step;
                seconds += Step;

                if (damage >= integrity) return (float)seconds;
            }

            return float.PositiveInfinity;
        }
    }
}
