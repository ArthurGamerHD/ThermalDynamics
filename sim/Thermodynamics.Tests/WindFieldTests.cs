using System;
using Thermodynamics.Core;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The wind map that stands in for the one the game does not have.
    ///
    /// What the game gives is a planet-wide ceiling — its definition's maximum wind scaled by air
    /// density — which on an earthlike world reads 80 m/s at sea level everywhere. Treated as a
    /// wind it puts every parked ship in a permanent hurricane, over the friction threshold and at
    /// double the still-air convection. These pin the field that replaces it: bands like Earth's,
    /// a speed that is a fraction of the ceiling, and nothing that varies from one moment to the
    /// next.
    /// </summary>
    public class WindFieldTests
    {
        private static readonly Vector3 Axis = new Vector3(0f, 1f, 0f);

        /// <summary>A point at this latitude, on a sphere whose north is +Y.</summary>
        private static Vector3 Up(double latitudeDegrees, double longitudeDegrees = 0d)
        {
            double lat = latitudeDegrees * Math.PI / 180d;
            double lon = longitudeDegrees * Math.PI / 180d;

            return Vector3.Normalize(new Vector3(
                (float)(Math.Cos(lat) * Math.Cos(lon)),
                (float)Math.Sin(lat),
                (float)(Math.Cos(lat) * Math.Sin(lon))));
        }

        /// <summary>How far east the wind blows here: +1 due east, −1 due west.</summary>
        private static float Eastward(double latitude)
        {
            Vector3 up = Up(latitude);
            Vector3 east = Vector3.Normalize(Vector3.Cross(Axis, up));

            return Vector3.Dot(WindField.Direction(up, Axis), east);
        }

        [Fact]
        public void WindBlowsAlongTheGroundAndNotThroughIt()
        {
            // The one thing that would be nonsense: air moving into or out of the planet.
            for (int latitude = -80; latitude <= 80; latitude += 10)
            {
                Vector3 up = Up(latitude);
                Vector3 wind = WindField.Direction(up, Axis);

                Assert.True(Math.Abs(Vector3.Dot(wind, up)) < 1e-4f,
                    "wind at " + latitude + " should be tangent to the surface");
                Assert.Equal(1f, wind.Length(), 3);
            }
        }

        [Fact]
        public void TheBandsRunEasterlyWesterlyEasterlyOutFromTheEquator()
        {
            // Earth's pattern, and the reason to have a map at all: trades, westerlies, polar
            // easterlies. Anyone who has looked at a globe recognises it.
            Assert.True(Eastward(10) < -0.5f, "trades near the equator should blow west");
            Assert.True(Eastward(45) > 0.5f, "mid latitudes should blow east");
            Assert.True(Eastward(75) < -0.5f, "polar latitudes should blow west");
        }

        [Fact]
        public void TheSouthernHemisphereMirrorsTheNorthern()
        {
            for (int latitude = 5; latitude <= 85; latitude += 10)
            {
                Assert.Equal(Eastward(latitude), Eastward(-latitude), 3);
            }
        }

        [Fact]
        public void TheWindSwingsThroughTheCalmsRatherThanReversingAcrossALine()
        {
            // Between bands it has to pass through a turn, not a step: a line on the map where the
            // wind flips end for end would be visible from a cockpit as a wall.
            float previous = Eastward(0);

            for (int latitude = 1; latitude <= 89; latitude++)
            {
                float current = Eastward(latitude);
                Assert.True(Math.Abs(current - previous) < 0.25f,
                    "wind turned too sharply between " + (latitude - 1) + " and " + latitude);
                previous = current;
            }
        }

        [Fact]
        public void APoleHasNoOneDirectionAndSaysSo()
        {
            // Every direction from a pole is south; there is no east to blow along, and a
            // normalised rounding error would be a lie.
            Assert.Equal(Vector3.Zero, WindField.Direction(Axis, Axis));
        }

        [Fact]
        public void OrdinaryWeatherIsABreezeRatherThanTheCeiling()
        {
            // 80 m/s is the earthlike ceiling. What a calm day gets out of it has to be something
            // a ship can sit in without heating up.
            float calm = WindField.Speed(80f, 0f, 0.5f);

            Assert.InRange(calm, 4f, 20f);
        }

        [Fact]
        public void WeatherRaisesTheWindWithoutReachingTheCeiling()
        {
            float calm = WindField.Speed(80f, 0f, 0.5f);
            float storm = WindField.Speed(80f, 1f, 0.5f);

            Assert.True(storm > calm * 2f, "a storm should be much windier than a calm day");
            Assert.True(storm < 80f, "and still under the planet's own maximum");
        }

        [Fact]
        public void NoAtmosphereIsNoWind()
        {
            Assert.Equal(0f, WindField.Speed(0f, 1f, 1f), 5);
        }

        [Fact]
        public void TwoPlacesInTheSameWeatherDoNotHaveTheSameWind()
        {
            float here = WindField.Speed(80f, 0.2f, WindField.Variation(new Vector3D(0, 0, 0)));
            float there = WindField.Speed(80f, 0.2f, WindField.Variation(new Vector3D(4000, 0, 0)));

            Assert.NotEqual(here, there, 2);
        }

        [Fact]
        public void TheSamePlaceAlwaysHasTheSameWind()
        {
            // Steady, because a wind that changed every time it was asked would be a heat source
            // that flickered: the sample is taken every few steps and the answer feeds convection.
            Vector3D place = new Vector3D(1234.5, -678.9, 4321.0);

            Assert.Equal(WindField.Variation(place), WindField.Variation(place), 6);
            Assert.Equal(
                WindField.Velocity(Up(30), Axis, 80f, 0.3f, WindField.Variation(place)),
                WindField.Velocity(Up(30), Axis, 80f, 0.3f, WindField.Variation(place)));
        }

        [Fact]
        public void VariationStaysInsideItsRange()
        {
            Random random = new Random(11);

            for (int i = 0; i < 500; i++)
            {
                Vector3D place = new Vector3D(
                    random.NextDouble() * 120000d - 60000d,
                    random.NextDouble() * 120000d - 60000d,
                    random.NextDouble() * 120000d - 60000d);

                float variation = WindField.Variation(place);
                Assert.InRange(variation, 0f, 1f);
            }
        }

        [Fact]
        public void AParkedShipIsNotFlying()
        {
            // The defect this exists to fix, stated as a test: on an earthlike world at sea level,
            // the wind a stationary grid stands in must be under the speed at which the model
            // starts heating things by friction.
            ThermalSettings settings = new ThermalSettings();

            for (int latitude = -85; latitude <= 85; latitude += 5)
            {
                float speed = WindField.Speed(80f, 0f, WindField.Variation(new Vector3D(latitude * 700, 0, 0)));

                Assert.True(speed < settings.FrictionAtSpeedsAbove,
                    "wind at " + latitude + " was " + speed + " m/s, over the friction threshold");
            }
        }
    }
}
