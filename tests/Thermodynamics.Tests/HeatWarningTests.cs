using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The lead cue's forecast, and the defect it exists to avoid.
    ///
    /// <para>
    /// The interesting tests here are the last two. Everything above them checks the arithmetic
    /// against itself, which is worth doing and proves nothing about whether the forecast is right
    /// about a real block; the last two run an actual grid and ask the solver.
    /// </para>
    /// </summary>
    public class HeatWarningTests
    {
        /// <summary>A block already past the threshold is not a forecast, and says so at once.</summary>
        [Fact]
        public void AlreadyCrossedReadsZeroSeconds()
        {
            HeatForecast forecast = HeatWarning.Forecast(950f, 1f, 1f, 1f, 900f);

            Assert.True(forecast.WillCross);
            Assert.Equal(0f, forecast.Seconds);
        }

        /// <summary>A block that is not heating is not going anywhere.</summary>
        [Fact]
        public void ACoolingBlockIsNeverWarnedAbout()
        {
            Assert.False(HeatWarning.Forecast(800f, -1f, 5f, 1f, 900f).WillCross);
            Assert.False(HeatWarning.Forecast(800f, 0f, 5f, 1f, 900f).WillCross);
        }

        /// <summary>
        /// With one sample there is no decay to read, so the straight line answers — and the
        /// straight line is exactly right for a block heating at a constant rate.
        /// </summary>
        [Fact]
        public void OneSampleGivesTheStraightLine()
        {
            HeatForecast forecast = HeatWarning.Forecast(800f, 10f, 0f, 1f, 900f);

            Assert.True(forecast.WillCross);
            Assert.Equal(10f, forecast.Seconds, 3);
        }

        /// <summary>
        /// **The defect this file is about.** A block levelling off below its rating is not warned
        /// about, where a straight line would have warned every time.
        ///
        /// The rates here are a real exponential approach: T∞ = 850 K with τ = 10 s, sampled a
        /// second apart at 800 K. A straight line says the block crosses 900 K in twenty seconds.
        /// It never does.
        /// </summary>
        [Fact]
        public void ABlockLevellingOffShortOfItsRatingIsNotWarnedAbout()
        {
            const float tau = 10f;
            const float settles = 850f;
            const float kelvin = 800f;

            float rate = (settles - kelvin) / tau;
            float previousRate = rate * (float)Math.Exp(1d / tau);

            // What the naive projection would have said, so the difference is on the record.
            float straightLine = (900f - kelvin) / rate;
            Assert.InRange(straightLine, 19f, 21f);

            HeatForecast forecast = HeatWarning.Forecast(kelvin, rate, previousRate, 1f, 900f);

            Assert.False(forecast.WillCross);
            Assert.Equal(settles, forecast.Settles, 0);
        }

        /// <summary>
        /// And where the equilibrium is above the rating, the forecast is the exponential's own
        /// crossing time rather than the straight line's.
        /// </summary>
        [Fact]
        public void ABlockHeadingPastItsRatingIsTimedOnTheCurveNotTheLine()
        {
            const float tau = 20f;
            const float settles = 1000f;
            const float kelvin = 800f;

            float rate = (settles - kelvin) / tau;
            float previousRate = rate * (float)Math.Exp(1d / tau);

            HeatForecast forecast = HeatWarning.Forecast(kelvin, rate, previousRate, 1f, 900f);

            Assert.True(forecast.WillCross);
            Assert.Equal(settles, forecast.Settles, 0);

            // tau * ln(200/100)
            Assert.Equal(tau * (float)Math.Log(2d), forecast.Seconds, 1);

            // The straight line would have said ten seconds, which is early by four.
            Assert.True(forecast.Seconds > (900f - kelvin) / rate);
        }

        /// <summary>
        /// A block heating faster than it was has no equilibrium to read, so the straight line
        /// answers — and being early is the direction it is safe to be wrong in.
        /// </summary>
        [Fact]
        public void AnAcceleratingBlockFallsBackToTheStraightLineAndWarnsEarly()
        {
            HeatForecast forecast = HeatWarning.Forecast(800f, 20f, 10f, 1f, 900f);

            Assert.True(forecast.WillCross);
            Assert.True(forecast.Accelerating);
            Assert.Equal(5f, forecast.Seconds, 3);
        }

        /// <summary>
        /// Nonsense in does not become a warning out.
        /// </summary>
        [Fact]
        public void NonsenseNumbersDoNotEscapeAsAWarning()
        {
            Assert.False(HeatWarning.Forecast(float.NaN, 1f, 1f, 1f, 900f).WillCross);
            Assert.False(HeatWarning.Forecast(800f, float.NaN, 1f, 1f, 900f).WillCross);
            Assert.False(HeatWarning.Forecast(800f, 1f, 1f, 1f, 0f).WillCross);
            // A negative interval is no interval: there is no decay to read, so the straight line
            // answers and says so rather than dividing by it.
            HeatForecast backwards = HeatWarning.Forecast(800f, 1f, 1f, -1f, 900f);
            Assert.True(backwards.Accelerating);
            Assert.Equal(100f, backwards.Seconds, 3);
            Assert.True(float.IsPositiveInfinity(backwards.Settles));
        }

        /// <summary>
        /// **The check that is not against the arithmetic.** A real block warming toward a real
        /// equilibrium below its rating, stepped by the solver, is never warned about — and its
        /// forecast equilibrium never once reads above the rating either, which is the claim the
        /// warning rests on rather than the one it reports.
        ///
        /// <para>
        /// This is the case the intent document says a cue must not cry wolf on: an ordinary block
        /// doing its job and warming up while it does. It settles at 700 K against a 900 K rating,
        /// which is inside the band <see cref="HeatCueState.WatchFraction"/> watches — so the whole
        /// climb is spent as a candidate for a cue, and none is due.
        /// </para>
        /// </summary>
        [Fact]
        public void TheForecastNeverCriesWolfOnABlockThatSettlesShortOfCritical()
        {
            Sampled run = March(700f);

            Assert.True(run.Peak < run.Critical,
                "the rig was supposed to settle short of critical and reached "
                + run.Peak.ToString("n0") + " K against " + run.Critical.ToString("n0"));

            Assert.True(run.Peak >= run.Critical * HeatCueState.WatchFraction,
                "the block never entered the watched band, so nothing here was tested: "
                + run.Peak.ToString("n0") + " K against a watch point of "
                + (run.Critical * HeatCueState.WatchFraction).ToString("n0"));

            Assert.False(run.Warned,
                "the forecast warned about a block that settled at " + run.Peak.ToString("n0")
                + " K, short of " + run.Critical.ToString("n0"));

            Assert.True(run.HighestSettles < run.Critical,
                "the forecast read an equilibrium of " + run.HighestSettles.ToString("n0")
                + " K on a block that settles at " + run.Peak.ToString("n0"));
        }

        /// <summary>
        /// And on a block that really does cross, the cue arrives before the crossing and close to
        /// the lead it was asked for.
        ///
        /// <para>
        /// **Before is the requirement and close is the quality.** A forecast that fires at the
        /// crossing is not a warning, and one that fires a minute early is not about this block's
        /// state any more. The tolerance is generous in the early direction and hard in the late
        /// one, which is the asymmetry the feature is for.
        /// </para>
        /// </summary>
        [Fact]
        public void TheCueArrivesBeforeTheCrossing()
        {
            Sampled run = March(1200f);

            Assert.True(run.Peak >= run.Critical,
                "the rig was supposed to cross and reached only " + run.Peak.ToString("n0") + " K");
            Assert.True(run.Warned, "no cue was raised before a crossing");

            float lead = run.CrossedAt - run.WarnedAt;
            Assert.True(lead > 0f,
                "the cue arrived at " + run.WarnedAt.ToString("n1") + " s and the crossing at "
                + run.CrossedAt.ToString("n1") + " s, so it was not a warning");

            // Generous in the early direction and hard in the late one, which is the asymmetry
            // the feature is for. The measured lead on this rig is about two seconds — under the
            // nominal three, because the crossing itself is only a couple of seconds after the
            // load steps up, which is C11's finding rather than this forecast's.
            Assert.InRange(lead, 0.5f, 4f * HeatWarning.LeadSeconds);
        }

        private struct Sampled
        {
            public float Critical;
            public float Peak;
            public bool Warned;
            public float WarnedAt;
            public float CrossedAt;

            /// <summary>The highest equilibrium the forecast read over the run, K.</summary>
            public float HighestSettles;
        }

        private const float Critical = 900f;

        /// <summary>
        /// The temperature the block is already holding when the event starts, K. Roughly a warm
        /// hull, and deliberately not 2.7 K.
        /// </summary>
        private const float Resting = 600f;

        /// <summary>
        /// Settles one bare block at <see cref="Resting"/>, then turns its load up until its new
        /// equilibrium is <paramref name="settlesAt"/>, and asks the forecast on the cue's own
        /// cadence from that moment.
        ///
        /// <para>
        /// **One block and no hull, and a step from an equilibrium rather than from absolute zero.**
        /// Alone, its only way out is its own skin, so where it settles is
        /// <c>εσA(T⁴ − T∞⁴) = W</c> and nothing else, and the watts for a named equilibrium are
        /// solved from the node's own radiation coefficient rather than guessed. Starting from a
        /// settled state is what makes it the event the cue is about — a reactor coming on under a
        /// block that was already sitting somewhere — and it is also where a first-order forecast is
        /// entitled to be believed. From 2.7 K the same rig warns about twelve seconds early rather
        /// than three, because the radiative approach barely decays while the block is cold and a
        /// rate that is not decaying reads as a straight line.
        /// </para>
        ///
        /// <para>
        /// The mass is small on purpose. τ = C/(4εσAT³), so a heavier block is a slower one, and a
        /// block slower than about three seconds is one the straight line never cries wolf about
        /// either — which would leave the comparison below with nothing to compare.
        /// </para>
        /// </summary>
        private static Sampled March(float settlesAt)
        {
            const float seconds = 120f;
            const float mass = 1000f;

            ThermalSettings settings = new ThermalSettings();
            settings.EnableDamage = false;
            settings.Derive();

            // **The scan cadence has to be the solver's own, and this is where that was learnt.**
            // A step spans several frames, so asking twice inside one step reads a rate of zero and
            // then a spike, and a decay cannot be read off either. The game pass runs from
            // AfterSteps for the same reason.
            float interval = settings.StepSeconds;

            BlockThermalProperties source = Catalog.DefaultThermal();
            source.ProducerWasteEnergy = 0f;
            source.CriticalTemperature = Critical;

            GridBuilder builder = GridBuilder.Large();
            builder.Place(BlockModel.Solid("source", Vector3I.One, mass, source), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(settings, seconds);
            ThermalNode block = simulation.Solver.GetNode(builder.Last);

            double gap = Math.Pow(Resting, 4d) - Math.Pow(settings.VacuumTemperature, 4d);
            source.HeatSourceWatts = (float)(block.RadiationCoefficient * gap);
            block.RefreshHeatGeneration();

            for (float t = 0f; t < 600f; t += 1f) simulation.Update(1f, Worlds.Shadow());

            Assert.InRange(block.Temperature, Resting - 5f, Resting + 5f);

            gap = Math.Pow(settlesAt, 4d) - Math.Pow(settings.VacuumTemperature, 4d);
            source.HeatSourceWatts = (float)(block.RadiationCoefficient * gap);
            block.RefreshHeatGeneration();

            Sampled run = new Sampled();
            run.Critical = Critical;
            run.CrossedAt = float.PositiveInfinity;

            float previousKelvin = block.Temperature;
            float previousRate = 0f;
            bool hasRate = false;

            for (float t = interval; t <= seconds; t += interval)
            {
                simulation.Update(interval, Worlds.Shadow());

                float kelvin = block.Temperature;
                if (kelvin > run.Peak) run.Peak = kelvin;

                float rate = (kelvin - previousKelvin) / interval;

                if (kelvin >= run.Critical && float.IsPositiveInfinity(run.CrossedAt))
                {
                    run.CrossedAt = t;
                }

                HeatForecast forecast = HeatWarning.Forecast(kelvin, rate,
                    hasRate ? previousRate : 0f, interval, run.Critical);

                bool due = forecast.WillCross && forecast.Seconds <= HeatWarning.LeadSeconds;
                if (due && kelvin < run.Critical && !run.Warned)
                {
                    run.Warned = true;
                    run.WarnedAt = t;
                }

                if (!float.IsInfinity(forecast.Settles) && forecast.Settles > run.HighestSettles)
                {
                    run.HighestSettles = forecast.Settles;
                }

                previousKelvin = kelvin;
                previousRate = rate;
                hasRate = true;
            }

            return run;
        }

    }
}
