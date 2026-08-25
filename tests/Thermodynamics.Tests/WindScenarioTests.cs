using System;
using System.Collections.Generic;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The wind model on every world the game ships, at every size, in every corner.
    ///
    /// <para><b>Space Engineers planets are not small Earths, they are a different kind of object.</b>
    /// An earthlike world is 120 km across against Earth's 12,742, and its hills reach 12% of its own
    /// radius against Earth's 0.14%. Terrain is therefore about eighty times steeper relative to the
    /// world it sits on, a circulation band is 31 km wide rather than 3,300, and the horizon from
    /// head height is 490 m rather than five kilometres. Every constant in this model came from
    /// terrestrial meteorology and every one of them lands somewhere unexpected here.</para>
    ///
    /// <para>These run the whole matrix at a coarse day so the suite stays quick; the full-resolution
    /// version is <c>dotnet run --project tests/Thermodynamics.Sim -- wind scenarios</c>.</para>
    /// </summary>
    public class WindScenarioTests
    {
        /// <summary>Every scenario, at a resolution that keeps the suite fast.</summary>
        private static List<WindScenarios.Scenario> Coarse()
        {
            List<WindScenarios.Scenario> scenarios = WindScenarios.All();
            for (int i = 0; i < scenarios.Count; i++) scenarios[i].Options.StepsPerDay = 8;
            return scenarios;
        }

        private static WindScenarios.Outcome One(string name)
        {
            List<WindScenarios.Scenario> scenarios = Coarse();
            for (int i = 0; i < scenarios.Count; i++)
            {
                if (scenarios[i].Name == name) return WindScenarios.Run(scenarios[i]);
            }
            throw new ArgumentException("no scenario named " + name);
        }

        // ---- the worlds, as the engine derives them ------------------------------------------

        [Fact]
        public void APlanetIsDerivedTheWayTheEngineDerivesOne()
        {
            // maxHillHeight = HillParams.Max * radius; AtmosphereAltitude = maxHillHeight *
            // LimitAltitude. Both read out of Sandbox.Game.dll. An earthlike world 120 km across
            // therefore has 7.2 km of mountain and 14.4 km of air — neither of which is a figure
            // anybody would have guessed.
            WindLab.Planet earth = WindLab.Planet.Vanilla("EarthLike");

            Assert.Equal(60000d, earth.AverageRadius, 3);
            Assert.Equal(7200d, earth.MaxHillHeight, 3);
            Assert.Equal(-600d, earth.MinHillHeight, 3);
            Assert.Equal(14400d, earth.AtmosphereAltitude, 3);
            Assert.Equal(67200d, earth.OuterRadius, 3);
        }

        [Fact]
        public void TritonsPeaksStandAboveItsOwnAtmosphere()
        {
            // 20% of the radius in mountain against a LimitAltitude of 0.47, so the air runs out
            // less than half way up the highest ground. A ship on a Triton summit is in vacuum, and
            // every wind figure there must be zero rather than merely small.
            WindLab.Planet triton = WindLab.Planet.Vanilla("Triton");

            Assert.True(triton.PeaksAboveAir);
            Assert.True(triton.MaxHillHeight > triton.AtmosphereAltitude,
                triton.MaxHillHeight + " m of mountain against " + triton.AtmosphereAltitude + " m of air");

            Assert.Equal(0f, triton.WindCeiling(triton.AverageRadius + triton.MaxHillHeight), 5);
        }

        [Fact]
        public void NoOtherShippedWorldHasPeaksInVacuum()
        {
            // Worth pinning the other way round too: if a future definition change puts another
            // world in Triton's position, this is where it shows.
            for (int i = 0; i < WindLab.Planet.VanillaNames.Length; i++)
            {
                string name = WindLab.Planet.VanillaNames[i];
                if (name == "Triton") continue;

                WindLab.Planet planet = WindLab.Planet.Vanilla(name);
                Assert.False(planet.PeaksAboveAir, name + " unexpectedly has peaks above its air");
            }
        }

        [Fact]
        public void ACirculationBandIsAShortFlightRatherThanAContinent()
        {
            // 30° of latitude is a band of the general circulation. On Earth that is 3,336 km; on an
            // earthlike SE world it is 31 km. The bands this model draws are local features, and
            // anyone reasoning about them from terrestrial intuition will be out by a hundredfold.
            WindLab.Planet earth = WindLab.Planet.Vanilla("EarthLike");
            double band = earth.MetresPerDegree * 30d;

            Assert.InRange(band, 30000d, 33000d);

            // Earth's, for the comparison the number only means something against.
            double earthBand = 6371000d * Math.PI / 180d * 30d;
            Assert.True(earthBand / band > 90d, "the ratio should be about a hundred");
        }

        [Fact]
        public void TheHorizonIsCloseEnoughToMatterToWhatTheMapDraws()
        {
            // From head height: 490 m on an earthlike world, 195 m on a moon, against 5 km on Earth.
            // The local wind map reaches five kilometres, which is ten times past the horizon on the
            // largest world in the game — the far arrows are behind the planet, not merely far away.
            Assert.InRange(WindLab.Planet.Vanilla("EarthLike").HorizonFrom(2d), 450d, 520d);
            Assert.InRange(WindLab.Planet.Vanilla("Titan").HorizonFrom(2d), 170d, 220d);
        }

        [Fact]
        public void TheBoundaryLayerCanBeTallerThanAWholeAtmosphere()
        {
            // The shipped gradient height is 600 m. Titan's entire atmosphere is 285 m deep. The
            // vertical profile is therefore being asked about heights that are in vacuum, and the
            // only thing that keeps the answer sane is the ceiling going to zero first.
            WindLab.Planet titan = WindLab.Planet.Vanilla("Titan");

            Assert.True(titan.AtmosphereAltitude < 600d,
                "Titan's air is " + titan.AtmosphereAltitude + " m deep");

            Assert.Equal(0f, titan.WindCeiling(titan.AverageRadius + 600d), 5);
        }

        // ---- nothing anywhere produces nonsense ------------------------------------------------

        [Fact]
        public void NoScenarioAnywhereProducesNaNOrANegativeWind()
        {
            // The whole point of the matrix. Every shipped world, four sizes plus two modded
            // extremes, ten settings pushed to their ends and four degenerate worlds — and not one
            // sample may come back NaN, infinite or negative.
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
        public void EveryScenarioKeepsTheTerrainFactorsInsideTheirDeclaredCaps()
        {
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

        // ---- the degenerate worlds --------------------------------------------------------------

        [Fact]
        public void AnAirlessWorldHasNoWindAtAll()
        {
            // The Moon. Every figure zero, and nothing in the model divides by it.
            WindScenarios.Outcome moon = One("vanilla:Moon");

            Assert.Equal(0f, moon.MinSpeed, 5);
            Assert.Equal(0f, moon.MaxSpeed, 5);
            Assert.Equal(0, moon.Bad);
        }

        [Fact]
        public void TakingTheAirOrTheWindRatingAwayGivesTheSameStillWorld()
        {
            foreach (string name in new[] { "degenerate:no-atmosphere", "degenerate:no-wind-rating" })
            {
                WindScenarios.Outcome o = One(name);
                Assert.Equal(0f, o.MaxSpeed, 5);
                Assert.Equal(0, o.Bad);
            }
        }

        [Fact]
        public void AFlatWorldLeavesEveryTerrainFactorExactlyAlone()
        {
            WindScenarios.Outcome flat = One("degenerate:flat-world");

            Assert.Equal(1f, flat.MinSpeedUp, 4);
            Assert.Equal(1f, flat.MaxSpeedUp, 4);
            Assert.Equal(1f, flat.MinShelter, 4);
            Assert.Equal(0f, flat.MaxChannelDegrees, 3);
            Assert.True(flat.MaxSpeed > 0f, "and there should still be wind");
        }

        [Fact]
        public void TurningTerrainOffIsTheSameAsHavingNone()
        {
            WindScenarios.Outcome off = One("settings:no-terrain");

            Assert.Equal(1f, off.MinSpeedUp, 4);
            Assert.Equal(1f, off.MinShelter, 4);
            Assert.Equal(0f, off.MaxChannelDegrees, 3);
        }

        [Fact]
        public void ADayShorterThanTheLagThatFollowsItStillBehaves()
        {
            // A four-minute day against a 45-second climate lag. The heating curve cannot keep up,
            // which is fine — what matters is that it stays in range and produces no nonsense.
            WindScenarios.Outcome fast = One("degenerate:four-minute-day");

            Assert.Equal(0, fast.Bad);
            Assert.True(fast.MaxSpeed > 0f);
        }

        // ---- what world size actually changes ---------------------------------------------------

        /// <summary>
        /// SE terrain still reaches for its bounds on every world size, and no longer sits on them.
        ///
        /// <para>
        /// **This test used to pin the defect.** SE's hills reach 12 % of a planet's radius where
        /// Earth's reach 0.14 %, so the linear laws the terrain factors come from ran past their
        /// clips on ordinary ground and *every* site read exactly the cap — which meant terrain had
        /// stopped telling one place from another, the only thing it was there to do.
        /// backlog.md `B19`.
        /// </para>
        ///
        /// <para>
        /// The factors saturate onto their bounds now instead of clipping to them, so what this
        /// asserts is the corrected shape: the bounds still bound, the steepest ground still gets
        /// near them, and two worlds of different steepness give different answers.
        /// </para>
        /// </summary>
        [Fact]
        public void TerrainReachesForItsBoundsWithoutSittingOnThem()
        {
            foreach (string name in new[] { "size:19km", "size:60km", "size:120km" })
            {
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

        /// <summary>
        /// And the property the clip made impossible: two worlds whose ground is differently steep
        /// answer differently. Pertam's hills are a fortieth of its radius against an earthlike
        /// world's eighth, and under the clip both read the cap.
        /// </summary>
        [Fact]
        public void TerrainTellsOneWorldFromAnother()
        {
            WindScenarios.Outcome earth = One("vanilla:EarthLike");
            WindScenarios.Outcome pertam = One("vanilla:Pertam");

            Assert.True(pertam.MaxSpeedUp < earth.MaxSpeedUp - 0.05f,
                "the gentler world should be less exposed: " + pertam.MaxSpeedUp
                + " against " + earth.MaxSpeedUp);

            Assert.True(pertam.MinShelter > earth.MinShelter + 0.05f,
                "and less sheltered: " + pertam.MinShelter + " against " + earth.MinShelter);
        }

        [Fact]
        public void AWorldSmallEnoughThatTheTerrainRingSpansManyLandformsAveragesThemOut()
        {
            // A 2 km modded world. Its landforms are 2 km/60 km of the earthlike ones, so a 300 m
            // ring reads across dozens of them and the relief it sees is the average rather than the
            // shape — every terrain factor comes back milder. Not a fault; a consequence of the ring
            // being a fixed number of metres on a world whose features scale with its radius.
            WindScenarios.Outcome tiny = One("size:2km-moddedtiny");
            WindScenarios.Outcome usual = One("size:120km");

            Assert.True(tiny.MaxSpeedUp < usual.MaxSpeedUp,
                "the tiny world should see milder exposure: " + tiny.MaxSpeedUp + " vs " + usual.MaxSpeedUp);
            Assert.True(tiny.MinShelter > usual.MinShelter);
            Assert.True(tiny.MaxChannelDegrees < usual.MaxChannelDegrees);
        }

        // ---- the known fault, pinned ------------------------------------------------------------

        /// <summary>
        /// The engine's figure bounds the wind everywhere, and a storm is the only thing that
        /// reaches the friction threshold with nothing moving.
        ///
        /// <para>
        /// This replaces a test that pinned the size of a defect. The composed model used to reach
        /// **187 m/s against a planet rating of 80**, with 143 of that within a hundred metres of
        /// the ground — the exact failure `WindField` was written to prevent, reopened because no
        /// unit test could see a compound where every factor was reasonable alone. Two of the three
        /// causes were faults: the weather's own wind modifier multiplied a share that already
        /// meant "the worst weather", counting one storm twice, and nothing bounded the composed
        /// speed at all. Both are fixed, and `&gt;ceil` is zero on every scenario in the matrix.
        /// </para>
        ///
        /// <para>
        /// The third is not a fault. A storm at the planet's own ceiling is over
        /// `FrictionAtSpeedsAbove`, and `v_rel` is relative wind by design — a hull parked in a
        /// hurricane heats like a hull flying at hurricane speed. `StormHeatingTests` measures what
        /// that costs and it is degrees rather than hundreds of degrees, because the wind that
        /// heats it is also the wind that cools it. Backlog `B17`, `B18`.
        /// </para>
        /// </summary>
        [Fact]
        public void OnlyAStormReachesTheFrictionThresholdAndNothingPassesTheCeiling()
        {
            WindScenarios.Outcome storm = One("settings:storm");

            Assert.Equal(0, storm.OverCeiling);
            Assert.True(storm.MaxSpeed > WindScenarios.FrictionThreshold,
                "a storm should still be a storm: " + storm.MaxSpeed);

            Assert.True(storm.OverFrictionNearGround > 0,
                "a parked grid in the worst weather the game reports is expected to be friction"
                + " heated; if it no longer is, this test has outlived its subject");

            // And the control: ordinary weather comes nowhere near either bound, at any height or
            // on any ground. That is what makes the storm case the extreme rather than the norm.
            WindScenarios.Outcome calm = One("vanilla:EarthLike");
            Assert.Equal(0, calm.OverCeiling);
            Assert.Equal(0, calm.OverFriction);
            Assert.True(calm.MaxSpeedNearGround < WindScenarios.FrictionThreshold * 0.6f,
                "a fair day near the ground should be well under the threshold: "
                + calm.MaxSpeedNearGround);
        }

        [Fact]
        public void OrdinaryWeatherOnEveryShippedWorldStaysUnderTheCeiling()
        {
            // The bound holds everywhere it is supposed to, which is what makes the storm case a
            // specific defect rather than a general one.
            for (int i = 0; i < WindLab.Planet.VanillaNames.Length; i++)
            {
                WindScenarios.Outcome o = One("vanilla:" + WindLab.Planet.VanillaNames[i]);

                Assert.Equal(0, o.OverCeiling);
                Assert.Equal(0, o.OverFriction);
            }
        }
    }
}
