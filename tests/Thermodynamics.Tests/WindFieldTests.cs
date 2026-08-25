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

        /// <summary>
        /// How far toward the nearer pole the wind blows here: +1 poleward, −1 equatorward.
        ///
        /// **The half of the direction nothing measured.** `WindField.Direction` has two
        /// components and every test in this class read only the first, which is how a meridional
        /// term that was poleward on both sides of the equator — where Earth's trades converge —
        /// survived as long as it did. backlog.md `B14`.
        /// </summary>
        private static float Poleward(double latitude)
        {
            Vector3 up = Up(latitude);
            Vector3 east = Vector3.Normalize(Vector3.Cross(Axis, up));
            Vector3 north = Vector3.Normalize(Vector3.Cross(up, east));

            return Vector3.Dot(WindField.Direction(up, Axis), latitude < 0d ? -north : north);
        }

        /// <summary>The wind as a velocity: direction times the strength of its own band.</summary>
        private static Vector3 Velocity(double latitude)
        {
            Vector3 up = Up(latitude);
            return WindField.Direction(up, Axis) * WindField.BandStrength(up, Axis);
        }

        [Fact]
        public void WindBlowsAlongTheGroundAndNotThroughIt()
        {
            // The one thing that would be nonsense: air moving into or out of the planet.
            for (int latitude = -85; latitude <= 85; latitude += 5)
            {
                Vector3 up = Up(latitude);
                Vector3 wind = WindField.Direction(up, Axis);

                Assert.True(Math.Abs(Vector3.Dot(wind, up)) < 1e-4f,
                    "wind at " + latitude + " should be tangent to the surface");

                // A unit direction, or none at all where the band has no strength to give one:
                // the equator and the two band edges are calms, and a calm has no bearing.
                float strength = WindField.BandStrength(up, Axis);
                if (strength < 1e-3f)
                {
                    Assert.Equal(0f, wind.Length(), 3);
                    continue;
                }

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

        /// <summary>
        /// Between bands the wind passes through a calm rather than a step: a line on the map where
        /// it flips end for end would be visible from a cockpit as a wall.
        ///
        /// <para>
        /// **Measured on the velocity, not on the bearing.** The old form read the east component
        /// of the unit direction, which is not the wind — and it passed for as long as the wind
        /// reversed at full strength across the equator, because the east component happened to be
        /// zero on both sides of that reversal. What has to be continuous is the vector a ship is
        /// standing in.
        /// </para>
        /// </summary>
        [Fact]
        public void TheWindSwingsThroughTheCalmsRatherThanReversingAcrossALine()
        {
            Vector3 previous = Velocity(-89);

            for (int latitude = -88; latitude <= 89; latitude++)
            {
                Vector3 current = Velocity(latitude);
                Assert.True((current - previous).Length() < 0.25f,
                    "wind turned too sharply between " + (latitude - 1) + " and " + latitude
                    + ": " + previous + " to " + current);
                previous = current;
            }
        }

        /// <summary>
        /// The sideways component follows the band rather than the hemisphere: equatorward in the
        /// trades and the polar easterlies, poleward in the westerlies.
        ///
        /// **This is `B14`.** It used to be poleward everywhere, so air diverged from the equator
        /// where Earth's trades converge, and it was the largest known fault in the pattern.
        /// </summary>
        [Fact]
        public void TheSidewaysComponentFollowsTheBand()
        {
            foreach (int latitude in new[] { 15, -15, 75, -75 })
            {
                Assert.True(Poleward(latitude) < -0.1f,
                    "the trades and the polar easterlies blow toward the equator, not away from it;"
                    + " at " + latitude + " the poleward component is " + Poleward(latitude));
            }

            foreach (int latitude in new[] { 45, -45 })
            {
                Assert.True(Poleward(latitude) > 0.1f,
                    "the westerlies blow toward the pole; at " + latitude
                    + " the poleward component is " + Poleward(latitude));
            }
        }

        /// <summary>
        /// The equator is a calm, not a seam. Two ships either side of it are not in opposite
        /// hurricanes, which is what a meridional component that stepped across latitude 0 gave
        /// them.
        /// </summary>
        [Fact]
        public void TheEquatorIsACalmRatherThanAReversal()
        {
            Assert.True(Velocity(0).Length() < 0.02f,
                "the doldrums should be calm, not the windiest place on the planet");

            Assert.True((Velocity(0.5) - Velocity(-0.5)).Length() < 0.2f,
                "half a degree either side of the equator should be nearly the same wind");
        }

        /// <summary>
        /// The band edges are calms too, and the strongest wind is in the middle of a band. The
        /// pattern a reader of a globe expects, and the one a pilot would learn.
        /// </summary>
        [Fact]
        public void ABandIsStrongestInItsMiddleAndCalmAtItsEdges()
        {
            foreach (int latitude in new[] { 0, 30, 60, 90 })
            {
                Assert.True(WindField.BandStrength(Up(latitude), Axis) < 0.01f,
                    latitude + " is a band edge and should be calm");
            }

            foreach (int latitude in new[] { 15, 45, 75 })
            {
                Assert.True(WindField.BandStrength(Up(latitude), Axis) > 0.99f,
                    latitude + " is the middle of a band and should be its windiest");
            }

            // Symmetric, like the bands themselves.
            for (int latitude = 5; latitude <= 85; latitude += 10)
            {
                Assert.Equal(WindField.BandStrength(Up(latitude), Axis),
                    WindField.BandStrength(Up(-latitude), Axis), 3);
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
