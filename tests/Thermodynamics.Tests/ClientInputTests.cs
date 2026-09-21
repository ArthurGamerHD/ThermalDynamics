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
    [Trait("speed", "slow")]
    public class ClientInputTests
    {
        private readonly ITestOutputHelper output;

/// <summary>ClientInputTests operation.</summary>
        public ClientInputTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const int Blocks = ClientInputLab.SmallestHullWithACompartment;

        private const float Seconds = 480f;

/// <summary>Run operation.</summary>
        private static ClientInputLab.Result Run(ClientInputLab.Degradation how,
            ClientDriftLab.Correction fix = null)
        {
            return ClientInputLab.Measure(how, fix ?? ClientDriftLab.Correction.None,
                "sunlit", Seconds, Blocks);
        }

        [Fact]
/// <summary>AnUndegradedClientAgreesExactly operation.</summary>
        public void AnUndegradedClientAgreesExactly()
        {
/// <summary>Run operation.</summary>
            ClientInputLab.Result result = Run(new ClientInputLab.Degradation { Name = "none" });

            Assert.Equal(0f, result.PeakKelvin, 4);
            Assert.Equal(0f, result.StandingKelvin, 4);
            Assert.Equal(0f, result.SecondsMisreading, 4);
            Assert.Equal(0, result.PeakDisagreeing);

            Assert.True(result.Rooms > 0,
                "the rig has no sealed compartment, so its two room rows judge nothing");
        }

        [Fact]
/// <summary>APerturbationDecaysAndABiasDoesNot operation.</summary>
        public void APerturbationDecaysAndABiasDoesNot()
        {
/// <summary>Run operation.</summary>
            ClientInputLab.Result perturbed = Run(new ClientInputLab.Degradation
            {
                Name = "stale join",
                StaleSeconds = 60f,
            });

/// <summary>Run operation.</summary>
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

            Assert.True(biased.StandingKelvin > 5f * perturbed.StandingKelvin,
                "a wrong input should settle far further out than a wrong state that decayed: "
                + biased.StandingKelvin + " K against " + perturbed.StandingKelvin + " K");
        }

        [Fact]
/// <summary>ABiggerInputErrorIsABiggerStandingError operation.</summary>
        public void ABiggerInputErrorIsABiggerStandingError()
        {
/// <summary>Run operation.</summary>
            ClientInputLab.Result small = Run(new ClientInputLab.Degradation
            {
                Name = "power error 2%",
                PowerErrorShare = 0.02f,
            });

/// <summary>Run operation.</summary>
            ClientInputLab.Result large = Run(new ClientInputLab.Degradation
            {
                Name = "power error 10%",
                PowerErrorShare = 0.10f,
            });

            Assert.True(large.StandingKelvin > small.StandingKelvin,
                "ten per cent out should settle further from the server than two: "
                + large.StandingKelvin + " K against " + small.StandingKelvin + " K");
        }

        [Fact]
/// <summary>TheScriptedLoadActuallyChanges operation.</summary>
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

        [Fact]
/// <summary>TheCombinedCaseIsTheUnionOfEveryOtherCase operation.</summary>
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

        [Fact]
/// <summary>EveryCaseSaysWhatItDegradesAndWhy operation.</summary>
        public void EveryCaseSaysWhatItDegradesAndWhy()
        {
            foreach (ClientInputLab.Degradation one in ClientInputLab.All())
            {
                Assert.False(string.IsNullOrEmpty(one.Name));
                Assert.False(string.IsNullOrEmpty(one.Because),
                    one.Name + " does not say why it is a thing that happens");
            }
        }

        [Fact]
/// <summary>ARunWithNothingHotSaysSoRatherThanReportingAgreement operation.</summary>
        public void ARunWithNothingHotSaysSoRatherThanReportingAgreement()
        {
            ClientInputLab.Result cold = ClientInputLab.Measure(
                new ClientInputLab.Degradation { Name = "none" },
                ClientDriftLab.Correction.None, "planet", 120f, Blocks);

/// <summary>Run operation.</summary>
            ClientInputLab.Result hot = Run(new ClientInputLab.Degradation
            {
                Name = "stale join",
                StaleSeconds = 60f,
            });

            Assert.Equal(0, cold.PeakServerCritical);
            Assert.True(hot.PeakServerCritical > 0,
                "the sunlit rig is supposed to put blocks past critical; it did not");
        }

        [Fact]
/// <summary>TheCorrectionNarrowsWhatABiasedClientIsWrongAbout operation.</summary>
        public void TheCorrectionNarrowsWhatABiasedClientIsWrongAbout()
        {
            ClientInputLab.Degradation biased = new ClientInputLab.Degradation
            {
                Name = "power error",
                PowerErrorShare = 0.10f,
            };

/// <summary>Run operation.</summary>
            ClientInputLab.Result alone = Run(biased);
/// <summary>Run operation.</summary>
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

        [Fact]
/// <summary>ThrustBiasesAClientMoreThanPowerDoesAtTheSameError operation.</summary>
        public void ThrustBiasesAClientMoreThanPowerDoesAtTheSameError()
        {
            const float Error = 0.10f;

/// <summary>Flying operation.</summary>
            ClientInputLab.Result thrust = Flying(new ClientInputLab.Degradation
            {
                Name = "thrust error",
                ThrustErrorShare = Error,
            });

/// <summary>Flying operation.</summary>
            ClientInputLab.Result power = Flying(new ClientInputLab.Degradation
            {
                Name = "power error",
                PowerErrorShare = Error,
            });

            output.WriteLine("at {0:P0}: thrust {1:n1} K standing, power {2:n1} K",
                Error, thrust.StandingKelvin, power.StandingKelvin);

            Assert.True(power.StandingKelvin > 1f,
                "power at " + Error + " settled at " + power.StandingKelvin + " K, so the rig is"
                + " not one where an input error matters");

            Assert.True(thrust.StandingKelvin > power.StandingKelvin,
                "thrust settled at " + thrust.StandingKelvin + " K against power's "
                + power.StandingKelvin + " K at the same error, so thrust is not the worse input");
        }

        [Fact]
/// <summary>AThrustErrorSettlesRatherThanDecayingAndOnlyOnAShipUnderWay operation.</summary>
        public void AThrustErrorSettlesRatherThanDecayingAndOnlyOnAShipUnderWay()
        {
            ClientInputLab.Degradation how = new ClientInputLab.Degradation
            {
                Name = "thrust error",
                ThrustErrorShare = 0.2f,
            };

/// <summary>Flying operation.</summary>
            ClientInputLab.Result flying = Flying(how);
/// <summary>Run operation.</summary>
            ClientInputLab.Result resting = Run(how);

            output.WriteLine("flying: peak {0:n1} K, standing {1:n1} K. at rest: peak {2:n1} K",
                flying.PeakKelvin, flying.StandingKelvin, resting.PeakKelvin);

            Assert.True(flying.StandingKelvin > 1f,
                "a flying hull showed " + flying.StandingKelvin + " K, so the knob reached nothing");

            Assert.True(flying.StandingKelvin > flying.PeakKelvin * 0.8f,
                "the error peaked at " + flying.PeakKelvin + " K and settled at "
                + flying.StandingKelvin + " K, which is a perturbation rather than a bias");

            Assert.Equal(0f, resting.PeakKelvin, 3);
        }

        [Fact]
/// <summary>MissingTheWeatherIsABiasAndTheLargestOne operation.</summary>
        public void MissingTheWeatherIsABiasAndTheLargestOne()
        {
/// <summary>OnAPlanet operation.</summary>
            ClientInputLab.Result weather = OnAPlanet(new ClientInputLab.Degradation
            {
                Name = "wrong weather",
                MissesWeather = true,
            });

/// <summary>OnAPlanet operation.</summary>
            ClientInputLab.Result air = OnAPlanet(new ClientInputLab.Degradation
            {
                Name = "thinner air",
                AirDensityError = 0.2f,
            });

            output.WriteLine("weather: peak {0:n1} K, standing {1:n1} K. air: standing {2:n1} K",
                weather.PeakKelvin, weather.StandingKelvin, air.StandingKelvin);

            Assert.True(weather.StandingKelvin > 5f,
                "missing the weather showed " + weather.StandingKelvin
                + " K, so the knob reached nothing");

/// <summary>OnAPlanet operation.</summary>
            ClientInputLab.Result perturbation = OnAPlanet(new ClientInputLab.Degradation
            {
                Name = "stale join",
                StaleSeconds = 60f,
            });

            output.WriteLine("stale join, the perturbation: peak {0:n1} K, standing {1:n2} K",
                perturbation.PeakKelvin, perturbation.StandingKelvin);

            Assert.True(weather.StandingKelvin > perturbation.StandingKelvin * 20f,
                "missing the weather settled at " + weather.StandingKelvin + " K against a stale"
                + " join's " + perturbation.StandingKelvin + " K, so it is not clearly a bias");

            Assert.True(weather.StandingKelvin > air.StandingKelvin,
/// <summary>weather operation.</summary>
                "missing the weather (" + weather.StandingKelvin + " K) is no worse than a 20 %"
/// <summary>error operation.</summary>
                + " air density error (" + air.StandingKelvin + " K), so it says nothing new");
        }

        [Fact]
/// <summary>APositionLagCostsNothingLevelAndALapseRateOnADescent operation.</summary>
        public void APositionLagCostsNothingLevelAndALapseRateOnADescent()
        {
            ClientInputLab.Degradation how = new ClientInputLab.Degradation
            {
                Name = "position lag",
                PositionLagSeconds = 5f,
            };

/// <summary>Descending operation.</summary>
            ClientInputLab.Result descending = Descending(how);
/// <summary>Run operation.</summary>
            ClientInputLab.Result level = Run(how);

            output.WriteLine("descending: peak {0:n2} K, standing {1:n2} K. level: peak {2:n2} K",
                descending.PeakKelvin, descending.StandingKelvin, level.PeakKelvin);

            Assert.Equal(0f, level.PeakKelvin, 3);

            Assert.True(descending.PeakKelvin > 0f,
                "a descending hull showed exactly " + descending.PeakKelvin
                + " K at its worst, so the knob reached nothing");

            Assert.True(descending.StandingKelvin < 1f,
                "position lag settled at " + descending.StandingKelvin + " K, which would make it a"
                + " bias — the sweep and this test both read it as a perturbation");
        }

/// <summary>Descending operation.</summary>
        private static ClientInputLab.Result Descending(ClientInputLab.Degradation how,
            float seconds = Seconds)
        {
            return ClientInputLab.Measure(how, ClientDriftLab.Correction.None, "descent", seconds, Blocks);
        }

/// <summary>OnAPlanet operation.</summary>
        private static ClientInputLab.Result OnAPlanet(ClientInputLab.Degradation how,
            float seconds = Seconds)
        {
            return ClientInputLab.Measure(how, ClientDriftLab.Correction.None, "planet", seconds, Blocks);
        }

        [Fact]
/// <summary>ACaseThatCannotActOnThisScenarioIsSaidRatherThanScored operation.</summary>
        public void ACaseThatCannotActOnThisScenarioIsSaidRatherThanScored()
        {
            ClientInputLab.Degradation how = new ClientInputLab.Degradation
            {
                Name = "position lag",
                Scenarios = new[] { "descent" },
                PositionLagSeconds = 5f,
            };

/// <summary>Run operation.</summary>
            ClientInputLab.Result wrong = Run(how);          // on "sunlit"
/// <summary>Descending operation.</summary>
            ClientInputLab.Result right = Descending(how);

            Assert.True(wrong.NotExercised,
                "a case naming descent was run on sunlit and did not say so");
            Assert.False(right.NotExercised);

            string report = ClientInputLab.Report(new List<ClientInputLab.Result> { wrong, right });
            Assert.Contains("not exercised on sunlit", report);
            Assert.Contains("needs descent", report);

            Assert.Contains(right.StandingKelvin.ToString("n2"), report);
        }

/// <summary>Flying operation.</summary>
        private static ClientInputLab.Result Flying(ClientInputLab.Degradation how,
            float seconds = Seconds)
        {
            return ClientInputLab.Measure(how, ClientDriftLab.Correction.None, "burn", seconds, Blocks);
        }

        [Fact]
/// <summary>AWrongShadowPeaksHardAndThenDecays operation.</summary>
        public void AWrongShadowPeaksHardAndThenDecays()
        {
/// <summary>Run operation.</summary>
            ClientInputLab.Result result = Run(new ClientInputLab.Degradation
            {
                Name = "wrong shadow",
                OcclusionWrongEverySeconds = 30f,
                OcclusionWrongForSeconds = 3f,
            });

            output.WriteLine("intermittent: peak {0:n1} K, standing {1:n1} K",
                result.PeakKelvin, result.StandingKelvin);

            Assert.True(result.PeakKelvin > 2f,
                "a wrong shadow peaked at only " + result.PeakKelvin + " K, so the knob reached"
                + " nothing and this judges nothing");

            Assert.True(result.StandingKelvin < result.PeakKelvin * 0.5f,
                "it peaked at " + result.PeakKelvin + " K and settled at " + result.StandingKelvin
                + " K, which is a bias rather than the perturbation this pins");
        }

        [Fact]
/// <summary>AClientPermanentlyInTheWrongShadowSettlesThereInstead operation.</summary>
        public void AClientPermanentlyInTheWrongShadowSettlesThereInstead()
        {
/// <summary>Run operation.</summary>
            ClientInputLab.Result always = Run(new ClientInputLab.Degradation
            {
                Name = "always in shade",

                OcclusionWrongEverySeconds = 1f,
                OcclusionWrongForSeconds = 1f,
            });

            ClientInputLab.Result longer = ClientInputLab.Measure(
                new ClientInputLab.Degradation
                {
                    Name = "always in shade",
                    OcclusionWrongEverySeconds = 1f,
                    OcclusionWrongForSeconds = 1f,
                },
                ClientDriftLab.Correction.None, "sunlit", Seconds * 2f, Blocks);

            output.WriteLine("permanent: peak {0:n1} K, standing {1:n1} K; twice the run {2:n1} K",
                always.PeakKelvin, always.StandingKelvin, longer.StandingKelvin);

            Assert.True(always.StandingKelvin > 2f,
                "a client permanently in the wrong shadow settled " + always.StandingKelvin
                + " K out, which is not a bias");

            Assert.True(longer.StandingKelvin > always.StandingKelvin * 0.9f,
                "it settled " + always.StandingKelvin + " K out and " + longer.StandingKelvin
                + " K out over twice the run, so it is decaying rather than standing");
        }

        [Fact]
/// <summary>ASpeedErrorStandsAndOnlyOnAShipThatIsMoving operation.</summary>
        public void ASpeedErrorStandsAndOnlyOnAShipThatIsMoving()
        {
            ClientInputLab.Degradation how = new ClientInputLab.Degradation
            {
                Name = "speed error",
                SpeedErrorShare = 0.2f,
            };

/// <summary>Flying operation.</summary>
            ClientInputLab.Result shorter = Flying(how, 240f);
/// <summary>Flying operation.</summary>
            ClientInputLab.Result longer = Flying(how, 480f);
/// <summary>Run operation.</summary>
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

            Assert.Equal(0f, resting.PeakKelvin, 3);
        }

/// <summary>InTheDark operation.</summary>
        private static ClientInputLab.Result InTheDark(ClientInputLab.Degradation how, float period)
        {
            return ClientInputLab.Measure(how, ClientDriftLab.Correction.None,
                "shadow", Seconds, Blocks, period);
        }

        [Fact]
/// <summary>AMassErrorStandsUnderAMovingLoadAndDecaysUnderASteadyOne operation.</summary>
        public void AMassErrorStandsUnderAMovingLoadAndDecaysUnderASteadyOne()
        {
            ClientInputLab.Degradation how = new ClientInputLab.Degradation
            {
                Name = "mass error",
                MassErrorShare = 0.2f,
            };

/// <summary>InTheDark operation.</summary>
            ClientInputLab.Result moving = InTheDark(how, ClientInputLab.LoadPeriodSeconds);
/// <summary>InTheDark operation.</summary>
            ClientInputLab.Result steady = InTheDark(how, 1e9f);

            output.WriteLine("moving load: peak {0:n1} K, standing {1:n1} K. steady: peak {2:n1} K,"
                + " standing {3:n1} K", moving.PeakKelvin, moving.StandingKelvin,
                steady.PeakKelvin, steady.StandingKelvin);

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

        [Fact]
/// <summary>ABlockBelievedOffIsWorseThanTheSameWattsSpreadOverTheHull operation.</summary>
        public void ABlockBelievedOffIsWorseThanTheSameWattsSpreadOverTheHull()
        {
            const float Share = 0.1f;

/// <summary>Run operation.</summary>
            ClientInputLab.Result concentrated = Run(new ClientInputLab.Degradation
            {
                Name = "blocks off",
                BlocksOffShare = Share,
            });

/// <summary>Run operation.</summary>
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

            ClientInputLab.Result longer = ClientInputLab.Measure(
                new ClientInputLab.Degradation { Name = "blocks off", BlocksOffShare = Share },
                ClientDriftLab.Correction.None, "sunlit", 480f, Blocks);

            Assert.True(longer.StandingKelvin >= concentrated.StandingKelvin * 0.95f,
                "the standing error fell from " + concentrated.StandingKelvin + " K to "
                + longer.StandingKelvin + " K over twice the run, which is a perturbation rather"
                + " than the bias this pins");
        }

        [Fact]
/// <summary>TheLastOnePerCentOfARoomsAirIsWorthMoreThanTheFirstNinetyNine operation.</summary>
        public void TheLastOnePerCentOfARoomsAirIsWorthMoreThanTheFirstNinetyNine()
        {
/// <summary>Run operation.</summary>
            ClientInputLab.Result fifth = Run(new ClientInputLab.Degradation
            {
                Name = "room pressure",
                RoomPressureError = 0.2f,
            });

/// <summary>Run operation.</summary>
            ClientInputLab.Result nearlyAll = Run(new ClientInputLab.Degradation
            {
                Name = "room pressure 99 %",
                RoomPressureError = 0.99f,
            });

/// <summary>Run operation.</summary>
            ClientInputLab.Result gone = Run(new ClientInputLab.Degradation
            {
                Name = "air gone",
                RoomPressureError = 1f,
            });

            output.WriteLine("standing: a fifth out {0:n2} K, 99 % out {1:n2} K, all of it {2:n2} K",
                fifth.StandingKelvin, nearlyAll.StandingKelvin, gone.StandingKelvin);

            Assert.True(gone.StandingKelvin > 1f,
                "a client that believes the compartments are empty settled only "
                + gone.StandingKelvin + " K out, so the knob reached nothing");

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

            Assert.True(nearlyAll.StandingKelvin < 5f * fifth.StandingKelvin,
                "a fifth of the air out settled at " + fifth.StandingKelvin + " K and 99 % out at "
                + nearlyAll.StandingKelvin + " K, so pressure is behaving as a graded input");
        }

        [Fact]
/// <summary>AnUnconvergedRoomMapPeaksHardAndThenLeavesNothing operation.</summary>
        public void AnUnconvergedRoomMapPeaksHardAndThenLeavesNothing()
        {
            ClientInputLab.Degradation how = new ClientInputLab.Degradation
            {
                Name = "room map lag",
                RoomMapLagSeconds = 60f,
            };

/// <summary>Run operation.</summary>
            ClientInputLab.Result shorter = Run(how);
            ClientInputLab.Result longer = ClientInputLab.Measure(how,
                ClientDriftLab.Correction.None, "sunlit", 480f, Blocks);

/// <summary>Run operation.</summary>
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

            Assert.True(shorter.PeakKelvin > 2f * airGone.PeakKelvin,
                "an unmapped hull peaked at " + shorter.PeakKelvin + " K against "
                + airGone.PeakKelvin + " K for the air alone, so the skin is not the larger half");

            Assert.True(longer.StandingKelvin <= shorter.StandingKelvin + 0.01f,
                "the standing error rose over twice the run — " + shorter.StandingKelvin
                + " K to " + longer.StandingKelvin + " K — which is a bias rather than the"
                + " perturbation this pins");

            Assert.True(longer.StandingKelvin < shorter.PeakKelvin * 0.05f,
                "it peaked at " + shorter.PeakKelvin + " K and was still " + longer.StandingKelvin
                + " K out at the end of a run twice as long, which is not a perturbation");
        }

        [Fact]
/// <summary>AHullWithNoRoomMapBelievesItsSkinIsAQuarterLarger operation.</summary>
        public void AHullWithNoRoomMapBelievesItsSkinIsAQuarterLarger()
        {
            ThermalSimulation hull = Hulls.Driven(Hulls.Uncapped(), 2000);

/// <summary>ExposedArea operation.</summary>
            float mapped = ExposedArea(hull);
            Assert.True(hull.Rooms.Map.RoomCount > 0,
                "the census hull has no compartment, so there is no interior to lose");

            hull.Solver.RefreshExposure(new RoomMap());
/// <summary>ExposedArea operation.</summary>
            float unmapped = ExposedArea(hull);

            output.WriteLine("exposed area: {0:n1} m2 mapped, {1:n1} m2 unmapped, {2:n3}x",
                mapped, unmapped, unmapped / mapped);

            Assert.True(unmapped / mapped > 1.2f && unmapped / mapped < 1.35f,
                "an unmapped hull exposed " + (unmapped / mapped) + " times the area of a mapped"
                + " one, which is not the quarter this knob is worth");

            hull.Solver.RefreshExposure(hull.Rooms.Map);
            Assert.Equal(mapped, ExposedArea(hull), 3);
        }

/// <summary>ExposedArea operation.</summary>
        private static float ExposedArea(ThermalSimulation hull)
        {
            IList<ThermalNode> nodes = hull.Solver.Nodes;
            float total = 0f;
            for (int i = 0; i < nodes.Count; i++) total += nodes[i].ExposedArea;
            return total;
        }

        [Fact]
/// <summary>ARoomMapThatNeverLandsOutSettlesEveryOtherInputInTheSweep operation.</summary>
        public void ARoomMapThatNeverLandsOutSettlesEveryOtherInputInTheSweep()
        {
/// <summary>Run operation.</summary>
            ClientInputLab.Result never = Run(new ClientInputLab.Degradation
            {
                Name = "no room map",

                RoomMapLagSeconds = 1e9f,
            });

/// <summary>Run operation.</summary>
            ClientInputLab.Result switched = Run(new ClientInputLab.Degradation
            {
                Name = "blocks off",
                BlocksOffShare = 0.1f,
            });

            output.WriteLine("no map: peak {0:n1} K, standing {1:n2} K. blocks off: peak {2:n1} K,"
                + " standing {3:n2} K", never.PeakKelvin, never.StandingKelvin,
                switched.PeakKelvin, switched.StandingKelvin);

            Assert.True(never.StandingKelvin > never.PeakKelvin * 0.5f,
                "a map that never lands peaked at " + never.PeakKelvin + " K and settled at "
                + never.StandingKelvin + " K, which is a perturbation rather than the bias this pins");

            Assert.True(never.StandingKelvin > switched.StandingKelvin,
                "a client with no room map settled " + never.StandingKelvin + " K out against "
                + switched.StandingKelvin + " K for a tenth of the producers switched off, so it"
                + " is not the worst standing input here");
        }

        [Fact]
/// <summary>TheSameShipInADifferentOrderDiffersOnlyInTheLastBits operation.</summary>
        public void TheSameShipInADifferentOrderDiffersOnlyInTheLastBits()
        {
            ClientInputLab.Degradation how = new ClientInputLab.Degradation
            {
                Name = "build order",
                BuildOrderSeed = 20260824,
            };

/// <summary>Run operation.</summary>
            ClientInputLab.Result alone = Run(how);
/// <summary>Run operation.</summary>
            ClientInputLab.Result corrected = Run(how, new ClientDriftLab.Correction
            {
                IntervalSeconds = 5f,
                WholeHullOnJoin = true,
            });

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

            Assert.True(alone.PeakKelvin < HotTailCodec.TemperatureStep,
                "the same ship in a different order disagreed by " + alone.PeakKelvin
                + " K, which is more than rounding: the two hulls are not the same ship");

            Assert.True(alone.StandingKelvin < HotTailCodec.TemperatureStep,
                "it settled " + alone.StandingKelvin + " K out, which is a difference rather than"
                + " an accumulation of rounding");

            Assert.Equal(0, alone.PeakDisagreeing);
            Assert.Equal(0, alone.PeakMissing);

            Assert.True(corrected.PeakKelvin < HotTailCodec.TemperatureStep,
                "the correction left the client " + corrected.PeakKelvin + " K out on a hull it"
                + " agreed with, so it is not landing on the blocks it names");

            Assert.Equal(0, corrected.PeakDisagreeing);
        }

        [Fact]
/// <summary>AnIndexKeyedCorrectionWouldLandEveryTemperatureOnTheWrongBlock operation.</summary>
        public void AnIndexKeyedCorrectionWouldLandEveryTemperatureOnTheWrongBlock()
        {
            ThermalSimulation server = Hulls.Driven(Hulls.Uncapped(), Blocks);
            ThermalSimulation client = Hulls.Driven(Hulls.Uncapped(), Blocks, 20260824);

            IList<ThermalNode> mine = server.Solver.Nodes;
            for (int i = 0; i < mine.Count; i++)
            {
                client.Solver.GetNodeAt(mine[i].Block.Position).Temperature = mine[i].Temperature;
            }

            Assert.Equal(0f, WorstDisagreement(server, client), 4);

/// <summary>List operation.</summary>
            List<StoredTemperature> tail = new List<StoredTemperature>();
            server.ExportHotTail(float.MaxValue, 0, tail);
            Assert.True(tail.Count > 0, "the server sent nothing, so this judges nothing");

            IList<ThermalNode> theirs = client.Solver.Nodes;
            for (int i = 0; i < tail.Count && i < theirs.Count; i++)
            {
                theirs[i].Temperature = tail[i].Temperature;
            }

/// <summary>WorstDisagreement operation.</summary>
            float byIndex = WorstDisagreement(server, client);
            output.WriteLine("{0:n0} blocks in the band; an index-keyed apply leaves {1:n1} K",
                tail.Count, byIndex);

            Assert.True(byIndex > 100f,
                "an index-keyed correction left the client only " + byIndex + " K out on a permuted"
                + " hull, so the position key is not buying what the codec says it is");
        }

/// <summary>WorstDisagreement operation.</summary>
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

        [Fact]
/// <summary>TheCorrectionNarrowsAPartialHullsReadoutAndCannotTouchWhatIsAbsent operation.</summary>
        public void TheCorrectionNarrowsAPartialHullsReadoutAndCannotTouchWhatIsAbsent()
        {
            ClientInputLab.Degradation how = new ClientInputLab.Degradation
            {
                Name = "blocks missing",
                BlocksMissingShare = 0.1f,
            };

/// <summary>Run operation.</summary>
            ClientInputLab.Result alone = Run(how);
/// <summary>Run operation.</summary>
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

        [Fact]
/// <summary>BlocksArrivingLateAreAPerturbationAndBlocksNeverArrivingAreABias operation.</summary>
        public void BlocksArrivingLateAreAPerturbationAndBlocksNeverArrivingAreABias()
        {
/// <summary>Run operation.</summary>
            ClientInputLab.Result late = Run(new ClientInputLab.Degradation
            {
                Name = "blocks missing",
                BlocksMissingShare = 0.1f,
                BlocksMissingSeconds = 60f,
            });

/// <summary>Run operation.</summary>
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

        [Fact]
/// <summary>ARateDifferenceIsWorthWhatTheLoadIsDoingAndNothingElse operation.</summary>
        public void ARateDifferenceIsWorthWhatTheLoadIsDoingAndNothingElse()
        {
            ClientInputLab.Degradation how = new ClientInputLab.Degradation
            {
                Name = "slow clock",
                SimSpeedError = -0.1f,
            };

/// <summary>InTheDark operation.</summary>
            ClientInputLab.Result moving = InTheDark(how, ClientInputLab.LoadPeriodSeconds);
/// <summary>InTheDark operation.</summary>
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

            ClientInputLab.Result longer = ClientInputLab.Measure(how,
                ClientDriftLab.Correction.None, "sunlit", 480f, Blocks);
/// <summary>Run operation.</summary>
            ClientInputLab.Result shorter = Run(how);

            output.WriteLine("sunlit: {0:n2} K at 240 s, {1:n2} K at 480 s",
                shorter.StandingKelvin, longer.StandingKelvin);

            Assert.True(longer.StandingKelvin > shorter.StandingKelvin * 0.6f,
                "the standing error fell from " + shorter.StandingKelvin + " K to "
                + longer.StandingKelvin + " K over twice the run, which is a perturbation rather"
                + " than the standing difference this pins");
        }

        [Fact]
/// <summary>TheSameLostTimeInLumpsPeaksFarHigherThanAsASlope operation.</summary>
        public void TheSameLostTimeInLumpsPeaksFarHigherThanAsASlope()
        {
            const float Deficit = 1f / 6f;

/// <summary>Run operation.</summary>
            ClientInputLab.Result lumps = Run(new ClientInputLab.Degradation
            {
                Name = "hitching",
                HitchEverySeconds = 30f,
                HitchLosesSeconds = 5f,
            });

/// <summary>Run operation.</summary>
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

        [Fact]
/// <summary>AHeatSourceTheClientNeverHeardAboutIsABias operation.</summary>
        public void AHeatSourceTheClientNeverHeardAboutIsABias()
        {
/// <summary>Run operation.</summary>
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

        [Fact]
/// <summary>ARoomKnobOnAHullWithNoCompartmentIsRefusedRatherThanReportedAsHarmless operation.</summary>
        public void ARoomKnobOnAHullWithNoCompartmentIsRefusedRatherThanReportedAsHarmless()
        {
            const int NoCompartment = 500;

            InvalidOperationException raised = Assert.Throws<InvalidOperationException>(() =>
                ClientInputLab.Measure(
                    new ClientInputLab.Degradation { Name = "room pressure", RoomPressureError = 1f },
                    ClientDriftLab.Correction.None, "sunlit", 30f, NoCompartment));

            Assert.Contains("measure nothing", raised.Message);

            ClientInputLab.Result solid = ClientInputLab.Measure(
                new ClientInputLab.Degradation { Name = "power error", PowerErrorShare = 0.1f },
                ClientDriftLab.Correction.None, "sunlit", 30f, NoCompartment);

            Assert.Equal(0, solid.Rooms);

            Assert.True(Run(new ClientInputLab.Degradation { Name = "none" }).Rooms > 0,
                "the suite's rig fell below the first compartment, so its room rows judge nothing");
        }

        [Fact]
/// <summary>MassIsTheOnlyChannelBlockConditionHasIntoTheModel operation.</summary>
        public void MassIsTheOnlyChannelBlockConditionHasIntoTheModel()
        {
            BlockThermalProperties thermal = Catalog.DefaultThermal();
            BlockModel model = BlockModel.Solid("block", Vector3I.One, 1000f, thermal);
/// <summary>BlockInstance operation.</summary>
            BlockInstance block = new BlockInstance(model, Vector3I.Zero, BlockOrientation.Identity);

/// <summary>ThermalNode operation.</summary>
            ThermalNode node = new ThermalNode(block, 2.5f, 300f);
            float whole = node.ThermalMass;

            block.Mass = model.Mass * 0.5f;
            node.RefreshThermalMass();

            Assert.Equal(whole * 0.5f, node.ThermalMass, 3);

            string root = ShippedBlocks.RepoRoot();
            string scripts = Path.Combine(root, "Thermodynamics");
            string[] routes = { "BuildLevelRatio", "BuildIntegrity", "CurrentDamage", "MaxIntegrity" };
/// <summary>List operation.</summary>
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
