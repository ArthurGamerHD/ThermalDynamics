using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The glow, checked against Planck's law rather than against itself.
    ///
    /// <para>
    /// <see cref="Incandescence"/> ships two fitted constants and a twelve-entry colour table, and
    /// every one of those numbers came out of an integration that is not in the mod. This file
    /// carries that integration — Planck's law against the CIE observer, in SI units, from first
    /// principles — and re-runs it. Nothing here compares the shipped code to a number a previous
    /// run of the shipped code produced, which is the only way a fit can be checked at all (`E7`).
    /// </para>
    /// </summary>
    public class IncandescenceTests
    {
        private const double Planck = 6.62607015e-34;      // J s
        private const double LightSpeed = 2.99792458e8;    // m/s
        private const double Boltzmann = 1.380649e-23;     // J/K

        /// <summary>Wien's second radiation constant, m K. The one cross-check on the Wien fit.</summary>
        private const double SecondRadiation = 1.4387769e-2;

        /// <summary>Spectral radiance of a black body, W/(m^2 sr m).</summary>
        private static double Spectral(double metres, double kelvin)
        {
            double exponent = (Planck * LightSpeed) / (metres * Boltzmann * kelvin);
            if (exponent > 700d) return 0d;

            return (2d * Planck * LightSpeed * LightSpeed / Math.Pow(metres, 5d))
                / (Math.Exp(exponent) - 1d);
        }

        /// <summary>
        /// The CIE photopic response, from the Gaussian fit in Wyman, Sloan and Shirley (2013).
        /// Wavelength in micrometres.
        /// </summary>
        private static double Photopic(double micrometres)
        {
            double d = micrometres - 0.559d;
            return 1.019d * Math.Exp(-285.4d * d * d);
        }

        /// <summary>Luminance of a black body over the visible band, arbitrary units.</summary>
        private static double Luminance(double kelvin)
        {
            const int steps = 800;
            const double low = 380e-9;
            const double high = 780e-9;

            double sum = 0d;
            for (int i = 0; i <= steps; i++)
            {
                double metres = low + ((high - low) * i / steps);
                double weight = (i == 0 || i == steps) ? 0.5d : 1d;
                sum += weight * Spectral(metres, kelvin) * Photopic(metres * 1e6d);
            }

            return sum * (high - low) / steps;
        }

        /// <summary>
        /// The two fitted constants still describe the integral they were fitted to.
        ///
        /// A luminance ratio is what the glow reads, so the ratio is what this checks, over the
        /// whole range the ramp lives in.
        /// </summary>
        [Fact]
        public void TheLuminanceFitStillFollowsPlancksLaw()
        {
            double worst = 0d;
            float worstAt = 0f;

            for (float kelvin = 700f; kelvin <= 1900f; kelvin += 10f)
            {
                double truth = Luminance(kelvin) / Luminance(Incandescence.FullGlowKelvin);
                double fit = Incandescence.Luminance(kelvin)
                    / Incandescence.Luminance(Incandescence.FullGlowKelvin);

                double error = Math.Abs((fit / truth) - 1d);
                if (error > worst)
                {
                    worst = error;
                    worstAt = kelvin;
                }
            }

            Assert.True(worst < 0.05d,
                "the luminance fit is out by " + (worst * 100d).ToString("n1") + " % at "
                + worstAt.ToString("n0") + " K");
        }

        /// <summary>
        /// The Wien constant in the fit is a wavelength, and it is a red one.
        ///
        /// **This is the check that the fit is physics.** <c>c2/b</c> has to land in the visible
        /// band, at the red end, because that is where a dull-hot body is seen — and if it did not,
        /// the fit would be two numbers that happen to draw the right curve.
        /// </summary>
        [Fact]
        public void TheFittedWienConstantIsARedWavelength()
        {
            double metres = SecondRadiation / Incandescence.LuminanceWienKelvin;
            double nanometres = metres * 1e9d;

            Assert.InRange(nanometres, 600d, 700d);
        }

        /// <summary>
        /// The ramp runs from nothing at the Draper point to full at forge heat, and rises the
        /// whole way.
        /// </summary>
        [Fact]
        public void TheGlowRampRunsFromTheDraperPointToFull()
        {
            Assert.Equal(0f, Incandescence.Glow(Incandescence.DraperKelvin));
            Assert.Equal(0f, Incandescence.Glow(500f));
            Assert.Equal(1f, Incandescence.Glow(Incandescence.FullGlowKelvin));
            Assert.Equal(1f, Incandescence.Glow(5000f));

            float previous = 0f;
            for (float kelvin = Incandescence.DraperKelvin + 1f;
                kelvin <= Incandescence.FullGlowKelvin; kelvin += 5f)
            {
                float glow = Incandescence.Glow(kelvin);
                Assert.InRange(glow, previous, 1f);
                previous = glow;
            }

            Assert.True(previous > 0.9f, "the ramp never reached the top: " + previous);
        }

        /// <summary>
        /// **The eye's transfer function is doing real work**, and this is how much.
        ///
        /// Without it the glow is raw luminance, which spans six decades over the ramp: a block at
        /// 1,100 K — hot enough to be visibly orange in a dark room — would come out at three
        /// thousandths and render as black. The cube root is what makes the middle of the ramp
        /// visible, and this fails if it is ever taken out.
        /// </summary>
        [Fact]
        public void TheLightnessExponentIsWhatMakesTheMiddleOfTheRampVisible()
        {
            double raw = Luminance(1100d) / Luminance(Incandescence.FullGlowKelvin);
            Assert.True(raw < 0.01d, "raw luminance at 1,100 K was already visible: " + raw);

            float glow = Incandescence.Glow(1100f);
            Assert.InRange(glow, 0.1f, 0.25f);
        }

        /// <summary>
        /// **Nothing the game ships glows from the weather.** No block type, in the air of any
        /// planet in <c>Planets.xml</c> — up to the 390 K world, which is 117 °C — is anywhere near
        /// the temperature at which a solid emits light.
        ///
        /// This is what makes the glow mean something when it appears: it is the ship's own doing,
        /// never the sky's.
        /// </summary>
        [Fact]
        public void NothingGlowsFromTheWeatherOnAnyPlanetTheGameShips()
        {
            const float hottestPlanetDay = 390f;

            Assert.Equal(0f, Incandescence.Glow(hottestPlanetDay));
            Assert.True(hottestPlanetDay < Incandescence.DraperKelvin,
                "a planet got hotter than the Draper point, which would make every hull on it glow");
        }

        /// <summary>
        /// The colour table is the Planckian locus, checked entry by entry against the integral —
        /// including at the midpoints between entries, which is where a table too coarse to
        /// interpolate would show.
        /// </summary>
        [Fact]
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

                // Wider, because this is the interpolation error of a straight chord across a
                // curve rather than the table's own accuracy. A twentieth of full scale is under
                // the step an eye resolves on a dim emissive surface.
                AssertColour(kelvin, Incandescence.Colour(kelvin), 0.05f);
            }
        }

        /// <summary>
        /// Colour goes red, then orange, then yellow, which is the thing a player actually reads
        /// off a hot block. Stated as an ordering so it survives any change to the table that keeps
        /// the physics.
        /// </summary>
        [Fact]
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

            // And nothing is blue-hot at temperatures a block reaches.
            Assert.True(bright.Z < bright.Y, "a block at 2,800 K should still read orange");
        }

        /// <summary>
        /// A quarter of the game is rated below the Draper point, so the glow cannot be the only
        /// channel — which is the reason the audio cue is keyed to a block's own rating instead.
        ///
        /// <para>
        /// This is a claim about the shipped definitions rather than about the glow, and it is
        /// quoted in <see cref="Incandescence"/>'s own summary and in
        /// [document-of-intent.md](../../docs/document-of-intent.md), so it is measured here rather
        /// than asserted there. Measured over the installed game's block types, which is the
        /// population both of those sentences are about, so it stands down where the game is not
        /// installed rather than answering from a smaller set.
        /// </para>
        /// </summary>
        [Fact]
        public void AQuarterOfBlockTypesFailBeforeTheyGlow()
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

            // Wide, because this is a fact about the game rather than about this repository and it
            // moves when Keen ships blocks. What it is guarding is the shape of the argument: that
            // a real and substantial share of the game dies before it glows, and that no single
            // temperature is near every block's rating.
            Assert.InRange(share, 0.1f, 0.45f);
            Assert.True(highest - lowest > 300f,
                "critical temperatures span " + lowest.ToString("n0") + " to "
                + highest.ToString("n0") + " K, which would make a single watch temperature "
                + "workable and HeatCueState.WatchFraction unnecessary");
        }

        private static void AssertColour(float kelvin, Vector3 actual, float tolerance)
        {
            Vector3 expected = IntegratedColour(kelvin);

            Assert.True(Math.Abs(expected.X - actual.X) <= tolerance
                && Math.Abs(expected.Y - actual.Y) <= tolerance
                && Math.Abs(expected.Z - actual.Z) <= tolerance,
                kelvin.ToString("n0") + " K: table " + actual + " against the integral " + expected);
        }

        /// <summary>
        /// The Planckian locus at one temperature, computed the long way: Planck's law against the
        /// CIE 1931 observer, through the sRGB matrix and its transfer curve, normalised so the
        /// brightest channel is full.
        /// </summary>
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
                double power = Spectral(nm * 1e-9d, kelvin) * weight;

                x += power * ObserverX(nm);
                y += power * ObserverY(nm);
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

        /// <summary>The sRGB transfer curve, linear light to display.</summary>
        private static double Transfer(double linear)
        {
            return linear <= 0.0031308d
                ? 12.92d * linear
                : (1.055d * Math.Pow(linear, 1d / 2.4d)) - 0.055d;
        }

        // The CIE 1931 colour matching functions, from the multi-lobe Gaussian fit in Wyman, Sloan
        // and Shirley (2013). Wavelength in nanometres.

        private static double Lobe(double nm, double centre, double lower, double upper)
        {
            double spread = nm < centre ? lower : upper;
            double t = (nm - centre) / spread;
            return Math.Exp(-0.5d * t * t);
        }

        private static double ObserverX(double nm)
        {
            return (1.056d * Lobe(nm, 599.8d, 37.9d, 31.0d))
                + (0.362d * Lobe(nm, 442.0d, 16.0d, 26.7d))
                - (0.065d * Lobe(nm, 501.1d, 20.4d, 26.2d));
        }

        private static double ObserverY(double nm)
        {
            return (0.821d * Lobe(nm, 568.8d, 46.9d, 40.5d))
                + (0.286d * Lobe(nm, 530.9d, 16.3d, 31.1d));
        }

        private static double ObserverZ(double nm)
        {
            return (1.217d * Lobe(nm, 437.0d, 11.8d, 36.0d))
                + (0.681d * Lobe(nm, 459.0d, 26.0d, 13.8d));
        }
    }
}
