using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The offline planet, and the three questions a field dump could not answer.
    ///
    /// The first run of wind telemetry from a real world produced 2.9 minutes of samples, at
    /// latitudes 5° to 30°, on ground so gentle that sheltering never took more than 7% and
    /// channelling never turned the wind more than a degree for 57% of samples. None of the model's
    /// interesting behaviour was exercised. These hold the simulator to exercising all of it, so
    /// that a change to the model is answerable at a desk and only the *world* has to be checked in
    /// game.
    /// </summary>
    public class WindLabTests
    {
        private static List<WindLab.Row> AtHeight(List<WindLab.Row> rows, double height)
        {
            List<WindLab.Row> found = new List<WindLab.Row>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (Math.Abs(rows[i].HeightAboveGround - height) < 1e-6d) found.Add(rows[i]);
            }
            return found;
        }

        private static double Mean(List<WindLab.Row> rows, Func<WindLab.Row, double> of)
        {
            if (rows.Count == 0) return 0d;
            double total = 0d;
            for (int i = 0; i < rows.Count; i++) total += of(rows[i]);
            return total / rows.Count;
        }

        /// <summary>
        /// The afternoon and the night at one height, **paired by latitude before they are
        /// compared** (`E6`).
        ///
        /// The unpaired form averaged every latitude into one figure and divided the two. That was
        /// wrong in a way nothing noticed while every latitude had wind: the day and the night
        /// samples are not the same set — the sun does not rise at every latitude — so the ratio
        /// carried a latitude difference as well as a diurnal one. It became visible when the band
        /// edges became the calms they should always have been and three of the nine sampled
        /// latitudes went to zero.
        ///
        /// A latitude with no wind at either end of the day has nothing to say about the diurnal
        /// cycle and is skipped rather than averaged in as a zero.
        /// </summary>
        private static void DayNight(
            List<WindLab.Row> rows, double height, out double day, out double night)
        {
            Dictionary<double, double> dayTotal = new Dictionary<double, double>();
            Dictionary<double, double> nightTotal = new Dictionary<double, double>();
            Dictionary<double, int> dayCount = new Dictionary<double, int>();
            Dictionary<double, int> nightCount = new Dictionary<double, int>();

            for (int i = 0; i < rows.Count; i++)
            {
                WindLab.Row r = rows[i];
                if (Math.Abs(r.HeightAboveGround - height) > 1e-6d) continue;

                if (r.Heating > 0.6f) Add(dayTotal, dayCount, r.Latitude, r.Speed);
                else if (r.Heating < 0.05f) Add(nightTotal, nightCount, r.Latitude, r.Speed);
            }

            double dayMeans = 0d, nightMeans = 0d;
            int latitudes = 0;

            foreach (KeyValuePair<double, int> entry in dayCount)
            {
                int nights;
                if (!nightCount.TryGetValue(entry.Key, out nights) || nights == 0) continue;

                double dayMean = dayTotal[entry.Key] / entry.Value;
                double nightMean = nightTotal[entry.Key] / nights;

                // A band edge is calm all day and all night. It agrees with itself and says nothing
                // about the cycle, so it is not evidence either way.
                if (dayMean <= 1e-6d && nightMean <= 1e-6d) continue;

                dayMeans += dayMean;
                nightMeans += nightMean;
                latitudes++;
            }

            Assert.True(latitudes > 0,
                "no latitude at " + height + " m had both a hot afternoon and a cold night with"
                + " wind in it, so this comparison judges nothing");

            day = dayMeans / latitudes;
            night = nightMeans / latitudes;
        }

        private static void Add(Dictionary<double, double> totals, Dictionary<double, int> counts,
            double latitude, double speed)
        {
            double total;
            totals.TryGetValue(latitude, out total);
            totals[latitude] = total + speed;

            int count;
            counts.TryGetValue(latitude, out count);
            counts[latitude] = count + 1;
        }

        // ---- the engine, reproduced ----------------------------------------------------------

        [Fact]
        public void TheEnginesOwnWindFigureIsReproducedExactly()
        {
            // Decompiled from Sandbox.Game.dll:
            //   clamp(1 - (r - AverageRadius)/AtmosphereAltitude, 0, 1) * Density, times MaxWindSpeed.
            // If this drifts, every comparison between a modelled run and a field dump is worthless.
            WindLab.Planet planet = new WindLab.Planet();

            Assert.Equal(80f, planet.WindCeiling(planet.AverageRadius), 4);
            Assert.Equal(40f, planet.WindCeiling(planet.AverageRadius + (planet.AtmosphereAltitude / 2d)), 4);
            Assert.Equal(0f, planet.WindCeiling(planet.AverageRadius + planet.AtmosphereAltitude), 4);
            Assert.Equal(0f, planet.WindCeiling(planet.AverageRadius + (planet.AtmosphereAltitude * 5d)), 4);

            // Below the mean radius it clamps rather than exceeding the maximum.
            Assert.Equal(80f, planet.WindCeiling(planet.AverageRadius - 2000d), 4);
        }

        [Fact]
        public void TheEnginesFalloffIsLinearRatherThanExponential()
        {
            // Worth pinning because it is the surprising half: real air thins exponentially and this
            // does not, so anything reasoning about altitude from physics will disagree with the game.
            WindLab.Planet planet = new WindLab.Planet();

            double quarter = planet.AverageRadius + (planet.AtmosphereAltitude * 0.25d);
            double half = planet.AverageRadius + (planet.AtmosphereAltitude * 0.5d);
            double threeQuarters = planet.AverageRadius + (planet.AtmosphereAltitude * 0.75d);

            Assert.Equal(
                planet.WindCeiling(quarter) - planet.WindCeiling(half),
                planet.WindCeiling(half) - planet.WindCeiling(threeQuarters), 3);
        }

        [Fact]
        public void TheEnginesWindIgnoresTheGroundEntirelyAndIsInvertedByIt()
        {
            // The finding that decided this model: altitude is measured against the mean sphere, so
            // a valley floor below it reads *windier* than the ridge above. Reproduced deliberately.
            WindLab.Planet planet = new WindLab.Planet();

            float valley = planet.WindCeiling(planet.AverageRadius - 500d);
            float ridge = planet.WindCeiling(planet.AverageRadius + 500d);

            Assert.True(valley > ridge,
                "the engine gives a valley more wind than a ridge; the sim must not quietly fix that");
        }

        // ---- the daily cycle -----------------------------------------------------------------

        [Fact]
        public void TheSurfaceIsWindiestByDayAndTheAirAloftIsWindiestAtNight()
        {
            // The behaviour that cannot be seen in a three-minute session, and the reason the
            // simulator exists. One modelled day shows both halves of the cycle at once.
            //
            // Slope wind is switched off here on purpose. This tests the *boundary layer* cycle —
            // mixing by day, decoupling by night — and slope wind is a separate mechanism that
            // legitimately opposes it near the ground. Leaving it on makes this measure the two
            // fighting each other, which is what the test below is for.
            WindLab.Options options = new WindLab.Options();
            options.SlopeStrength = 0f;

            List<WindLab.Row> rows = WindLab.Run(new WindLab.Planet(), options);

            double day, night;

            DayNight(rows, 2d, out day, out night);
            Assert.True(day > night * 1.3d,
                "at 2 m the afternoon should be much windier than the night: " + day + " vs " + night);

            DayNight(rows, 400d, out day, out night);
            Assert.True(night > day * 1.3d,
                "at 400 m the nocturnal jet should beat the afternoon: " + night + " vs " + day);
        }

        [Fact]
        public void SlopeWindCanOverturnTheSurfaceCycleOnSlopedGround()
        {
            // An emergent result worth recording rather than smoothing away: with slope winds on,
            // the mean 2 m wind over this planet's hills is very nearly as strong before dawn as in
            // the afternoon — the nocturnal drainage flow all but cancels the boundary layer's own
            // night-time minimum.
            //
            // That is what happens on real sloped ground, and it is why a valley at night is not the
            // still place the boundary layer alone would predict.
            List<WindLab.Row> withSlope = WindLab.Run(new WindLab.Planet(), new WindLab.Options());

            WindLab.Options off = new WindLab.Options();
            off.SlopeStrength = 0f;
            List<WindLab.Row> without = WindLab.Run(new WindLab.Planet(), off);

            double dayOn, nightOn, dayOff, nightOff;
            DayNight(withSlope, 2d, out dayOn, out nightOn);
            DayNight(without, 2d, out dayOff, out nightOff);

            Assert.True(nightOn > nightOff * 1.3d,
                "slope wind should lift the night-time surface wind materially: "
                + nightOff + " to " + nightOn);

            Assert.True(dayOff > nightOff * 1.3d, "and without it the plain cycle still shows");
        }

        [Fact]
        public void ThereIsAHeightBetweenThemWhereTheDayBarelyMatters()
        {
            WindLab.Options options = new WindLab.Options();
            options.SlopeStrength = 0f;
            List<WindLab.Row> rows = WindLab.Run(new WindLab.Planet(), options);

            double day, night;
            DayNight(rows, 100d, out day, out night);

            double ratio = day / night;
            Assert.InRange(ratio, 0.7d, 1.3d);
        }

        [Fact]
        public void AboveTheBoundaryLayerTheDayStopsMatteringAtAll()
        {
            // The fault the first field dump caught: a grid 7 km above the ground was reading a
            // full-strength nocturnal jet. Above the boundary layer there is no cycle to have.
            List<WindLab.Row> rows = WindLab.Run(new WindLab.Planet(), new WindLab.Options());

            double day, night;
            DayNight(rows, 1200d, out day, out night);

            Assert.InRange(day / night, 0.93d, 1.07d);
        }

        [Fact]
        public void TheHeatingCurveCoversTheWholeDay()
        {
            // A lag needs a day to move through. The field run reached 0.286 and fell to 0.003
            // because it was three minutes long and after sunset; a modelled day must not have that
            // excuse, or none of the tests above are testing anything.
            List<WindLab.Row> rows = WindLab.Run(new WindLab.Planet(), new WindLab.Options());

            float low = 1f, high = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Heating < low) low = rows[i].Heating;
                if (rows[i].Heating > high) high = rows[i].Heating;
            }

            Assert.True(low < 0.05f, "the modelled day must contain a proper night, got " + low);
            Assert.True(high > 0.8f, "and a proper afternoon, got " + high);
        }

        // ---- the ground ----------------------------------------------------------------------

        [Fact]
        public void TheSimulatedGroundIsRoughEnoughToExerciseTheTerrainModel()
        {
            // The world the first dump was taken in was flat enough that shelter never took more
            // than 7% and channelling never turned the wind past a degree in most samples. A
            // simulator that reproduced that would be no use for developing the terrain model.
            List<WindLab.Row> rows = AtHeight(
                WindLab.Run(new WindLab.Planet(), new WindLab.Options()), 10d);

            float leastShelter = 1f, mostSpeedUp = 0f, mostChannel = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Shelter < leastShelter) leastShelter = rows[i].Shelter;
                if (rows[i].SpeedUp > mostSpeedUp) mostSpeedUp = rows[i].SpeedUp;
                if (rows[i].ChannelDegrees > mostChannel) mostChannel = rows[i].ChannelDegrees;
            }

            Assert.True(leastShelter < 0.8f, "somewhere should be properly sheltered, got " + leastShelter);
            Assert.True(mostSpeedUp > 1.15f, "somewhere should be properly exposed, got " + mostSpeedUp);
            Assert.True(mostChannel > 15f, "somewhere should be properly channelled, got " + mostChannel);
        }

        [Fact]
        public void LevellingTheGroundSwitchesEveryTerrainEffectOff()
        {
            // The control. Whatever the terrain factors are doing on the rough world, they must be
            // doing exactly nothing on a flat one — otherwise they are reading something that is
            // not the ground.
            WindLab.Planet flat = new WindLab.Planet();
            flat.Ground = new WindLab.FlatTerrain();

            List<WindLab.Row> rows = AtHeight(WindLab.Run(flat, new WindLab.Options()), 10d);
            Assert.NotEmpty(rows);

            for (int i = 0; i < rows.Count; i++)
            {
                Assert.Equal(1f, rows[i].SpeedUp, 4);
                Assert.Equal(1f, rows[i].Shelter, 4);
                Assert.Equal(0f, rows[i].ChannelDegrees, 3);
            }
        }

        [Fact]
        public void TurningTerrainOffMatchesFlatteningTheWorld()
        {
            // Two ways of removing the ground's influence that have to agree, since one is a setting
            // a player can move and the other is the physical case it is standing in for.
            WindLab.Options off = new WindLab.Options();
            off.TerrainInfluence = 0f;

            WindLab.Planet flat = new WindLab.Planet();
            flat.Ground = new WindLab.FlatTerrain();

            List<WindLab.Row> a = AtHeight(WindLab.Run(new WindLab.Planet(), off), 10d);
            List<WindLab.Row> b = AtHeight(WindLab.Run(flat, new WindLab.Options()), 10d);

            Assert.Equal(a.Count, b.Count);
            Assert.Equal(Mean(a, r => r.SpeedUp), Mean(b, r => r.SpeedUp), 4);
            Assert.Equal(Mean(a, r => r.Shelter), Mean(b, r => r.Shelter), 4);
        }

        // ---- the whole planet ------------------------------------------------------------------

        [Fact]
        public void EveryLatitudeIsSampledIncludingTheOnesAFieldRunNeverReaches()
        {
            // The field data spans 5° to 30°. The circulation bands live at 15°, 45° and 75°, and
            // the equator is where the pattern's known fault is. A model run has to cover all of it.
            List<WindLab.Row> rows = WindLab.Run(new WindLab.Planet(), new WindLab.Options());

            bool equator = false, mid = false, polar = false;
            for (int i = 0; i < rows.Count; i++)
            {
                double latitude = Math.Abs(rows[i].Latitude);
                if (latitude < 1d) equator = true;
                if (latitude > 35d && latitude < 65d) mid = true;
                if (latitude > 70d) polar = true;
            }

            Assert.True(equator && mid && polar);
        }

        [Fact]
        public void TheWindIsNeverNegativeNorAbsurdAnywhereOnThePlanetAtAnyHour()
        {
            // A sweep over every latitude, height and hour at once, which is the cheapest thing the
            // simulator buys: whatever a change to the model does, it has to survive the whole globe
            // rather than the one place a test world happens to sit.
            WindLab.Options options = new WindLab.Options();
            options.WeatherIntensity = 1f;

            List<WindLab.Row> rows = WindLab.Run(new WindLab.Planet(), options);
            Assert.True(rows.Count > 10000);

            for (int i = 0; i < rows.Count; i++)
            {
                WindLab.Row r = rows[i];
                Assert.True(r.Speed >= 0f, "negative wind at latitude " + r.Latitude);
                Assert.True(r.Speed < 200f, "absurd wind " + r.Speed + " at latitude " + r.Latitude);
                Assert.InRange(r.SpeedUp, 0.1f, 2f);
                Assert.InRange(r.Shelter, 0.1f, 1.0001f);
                Assert.InRange(r.ChannelDegrees, 0f, 180f);
            }
        }

        [Fact]
        public void TheCsvCarriesTheSameColumnNamesTheGameWrites()
        {
            // The point of the whole exercise: a modelled day and a measured session line up without
            // anything being translated between them.
            string csv = WindLab.Csv(WindLab.Run(new WindLab.Planet(), new WindLab.Options()));
            string header = csv.Substring(0, csv.IndexOf('\n'));

            foreach (string column in new string[]
            {
                "wind_agl_m", "wind_ceiling", "wind_band_share", "wind_profile", "wind_heating",
                "wind_speedup", "wind_shelter", "wind_channel_deg", "wind_speed", "wind_bearing_deg",
            })
            {
                Assert.Contains(column, header);
            }
        }

        [Fact]
        public void ARunIsRepeatable()
        {
            // Nothing in the field is random, so two runs of the same planet must agree exactly, or
            // a change measured against a previous run is measuring noise.
            List<WindLab.Row> a = WindLab.Run(new WindLab.Planet(), new WindLab.Options());
            List<WindLab.Row> b = WindLab.Run(new WindLab.Planet(), new WindLab.Options());

            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i += 97)
            {
                Assert.Equal(a[i].Speed, b[i].Speed, 6);
                Assert.Equal(a[i].BearingDegrees, b[i].BearingDegrees, 6);
            }
        }
    }
}
