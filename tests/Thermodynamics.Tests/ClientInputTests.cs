using System.Collections.Generic;
using Thermodynamics.Harness;
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

        private static ClientInputLab.Result Run(ClientInputLab.Degradation how,
            ClientDriftLab.Correction fix = null)
        {
            return ClientInputLab.Measure(how, fix ?? ClientDriftLab.Correction.None,
                "sunlit", 240f, 400);
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
                Assert.True(everything.PowerLagSeconds >= one.PowerLagSeconds, one.Name + ": power lag");
                Assert.True(everything.PowerErrorShare >= one.PowerErrorShare, one.Name + ": power error");
                Assert.True(everything.AirDensityError >= one.AirDensityError, one.Name + ": air");
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
                ClientDriftLab.Correction.None, "planet", 120f, 400);

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
            const float Error = 0.05f;

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

        /// <summary>The same run, on a hull that is flying rather than sitting.</summary>
        private static ClientInputLab.Result Flying(ClientInputLab.Degradation how)
        {
            return ClientInputLab.Measure(how, ClientDriftLab.Correction.None, "burn", 240f, 400);
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
    }
}
