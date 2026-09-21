using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class IncandescenceTests
    {
        private const double Planck = 6.62607015e-34;      // J s
        private const double LightSpeed = 2.99792458e8;    // m/s
        private const double Boltzmann = 1.380649e-23;     // J/K

        private const double SecondRadiation = 1.4387769e-2;

/// <summary>Spectral operation.</summary>
        private static double Spectral(double metres, double kelvin)
        {
            double exponent = (Planck * LightSpeed) / (metres * Boltzmann * kelvin);
            if (exponent > 700d) return 0d;

            return (2d * Planck * LightSpeed * LightSpeed / Math.Pow(metres, 5d))
                / (Math.Exp(exponent) - 1d);
        }


        [Fact]
/// <summary>TheRampIsTheLastHundredKelvinBeforeTheRating operation.</summary>
        public void TheRampIsTheLastHundredKelvinBeforeTheRating()
        {
            const float critical = 900f;

            Assert.Equal(0f, Incandescence.Glow(critical - Incandescence.GlowBandKelvin, critical));
            Assert.Equal(0f, Incandescence.Glow(300f, critical));
            Assert.Equal(0.5f, Incandescence.Glow(critical - 50f, critical), 4);
            Assert.Equal(1f, Incandescence.Glow(critical, critical));
            Assert.Equal(1f, Incandescence.Glow(5000f, critical));

            float previous = 0f;
            for (float kelvin = critical - Incandescence.GlowBandKelvin;
                kelvin <= critical; kelvin += 2f)
            {
                float glow = Incandescence.Glow(kelvin, critical);
                Assert.InRange(glow, previous, 1f);
                previous = glow;
            }
        }

        [Fact]
/// <summary>TheSameDistanceFromFailureReadsTheSameOnEveryBlock operation.</summary>
        public void TheSameDistanceFromFailureReadsTheSameOnEveryBlock()
        {
            float[] ratings = new float[] { 583f, 700f, 900f, 1200f, 1522f };

            foreach (float critical in ratings)
            {
                Assert.Equal(0.25f, Incandescence.Glow(critical - 75f, critical), 4);
                Assert.Equal(0.75f, Incandescence.Glow(critical - 25f, critical), 4);
                Assert.Equal(0f, Incandescence.Glow(critical - 101f, critical));
            }
        }

        [Fact]
/// <summary>NoBlockGlowsFromTheWeatherOnAnyPlanetTheGameShips operation.</summary>
        public void NoBlockGlowsFromTheWeatherOnAnyPlanetTheGameShips()
        {
            if (!GameBlocks.IsInstalled) return;

            const float hottestPlanetDay = 390f;

            List<BlockCatalogLab.TypeRow> catalog = BlockCatalogLab.Catalog();
            Assert.NotEmpty(catalog);

            int rated = 0;
            float lowest = float.PositiveInfinity;

            foreach (BlockCatalogLab.TypeRow row in catalog)
            {
                float critical = row.Properties.CriticalTemperature;
                if (critical <= 0f) continue;

                rated++;
                if (critical < lowest) lowest = critical;

                Assert.Equal(0f, Incandescence.Glow(hottestPlanetDay, critical));
            }

            Assert.True(rated > 50, "the catalog produced too few ratings to mean anything");
            Assert.True(lowest - Incandescence.GlowBandKelvin > hottestPlanetDay,
                "the coolest block type is rated " + lowest.ToString("n0")
                + " K, so its band reaches into the hottest planet's air");
        }

        [Fact]
/// <summary>TheDrawFloorIsWhereTheRampStarts operation.</summary>
        public void TheDrawFloorIsWhereTheRampStarts()
        {
            float[] ratings = new float[] { 583f, 900f, 1522f };

            foreach (float critical in ratings)
            {
                float start = Incandescence.GlowStartKelvin(critical);

                Assert.Equal(critical - Incandescence.GlowBandKelvin, start, 3);
                Assert.Equal(0f, Incandescence.Glow(start, critical));
                Assert.True(Incandescence.Glow(start + 1f, critical) > 0f);
            }
        }

        [Fact]
/// <summary>NonsenseRatingsDoNotProduceANonsenseRamp operation.</summary>
        public void NonsenseRatingsDoNotProduceANonsenseRamp()
        {
            Assert.Equal(0f, Incandescence.GlowStartKelvin(60f));
            Assert.Equal(0.5f, Incandescence.Glow(30f, 60f), 4);
            Assert.Equal(1f, Incandescence.Glow(60f, 60f));

            Assert.Equal(0f, Incandescence.Glow(300f, 0f));
            Assert.Equal(0f, Incandescence.Glow(float.NaN, 900f));
            Assert.Equal(0f, Incandescence.Glow(500f, float.NaN));
        }

        [Fact]
/// <summary>TheColourTableIsThePlanckianLocus operation.</summary>
        public void TheColourTableIsThePlanckianLocus()
        {
            for (int i = 0; i < Incandescence.ColourSamples; i++)
            {
                float kelvin = Incandescence.ColourFirstKelvin + (i * Incandescence.ColourStepKelvin);
                AssertColour(kelvin, Incandescence.Sample(i), 0.01f);
            }

            for (int i = 0; i + 1 < Incandescence.ColourSamples; i++)
            {
                float kelvin = Incandescence.ColourFirstKelvin
                    + ((i + 0.5f) * Incandescence.ColourStepKelvin);

                AssertColour(kelvin, Incandescence.Colour(kelvin), 0.05f);
            }
        }

        [Fact]
/// <summary>HotterIsYellower operation.</summary>
        public void HotterIsYellower()
        {
            Vector3 dull = Incandescence.Colour(900f);
            Vector3 orange = Incandescence.Colour(1600f);
            Vector3 bright = Incandescence.Colour(2800f);

            Assert.Equal(1f, dull.X, 3);
            Assert.Equal(1f, orange.X, 3);
            Assert.Equal(1f, bright.X, 3);

            Assert.True(dull.Y < orange.Y && orange.Y < bright.Y,
                "green rose from " + dull.Y + " to " + orange.Y + " to " + bright.Y);
            Assert.True(dull.Z <= orange.Z && orange.Z < bright.Z,
                "blue rose from " + dull.Z + " to " + orange.Z + " to " + bright.Z);

            Assert.True(bright.Z < bright.Y, "a block at 2,800 K should still read orange");
        }

        [Fact]
/// <summary>AQuarterOfBlockTypesWouldNeverGlowIfTheBrightnessWerePhysical operation.</summary>
        public void AQuarterOfBlockTypesWouldNeverGlowIfTheBrightnessWerePhysical()
        {
            if (!GameBlocks.IsInstalled) return;

            List<BlockCatalogLab.TypeRow> catalog = BlockCatalogLab.Catalog();
            Assert.NotEmpty(catalog);

            int below = 0;
            int total = 0;
            float lowest = float.PositiveInfinity;
            float highest = 0f;

            foreach (BlockCatalogLab.TypeRow row in catalog)
            {
                float critical = row.Properties.CriticalTemperature;
                if (critical <= 0f) continue;

                total++;
                if (critical < Incandescence.DraperKelvin) below++;
                if (critical < lowest) lowest = critical;
                if (critical > highest) highest = critical;
            }

            float share = below / (float)total;

            Assert.InRange(share, 0.1f, 0.45f);
            Assert.True(highest - lowest > 300f,
                "critical temperatures span " + lowest.ToString("n0") + " to "
                + highest.ToString("n0") + " K, which would make one glow temperature workable and "
                + "the band's per-block form unnecessary");
        }

/// <summary>AssertColour operation.</summary>
        private static void AssertColour(float kelvin, Vector3 actual, float tolerance)
        {
/// <summary>IntegratedColour operation.</summary>
            Vector3 expected = IntegratedColour(kelvin);

            Assert.True(Math.Abs(expected.X - actual.X) <= tolerance
                && Math.Abs(expected.Y - actual.Y) <= tolerance
                && Math.Abs(expected.Z - actual.Z) <= tolerance,
                kelvin.ToString("n0") + " K: table " + actual + " against the integral " + expected);
        }

/// <summary>IntegratedColour operation.</summary>
        private static Vector3 IntegratedColour(double kelvin)
        {
            const int steps = 800;
            const double low = 360d;
            const double high = 830d;

            double x = 0d, y = 0d, z = 0d;
            for (int i = 0; i <= steps; i++)
            {
                double nm = low + ((high - low) * i / steps);
                double weight = (i == 0 || i == steps) ? 0.5d : 1d;
/// <summary>Spectral operation.</summary>
                double power = Spectral(nm * 1e-9d, kelvin) * weight;

/// <summary>ObserverX operation.</summary>
                x += power * ObserverX(nm);
/// <summary>ObserverY operation.</summary>
                y += power * ObserverY(nm);
/// <summary>ObserverZ operation.</summary>
                z += power * ObserverZ(nm);
            }

            double sum = x + y + z;
            x /= sum;
            y /= sum;
            z /= sum;

            double r = (3.2406d * x) - (1.5372d * y) - (0.4986d * z);
            double g = (-0.9689d * x) + (1.8758d * y) + (0.0415d * z);
            double b = (0.0557d * x) - (0.2040d * y) + (1.0570d * z);

            double peak = Math.Max(r, Math.Max(g, b));
            return new Vector3(
                (float)Transfer(Math.Max(0d, r / peak)),
                (float)Transfer(Math.Max(0d, g / peak)),
                (float)Transfer(Math.Max(0d, b / peak)));
        }

/// <summary>Transfer operation.</summary>
        private static double Transfer(double linear)
        {
            return linear <= 0.0031308d
                ? 12.92d * linear
                : (1.055d * Math.Pow(linear, 1d / 2.4d)) - 0.055d;
        }


/// <summary>Lobe operation.</summary>
        private static double Lobe(double nm, double centre, double lower, double upper)
        {
            double spread = nm < centre ? lower : upper;
            double t = (nm - centre) / spread;
            return Math.Exp(-0.5d * t * t);
        }

/// <summary>ObserverX operation.</summary>
        private static double ObserverX(double nm)
        {
            return (1.056d * Lobe(nm, 599.8d, 37.9d, 31.0d))
                + (0.362d * Lobe(nm, 442.0d, 16.0d, 26.7d))
                - (0.065d * Lobe(nm, 501.1d, 20.4d, 26.2d));
        }

/// <summary>ObserverY operation.</summary>
        private static double ObserverY(double nm)
        {
            return (0.821d * Lobe(nm, 568.8d, 46.9d, 40.5d))
                + (0.286d * Lobe(nm, 530.9d, 16.3d, 31.1d));
        }

/// <summary>ObserverZ operation.</summary>
        private static double ObserverZ(double nm)
        {
            return (1.217d * Lobe(nm, 437.0d, 11.8d, 36.0d))
                + (0.681d * Lobe(nm, 459.0d, 26.0d, 13.8d));
        }
    }
}
