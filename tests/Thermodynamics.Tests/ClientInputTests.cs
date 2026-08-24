using System;
using System.Collections.Generic;
using System.IO;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The claim the degraded-input lab exists to make**, which is that a client's inputs and a
    /// client's initial state fail in different ways: a wrong state is a perturbation and decays,
    /// a wrong input is a bias and does not.
    ///
    /// <para>
    /// The whole value of that lab is the standing-error column, and a standing error is only
    /// meaningful if the rig can produce a zero one. So the two tests that matter here are that an
    /// undegraded client agrees **exactly** — anything else and every figure the lab prints is
    /// measuring the harness (`E8`) — and that a perturbation and a bias come out on opposite
    /// sides of that column.
    /// </para>
    ///
    /// <para>
    /// The runs are short and small because the claims are about shape. The figures a decision is
    /// quoted from come from `-- inputs`, at sizes and clocks a suite has no business running.
    /// See [backlog.md](../../docs/backlog.md) `B4`.
    /// </para>
    /// </summary>
    public class ClientInputTests
    {
        private readonly ITestOutputHelper output;

        public ClientInputTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>
        /// Blocks every run in this file asks for.
        ///
        /// **The smallest census hull that has a sealed compartment**, and it is a measured
        /// threshold rather than a round number: at 500 blocks the hull is 907 nodes and has no
        /// room at all, at 600 it is 1,004 nodes and has one. Below it the two room knobs turn
        /// against nothing and report a client that recovered from a degradation it was never
        /// given (`E8`) — the lab raises rather than letting that happen, and this is the size that
        /// answers it. One size for every row, because a fixture that differs between two claims
        /// makes them incomparable.
        /// </summary>
        private const int Blocks = ClientInputLab.SmallestHullWithACompartment;

        private static ClientInputLab.Result Run(ClientInputLab.Degradation how,
            ClientDriftLab.Correction fix = null)
        {
            return ClientInputLab.Measure(how, fix ?? ClientDriftLab.Correction.None,
                "sunlit", 240f, Blocks);
        }

        /// <summary>
        /// The control. Two clients given the same world must agree to the last bit, or the
        /// standing error of every other row is this row's number plus a degradation.
        /// </summary>
        [Fact]
        public void AnUndegradedClientAgreesExactly()
        {
            ClientInputLab.Result result = Run(new ClientInputLab.Degradation { Name = "none" });

            Assert.Equal(0f, result.PeakKelvin, 4);
            Assert.Equal(0f, result.StandingKelvin, 4);
            Assert.Equal(0f, result.SecondsMisreading, 4);
            Assert.Equal(0, result.PeakDisagreeing);

            // And the rig is one the room knobs can bite on. A hull with no compartment agrees
            // exactly about rooms for the same reason a blank page has no spelling mistakes, and
            // the control row is where that has to be caught (`E8`).
            Assert.True(result.Rooms > 0,
                "the rig has no sealed compartment, so its two room rows judge nothing");
        }

        /// <summary>
        /// **A perturbation decays and a bias does not**, which is the distinction the lab was
        /// built to draw and the reason the standing column exists beside the peak one.
        /// </summary>
        [Fact]
        public void APerturbationDecaysAndABiasDoesNot()
        {
            ClientInputLab.Result perturbed = Run(new ClientInputLab.Degradation
            {
                Name = "stale join",
                StaleSeconds = 60f,
            });

            ClientInputLab.Result biased = Run(new ClientInputLab.Degradation
            {
                Name = "power error",
                PowerErrorShare = 0.05f,
            });

            Assert.True(perturbed.PeakKelvin > 1f,
                "the rig needs a stale client to start wrong; it was " + perturbed.PeakKelvin + " K");

            Assert.True(perturbed.StandingKelvin < perturbed.PeakKelvin * 0.25f,
                "a one-off wrong state should have decayed by the end: peaked at "
                + perturbed.PeakKelvin + " K and settled at " + perturbed.StandingKelvin + " K");

            // **Against the perturbation, not against the bias's own peak.** The peak of a biased
            // run is a transient — the load switches every two minutes and the client's copy of it
            // is wrong, so the disagreement spikes at every switch and settles between them. A
            // threshold on peak would therefore be measuring the load script. What the claim needs
            // is that the bias settles somewhere the perturbation does not.
            Assert.True(biased.StandingKelvin > 5f * perturbed.StandingKelvin,
                "a wrong input should settle far further out than a wrong state that decayed: "
                + biased.StandingKelvin + " K against " + perturbed.StandingKelvin + " K");
        }

        /// <summary>
        /// A client whose power is a fixed share out is wrong by more the further out it is. Without
        /// this, the bias above could be any constant the rig happens to produce.
        /// </summary>
        [Fact]
        public void ABiggerInputErrorIsABiggerStandingError()
        {
            ClientInputLab.Result small = Run(new ClientInputLab.Degradation
            {
                Name = "power error 2%",
                PowerErrorShare = 0.02f,
            });

            ClientInputLab.Result large = Run(new ClientInputLab.Degradation
            {
                Name = "power error 10%",
                PowerErrorShare = 0.10f,
            });

            Assert.True(large.StandingKelvin > small.StandingKelvin,
                "ten per cent out should settle further from the server than two: "
                + large.StandingKelvin + " K against " + small.StandingKelvin + " K");
        }

        /// <summary>
        /// The scripted load actually moves. A constant load would make every lag knob measure
        /// nothing, and the table would report a client that recovers from a degradation it was
        /// never given (`E8`).
        /// </summary>
        [Fact]
        public void TheScriptedLoadActuallyChanges()
        {
            float first = ClientInputLab.Watts(0f);
            float later = ClientInputLab.Watts(ClientInputLab.LoadPeriodSeconds + 1f);
            float back = ClientInputLab.Watts(2f * ClientInputLab.LoadPeriodSeconds + 1f);

            Assert.NotEqual(first, later);
            Assert.Equal(first, back);
            Assert.True(first > later, "the run should start loaded");
            Assert.True(later > 0f, "idle is not zero");
        }

        /// <summary>
        /// **The combined case is the union of the others**, computed rather than written out, so
        /// it cannot quietly become the case that flatters the correction.
        /// </summary>
        [Fact]
        public void TheCombinedCaseIsTheUnionOfEveryOtherCase()
        {
            List<ClientInputLab.Degradation> cases = ClientInputLab.All();
            ClientInputLab.Degradation everything = cases[cases.Count - 1];

            Assert.Equal("all at once", everything.Name);

            for (int i = 0; i < cases.Count - 1; i++)
            {
                ClientInputLab.Degradation one = cases[i];

                Assert.True(everything.StaleSeconds >= one.StaleSeconds, one.Name + ": stale");
                Assert.True(everything.EnvironmentLagSeconds >= one.EnvironmentLagSeconds, one.Name + ": environment");
                Assert.True(everything.SunAngleDegrees >= one.SunAngleDegrees, one.Name + ": sun");
                Assert.True(everything.MassErrorShare >= one.MassErrorShare, one.Name + ": mass");
                Assert.True(everything.BlocksOffShare >= one.BlocksOffShare, one.Name + ": blocks off");
                Assert.True(everything.PowerLagSeconds >= one.PowerLagSeconds, one.Name + ": power lag");
                Assert.True(everything.PowerErrorShare >= one.PowerErrorShare, one.Name + ": power error");
                Assert.True(everything.AirDensityError >= one.AirDensityError, one.Name + ": air");
                Assert.True(everything.RoomPressureError >= one.RoomPressureError, one.Name + ": room pressure");
                Assert.True(everything.RoomMapLagSeconds >= one.RoomMapLagSeconds, one.Name + ": room map");
                if (one.OnDefaultSettings) Assert.True(everything.OnDefaultSettings, one.Name + ": settings");
                if (one.HitchEverySeconds > 0f) Assert.True(everything.HitchEverySeconds > 0f, one.Name + ": hitch");
            }
        }

        /// <summary>
        /// Every case says what it degrades and why that is what the engine does. A knob with no
        /// stated mechanism is a number somebody picked (`D6`).
        /// </summary>
        [Fact]
        public void EveryCaseSaysWhatItDegradesAndWhy()
        {
            foreach (ClientInputLab.Degradation one in ClientInputLab.All())
            {
                Assert.False(string.IsNullOrEmpty(one.Name));
                Assert.False(string.IsNullOrEmpty(one.Because),
                    one.Name + " does not say why it is a thing that happens");
            }
        }

        /// <summary>
        /// **A run where nothing on the server ever failed reports that**, rather than reporting a
        /// readout that agreed about nothing. This is the guard that made the first sweep of this
        /// lab readable: on a planet at this size no block crosses critical, and every readout
        /// column was a zero that meant *not measured* (`E8`).
        /// </summary>
        [Fact]
        public void ARunWithNothingHotSaysSoRatherThanReportingAgreement()
        {
            // **A planet, and that is the cold case here rather than the obvious one.** Shadow
            // looks like the cold scenario and is the hot one: a hull in vacuum sheds only by
            // radiation, so at this load it is past critical inside a minute, while the same hull
            // on a planet convects into the air and never gets there. The rig was written the
            // other way round first and the guard caught it.
            ClientInputLab.Result cold = ClientInputLab.Measure(
                new ClientInputLab.Degradation { Name = "none" },
                ClientDriftLab.Correction.None, "planet", 120f, Blocks);

            ClientInputLab.Result hot = Run(new ClientInputLab.Degradation
            {
                Name = "stale join",
                StaleSeconds = 60f,
            });

            Assert.Equal(0, cold.PeakServerCritical);
            Assert.True(hot.PeakServerCritical > 0,
                "the sunlit rig is supposed to put blocks past critical; it did not");
        }

        /// <summary>
        /// The correction reduces how much of the hull a biased client is wrong about. It does not
        /// remove the bias — the inputs are still wrong — and the assertion is the direction rather
        /// than a figure, because the figure is what `-- inputs` is for.
        /// </summary>
        [Fact]
        public void TheCorrectionNarrowsWhatABiasedClientIsWrongAbout()
        {
            ClientInputLab.Degradation biased = new ClientInputLab.Degradation
            {
                Name = "power error",
                PowerErrorShare = 0.10f,
            };

            ClientInputLab.Result alone = Run(biased);
            ClientInputLab.Result corrected = Run(biased, new ClientDriftLab.Correction
            {
                IntervalSeconds = 5f,
                WholeHullOnJoin = true,
            });

            Assert.True(alone.PeakDisagreeing > 0,
                "the rig needs an uncorrected client that misreads critical; it did not");

            Assert.True(corrected.PeakDisagreeing <= alone.PeakDisagreeing,
                "the correction should not widen the disagreement: "
                + corrected.PeakDisagreeing + " against " + alone.PeakDisagreeing);

            Assert.True(corrected.StandingKelvin < alone.StandingKelvin,
                "the correction should leave a biased client nearer the server: "
                + corrected.StandingKelvin + " K against " + alone.StandingKelvin + " K");
        }

        /// <summary>
        /// **Thrust is a bias that never decays, and per unit of error it is the worst input there
        /// is** — which is what [backlog.md](../../docs/backlog.md) `F17` suspected and nothing had
        /// measured.
        ///
        /// <para>
        /// A thruster's heat is charged against `CurrentThrust`, and the engine *predicts* physics
        /// state on a client rather than replicating it: a client's thrust is its own guess about a
        /// ship whose physics it is not running. Compared at the same 5 % against block power,
        /// which the engine does replicate, the thrust error is the larger of the two — and on a
        /// burning hull thrust is also the larger term, so the two multiply.
        /// </para>
        ///
        /// <para>
        /// The comparison is per unit of error on purpose. The magnitudes in the sweep are knobs
        /// this lab turns rather than figures measured from a session, so *thrust at 20 % beats
        /// power at 5 %* would be a statement about the knobs (`E3`, and `P1`).
        /// </para>
        /// </summary>
        [Fact]
        public void ThrustBiasesAClientMoreThanPowerDoesAtTheSameError()
        {
            // **A knob rather than a measurement**, and it was 0.05 until `C24`: at four times the
            // conduction pace a hull carries a wrong wattage further before it shows, and a five
            // per cent power error settles 0.73 K out — under the kelvin this rig needs before an
            // ordering of two numbers means anything. The claim is per unit of error and is
            // unaffected by which unit; what would break it is the two knobs differing.
            const float Error = 0.10f;

            ClientInputLab.Result thrust = Flying(new ClientInputLab.Degradation
            {
                Name = "thrust error",
                ThrustErrorShare = Error,
            });

            ClientInputLab.Result power = Flying(new ClientInputLab.Degradation
            {
                Name = "power error",
                PowerErrorShare = Error,
            });

            output.WriteLine("at {0:P0}: thrust {1:n1} K standing, power {2:n1} K",
                Error, thrust.StandingKelvin, power.StandingKelvin);

            // Both have to bite at all, or the ordering below is an ordering of two zeroes (`E8`).
            Assert.True(power.StandingKelvin > 1f,
                "power at " + Error + " settled at " + power.StandingKelvin + " K, so the rig is"
                + " not one where an input error matters");

            Assert.True(thrust.StandingKelvin > power.StandingKelvin,
                "thrust settled at " + thrust.StandingKelvin + " K against power's "
                + power.StandingKelvin + " K at the same error, so thrust is not the worse input");
        }

        /// <summary>
        /// **And it is a bias rather than a perturbation**: its peak and its standing error are the
        /// same number, which is what a wrong input that never decays looks like. A hull at rest
        /// shows none of it, which is what says the knob reaches the thrust term and nothing else.
        /// </summary>
        [Fact]
        public void AThrustErrorSettlesRatherThanDecayingAndOnlyOnAShipUnderWay()
        {
            ClientInputLab.Degradation how = new ClientInputLab.Degradation
            {
                Name = "thrust error",
                ThrustErrorShare = 0.2f,
            };

            ClientInputLab.Result flying = Flying(how);
            ClientInputLab.Result resting = Run(how);

            output.WriteLine("flying: peak {0:n1} K, standing {1:n1} K. at rest: peak {2:n1} K",
                flying.PeakKelvin, flying.StandingKelvin, resting.PeakKelvin);

            Assert.True(flying.StandingKelvin > 1f,
                "a flying hull showed " + flying.StandingKelvin + " K, so the knob reached nothing");

            // A bias does not decay: what it peaked at is what it settles at.
            Assert.True(flying.StandingKelvin > flying.PeakKelvin * 0.8f,
                "the error peaked at " + flying.PeakKelvin + " K and settled at "
                + flying.StandingKelvin + " K, which is a perturbation rather than a bias");

            Assert.Equal(0f, resting.PeakKelvin, 3);
        }

        /// <summary>
        /// The same run, on a hull that is flying rather than sitting.
        ///
        /// **The duration is a parameter because a bias needs time to become one.** A standing
        /// error is the mean over a run's final third, and a run cut short reports a bias that is
        /// still climbing as a perturbation — which is `C8` arriving in a different lab.
        /// </summary>
        private static ClientInputLab.Result Flying(ClientInputLab.Degradation how,
            float seconds = 240f)
        {
            return ClientInputLab.Measure(how, ClientDriftLab.Correction.None, "burn", seconds, Blocks);
        }

        /// <summary>
        /// **The one binary input, and it turns out to be a perturbation rather than a bias.**
        ///
        /// <para>
        /// Solar occlusion is resolved by raycasting on each machine's own budget, so a client can
        /// hold a hull in shade while the server has it in full sun — wrong by the entire solar
        /// term at once rather than by an amount ([backlog.md](../../docs/backlog.md) `F18`). It is
        /// the largest single-step input error available, and that is exactly what it turns out to
        /// be: large while it lasts and gone afterwards, because the model is dissipative and the
        /// disagreement ends.
        /// </para>
        ///
        /// <para>
        /// Which makes it the opposite of `thrust error`, whose peak and standing error are the
        /// same number. The two together are what the standing column was added to tell apart.
        /// </para>
        /// </summary>
        [Fact]
        public void AWrongShadowPeaksHardAndThenDecays()
        {
            ClientInputLab.Result result = Run(new ClientInputLab.Degradation
            {
                Name = "wrong shadow",
                OcclusionWrongEverySeconds = 30f,
                OcclusionWrongForSeconds = 3f,
            });

            output.WriteLine("intermittent: peak {0:n1} K, standing {1:n1} K",
                result.PeakKelvin, result.StandingKelvin);

            // It has to bite at all, or the decay below is the decay of nothing (`E8`).
            Assert.True(result.PeakKelvin > 2f,
                "a wrong shadow peaked at only " + result.PeakKelvin + " K, so the knob reached"
                + " nothing and this judges nothing");

            Assert.True(result.StandingKelvin < result.PeakKelvin * 0.5f,
                "it peaked at " + result.PeakKelvin + " K and settled at " + result.StandingKelvin
                + " K, which is a bias rather than the perturbation this pins");
        }

        /// <summary>
        /// **And permanently wrong is a bias, which is the bound rather than the description.** A
        /// client whose raycast never agrees settles a long way out and stays there; that is what
        /// says the intermittent row above decays because the disagreement ends and not because
        /// the solar term is small.
        /// </summary>
        [Fact]
        public void AClientPermanentlyInTheWrongShadowSettlesThereInstead()
        {
            ClientInputLab.Result always = Run(new ClientInputLab.Degradation
            {
                Name = "always in shade",

                // Every second of every second: the flag never agrees.
                OcclusionWrongEverySeconds = 1f,
                OcclusionWrongForSeconds = 1f,
            });

            output.WriteLine("permanent: peak {0:n1} K, standing {1:n1} K",
                always.PeakKelvin, always.StandingKelvin);

            Assert.True(always.StandingKelvin > 2f,
                "a client permanently in the wrong shadow settled " + always.StandingKelvin
                + " K out, which is not a bias");

            Assert.True(always.StandingKelvin > always.PeakKelvin * 0.5f,
                "it peaked at " + always.PeakKelvin + " K and settled at " + always.StandingKelvin
                + " K, which is a perturbation rather than the bias this pins");
        }

        /// <summary>
        /// **A client's speed error is a bias, and it reaches the cubic term rather than the
        /// saturating one.**
        ///
        /// <para>
        /// Velocity is predicted on a client, and it sets the airflow over the hull — which feeds
        /// two terms of very different shape. Forced convection saturates, so a fifth more speed is
        /// a few per cent more cooling; aerodynamic friction goes as the *cube* of airspeed, so the
        /// same fifth is 1.7× the heating. A client that is guessing fast therefore runs hot rather
        /// than cold, and the error stands rather than decaying
        /// ([backlog.md](../../docs/backlog.md) `F19`).
        /// </para>
        /// </summary>
        [Fact]
        public void ASpeedErrorStandsAndOnlyOnAShipThatIsMoving()
        {
            ClientInputLab.Degradation how = new ClientInputLab.Degradation
            {
                Name = "speed error",
                SpeedErrorShare = 0.2f,
            };

            // **Two run lengths, because that is what tells a bias from a perturbation here.**
            // The peak cannot: this rig alternates its load every two minutes, so a peak carries a
            // transient the standing error does not. What a bias does is fail to shrink when the
            // run is longer, and what a perturbation does is shrink.
            ClientInputLab.Result shorter = Flying(how, 240f);
            ClientInputLab.Result longer = Flying(how, 480f);
            ClientInputLab.Result resting = Run(how);

            output.WriteLine("standing {0:n1} K at 240 s, {1:n1} K at 480 s; still and sunlit {2:n1} K",
                shorter.StandingKelvin, longer.StandingKelvin, resting.PeakKelvin);

            Assert.True(shorter.StandingKelvin > 1f,
                "a 20 % speed error settled " + shorter.StandingKelvin + " K out, which is too small"
                + " for the rest of this to be measuring anything");

            Assert.True(longer.StandingKelvin >= shorter.StandingKelvin * 0.95f,
                "the standing error fell from " + shorter.StandingKelvin + " K to "
                + longer.StandingKelvin + " K over twice the run, which is a perturbation rather"
                + " than the bias this pins");

            // And it is the airflow it reaches, not something else the knob touches by accident.
            Assert.Equal(0f, resting.PeakKelvin, 3);
        }

        /// <summary>
        /// The same run in the dark, at a chosen load period. A period longer than the run is a
        /// steady load, which is the only way to ask a capacity question of this rig.
        ///
        /// **Shadow rather than sunlit**, because `sunlit` turns the hull under the sun a quarter
        /// turn every five minutes: a steady *load* there is still a moving *environment*, and the
        /// comparison below would be measuring the sun.
        /// </summary>
        private static ClientInputLab.Result InTheDark(ClientInputLab.Degradation how, float period)
        {
            return ClientInputLab.Measure(how, ClientDriftLab.Correction.None,
                "shadow", 240f, Blocks, period);
        }

        /// <summary>
        /// **A mass error is an error in a rate and not in an equilibrium**, which is what makes it
        /// unlike every other input in the sweep.
        ///
        /// <para>
        /// Mass is heat capacity ([backlog.md](../../docs/backlog.md) `F20`), and capacity does not
        /// appear in the balance a hull settles at: the temperature where losses equal generation
        /// is set by area, emissivity, conductance and watts, and by nothing about how much metal
        /// is being heated. What capacity sets is *how long* the hull takes to get there. So a
        /// client whose masses are 20 % out is not heading somewhere else — it is heading to the
        /// same place at a different speed.
        /// </para>
        ///
        /// <para>
        /// Which means the shape of this input depends on the load rather than on the input. Under
        /// the moving load the sweep runs it stands, because the hull is always chasing; under a
        /// load that stops moving it decays, because there is nothing left to chase. Both runs are
        /// in the dark with the same degradation and differ only in the load script, so the
        /// difference cannot be anything else.
        /// </para>
        /// </summary>
        [Fact]
        public void AMassErrorStandsUnderAMovingLoadAndDecaysUnderASteadyOne()
        {
            ClientInputLab.Degradation how = new ClientInputLab.Degradation
            {
                Name = "mass error",
                MassErrorShare = 0.2f,
            };

            ClientInputLab.Result moving = InTheDark(how, ClientInputLab.LoadPeriodSeconds);
            ClientInputLab.Result steady = InTheDark(how, 1e9f);

            output.WriteLine("moving load: peak {0:n1} K, standing {1:n1} K. steady: peak {2:n1} K,"
                + " standing {3:n1} K", moving.PeakKelvin, moving.StandingKelvin,
                steady.PeakKelvin, steady.StandingKelvin);

            // It has to bite at all under both, or the comparison is of two zeroes (`E8`).
            Assert.True(steady.PeakKelvin > 1f,
                "a 20 % capacity error peaked at only " + steady.PeakKelvin + " K under a steady"
                + " load, so the knob reached nothing and this judges nothing");

            Assert.True(moving.StandingKelvin > 1f,
                "under the moving load it settled at " + moving.StandingKelvin + " K, which is too"
                + " small for the comparison below to be measuring anything");

            Assert.True(steady.StandingKelvin < steady.PeakKelvin * 0.5f,
                "under a steady load a capacity error should have decayed: peaked at "
                + steady.PeakKelvin + " K and settled at " + steady.StandingKelvin + " K");

            Assert.True(moving.StandingKelvin > steady.StandingKelvin,
                "a capacity error should stand while the load moves and not when it stops: "
                + moving.StandingKelvin + " K against " + steady.StandingKelvin + " K");
        }

        /// <summary>
        /// **A block believed switched off is wrong by all of its heat, and where that error lands
        /// is what makes it worse than the same watts spread thin.**
        ///
        /// <para>
        /// The comparison is at equal missing wattage: a tenth of the producers making nothing is
        /// the same total heat as every producer making a tenth less. What differs is only where
        /// the error sits — concentrated on a tenth of the blocks, or spread over all of them — and
        /// that is the whole claim, because the readout is a per-block one and the hull cannot
        /// conduct fast enough to average a missing thruster away
        /// ([backlog.md](../../docs/backlog.md) `F20`).
        /// </para>
        /// </summary>
        [Fact]
        public void ABlockBelievedOffIsWorseThanTheSameWattsSpreadOverTheHull()
        {
            const float Share = 0.1f;

            ClientInputLab.Result concentrated = Run(new ClientInputLab.Degradation
            {
                Name = "blocks off",
                BlocksOffShare = Share,
            });

            ClientInputLab.Result spread = Run(new ClientInputLab.Degradation
            {
                Name = "power short",
                PowerErrorShare = -Share,
            });

            output.WriteLine("concentrated: peak {0:n1} K, standing {1:n1} K. spread: peak {2:n1} K,"
                + " standing {3:n1} K", concentrated.PeakKelvin, concentrated.StandingKelvin,
                spread.PeakKelvin, spread.StandingKelvin);

            Assert.True(spread.StandingKelvin > 1f,
                "the same watts spread over the hull settled at " + spread.StandingKelvin
                + " K, which is too small for the ordering below to be measuring anything");

            Assert.True(concentrated.StandingKelvin > spread.StandingKelvin,
                "the same missing wattage concentrated on a tenth of the producers settled at "
                + concentrated.StandingKelvin + " K against " + spread.StandingKelvin
                + " K spread, so where the error lands is not what decides it");

            // **And it is a bias, read the way `F19`'s velocity had to be read.** The peak cannot
            // say so here: the load alternates every two minutes and a silenced producer is wrong
            // by *all* of its heat at the top of that wave, so the peak is mostly the load script.
            // What a bias does is fail to shrink when the run is longer.
            ClientInputLab.Result longer = ClientInputLab.Measure(
                new ClientInputLab.Degradation { Name = "blocks off", BlocksOffShare = Share },
                ClientDriftLab.Correction.None, "sunlit", 480f, Blocks);

            Assert.True(longer.StandingKelvin >= concentrated.StandingKelvin * 0.95f,
                "the standing error fell from " + concentrated.StandingKelvin + " K to "
                + longer.StandingKelvin + " K over twice the run, which is a perturbation rather"
                + " than the bias this pins");
        }

        /// <summary>
        /// **Room air is a binary input wearing the clothes of a continuous one, and the last one
        /// per cent of it is worth more than the first ninety-nine.**
        ///
        /// <para>
        /// Pressure is the game's answer rather than this model's (`C9`), and it reaches the
        /// simulation twice: it scales the air's heat capacity, and it decides whether the room has
        /// air *at all*. Only the second of those moves anything that lasts — the link conductance
        /// is `RoomConvectionCoefficient × area` and carries no pressure term
        /// (`RoomAirCouplingTests`), so a compartment at a fifth of an atmosphere couples its walls
        /// exactly as hard as a full one and differs only in inertia, which is the `mass error`
        /// finding arriving through a different input.
        /// </para>
        ///
        /// <para>
        /// So a client's disagreement about pressure is worth almost nothing until it crosses zero,
        /// and then it is worth the whole coupling ([backlog.md](../../docs/backlog.md) `F21`).
        /// </para>
        /// </summary>
        [Fact]
        public void TheLastOnePerCentOfARoomsAirIsWorthMoreThanTheFirstNinetyNine()
        {
            ClientInputLab.Result fifth = Run(new ClientInputLab.Degradation
            {
                Name = "room pressure",
                RoomPressureError = 0.2f,
            });

            ClientInputLab.Result nearlyAll = Run(new ClientInputLab.Degradation
            {
                Name = "room pressure 99 %",
                RoomPressureError = 0.99f,
            });

            ClientInputLab.Result gone = Run(new ClientInputLab.Degradation
            {
                Name = "air gone",
                RoomPressureError = 1f,
            });

            output.WriteLine("standing: a fifth out {0:n2} K, 99 % out {1:n2} K, all of it {2:n2} K",
                fifth.StandingKelvin, nearlyAll.StandingKelvin, gone.StandingKelvin);

            // The knob has to reach anything at all, or the ordering below is of three zeroes.
            Assert.True(gone.StandingKelvin > 1f,
                "a client that believes the compartments are empty settled only "
                + gone.StandingKelvin + " K out, so the knob reached nothing");

            // **Per point of pressure, which is the form of the claim.** The last one per cent is
            // worth 1.22 K and the first ninety-nine are worth 1.53 K between them, so a point of
            // pressure at the bottom of the range is about eighty times a point anywhere else.
            // Measured at 2.75 K against 1.53 K — 1.8x — where it was over four times before
            // `C24`: at four times the conduction pace a wall's neighbours carry more of it and
            // the room's coupling is a smaller share of what reaches that wall. The discontinuity
            // is smaller and it is still a discontinuity.
            float lastPoint = gone.StandingKelvin - nearlyAll.StandingKelvin;
            float perPointBelow = nearlyAll.StandingKelvin / 99f;

            Assert.True(lastPoint > 20f * perPointBelow,
                "the last one per cent of the air was worth " + lastPoint + " K against "
                + perPointBelow + " K a point for the first ninety-nine, which is not the"
                + " discontinuity this pins");

            Assert.True(gone.StandingKelvin > 1.5f * nearlyAll.StandingKelvin,
                "removing the last one per cent of the air settled the client "
                + gone.StandingKelvin + " K out against " + nearlyAll.StandingKelvin
                + " K for the first ninety-nine, which is not the discontinuity this pins");

            // And the graded part is nearly flat, which is what says the jump is the coupling
            // going rather than the capacity shrinking.
            Assert.True(nearlyAll.StandingKelvin < 5f * fifth.StandingKelvin,
                "a fifth of the air out settled at " + fifth.StandingKelvin + " K and 99 % out at "
                + nearlyAll.StandingKelvin + " K, so pressure is behaving as a graded input");
        }

        /// <summary>
        /// **A room map that has not landed is the loudest perturbation in the sweep, and it leaves
        /// nothing behind.**
        ///
        /// <para>
        /// The mapper publishes atomically, so a client mid-pass is not holding a rough map — it is
        /// holding the previous one, which on a grid it has just built is empty. Every interior face
        /// then has open air behind it (`RoomMap.IsExternal`), so the hull radiates from a skin 27 %
        /// larger than the server's and the compartments have no air to couple through. Then the
        /// pass lands and the client is simply right, which is what makes this a perturbation where
        /// `room pressure` is a bias ([backlog.md](../../docs/backlog.md) `F21`).
        /// </para>
        ///
        /// <para>
        /// Two run lengths rather than a peak-to-standing ratio, for the reason `F19` and `F20`
        /// both needed: the load alternates every two minutes, so a peak carries a transient that
        /// says nothing about decay. What a perturbation does is shrink when the run is longer.
        /// </para>
        /// </summary>
        [Fact]
        public void AnUnconvergedRoomMapPeaksHardAndThenLeavesNothing()
        {
            ClientInputLab.Degradation how = new ClientInputLab.Degradation
            {
                Name = "room map lag",
                RoomMapLagSeconds = 60f,
            };

            ClientInputLab.Result shorter = Run(how);
            ClientInputLab.Result longer = ClientInputLab.Measure(how,
                ClientDriftLab.Correction.None, "sunlit", 480f, Blocks);

            ClientInputLab.Result airGone = Run(new ClientInputLab.Degradation
            {
                Name = "air gone",
                RoomPressureError = 1f,
            });

            output.WriteLine("map lag: peak {0:n1} K, standing {1:n2} K at 240 s and {2:n2} K at"
                + " 480 s. air gone: peak {3:n1} K", shorter.PeakKelvin, shorter.StandingKelvin,
                longer.StandingKelvin, airGone.PeakKelvin);

            Assert.True(shorter.PeakKelvin > 20f,
                "a client with no room map peaked only " + shorter.PeakKelvin
                + " K out, so the knob reached nothing");

            // Louder than losing the air alone, because losing the map loses the air *and* opens
            // the interior. The decomposition is the point of running both.
            Assert.True(shorter.PeakKelvin > 2f * airGone.PeakKelvin,
                "an unmapped hull peaked at " + shorter.PeakKelvin + " K against "
                + airGone.PeakKelvin + " K for the air alone, so the skin is not the larger half");

            Assert.True(longer.StandingKelvin < shorter.StandingKelvin,
                "the standing error did not fall over twice the run — " + shorter.StandingKelvin
                + " K to " + longer.StandingKelvin + " K — which is a bias rather than the"
                + " perturbation this pins");

            Assert.True(longer.StandingKelvin < shorter.PeakKelvin * 0.05f,
                "it peaked at " + shorter.PeakKelvin + " K and was still " + longer.StandingKelvin
                + " K out at the end of a run twice as long, which is not a perturbation");
        }

        /// <summary>
        /// **The magnitude behind the map knob, pinned rather than asserted**: a hull whose room
        /// pass has not landed believes its own skin is a quarter larger than it is.
        ///
        /// <para>
        /// `RoomMap.IsExternal` answers *true* for any cell it has no room for, so on an empty map
        /// every interior face has open air behind it and radiates and convects to the sky. On the
        /// sweep's own 2,000-block census hull that is 17,762.5 m² against 14,012.5 m² — 26.8 %
        /// more — and it comes straight back when the pass lands, which is what makes the knob a
        /// perturbation rather than damage.
        /// </para>
        ///
        /// <para>
        /// The figure is here because the lab's `room map lag` row is worth exactly this and a
        /// number living only in a comment drifts. `RoomMapCompletionTests` records the same
        /// mechanism at the other end of the scale, where a pass that gave up left 95 % of a hull
        /// exposed against 34 %.
        /// </para>
        /// </summary>
        [Fact]
        public void AHullWithNoRoomMapBelievesItsSkinIsAQuarterLarger()
        {
            ThermalSimulation hull = Hulls.Driven(Hulls.Uncapped(), 2000);

            float mapped = ExposedArea(hull);
            Assert.True(hull.Rooms.Map.RoomCount > 0,
                "the census hull has no compartment, so there is no interior to lose");

            hull.Solver.RefreshExposure(new RoomMap());
            float unmapped = ExposedArea(hull);

            output.WriteLine("exposed area: {0:n1} m2 mapped, {1:n1} m2 unmapped, {2:n3}x",
                mapped, unmapped, unmapped / mapped);

            Assert.True(unmapped / mapped > 1.2f && unmapped / mapped < 1.35f,
                "an unmapped hull exposed " + (unmapped / mapped) + " times the area of a mapped"
                + " one, which is not the quarter this knob is worth");

            // And the pass landing puts it back exactly, which is the half that makes it decay.
            hull.Solver.RefreshExposure(hull.Rooms.Map);
            Assert.Equal(mapped, ExposedArea(hull), 3);
        }

        private static float ExposedArea(ThermalSimulation hull)
        {
            IList<ThermalNode> nodes = hull.Solver.Nodes;
            float total = 0f;
            for (int i = 0; i < nodes.Count; i++) total += nodes[i].ExposedArea;
            return total;
        }

        /// <summary>
        /// **And a map that never lands is the bound, which is a bias and the worst standing input
        /// in the sweep** — ahead of the wrong switch `F20` crowned.
        ///
        /// <para>
        /// It is the same reading `wrong shadow` needed: the shipped row is a client that is
        /// briefly wrong and recovers, and the bound is a client that never does. Here the bound is
        /// not hypothetical — `D2` measures the flood fill at 7,207 ticks on a million blocks, and
        /// a client that restarts its pass faster than it finishes one never publishes a map at
        /// all.
        /// </para>
        /// </summary>
        [Fact]
        public void ARoomMapThatNeverLandsOutSettlesEveryOtherInputInTheSweep()
        {
            ClientInputLab.Result never = Run(new ClientInputLab.Degradation
            {
                Name = "no room map",

                // Longer than any run this file makes, so the pass never lands.
                RoomMapLagSeconds = 1e9f,
            });

            ClientInputLab.Result switched = Run(new ClientInputLab.Degradation
            {
                Name = "blocks off",
                BlocksOffShare = 0.1f,
            });

            output.WriteLine("no map: peak {0:n1} K, standing {1:n2} K. blocks off: peak {2:n1} K,"
                + " standing {3:n2} K", never.PeakKelvin, never.StandingKelvin,
                switched.PeakKelvin, switched.StandingKelvin);

            // A bias: what it peaked at is roughly what it settles at.
            Assert.True(never.StandingKelvin > never.PeakKelvin * 0.5f,
                "a map that never lands peaked at " + never.PeakKelvin + " K and settled at "
                + never.StandingKelvin + " K, which is a perturbation rather than the bias this pins");

            Assert.True(never.StandingKelvin > switched.StandingKelvin,
                "a client with no room map settled " + never.StandingKelvin + " K out against "
                + switched.StandingKelvin + " K for a tenth of the producers switched off, so it"
                + " is not the worst standing input here");
        }

        /// <summary>
        /// **The same ship received in a different order differs only in the last bits**, and that
        /// is a claim about a design decision rather than about physics.
        ///
        /// <para>
        /// A node's index is its arrival order, and two machines have no reason to share one: a
        /// client takes blocks in whatever order the engine streams them, a server has them in the
        /// order they were welded or pasted. Every block here sits at the cell it sits at on the
        /// server and carries the model it carries there, so the conduction graph, the surfaces,
        /// the rooms and the physics are identical — only the index space differs.
        /// </para>
        ///
        /// <para>
        /// So the row reads zero to every instrument here, and it reads zero *through the
        /// correction* too, because the hot-tail packet is keyed on block position rather than on
        /// node index ([backlog.md](../../docs/backlog.md) `F22`). The permutation is checked
        /// first: a rig that quietly built both hulls the same way would report this as passing
        /// while measuring nothing (`E8`).
        /// </para>
        ///
        /// <para>
        /// **It is not bit-identical, and the reason is worth naming.** A node accumulates from its
        /// links, and float addition is not associative, so a permuted node order sums the same
        /// terms in a different order. Over four minutes that is **1.2e-4 K** — a thousandth of the
        /// significance window, and an eight-hundredth of one quantum of the wire this correction
        /// travels on. The order independence the threading work rests on is a claim about physics,
        /// not about bits, and this is where the difference between the two is measured.
        /// </para>
        /// </summary>
        [Fact]
        public void TheSameShipInADifferentOrderDiffersOnlyInTheLastBits()
        {
            ClientInputLab.Degradation how = new ClientInputLab.Degradation
            {
                Name = "build order",
                BuildOrderSeed = 20260824,
            };

            ClientInputLab.Result alone = Run(how);
            ClientInputLab.Result corrected = Run(how, new ClientDriftLab.Correction
            {
                IntervalSeconds = 5f,
                WholeHullOnJoin = true,
            });

            // The permutation is real, or the two zeroes below are the zeroes of two identical
            // hulls and this judges nothing.
            ThermalSimulation ordered = Hulls.Driven(Hulls.Uncapped(), Blocks);
            ThermalSimulation shuffled = Hulls.Driven(Hulls.Uncapped(), Blocks, how.BuildOrderSeed);

            int moved = 0;
            IList<ThermalNode> theirs = ordered.Solver.Nodes;
            for (int i = 0; i < theirs.Count; i++)
            {
                ThermalNode same = shuffled.Solver.GetNodeAt(theirs[i].Block.Position);
                Assert.NotNull(same);
                if (same.Index != theirs[i].Index) moved++;
            }

            output.WriteLine("{0:n0} of {1:n0} blocks changed index; peak {2:n3} K, corrected {3:n3} K",
                moved, theirs.Count, alone.PeakKelvin, corrected.PeakKelvin);

            Assert.True(moved > theirs.Count / 2,
                "only " + moved + " of " + theirs.Count + " blocks changed index, so the rig did"
                + " not really build the client's hull in a different order");

            // Smaller than one quantum of the wire the correction travels on, which is the
            // resolution below which nothing in this repository can tell two clients apart.
            Assert.True(alone.PeakKelvin < HotTailCodec.TemperatureStep,
                "the same ship in a different order disagreed by " + alone.PeakKelvin
                + " K, which is more than rounding: the two hulls are not the same ship");

            Assert.True(alone.StandingKelvin < HotTailCodec.TemperatureStep,
                "it settled " + alone.StandingKelvin + " K out, which is a difference rather than"
                + " an accumulation of rounding");

            Assert.Equal(0, alone.PeakDisagreeing);
            Assert.Equal(0, alone.PeakMissing);

            // And the packet lands on the right blocks: an index-keyed one would not, which the
            // test below measures.
            Assert.True(corrected.PeakKelvin < HotTailCodec.TemperatureStep,
                "the correction left the client " + corrected.PeakKelvin + " K out on a hull it"
                + " agreed with, so it is not landing on the blocks it names");

            Assert.Equal(0, corrected.PeakDisagreeing);
        }

        /// <summary>
        /// **What the position key is buying, measured against the alternative rather than
        /// asserted.**
        ///
        /// <para>
        /// `HotTailCodec` spends eight of its ten bytes a block on a position key, and the reason
        /// given is that node indices come from insertion order and two machines do not build a
        /// grid in the same order. The row above shows the packet landing correctly on a permuted
        /// hull; this shows what the cheaper key would have done to the same packet — every
        /// temperature written onto whichever block happens to hold that index on the client.
        /// </para>
        ///
        /// <para>
        /// It is the counterfactual and not a defect: nothing in the mod applies a tail by index.
        /// Without it, *the correction changed nothing* is equally consistent with the key being
        /// unnecessary ([backlog.md](../../docs/backlog.md) `F22`).
        /// </para>
        /// </summary>
        [Fact]
        public void AnIndexKeyedCorrectionWouldLandEveryTemperatureOnTheWrongBlock()
        {
            ThermalSimulation server = Hulls.Driven(Hulls.Uncapped(), Blocks);
            ThermalSimulation client = Hulls.Driven(Hulls.Uncapped(), Blocks, 20260824);

            // Two copies of one ship: the client takes the server's field by position, so before
            // any correction they agree exactly.
            IList<ThermalNode> mine = server.Solver.Nodes;
            for (int i = 0; i < mine.Count; i++)
            {
                client.Solver.GetNodeAt(mine[i].Block.Position).Temperature = mine[i].Temperature;
            }

            Assert.Equal(0f, WorstDisagreement(server, client), 4);

            // **The band is opened to the whole hull on purpose.** What is being measured is where
            // a record lands, not which records are chosen, and a hull that has not been run hot
            // has nothing inside the warning band to send.
            List<StoredTemperature> tail = new List<StoredTemperature>();
            server.ExportHotTail(float.MaxValue, 0, tail);
            Assert.True(tail.Count > 0, "the server sent nothing, so this judges nothing");

            // The same packet, keyed the cheap way: record n onto node n.
            IList<ThermalNode> theirs = client.Solver.Nodes;
            for (int i = 0; i < tail.Count && i < theirs.Count; i++)
            {
                theirs[i].Temperature = tail[i].Temperature;
            }

            float byIndex = WorstDisagreement(server, client);
            output.WriteLine("{0:n0} blocks in the band; an index-keyed apply leaves {1:n1} K",
                tail.Count, byIndex);

            Assert.True(byIndex > 100f,
                "an index-keyed correction left the client only " + byIndex + " K out on a permuted"
                + " hull, so the position key is not buying what the codec says it is");
        }

        private static float WorstDisagreement(ThermalSimulation server, ThermalSimulation client)
        {
            IList<ThermalNode> mine = server.Solver.Nodes;
            float worst = 0f;

            for (int i = 0; i < mine.Count; i++)
            {
                ThermalNode yours = client.Solver.GetNodeAt(mine[i].Block.Position);
                if (yours == null) continue;

                float difference = Math.Abs(mine[i].Temperature - yours.Temperature);
                if (difference > worst) worst = difference;
            }

            return worst;
        }

        /// <summary>
        /// **A block a client has not been told about is not a block it is wrong about, and the
        /// correction cannot reach one.**
        ///
        /// <para>
        /// Every other row in this sweep degrades a number both machines hold. A client still
        /// receiving a pasted blueprint, or one whose subgrid has not attached yet, holds *fewer
        /// numbers*: a different node set, a different conduction graph, and hull surfaces open to
        /// the sky where the missing blocks would have covered them. The hot-tail packet carries
        /// temperatures for blocks, and a block that is absent takes none of them — so the
        /// correction narrows what the client misreads about the hull it *has* and leaves the rest
        /// exactly where it was ([backlog.md](../../docs/backlog.md) `F22`).
        /// </para>
        /// </summary>
        [Fact]
        public void TheCorrectionNarrowsAPartialHullsReadoutAndCannotTouchWhatIsAbsent()
        {
            ClientInputLab.Degradation how = new ClientInputLab.Degradation
            {
                Name = "blocks missing",
                BlocksMissingShare = 0.1f,
            };

            ClientInputLab.Result alone = Run(how);
            ClientInputLab.Result corrected = Run(how, new ClientDriftLab.Correction
            {
                IntervalSeconds = 5f,
                WholeHullOnJoin = true,
            });

            output.WriteLine("absent {0:n0} blocks; standing {1:n2} K uncorrected, {2:n2} K"
                + " corrected; misread {3:n0} to {4:n0}", alone.PeakMissing, alone.StandingKelvin,
                corrected.StandingKelvin, alone.PeakDisagreeing, corrected.PeakDisagreeing);

            Assert.True(alone.PeakMissing > 0,
                "the rig took no blocks away, so nothing here judges a partial hull");

            Assert.Equal(alone.PeakMissing, corrected.PeakMissing);

            Assert.True(alone.StandingKelvin > 10f,
                "a hull a tenth short settled only " + alone.StandingKelvin + " K out, which is too"
                + " small for the rest of this to be measuring anything");

            Assert.True(corrected.PeakDisagreeing < alone.PeakDisagreeing,
                "the correction did not narrow what the client misreads about the blocks it has: "
                + corrected.PeakDisagreeing + " against " + alone.PeakDisagreeing);
        }

        /// <summary>
        /// **Blocks arriving late are a perturbation and blocks never arriving are a bias**, which
        /// is the same pair the room map turned out to be and for the same reason: one of them ends.
        ///
        /// <para>
        /// The late case is the loudest transient in the whole sweep, and most of the peak is not
        /// the missing hull at all — it is the *arrival*. A block that appears on a grid starts at
        /// the world's default temperature, because the simulation has no history for it and
        /// nothing tells it what its neighbours are holding, so a tenth of a hull at 1,000 K gains
        /// a tenth of itself at 293 K in one step. That is the engine's wrongness rather than the
        /// rig's, and it decays.
        /// </para>
        /// </summary>
        [Fact]
        public void BlocksArrivingLateAreAPerturbationAndBlocksNeverArrivingAreABias()
        {
            ClientInputLab.Result late = Run(new ClientInputLab.Degradation
            {
                Name = "blocks missing",
                BlocksMissingShare = 0.1f,
                BlocksMissingSeconds = 60f,
            });

            ClientInputLab.Result never = Run(new ClientInputLab.Degradation
            {
                Name = "blocks never arrive",
                BlocksMissingShare = 0.1f,
            });

            output.WriteLine("late: peak {0:n1} K, standing {1:n2} K. never: peak {2:n1} K,"
                + " standing {3:n2} K", late.PeakKelvin, late.StandingKelvin,
                never.PeakKelvin, never.StandingKelvin);

            Assert.True(late.PeakKelvin > never.PeakKelvin,
                "a hull that gets its blocks back peaked at " + late.PeakKelvin + " K against "
                + never.PeakKelvin + " K for one that never does, so the arrival is not the"
                + " transient this pins");

            Assert.True(late.StandingKelvin < late.PeakKelvin * 0.1f,
                "the late case peaked at " + late.PeakKelvin + " K and settled at "
                + late.StandingKelvin + " K, which is a bias rather than the perturbation this pins");

            Assert.True(never.StandingKelvin > never.PeakKelvin * 0.5f,
                "a hull that never gets its blocks peaked at " + never.PeakKelvin + " K and settled"
                + " at " + never.StandingKelvin + " K, which is a perturbation rather than a bias");
        }

        /// <summary>
        /// **A rate difference is worth exactly what the load is doing and nothing else.**
        ///
        /// <para>
        /// The mod advances one sixtieth of a simulated second per *simulation tick* rather than
        /// per real second, so a machine executing fewer ticks a second is a machine whose thermal
        /// clock runs slow — a server below 1.0 sim speed with a client at 1.0, or the reverse
        /// ([backlog.md](../../docs/backlog.md) `F23`). It is the one degradation here in which the
        /// client's inputs are all correct: it is not wrong about anything, it is *elsewhere on the
        /// same trajectory*.
        /// </para>
        ///
        /// <para>
        /// Which is why it disappears at equilibrium. Two hulls heading to the same settled
        /// temperature at different speeds agree once they arrive; two hulls chasing a load that
        /// keeps moving never do. Both runs below are in the dark with the same degradation and
        /// differ only in the load script, so the difference cannot be anything else — the same
        /// construction `mass error` needs, and for a related reason, since neither capacity nor
        /// clock appears in the balance a hull settles at.
        /// </para>
        /// </summary>
        [Fact]
        public void ARateDifferenceIsWorthWhatTheLoadIsDoingAndNothingElse()
        {
            ClientInputLab.Degradation how = new ClientInputLab.Degradation
            {
                Name = "slow clock",
                SimSpeedError = -0.1f,
            };

            ClientInputLab.Result moving = InTheDark(how, ClientInputLab.LoadPeriodSeconds);
            ClientInputLab.Result steady = InTheDark(how, 1e9f);

            output.WriteLine("moving load: peak {0:n1} K, standing {1:n2} K. steady: peak {2:n1} K,"
                + " standing {3:n2} K", moving.PeakKelvin, moving.StandingKelvin,
                steady.PeakKelvin, steady.StandingKelvin);

            Assert.True(moving.StandingKelvin > 5f,
                "a 10 % clock error settled only " + moving.StandingKelvin + " K out under a moving"
                + " load, which is too small for the comparison below to be measuring anything");

            Assert.True(steady.StandingKelvin < 0.1f,
                "under a load that stops moving a clock error should vanish; it settled at "
                + steady.StandingKelvin + " K");

            // And under the moving load it stands: twice the run does not shrink it, which is what
            // says the two hulls are apart rather than converging.
            ClientInputLab.Result longer = ClientInputLab.Measure(how,
                ClientDriftLab.Correction.None, "sunlit", 480f, Blocks);
            ClientInputLab.Result shorter = Run(how);

            output.WriteLine("sunlit: {0:n2} K at 240 s, {1:n2} K at 480 s",
                shorter.StandingKelvin, longer.StandingKelvin);

            Assert.True(longer.StandingKelvin > shorter.StandingKelvin * 0.6f,
                "the standing error fell from " + shorter.StandingKelvin + " K to "
                + longer.StandingKelvin + " K over twice the run, which is a perturbation rather"
                + " than the standing difference this pins");
        }

        /// <summary>
        /// **The same lost time in lumps peaks five times higher than as a slope**, and settles in
        /// much the same place — which is what says `hitching` and `slow clock` are one quantity in
        /// two shapes.
        ///
        /// <para>
        /// `hitching` loses five seconds of every thirty, which *is* five-sixths rate. The
        /// mechanism once written beside it was a solver backlog being dropped, and nothing in the
        /// mod does that: `ThermalGridScheduler` passes a constant frame length and
        /// `ThermalSimulation.Update` banks work credit against it, so the ceiling that would
        /// discard a backlog cannot bind at any legal `Frequency`. What a stalling machine does is
        /// run fewer simulation ticks ([backlog.md](../../docs/backlog.md) `F23`).
        /// </para>
        ///
        /// <para>
        /// So the pair is worth measuring rather than merging: the average deficit decides where a
        /// client settles, and how it is delivered decides how far wrong it gets on the way.
        /// </para>
        /// </summary>
        [Fact]
        public void TheSameLostTimeInLumpsPeaksFarHigherThanAsASlope()
        {
            // Five seconds of every thirty is a sixth of the client's time, however it arrives.
            const float Deficit = 1f / 6f;

            ClientInputLab.Result lumps = Run(new ClientInputLab.Degradation
            {
                Name = "hitching",
                HitchEverySeconds = 30f,
                HitchLosesSeconds = 5f,
            });

            ClientInputLab.Result slope = Run(new ClientInputLab.Degradation
            {
                Name = "slow clock",
                SimSpeedError = -Deficit,
            });

            output.WriteLine("lumps: peak {0:n1} K, standing {1:n2} K. slope: peak {2:n1} K,"
                + " standing {3:n2} K", lumps.PeakKelvin, lumps.StandingKelvin,
                slope.PeakKelvin, slope.StandingKelvin);

            Assert.True(slope.StandingKelvin > 5f,
                "the smooth case settled only " + slope.StandingKelvin + " K out, which is too"
                + " small for the comparison to be measuring anything");

            Assert.True(lumps.PeakKelvin > 3f * slope.PeakKelvin,
                "the same deficit in lumps peaked at " + lumps.PeakKelvin + " K against "
                + slope.PeakKelvin + " K as a slope, so how it is delivered is not what decides"
                + " the peak");

            Assert.True(lumps.StandingKelvin < 2f * slope.StandingKelvin,
                "the two settled " + lumps.StandingKelvin + " K and " + slope.StandingKelvin
                + " K apart, so they are not the same average deficit after all");
        }

        /// <summary>
        /// **A heat source the client never heard about is a bias, and it is the only input in the
        /// sweep that comes from outside this mod entirely.**
        ///
        /// <para>
        /// `ThermalHeatSources` is a registry another mod writes into through the API. A
        /// registration is a call made on whichever machine that mod runs its logic on, and nothing
        /// replicates it — so a client can be simulating a hull beside a furnace it does not know
        /// exists ([backlog.md](../../docs/backlog.md) `F23`). What reaches the solver is an
        /// `EnvironmentSample` with one fewer entry, and a missing steady watt is a standing error
        /// by construction.
        /// </para>
        /// </summary>
        [Fact]
        public void AHeatSourceTheClientNeverHeardAboutIsABias()
        {
            ClientInputLab.Result result = Run(new ClientInputLab.Degradation
            {
                Name = "missing source",
                MissingHeatSourceIrradiance = ClientInputLab.HeatSourceIrradiance,
            });

            output.WriteLine("peak {0:n2} K, standing {1:n2} K at {2:n0} W/m2",
                result.PeakKelvin, result.StandingKelvin, ClientInputLab.HeatSourceIrradiance);

            Assert.True(result.StandingKelvin > 0.1f,
                "a source worth " + ClientInputLab.HeatSourceIrradiance + " W/m2 that the client"
                + " never heard about settled it only " + result.StandingKelvin + " K out, so the"
                + " knob reached nothing");

            Assert.True(result.StandingKelvin > result.PeakKelvin * 0.5f,
                "it peaked at " + result.PeakKelvin + " K and settled at " + result.StandingKelvin
                + " K, which is a perturbation rather than the bias this pins");
        }

        /// <summary>
        /// **A hull with no sealed compartment is refused a room knob rather than reporting one as
        /// harmless.**
        ///
        /// <para>
        /// This is `E8` where it actually bit: the sweep's own test rig was 400 blocks, the census
        /// hull grows its first room somewhere between 500 and 600, and both room knobs would have
        /// come back at exactly zero — a client that recovered from a degradation it was never
        /// given. The guard is scoped to the two knobs that need a room, because every other row in
        /// the sweep is perfectly valid on a solid hull.
        /// </para>
        /// </summary>
        [Fact]
        public void ARoomKnobOnAHullWithNoCompartmentIsRefusedRatherThanReportedAsHarmless()
        {
            const int NoCompartment = 500;

            InvalidOperationException raised = Assert.Throws<InvalidOperationException>(() =>
                ClientInputLab.Measure(
                    new ClientInputLab.Degradation { Name = "room pressure", RoomPressureError = 1f },
                    ClientDriftLab.Correction.None, "sunlit", 30f, NoCompartment));

            Assert.Contains("measure nothing", raised.Message);

            // Scoped: the same hull is a fine rig for every knob that is not about a room.
            ClientInputLab.Result solid = ClientInputLab.Measure(
                new ClientInputLab.Degradation { Name = "power error", PowerErrorShare = 0.1f },
                ClientDriftLab.Correction.None, "sunlit", 30f, NoCompartment);

            Assert.Equal(0, solid.Rooms);

            // And the size this file runs at is on the other side of the threshold, which is what
            // makes every room row above a measurement rather than a zero.
            Assert.True(Run(new ClientInputLab.Degradation { Name = "none" }).Rooms > 0,
                "the suite's rig fell below the first compartment, so its room rows judge nothing");
        }

        /// <summary>
        /// **Mass is the only channel block condition has into the model**, which is why the sweep
        /// has a mass knob and not a separate damage one.
        ///
        /// <para>
        /// Two halves, and only the first is a claim about the mod. Nothing in `Data/Scripts`
        /// reads a build ratio or an integrity figure — pinned below, because a claim about what
        /// code does *not* do rots the moment somebody adds the line — and heat capacity is
        /// strictly proportional to the mass the adapter last polled. Whether the *game* moves
        /// that mass with build progress or damage is an engine question no harness can settle,
        /// and the two pages of this repository that touch it disagree
        /// ([backlog.md](../../docs/backlog.md) `F24`).
        /// </para>
        /// </summary>
        [Fact]
        public void MassIsTheOnlyChannelBlockConditionHasIntoTheModel()
        {
            BlockThermalProperties thermal = Catalog.DefaultThermal();
            BlockModel model = BlockModel.Solid("block", Vector3I.One, 1000f, thermal);
            BlockInstance block = new BlockInstance(model, Vector3I.Zero, BlockOrientation.Identity);

            ThermalNode node = new ThermalNode(block, 2.5f, 300f);
            float whole = node.ThermalMass;

            // A lighter block, however it got lighter: capacity follows mass and nothing else.
            block.Mass = model.Mass * 0.5f;
            node.RefreshThermalMass();

            Assert.Equal(whole * 0.5f, node.ThermalMass, 3);

            // And there is no second channel. `IMySlimBlock.Mass` is what the mass rota polls; a
            // build ratio or an integrity figure appearing anywhere in the mod would be an input
            // the sweep has no knob for and this file claims does not exist.
            string root = ShippedBlocks.RepoRoot();
            string scripts = Path.Combine(root, "Data", "Scripts", "Thermodynamics");
            string[] routes = { "BuildLevelRatio", "BuildIntegrity", "CurrentDamage", "MaxIntegrity" };
            List<string> offenders = new List<string>();

            foreach (string file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                foreach (string route in routes)
                {
                    if (text.Contains(route)) offenders.Add(route + " in " + Path.GetFileName(file));
                }
            }

            Assert.True(offenders.Count == 0,
                "block condition now reaches the model by a route the sweep has no knob for: "
                + string.Join(", ", offenders));
        }
    }
}
