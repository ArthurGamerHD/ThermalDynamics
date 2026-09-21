using System;
using System.Collections.Generic;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    public class WindScenarioTests
    {
/// <summary>Coarse operation.</summary>
        private static List<WindScenarios.Scenario> Coarse()
        {
            List<WindScenarios.Scenario> scenarios = WindScenarios.All();
            for (int i = 0; i < scenarios.Count; i++) scenarios[i].Options.StepsPerDay = 8;
            return scenarios;
        }

/// <summary>One operation.</summary>
        private static WindScenarios.Outcome One(string name)
        {
/// <summary>Coarse operation.</summary>
            List<WindScenarios.Scenario> scenarios = Coarse();
            for (int i = 0; i < scenarios.Count; i++)
            {
                if (scenarios[i].Name == name) return WindScenarios.Run(scenarios[i]);
            }
            throw new ArgumentException("no scenario named " + name);
        }


        [Fact]
/// <summary>APlanetIsDerivedTheWayTheEngineDerivesOne operation.</summary>
        public void APlanetIsDerivedTheWayTheEngineDerivesOne()
        {
            WindLab.Planet earth = WindLab.Planet.Vanilla("EarthLike");

            Assert.Equal(60000d, earth.AverageRadius, 3);
            Assert.Equal(7200d, earth.MaxHillHeight, 3);
            Assert.Equal(-600d, earth.MinHillHeight, 3);
            Assert.Equal(14400d, earth.AtmosphereAltitude, 3);
            Assert.Equal(67200d, earth.OuterRadius, 3);
        }

        [Fact]
/// <summary>TritonsPeaksStandAboveItsOwnAtmosphere operation.</summary>
        public void TritonsPeaksStandAboveItsOwnAtmosphere()
        {
            WindLab.Planet triton = WindLab.Planet.Vanilla("Triton");

            Assert.True(triton.PeaksAboveAir);
            Assert.True(triton.MaxHillHeight > triton.AtmosphereAltitude,
                triton.MaxHillHeight + " m of mountain against " + triton.AtmosphereAltitude + " m of air");

            Assert.Equal(0f, triton.WindCeiling(triton.AverageRadius + triton.MaxHillHeight), 5);
        }

        [Fact]
/// <summary>NoOtherShippedWorldHasPeaksInVacuum operation.</summary>
        public void NoOtherShippedWorldHasPeaksInVacuum()
        {
            for (int i = 0; i < WindLab.Planet.VanillaNames.Length; i++)
            {
                string name = WindLab.Planet.VanillaNames[i];
                if (name == "Triton") continue;

                WindLab.Planet planet = WindLab.Planet.Vanilla(name);
                Assert.False(planet.PeaksAboveAir, name + " unexpectedly has peaks above its air");
            }
        }

        [Fact]
/// <summary>ACirculationBandIsAShortFlightRatherThanAContinent operation.</summary>
        public void ACirculationBandIsAShortFlightRatherThanAContinent()
        {
            WindLab.Planet earth = WindLab.Planet.Vanilla("EarthLike");
            double band = earth.MetresPerDegree * 30d;

            Assert.InRange(band, 30000d, 33000d);

            double earthBand = 6371000d * Math.PI / 180d * 30d;
            Assert.True(earthBand / band > 90d, "the ratio should be about a hundred");
        }

        [Fact]
/// <summary>TheHorizonIsCloseEnoughToMatterToWhatTheMapDraws operation.</summary>
        public void TheHorizonIsCloseEnoughToMatterToWhatTheMapDraws()
        {
            Assert.InRange(WindLab.Planet.Vanilla("EarthLike").HorizonFrom(2d), 450d, 520d);
            Assert.InRange(WindLab.Planet.Vanilla("Titan").HorizonFrom(2d), 170d, 220d);
        }

        [Fact]
/// <summary>TheBoundaryLayerCanBeTallerThanAWholeAtmosphere operation.</summary>
        public void TheBoundaryLayerCanBeTallerThanAWholeAtmosphere()
        {
            WindLab.Planet titan = WindLab.Planet.Vanilla("Titan");

            Assert.True(titan.AtmosphereAltitude < 600d,
                "Titan's air is " + titan.AtmosphereAltitude + " m deep");

            Assert.Equal(0f, titan.WindCeiling(titan.AverageRadius + 600d), 5);
        }


        [Fact]
/// <summary>NoScenarioAnywhereProducesNaNOrANegativeWind operation.</summary>
        public void NoScenarioAnywhereProducesNaNOrANegativeWind()
        {
/// <summary>Coarse operation.</summary>
            List<WindScenarios.Scenario> scenarios = Coarse();
            Assert.True(scenarios.Count >= 25, "the matrix has shrunk: " + scenarios.Count);

            int samples = 0;
            for (int i = 0; i < scenarios.Count; i++)
            {
                WindScenarios.Outcome outcome = WindScenarios.Run(scenarios[i]);
                samples += outcome.Samples;

                Assert.True(outcome.Samples > 0, outcome.Name + " produced nothing");
                Assert.Equal(0, outcome.Bad);
                Assert.True(outcome.MinSpeed >= 0f, outcome.Name + " produced a negative wind");
            }

            Assert.True(samples > 200000, "only " + samples + " samples swept");
        }

        [Fact]
/// <summary>EveryScenarioKeepsTheTerrainFactorsInsideTheirDeclaredCaps operation.</summary>
        public void EveryScenarioKeepsTheTerrainFactorsInsideTheirDeclaredCaps()
        {
/// <summary>Coarse operation.</summary>
            List<WindScenarios.Scenario> scenarios = Coarse();

            for (int i = 0; i < scenarios.Count; i++)
            {
                WindScenarios.Outcome o = WindScenarios.Run(scenarios[i]);

                Assert.InRange(o.MinSpeedUp, 1f - Thermodynamics.Core.WindTerrain.MaximumSlowDown - 1e-4f, 1f);
                Assert.InRange(o.MaxSpeedUp, 1f, 1f + Thermodynamics.Core.WindTerrain.MaximumSpeedUp + 1e-4f);
                Assert.InRange(o.MinShelter, 1f - Thermodynamics.Core.WindTerrain.MaximumShelter - 1e-4f, 1.0001f);
                Assert.InRange(o.MaxChannelDegrees, 0f, 180f);
            }
        }


        [Fact]
/// <summary>AnAirlessWorldHasNoWindAtAll operation.</summary>
        public void AnAirlessWorldHasNoWindAtAll()
        {
/// <summary>One operation.</summary>
            WindScenarios.Outcome moon = One("vanilla:Moon");

            Assert.Equal(0f, moon.MinSpeed, 5);
            Assert.Equal(0f, moon.MaxSpeed, 5);
            Assert.Equal(0, moon.Bad);
        }

        [Fact]
/// <summary>TakingTheAirOrTheWindRatingAwayGivesTheSameStillWorld operation.</summary>
        public void TakingTheAirOrTheWindRatingAwayGivesTheSameStillWorld()
        {
            foreach (string name in new[] { "degenerate:no-atmosphere", "degenerate:no-wind-rating" })
            {
/// <summary>One operation.</summary>
                WindScenarios.Outcome o = One(name);
                Assert.Equal(0f, o.MaxSpeed, 5);
                Assert.Equal(0, o.Bad);
            }
        }

        [Fact]
/// <summary>AFlatWorldLeavesEveryTerrainFactorExactlyAlone operation.</summary>
        public void AFlatWorldLeavesEveryTerrainFactorExactlyAlone()
        {
/// <summary>One operation.</summary>
            WindScenarios.Outcome flat = One("degenerate:flat-world");

            Assert.Equal(1f, flat.MinSpeedUp, 4);
            Assert.Equal(1f, flat.MaxSpeedUp, 4);
            Assert.Equal(1f, flat.MinShelter, 4);
            Assert.Equal(0f, flat.MaxChannelDegrees, 3);
            Assert.True(flat.MaxSpeed > 0f, "and there should still be wind");
        }

        [Fact]
/// <summary>TurningTerrainOffIsTheSameAsHavingNone operation.</summary>
        public void TurningTerrainOffIsTheSameAsHavingNone()
        {
/// <summary>One operation.</summary>
            WindScenarios.Outcome off = One("settings:no-terrain");

            Assert.Equal(1f, off.MinSpeedUp, 4);
            Assert.Equal(1f, off.MinShelter, 4);
            Assert.Equal(0f, off.MaxChannelDegrees, 3);
        }

        [Fact]
/// <summary>ADayShorterThanTheLagThatFollowsItStillBehaves operation.</summary>
        public void ADayShorterThanTheLagThatFollowsItStillBehaves()
        {
/// <summary>One operation.</summary>
            WindScenarios.Outcome fast = One("degenerate:four-minute-day");

            Assert.Equal(0, fast.Bad);
            Assert.True(fast.MaxSpeed > 0f);
        }


        [Fact]
/// <summary>TerrainReachesForItsBoundsWithoutSittingOnThem operation.</summary>
        public void TerrainReachesForItsBoundsWithoutSittingOnThem()
        {
            foreach (string name in new[] { "size:19km", "size:60km", "size:120km" })
            {
/// <summary>One operation.</summary>
                WindScenarios.Outcome o = One(name);

                float ceiling = 1f + Thermodynamics.Core.WindTerrain.MaximumSpeedUp;
                float floor = 1f - Thermodynamics.Core.WindTerrain.MaximumShelter;

                Assert.True(o.MaxSpeedUp <= ceiling + 1e-3f,
                    name + " exceeded the speed-up bound: " + o.MaxSpeedUp);
                Assert.True(o.MaxSpeedUp > ceiling * 0.9f,
                    name + " never came near it: " + o.MaxSpeedUp);

                Assert.True(o.MinShelter >= floor - 1e-3f,
                    name + " went under the shelter floor: " + o.MinShelter);
                Assert.True(o.MinShelter < 0.5f,
                    name + " was never sheltered at all: " + o.MinShelter);

                Assert.True(o.MaxChannelDegrees > 30f, name + " channelled only " + o.MaxChannelDegrees);
            }
        }

        [Fact]
/// <summary>TerrainTellsOneWorldFromAnother operation.</summary>
        public void TerrainTellsOneWorldFromAnother()
        {
/// <summary>One operation.</summary>
            WindScenarios.Outcome earth = One("vanilla:EarthLike");
/// <summary>One operation.</summary>
            WindScenarios.Outcome pertam = One("vanilla:Pertam");

            Assert.True(pertam.MaxSpeedUp < earth.MaxSpeedUp - 0.05f,
                "the gentler world should be less exposed: " + pertam.MaxSpeedUp
                + " against " + earth.MaxSpeedUp);

            Assert.True(pertam.MinShelter > earth.MinShelter + 0.05f,
                "and less sheltered: " + pertam.MinShelter + " against " + earth.MinShelter);
        }

        [Fact]
/// <summary>AWorldSmallEnoughThatTheTerrainRingSpansManyLandformsAveragesThemOut operation.</summary>
        public void AWorldSmallEnoughThatTheTerrainRingSpansManyLandformsAveragesThemOut()
        {
/// <summary>One operation.</summary>
            WindScenarios.Outcome tiny = One("size:2km-moddedtiny");
/// <summary>One operation.</summary>
            WindScenarios.Outcome usual = One("size:120km");

            Assert.True(tiny.MaxSpeedUp < usual.MaxSpeedUp,
                "the tiny world should see milder exposure: " + tiny.MaxSpeedUp + " vs " + usual.MaxSpeedUp);
            Assert.True(tiny.MinShelter > usual.MinShelter);
            Assert.True(tiny.MaxChannelDegrees < usual.MaxChannelDegrees);
        }


        [Fact]
/// <summary>OnlyAStormReachesTheFrictionThresholdAndNothingPassesTheCeiling operation.</summary>
        public void OnlyAStormReachesTheFrictionThresholdAndNothingPassesTheCeiling()
        {
/// <summary>One operation.</summary>
            WindScenarios.Outcome storm = One("settings:storm");

            Assert.Equal(0, storm.OverCeiling);
            Assert.True(storm.MaxSpeed > WindScenarios.FrictionThreshold,
                "a storm should still be a storm: " + storm.MaxSpeed);

            Assert.True(storm.OverFrictionNearGround > 0,
                "a parked grid in the worst weather the game reports is expected to be friction"
                + " heated; if it no longer is, this test has outlived its subject");

/// <summary>One operation.</summary>
            WindScenarios.Outcome calm = One("vanilla:EarthLike");
            Assert.Equal(0, calm.OverCeiling);
            Assert.Equal(0, calm.OverFriction);
            Assert.True(calm.MaxSpeedNearGround < WindScenarios.FrictionThreshold * 0.6f,
                "a fair day near the ground should be well under the threshold: "
                + calm.MaxSpeedNearGround);
        }

        [Fact]
/// <summary>OrdinaryWeatherOnEveryShippedWorldStaysUnderTheCeiling operation.</summary>
        public void OrdinaryWeatherOnEveryShippedWorldStaysUnderTheCeiling()
        {
            for (int i = 0; i < WindLab.Planet.VanillaNames.Length; i++)
            {
/// <summary>One operation.</summary>
                WindScenarios.Outcome o = One("vanilla:" + WindLab.Planet.VanillaNames[i]);

                Assert.Equal(0, o.OverCeiling);
                Assert.Equal(0, o.OverFriction);
            }
        }
    }
}
