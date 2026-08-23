using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The glow: the brightness ramp, and the colour checked against Planck's law rather than
    /// against itself.
    ///
    /// <para>
    /// The two halves are tested differently because they are different claims. **Brightness is a
    /// design decision** — nothing at comfortable temperatures, full at a block's own rating — so
    /// what can be checked is that it does what it says at both ends and in between. **Colour is a
    /// measurement**, and <see cref="Incandescence"/>'s twelve-entry table came out of an
    /// integration that is not in the mod. This file carries that integration — Planck's law
    /// against the CIE observer, in SI units, from first principles — and re-runs it, so nothing
    /// about the colour compares the shipped code to a number the shipped code produced (`E7`).
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
        /// The ramp is nothing at comfortable temperatures, full at the block's own rating, and
        /// rises the whole way between.
        /// </summary>
        [Fact]
        public void TheRampRunsFromComfortableToTheBlocksOwnRating()
        {
            const float critical = 900f;

            Assert.Equal(0f, Incandescence.Glow(Incandescence.ComfortableKelvin, critical));
            Assert.Equal(0f, Incandescence.Glow(200f, critical));
            Assert.Equal(1f, Incandescence.Glow(critical, critical));
            Assert.Equal(1f, Incandescence.Glow(5000f, critical));

            float previous = 0f;
            for (float kelvin = Incandescence.ComfortableKelvin; kelvin <= critical; kelvin += 5f)
            {
                float glow = Incandescence.Glow(kelvin, critical);
                Assert.InRange(glow, previous, 1f);
                previous = glow;
            }
        }

        /// <summary>
        /// **The same fraction of the way to failure looks the same on every block**, which is the
        /// property the ramp exists for and the one an absolute ramp does not have.
        ///
        /// A decorative block rated 500 K and a refractory one rated 1,500 K are both half way to
        /// their own limit at very different temperatures, and both read the same brightness there.
        /// </summary>
        [Fact]
        public void TheSameShareOfTheWayToFailureReadsTheSameOnEveryBlock()
        {
            float[] ratings = new float[] { 500f, 700f, 900f, 1200f, 1522f };
            float first = 0f;

            for (int i = 0; i < ratings.Length; i++)
            {
                float halfway = Incandescence.ComfortableKelvin
                    + (0.5f * (ratings[i] - Incandescence.ComfortableKelvin));

                float glow = Incandescence.Glow(halfway, ratings[i]);
                if (i == 0) first = glow;

                Assert.Equal(first, glow, 4);
            }

            Assert.InRange(first, 0.1f, 0.15f);
        }

        /// <summary>
        /// **Ordinary play is dark.** A hull sitting in any climate a player can breathe in, made
        /// of any block the game ships, glows nothing at all.
        ///
        /// <para>
        /// <c>Planets.xml</c> runs from a 95 K day to a 390 K one, and the hottest a player can
        /// stand outside is the 330 K world. Nothing glows there. At 390 K — 117 °C, which is a
        /// planet a suit is keeping someone alive on — a handful of the lowest-rated block types
        /// do, faintly, and that is the ramp working rather than failing: a block rated 600 K in
        /// 390 K air really is a good part of the way to its limit.
        /// </para>
        /// </summary>
        [Fact]
        public void NoBlockGlowsInAClimateAPlayerCanStandIn()
        {
            if (!GameBlocks.IsInstalled) return;

            const float habitableDay = 330.5f;   // the hottest breathable world in Planets.xml
            const float hottestDay = 390f;       // the hottest world of any kind

            List<BlockCatalogLab.TypeRow> catalog = BlockCatalogLab.Catalog();
            Assert.NotEmpty(catalog);

            int rated = 0;
            int glowingOnTheHottest = 0;
            float brightest = 0f;

            foreach (BlockCatalogLab.TypeRow row in catalog)
            {
                float critical = row.Properties.CriticalTemperature;
                if (critical <= 0f) continue;

                rated++;

                Assert.Equal(0f, Incandescence.Glow(habitableDay, critical));

                float glow = Incandescence.Glow(hottestDay, critical);
                if (glow > 0f) glowingOnTheHottest++;
                if (glow > brightest) brightest = glow;
            }

            Assert.True(rated > 50, "the catalog produced too few ratings to mean anything");

            Assert.True(glowingOnTheHottest < rated / 5,
                glowingOnTheHottest + " of " + rated + " block types glow in 390 K air, which is "
                + "enough of a hull to be a cost rather than a signal");

            Assert.True(brightest < 0.1f,
                "a block glowed at " + brightest.ToString("n3") + " in air alone");
        }

        /// <summary>
        /// The floor the grid scan compares against is the temperature at which the ramp becomes
        /// worth drawing, and the two agree — a block below its own start glows nothing, and one
        /// just above it glows the minimum.
        /// </summary>
        [Fact]
        public void TheDrawFloorIsWhereTheRampBecomesVisible()
        {
            float[] ratings = new float[] { 500f, 900f, 1522f };

            foreach (float critical in ratings)
            {
                float start = Incandescence.GlowStartKelvin(critical);

                Assert.Equal(0f, Incandescence.Glow(start - 1f, critical));
                Assert.InRange(Incandescence.Glow(start + 1f, critical),
                    Incandescence.MinimumVisibleGlow, 0.05f);
            }

            // And it is far enough above any climate to keep an ordinary hull dark.
            Assert.True(Incandescence.GlowStartKelvin(900f) > 400f,
                "a 900 K block starts glowing at "
                + Incandescence.GlowStartKelvin(900f).ToString("n0") + " K");
        }

        /// <summary>
        /// A block rated at or below comfort is a nonsense definition rather than a glow question,
        /// and it does not produce a nonsense ramp.
        /// </summary>
        [Fact]
        public void NonsenseRatingsDoNotProduceANonsenseRamp()
        {
            Assert.Equal(0f, Incandescence.Glow(300f, 0f));
            Assert.Equal(1f, Incandescence.Glow(300f, 200f));
            Assert.Equal(0f, Incandescence.Glow(float.NaN, 900f));
            Assert.Equal(0f, Incandescence.Glow(500f, float.NaN));
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
        /// **A quarter of block types are rated below the Draper point**, and that is why the
        /// brightness ramp is not incandescence.
        ///
        /// <para>
        /// A real solid emits no visible light under 798 K. Keyed to that, a quarter of the game
        /// would fail with no visual warning at all — the measurement that decided the ramp, quoted
        /// in <see cref="Incandescence"/>'s own summary and in
        /// [document-of-intent.md](../../docs/document-of-intent.md), so it is measured here rather
        /// than asserted there. Measured over the installed game's block types, which is the
        /// population both of those sentences are about, so it stands down where the game is not
        /// installed rather than answering from a smaller set.
        /// </para>
        /// </summary>
        [Fact]
        public void AQuarterOfBlockTypesWouldNeverGlowIfTheGlowWerePhysical()
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
            // a real and substantial share of the game would fail before a physical glow ever
            // reached it, and that no single temperature is near every block's rating.
            Assert.InRange(share, 0.1f, 0.45f);
            Assert.True(highest - lowest > 300f,
                "critical temperatures span " + lowest.ToString("n0") + " to "
                + highest.ToString("n0") + " K, which would make a single glow temperature "
                + "workable and the ramp's per-block form unnecessary");
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
