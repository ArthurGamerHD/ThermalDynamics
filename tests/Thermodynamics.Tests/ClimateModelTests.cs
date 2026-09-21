using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class ClimateModelTests
    {
/// <summary>Earthlike operation.</summary>
        private static PlanetThermalProperties Earthlike()
        {
            PlanetThermalProperties planet = PlanetThermalProperties.Default();
            planet.NightTemperature = 283.15f;      // 10 C
            planet.DayTemperature = 294.15f;        // 21 C
            planet.PoleTemperatureDrop = 40f;
            return planet;
        }

/// <summary>Sine operation.</summary>
        private static float Sine(double degrees)
        {
            return (float)Math.Sin(degrees * Math.PI / 180d);
        }


        [Fact]
/// <summary>TheEquatorGetsThePlanetsOwnFigures operation.</summary>
        public void TheEquatorGetsThePlanetsOwnFigures()
        {
/// <summary>Earthlike operation.</summary>
            PlanetThermalProperties planet = Earthlike();

            Assert.Equal(planet.DayTemperature, ClimateModel.Target(planet, 0f, 1f, 0f), 2);
            Assert.Equal(planet.NightTemperature, ClimateModel.Target(planet, 0f, -1f, 0f), 2);
        }

        [Fact]
/// <summary>ThePolesAreColderThanTheEquatorDayAndNight operation.</summary>
        public void ThePolesAreColderThanTheEquatorDayAndNight()
        {
/// <summary>Earthlike operation.</summary>
            PlanetThermalProperties planet = Earthlike();

            float equatorNoon = ClimateModel.Target(planet, 0f, 1f, 0f);
            float poleNoon = ClimateModel.Target(planet, 1f, 1f, 0f);

            Assert.Equal(planet.PoleTemperatureDrop, equatorNoon - poleNoon, 2);
            Assert.True(ClimateModel.Target(planet, Sine(60), -1f, 0f)
                < ClimateModel.Target(planet, 0f, -1f, 0f));
        }

        [Fact]
/// <summary>TheDropGrowsWithLatitudeRatherThanSteppingAtABand operation.</summary>
        public void TheDropGrowsWithLatitudeRatherThanSteppingAtABand()
        {
/// <summary>Earthlike operation.</summary>
            PlanetThermalProperties planet = Earthlike();

            float previous = ClimateModel.Target(planet, 0f, 0.5f, 0f);

            for (int latitude = 5; latitude <= 90; latitude += 5)
            {
                float current = ClimateModel.Target(planet, Sine(latitude), 0.5f, 0f);

                Assert.True(current <= previous, "should not warm toward the pole at " + latitude);
                Assert.True(previous - current < 8f, "should not step at " + latitude);

                previous = current;
            }
        }

        [Fact]
/// <summary>APlanetWithNoPoleDropIsOneClimateAsItUsedToBe operation.</summary>
        public void APlanetWithNoPoleDropIsOneClimateAsItUsedToBe()
        {
/// <summary>Earthlike operation.</summary>
            PlanetThermalProperties planet = Earthlike();
            planet.PoleTemperatureDrop = 0f;

            Assert.Equal(
                ClimateModel.Target(planet, 0f, 0.3f, 0f),
                ClimateModel.Target(planet, Sine(75), 0.3f, 0f), 3);
        }


        [Fact]
/// <summary>BelowTheHorizonIsNightWhateverTheDepth operation.</summary>
        public void BelowTheHorizonIsNightWhateverTheDepth()
        {
/// <summary>Earthlike operation.</summary>
            PlanetThermalProperties planet = Earthlike();

            float dusk = ClimateModel.Target(planet, 0f, -0.01f, 0f);
            float midnight = ClimateModel.Target(planet, 0f, -1f, 0f);

            Assert.Equal(dusk, midnight, 3);
        }

        [Fact]
/// <summary>WarmthFollowsTheSunsHeight operation.</summary>
        public void WarmthFollowsTheSunsHeight()
        {
/// <summary>Earthlike operation.</summary>
            PlanetThermalProperties planet = Earthlike();

            Assert.True(ClimateModel.Target(planet, 0f, Sine(60), 0f)
                > ClimateModel.Target(planet, 0f, Sine(20), 0f));
        }


        [Fact]
/// <summary>TheGroundShiftsTheAirAboveItBothWays operation.</summary>
        public void TheGroundShiftsTheAirAboveItBothWays()
        {
/// <summary>Earthlike operation.</summary>
            PlanetThermalProperties planet = Earthlike();

            float bare = ClimateModel.Target(planet, 0f, 0.5f, 0f);

            Assert.Equal(bare - 14f, ClimateModel.Target(planet, 0f, 0.5f, -14f), 3);
            Assert.Equal(bare + 9f, ClimateModel.Target(planet, 0f, 0.5f, 9f), 3);
        }

        [Fact]
/// <summary>SnowIsColdAndSandIsWarmAndAnythingUnknownIsNeither operation.</summary>
        public void SnowIsColdAndSandIsWarmAndAnythingUnknownIsNeither()
        {
            Assert.True(GroundTemperature.OffsetFor("Snow") < -10f);
            Assert.True(GroundTemperature.OffsetFor("MyPlanet_Snow_01") < -10f);
            Assert.True(GroundTemperature.OffsetFor("Sand_02") > 5f);
            Assert.True(GroundTemperature.OffsetFor("Desert_Sand") > 5f);

            Assert.Equal(0f, GroundTemperature.OffsetFor("Unobtainium"), 3);
            Assert.Equal(0f, GroundTemperature.OffsetFor(""), 3);
            Assert.Equal(0f, GroundTemperature.OffsetFor(null), 3);
        }


        [Fact]
/// <summary>AWiderSwingCoolsTheNightAsMuchAsItWarmsTheNoon operation.</summary>
        public void AWiderSwingCoolsTheNightAsMuchAsItWarmsTheNoon()
        {
/// <summary>Earthlike operation.</summary>
            PlanetThermalProperties planet = Earthlike();

            float flatDay = ClimateModel.Target(planet, 0f, 1f, 0f, 1f);
            float flatNight = ClimateModel.Target(planet, 0f, -1f, 0f, 1f);
            float mean = (flatDay + flatNight) * 0.5f;

            float wideDay = ClimateModel.Target(planet, 0f, 1f, 0f, 2f);
            float wideNight = ClimateModel.Target(planet, 0f, -1f, 0f, 2f);

            Assert.Equal(mean, (wideDay + wideNight) * 0.5f, 2);
            Assert.Equal((flatDay - flatNight) * 2f, wideDay - wideNight, 2);
        }

        [Fact]
/// <summary>NoSwingIsTheSameTemperatureAllDay operation.</summary>
        public void NoSwingIsTheSameTemperatureAllDay()
        {
/// <summary>Earthlike operation.</summary>
            PlanetThermalProperties planet = Earthlike();

            Assert.Equal(
                ClimateModel.Target(planet, 0f, 1f, 0f, 0f),
                ClimateModel.Target(planet, 0f, -1f, 0f, 0f), 3);
        }

        [Fact]
/// <summary>SandSwingsMoreThanSnowAndBothStillPointTheRightWay operation.</summary>
        public void SandSwingsMoreThanSnowAndBothStillPointTheRightWay()
        {
            GroundTemperature.Ground sand = GroundTemperature.For("Sand_02");
            GroundTemperature.Ground snow = GroundTemperature.For("Snow");
            GroundTemperature.Ground unknown = GroundTemperature.For("Unobtainium");

            Assert.True(sand.Swing > 1.4f, "dry sand should swing hard");
            Assert.True(snow.Swing < 0.9f, "snow should hold its temperature");
            Assert.True(sand.Offset > 0f && snow.Offset < 0f);

            Assert.Equal(0f, unknown.Offset, 3);
            Assert.Equal(1f, unknown.Swing, 3);
        }


        [Fact]
/// <summary>AirChasesTheSunRatherThanTrackingIt operation.</summary>
        public void AirChasesTheSunRatherThanTrackingIt()
        {
            float ambient = ClimateModel.Follow(283.15f, 303.15f, 10f, 45f);

            Assert.True(ambient > 283.15f, "should have warmed");
            Assert.True(ambient < 303.15f, "should not have arrived");
        }

        [Fact]
/// <summary>OneLagClosesAboutTwoThirdsOfTheGap operation.</summary>
        public void OneLagClosesAboutTwoThirdsOfTheGap()
        {
            float ambient = ClimateModel.Follow(0f + 100f, 200f, 45f, 45f);

            Assert.Equal(100f + (100f * 0.632f), ambient, 1);
        }

        [Fact]
/// <summary>EnoughTimeArrives operation.</summary>
        public void EnoughTimeArrives()
        {
            Assert.Equal(300f, ClimateModel.Follow(200f, 300f, 1000f, 45f), 1);
        }

        [Fact]
/// <summary>NoLagMeansTheTargetAtOnce operation.</summary>
        public void NoLagMeansTheTargetAtOnce()
        {
            Assert.Equal(300f, ClimateModel.Follow(200f, 300f, 10f, 0f), 3);
            Assert.Equal(300f, ClimateModel.Follow(0f, 300f, 10f, 45f), 3);
        }

        [Fact]
/// <summary>TheHottestPartOfTheDayLandsAfterNoon operation.</summary>
        public void TheHottestPartOfTheDayLandsAfterNoon()
        {
/// <summary>Earthlike operation.</summary>
            PlanetThermalProperties planet = Earthlike();

            float ambient = planet.NightTemperature;
            float hottest = float.MinValue;
            int hottestStep = 0;
            int noonStep = 0;
            float highestSun = float.MinValue;

            const int steps = 360;
            for (int i = 0; i < steps; i++)
            {
/// <summary>Sine operation.</summary>
                float elevation = Sine(i - 90);   // dawn, noon, dusk, midnight

                if (elevation > highestSun)
                {
                    highestSun = elevation;
                    noonStep = i;
                }

                float target = ClimateModel.Target(planet, 0f, elevation, 0f);
                ambient = ClimateModel.Follow(ambient, target, 1f, 20f);

                if (ambient > hottest)
                {
                    hottest = ambient;
                    hottestStep = i;
                }
            }

            Assert.True(hottestStep > noonStep,
                "peak at " + hottestStep + " should be after noon at " + noonStep);
        }
    }
}
