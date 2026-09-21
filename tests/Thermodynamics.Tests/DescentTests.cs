using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    public class DescentTests
    {
/// <summary>Readings operation.</summary>
        private static List<Descent.Reading> Readings()
        {
            return Descent.Run();
        }

        [Fact]
/// <summary>TheWindBlowsWhileTheHullIsAboveTheSurface operation.</summary>
        public void TheWindBlowsWhileTheHullIsAboveTheSurface()
        {
/// <summary>Readings operation.</summary>
            List<Descent.Reading> readings = Readings();

            Assert.True(Descent.At(readings, 40).WindSpeed > 0f);
            Assert.True(Descent.At(readings, 0).WindSpeed > 0f);
            Assert.Equal(1f, Descent.At(readings, 0).WindBurial);
        }

        [Fact]
/// <summary>TheWindIsGoneOnceTheHullIsWhollyBuried operation.</summary>
        public void TheWindIsGoneOnceTheHullIsWhollyBuried()
        {
/// <summary>Readings operation.</summary>
            List<Descent.Reading> readings = Readings();

            foreach (double height in new double[] { -6, -10, -20, -100, -1000, -20000 })
            {
                Descent.Reading reading = Descent.At(readings, height);

                Assert.Equal(0f, reading.WindSpeed);
                Assert.Equal(0f, reading.WindBurial);
            }
        }

        [Fact]
/// <summary>TheWindFadesOverTheHullRatherThanStopping operation.</summary>
        public void TheWindFadesOverTheHullRatherThanStopping()
        {
/// <summary>Readings operation.</summary>
            List<Descent.Reading> readings = Readings();

            float surface = Descent.At(readings, 0).WindSpeed;
            float half = Descent.At(readings, -Descent.HullReach * 0.5).WindSpeed;

            Assert.True(half > 0f);
            Assert.True(half < surface);
            Assert.InRange(Descent.At(readings, -Descent.HullReach * 0.5).WindBurial, 0.4f, 0.6f);
        }

        [Fact]
/// <summary>TheWindFallsMonotonicallyThroughTheDig operation.</summary>
        public void TheWindFallsMonotonicallyThroughTheDig()
        {
/// <summary>Readings operation.</summary>
            List<Descent.Reading> readings = Readings();

            float previous = float.MaxValue;
            for (int i = 0; i < readings.Count; i++)
            {
                if (readings[i].Height > 0) continue;

                Assert.True(readings[i].WindSpeed <= previous + 1e-4f,
                    "wind rose at " + readings[i].Height + " m");
                previous = readings[i].WindSpeed;
            }
        }

        [Fact]
/// <summary>TheSunIsGoneAsSoonAsTheHullIsUnderTheSurface operation.</summary>
        public void TheSunIsGoneAsSoonAsTheHullIsUnderTheSurface()
        {
/// <summary>Readings operation.</summary>
            List<Descent.Reading> readings = Readings();

            Assert.True(Descent.At(readings, 10).SolarWatts > 0f);

            for (int i = 0; i < readings.Count; i++)
            {
                if (readings[i].Height >= 0) continue;

                Assert.Equal(1f, readings[i].SolarOcclusion);
                Assert.Equal(0f, readings[i].SolarWatts);
            }
        }

        [Fact]
/// <summary>AmbientDampsToTheRockAndThenWarmsTowardsTheCore operation.</summary>
        public void AmbientDampsToTheRockAndThenWarmsTowardsTheCore()
        {
/// <summary>Readings operation.</summary>
            List<Descent.Reading> readings = Readings();
/// <summary>PlanetThermalProperties operation.</summary>
            PlanetThermalProperties planet = new PlanetThermalProperties();

            float shallow = Descent.At(readings, -100).AmbientKelvin;
            Assert.InRange(shallow, planet.UndergroundTemperature - 1f, planet.UndergroundTemperature + 1f);

            Assert.InRange(
                Descent.At(readings, -1000).AmbientKelvin,
                planet.UndergroundTemperature - 1f, planet.UndergroundTemperature + 1f);

            Assert.True(Descent.At(readings, -5000).AmbientKelvin > shallow);
            Assert.True(Descent.At(readings, -20000).AmbientKelvin > Descent.At(readings, -5000).AmbientKelvin);
            Assert.True(Descent.At(readings, -59000).AmbientKelvin < planet.CoreTemperature);
        }

        [Fact]
/// <summary>AmbientRisesMonotonicallyBelowTheDeadzone operation.</summary>
        public void AmbientRisesMonotonicallyBelowTheDeadzone()
        {
/// <summary>Readings operation.</summary>
            List<Descent.Reading> readings = Readings();
/// <summary>PlanetThermalProperties operation.</summary>
            PlanetThermalProperties planet = new PlanetThermalProperties();

            float previous = 0f;
            for (int i = 0; i < readings.Count; i++)
            {
                if (readings[i].Depth <= planet.SealevelDeadzone) continue;

                Assert.True(readings[i].AmbientKelvin >= previous - 1e-3f,
                    "ambient fell at " + readings[i].Height + " m");
                previous = readings[i].AmbientKelvin;
            }
        }

        [Fact]
/// <summary>TheGamesFlagTurnsBeforeTheHullIsActuallyBuried operation.</summary>
        public void TheGamesFlagTurnsBeforeTheHullIsActuallyBuried()
        {
/// <summary>Readings operation.</summary>
            List<Descent.Reading> readings = Readings();
            Descent.Reading dug = Descent.At(readings, -1);

            Assert.True(dug.GameUnderground);
            Assert.True(dug.WindBurial > 0f);
            Assert.True(dug.WindSpeed > 0f);
        }

        [Fact]
/// <summary>TheCsvCarriesEveryReading operation.</summary>
        public void TheCsvCarriesEveryReading()
        {
/// <summary>Readings operation.</summary>
            List<Descent.Reading> readings = Readings();
            string csv = Descent.Csv(readings);

            Assert.StartsWith("height_m,depth_m,ambient_k", csv);
            Assert.Equal(readings.Count + 1, csv.TrimEnd('\n').Split('\n').Length);
        }
    }
}
