using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// How long a block has after it crosses its critical temperature — the second half of the
    /// event the balance survey had only measured the first half of.
    ///
    /// <para>
    /// The solver damages an overheating block by <c>(T - critical) x OverheatDamagePerKelvin</c>
    /// per simulated second, so at the crossing the damage rate is exactly zero and the block is
    /// losing nothing. What it costs a player is when its hit points run out, and
    /// <see cref="BlockHeatIndex.SecondsFromCriticalToLoss"/> is the closed answer to that.
    /// This class is what stops that answer being its own oracle (`E7`): the closed form against
    /// arithmetic where arithmetic has an answer, and against the shipped solver where it does not.
    /// See balance.md, How long a block has after it crosses.
    /// </para>
    /// </summary>
    public class TimeToLossTests
    {
        private readonly Xunit.Abstractions.ITestOutputHelper output;

        public TimeToLossTests(Xunit.Abstractions.ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>
        /// With nothing radiating, temperature climbs linearly, damage accrues as the square of the
        /// time, and destruction lands at <c>sqrt(2 C I / (d W))</c> exactly.
        ///
        /// The calibration for every other figure in the column, and it needs no install, no
        /// corpus and no solver — which is the point, because the balance argument this figure
        /// carries should be checkable with a pencil.
        /// </summary>
        [Fact]
        public void WithNothingRadiatingTheLossTimeIsTheClosedForm()
        {
            const float Capacity = 5000f;            // J/K
            const float Watts = 250f;
            const float Critical = 600f;
            const float DamagePerKelvin = 2f;
            const float Integrity = 30000f;

            float expected = (float)Math.Sqrt(
                2d * Capacity * Integrity / (DamagePerKelvin * (double)Watts));

            float measured = BlockHeatIndex.SecondsFromCriticalToLoss(
                Capacity, Watts, 0f, Critical, DamagePerKelvin, Integrity);

            Assert.Equal(expected, measured, 1);

            // Square-root rather than linear in all three, which is what makes it a weak dial:
            // halving a block's waste heat buys a player forty per cent more time, not double.
            Assert.Equal(expected * (float)Math.Sqrt(2d), BlockHeatIndex.SecondsFromCriticalToLoss(
                Capacity, Watts / 2f, 0f, Critical, DamagePerKelvin, Integrity), 1);
            Assert.Equal(expected * (float)Math.Sqrt(2d), BlockHeatIndex.SecondsFromCriticalToLoss(
                Capacity, Watts, 0f, Critical, DamagePerKelvin, Integrity * 2f), 1);
        }

        /// <summary>
        /// A block that settles just above its own limit grinds down at a near-constant rate, and
        /// most of its life is spent there rather than climbing.
        ///
        /// <para>
        /// This is the branch the geometric grid and the closed tail exist for: the approach to
        /// equilibrium takes forever in principle, so an integrator that waited for it would report
        /// infinity for every block that is only slightly over. Checked against a plain forward
        /// Euler march at a thousandth of a second — a different algorithm rather than the same one
        /// twice (`E7`).
        /// </para>
        /// </summary>
        [Fact]
        public void ABlockThatSettlesJustAboveCriticalGrindsDownAtAConstantRate()
        {
            const float Capacity = 200f;
            const float Watts = 250f;
            const float Critical = 500f;
            const float DamagePerKelvin = 1f;
            const float Integrity = 20000f;

            // The coefficient that puts the equilibrium ten kelvin above critical.
            float ambient4 = Pow4(BlockHeatIndex.AmbientKelvin);
            float coefficient = Watts / (Pow4(Critical + 10f) - ambient4);

            float measured = BlockHeatIndex.SecondsFromCriticalToLoss(
                Capacity, Watts, coefficient, Critical, DamagePerKelvin, Integrity);
            float marched = MarchToLoss(Capacity, Watts, coefficient, Critical, DamagePerKelvin,
                Integrity);

            Assert.False(float.IsInfinity(measured), "a block ten kelvin over does die, eventually");
            Assert.Equal(marched, measured, marched * 0.005f);

            // And it is the tail that dominates: settled, the rate is 10 K x 1 hp/(K s), so the
            // last stretch alone is most of the answer. A player sees a block sitting slightly hot
            // for half an hour, not an explosion.
            Assert.True(measured > 1500f, "the tail is most of the life, got " + measured);
        }

        /// <summary>
        /// The same two coupled quantities marched forward in time at a fixed small step. Slow,
        /// obvious, and independent of the integrator it checks.
        /// </summary>
        private static float MarchToLoss(float capacity, float watts, float coefficient,
            float critical, float damagePerKelvin, float integrity)
        {
            const double Step = 0.001d;
            double ambient4 = Pow4(BlockHeatIndex.AmbientKelvin);
            double temperature = critical;
            double damage = 0d;

            for (int i = 0; i < 20000000; i++)
            {
                damage += damagePerKelvin * (temperature - critical) * Step;
                if (damage >= integrity) return (float)(i * Step);

                double squared = temperature * temperature;
                double net = watts - (coefficient * ((squared * squared) - ambient4));
                temperature += net * Step / capacity;
            }

            return float.PositiveInfinity;
        }

        /// <summary>
        /// A block whose own skin holds it under its limit never crosses, so it never takes damage
        /// and never dies — reported as infinite rather than as a large number, exactly as
        /// <see cref="BlockHeatIndex.SecondsToReach"/> reports the same block.
        ///
        /// The two columns are read together, and a row that is finite in one and infinite in the
        /// other is a table that cannot be believed.
        /// </summary>
        [Fact]
        public void ABlockThatNeverCrossesIsNeverDestroyed()
        {
            const float Capacity = 5000f;
            const float Watts = 250f;
            const float Critical = 900f;

            float ambient4 = Pow4(BlockHeatIndex.AmbientKelvin);
            float coefficient = Watts / (Pow4(Critical - 100f) - ambient4);

            Assert.True(float.IsPositiveInfinity(BlockHeatIndex.SecondsToReach(
                Capacity, Watts, coefficient, Critical)));
            Assert.True(float.IsPositiveInfinity(BlockHeatIndex.SecondsFromCriticalToLoss(
                Capacity, Watts, coefficient, Critical, 1f, 20000f)));
        }

        /// <summary>
        /// The closed form against the shipped solver, on a rig built so the closed form's
        /// assumptions are true rather than assumed: one block alone, every face exposed, in a
        /// vacuum whose sink is set to the ambient the index quotes against.
        ///
        /// <para>
        /// This is the check that matters. The column is about to carry a balance decision, and
        /// nothing else in the repository would notice if it were integrating the wrong quantity.
        /// </para>
        /// </summary>
        [Fact]
        public void TheClosedFormAgreesWithTheSolverOnASingleBlock()
        {
            const float Mass = 30000f;
            const float SpecificHeat = 450f;
            const float Critical = 700f;
            const float DamagePerKelvin = 2f;
            const float Integrity = 30000f;
            const float Watts = 400000f;

            BlockThermalProperties thermal = Catalog.DefaultThermal();
            thermal.SpecificHeat = SpecificHeat;
            thermal.CriticalTemperature = Critical;
            thermal.OverheatDamagePerKelvin = DamagePerKelvin;
            thermal.ProducerWasteEnergy = 1f;
            thermal.ExposedSurfaceMultiplier = 1f;

            ThermalSettings settings = new ThermalSettings();
            settings.EnableDamage = true;
            settings.EnableSolarHeat = false;
            settings.EnableFriction = false;
            settings.Frequency = 60;

            // The index quotes its radiation against room-temperature ambient, and says so. Making
            // the vacuum that temperature is what turns "generous" into "comparable" — the
            // alternative is comparing two different physics and calling the gap an error.
            settings.VacuumTemperature = BlockHeatIndex.AmbientKelvin;

            GridBuilder builder = GridBuilder.Large();
            builder.Place(BlockModel.Solid("Oracle", Vector3I.One, Mass, thermal), Vector3I.Zero)
                   .Producing(Watts);

            ThermalSimulation simulation =
                builder.BuildSimulation(settings, BlockHeatIndex.AmbientKelvin);

            float area = 6f * Catalog.LargeGridSize * Catalog.LargeGridSize;
            float capacity = Mass * SpecificHeat / settings.HeatTimeScale;
            float coefficient = thermal.Emissivity * ThermalConstants.StefanBoltzmann * area;

            float closedCritical = BlockHeatIndex.SecondsToReach(capacity,
                Watts * thermal.ProducerWasteEnergy, coefficient, Critical);
            float closedLoss = BlockHeatIndex.SecondsFromCriticalToLoss(capacity,
                Watts * thermal.ProducerWasteEnergy, coefficient, Critical, DamagePerKelvin,
                Integrity);

            Assert.False(float.IsInfinity(closedLoss),
                "the rig has to destroy the block or this test asserts nothing");

            float step = settings.StepSeconds;
            float elapsed = 0f;
            float damage = 0f;
            float measuredCritical = -1f;
            float measuredLoss = -1f;

            for (int i = 0; i < 200000 && measuredLoss < 0f; i++)
            {
                simulation.StepExact(1, Worlds.Shadow());
                elapsed += step;

                IList<OverheatEvent> events = simulation.Overheats;
                for (int e = 0; e < events.Count; e++)
                {
                    if (measuredCritical < 0f) measuredCritical = elapsed;
                    damage += events[e].Damage;
                    if (damage >= Integrity && measuredLoss < 0f) measuredLoss = elapsed;
                }
            }

            Assert.True(measuredLoss > 0f, "the solver never destroyed the block");

            // Within a per cent. The solver integrates in fixed steps and the closed form does not,
            // so they cannot be bit-equal; anything wider than this would be a different quantity.
            Assert.Equal(closedCritical, measuredCritical, closedCritical * 0.01f);
            Assert.Equal(closedLoss, measuredLoss - measuredCritical, closedLoss * 0.01f);
        }


        /// <summary>
        /// Past an hour of play the figure has stopped answering a question about a session, so it
        /// reads as never rather than as a number nobody will see reached.
        /// </summary>
        [Fact]
        public void DestructionBeyondTheHorizonReadsAsNever()
        {
            const float Capacity = 5000f;
            const float Watts = 250f;
            float critical = BlockHeatIndex.AmbientKelvin + 100f;

            // Barely over its limit, with hit points far past what that trickle can spend.
            float equilibrium = critical + 1f;
            float ambient4 = Pow4(BlockHeatIndex.AmbientKelvin);
            float coefficient = Watts / (Pow4(equilibrium) - ambient4);

            Assert.True(float.IsPositiveInfinity(BlockHeatIndex.SecondsFromCriticalToLoss(
                Capacity, Watts, coefficient, critical, 1f, 1e6f)),
                "a block an hour of overheating cannot finish is not a loss");
        }

        /// <summary>
        /// A block the install could not price reports zero rather than infinity, because a caller
        /// has to be able to tell *this block is not destroyed* from *this figure is unavailable*.
        /// </summary>
        [Fact]
        public void AnUnpricedBlockIsUnavailableRatherThanIndestructible()
        {
            Assert.Equal(0f, BlockHeatIndex.SecondsFromCriticalToLoss(
                5000f, 250f, 0f, BlockHeatIndex.AmbientKelvin + 100f, 1f, 0f));
        }

        /// <summary>
        /// Every shipped block that crosses is destroyed, and never at the moment it crosses.
        ///
        /// The ordering is the claim. A table reporting a loss at the crossing would be describing
        /// a different damage rule from the one the solver runs, where the rate there is zero by
        /// construction — and it is the whole table rather than a case, because the four above are
        /// arithmetic and this is the definitions the balance argument is actually about.
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
        /// Halving a block's damage per kelvin does *not* double how long it lasts, and the shape is
        /// what makes the dial a weak one: the damage rate rises from zero as the block climbs, so
        /// the span goes as the inverse square root of the dial rather than its inverse.
        /// </summary>
        [Fact]
        public void TheSpanGoesAsTheInverseSquareRootOfTheDamageDial()
        {
            const float Capacity = 5000f;
            const float Watts = 250f;
            const float Critical = 600f;
            const float Integrity = 30000f;

            float baseline = BlockHeatIndex.SecondsFromCriticalToLoss(
                Capacity, Watts, 0f, Critical, 2f, Integrity);

            Assert.Equal(baseline * (float)Math.Sqrt(2d), BlockHeatIndex.SecondsFromCriticalToLoss(
                Capacity, Watts, 0f, Critical, 1f, Integrity), 1);
            Assert.Equal(baseline / (float)Math.Sqrt(2d), BlockHeatIndex.SecondsFromCriticalToLoss(
                Capacity, Watts, 0f, Critical, 4f, Integrity), 1);
        }

        /// <summary>
        /// At the damage rule `OverheatDamagePerKelvin` was authored under, a block lasts a fraction
        /// as long — and this is the measurement that says the authored values should stay.
        ///
        /// <para>
        /// The solver used to apply the whole overshoot as damage on **every update**, so at the
        /// shipped `Frequency` of 8 the dial bit eight times harder than it does now. That is one
        /// of the three changes [backlog.md](../../docs/backlog.md) `C2` says the authored values
        /// predate — and re-running the shipped definitions at eight times the dial shows restoring
        /// their authored intent would put the median block's whole life past its rating inside ten
        /// seconds, which is the failure `C11` was opened for and `G5` forbids.
        /// </para>
        ///
        /// <para>
        /// **The scaling is not a square root here**, which is why this recomputes rather than
        /// dividing: a block that settles just over its limit grinds down at a constant rate, and
        /// that tail is linear in the dial. Blocks near the fast end lose about 2.8×, the slow end
        /// up to 8×. See balance.md, How long a block has after it crosses.
        /// </para>
        ///
        /// <para>
        /// **The seconds moved with `C24` and the comparison did not.** Every figure here is
        /// proportional to the clock, which went from 225 to 90, so the same two rules now read
        /// 32.7 s shipped against **10.1 s** authored where they read 13.1 s against 4.0 s. What
        /// the test asserts is the ratio and the size of the authored figure — a third of the
        /// shipped rule, and about ten seconds — rather than a bound the clock alone can cross.
        /// </para>
        /// </summary>
        [Fact]
        public void TheAuthoredDamageRuleWouldPutTheWholeEventInsideAboutTenSeconds()
        {
            if (!GameBlocks.IsInstalled) return;

            // The factor the per-step rule applied at the shipped frequency.
            const float Authored = 8f;

            List<float> shipped = new List<float>();
            List<float> authored = new List<float>();

            foreach (BlockHeatIndex.Reading reading in BlockHeatIndex.All())
            {
                if (float.IsInfinity(reading.SecondsToCritical)) continue;
                if (float.IsInfinity(reading.SecondsCriticalToLoss)) continue;

                float harsher = BlockHeatIndex.SecondsFromCriticalToLoss(
                    reading.HeatCapacity, reading.Watts, reading.RadiativeCoefficient,
                    reading.CriticalKelvin, reading.DamagePerKelvin * Authored, reading.Integrity);

                if (float.IsInfinity(harsher)) continue;

                shipped.Add(reading.SecondsCriticalToLoss);
                authored.Add(harsher);
            }

            Assert.True(shipped.Count > 20, "too few shipped blocks crossed to say anything");

            float shippedMedian = Median(shipped);
            float authoredMedian = Median(authored);

            output.WriteLine(string.Format("{0,-22}{1,9}{2,9}{3,9}", "damage per kelvin", "p10", "p50", "p90"));
            output.WriteLine(string.Format("{0,-22}{1,9:n1}{2,9:n1}{3,9:n1}", "as shipped (x1)",
                Percentile(shipped, 10), shippedMedian, Percentile(shipped, 90)));
            output.WriteLine(string.Format("{0,-22}{1,9:n1}{2,9:n1}{3,9:n1}", "as authored (x8)",
                Percentile(authored, 10), authoredMedian, Percentile(authored, 90)));
            output.WriteLine(shipped.Count + " shipped block types cross and are destroyed");

            Assert.True(shippedMedian > 20f,
                "the shipped rule gives the median block " + shippedMedian.ToString("n1") + " s");
            Assert.True(authoredMedian < 15f,
                "the authored rule gives the median block " + authoredMedian.ToString("n1") + " s");
            Assert.True(authoredMedian < shippedMedian / 3f,
                "the authored rule gives the median block " + authoredMedian.ToString("n1")
                + " s against the shipped rule's " + shippedMedian.ToString("n1")
                + " s, so restoring it would cost less than the third it costs now");
        }

        private static float Percentile(List<float> values, int percent)
        {
            List<float> ordered = new List<float>(values);
            ordered.Sort();
            int index = (ordered.Count - 1) * percent / 100;
            return ordered[index];
        }

        private static float Median(List<float> values)
        {
            List<float> ordered = new List<float>(values);
            ordered.Sort();
            return ordered[ordered.Count / 2];
        }

        private static float Pow4(float value)
        {
            float square = value * value;
            return square * square;
        }
    }
}
