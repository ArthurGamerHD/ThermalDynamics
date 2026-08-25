using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **How much colour the colour channel actually carries**, over the hundred kelvin a block
    /// spends glowing before it fails.
    ///
    /// <para>
    /// backlog.md `B34` asks who the glow's two-channel design is for.
    /// Brightness says how close to failing and colour says how hot, and the colour half is known
    /// to be flat for the 26 % of block types rated under the Draper point. What nobody had asked
    /// is how far it moves for the other 74 % — over their *own* band, which is what a player sees,
    /// rather than over the table's whole range from 800 to 3,000 K.
    /// </para>
    ///
    /// <para>
    /// **The answer is in ΔE rather than in channel values**, because the question is whether a
    /// person can see the difference and `0.09 of green` is not an answer to that. sRGB is converted
    /// to CIE L*a*b* and compared with ΔE76 — the plain Euclidean distance, quoted rather than
    /// ΔE2000 because it is short enough to read in this file and it *overstates* differences in
    /// the saturated reds this table lives in, which is the safe direction for a claim that the
    /// channel is flat.
    /// </para>
    ///
    /// <para>
    /// **The threshold is 2.3**, the usual just-noticeable difference for ΔE76 on adjacent patches
    /// of flat colour. A glowing block is neither flat nor adjacent to its own earlier self, so 2.3
    /// is generous to the channel in the same direction.
    /// </para>
    /// </summary>
    public static class GlowChannelLab
    {
        /// <summary>The ΔE76 usually quoted as a just-noticeable difference.</summary>
        public const double JustNoticeable = 2.3d;

        /// <summary>What one block type's glow band does to its colour.</summary>
        public class Band
        {
            public string TypeId;
            public float CriticalKelvin;

            /// <summary>Where the block starts glowing, K — a hundred below its rating.</summary>
            public float StartKelvin;

            /// <summary>Whether the whole band sits under the Draper point, so the colour is pinned.</summary>
            public bool BelowDraper;

            /// <summary>ΔE76 between the colour at the start of the band and at the rating.</summary>
            public double DeltaE;

            /// <summary>Whether a person could see that difference.</summary>
            public bool Visible
            {
                get { return DeltaE >= JustNoticeable; }
            }
        }

        /// <summary>
        /// sRGB 0..1 to CIE L*a*b*, D65. The transfer curve and the matrix are the standard ones,
        /// written out rather than referenced so the arithmetic can be checked here.
        /// </summary>
        public static double[] Lab(Vector3 srgb)
        {
            double r = Linear(srgb.X);
            double g = Linear(srgb.Y);
            double b = Linear(srgb.Z);

            double x = (0.4124564d * r) + (0.3575761d * g) + (0.1804375d * b);
            double y = (0.2126729d * r) + (0.7151522d * g) + (0.0721750d * b);
            double z = (0.0193339d * r) + (0.1191920d * g) + (0.9503041d * b);

            // D65 white, the reference sRGB is defined against.
            double fx = F(x / 0.95047d);
            double fy = F(y / 1.00000d);
            double fz = F(z / 1.08883d);

            return new[]
            {
                (116d * fy) - 16d,
                500d * (fx - fy),
                200d * (fy - fz)
            };
        }

        private static double Linear(double channel)
        {
            if (channel <= 0.04045d) return channel / 12.92d;
            return Math.Pow((channel + 0.055d) / 1.055d, 2.4d);
        }

        private static double F(double t)
        {
            if (t > 216d / 24389d) return Math.Pow(t, 1d / 3d);
            return ((24389d / 27d * t) + 16d) / 116d;
        }

        /// <summary>ΔE76 between two sRGB colours.</summary>
        public static double DeltaE(Vector3 a, Vector3 b)
        {
            double[] first = Lab(a);
            double[] second = Lab(b);

            double dl = first[0] - second[0];
            double da = first[1] - second[1];
            double db = first[2] - second[2];

            return Math.Sqrt((dl * dl) + (da * da) + (db * db));
        }

        /// <summary>Every block type in the installed catalogue, with what its own band does.</summary>
        public static List<Band> Bands()
        {
            List<Band> bands = new List<Band>();

            foreach (BlockCatalogLab.TypeRow row in BlockCatalogLab.Catalog())
            {
                float critical = row.Properties.CriticalTemperature;
                if (critical <= 0f) continue;

                float start = Incandescence.GlowStartKelvin(critical);

                bands.Add(new Band
                {
                    TypeId = row.TypeId,
                    CriticalKelvin = critical,
                    StartKelvin = start,
                    BelowDraper = critical < Incandescence.DraperKelvin,
                    DeltaE = DeltaE(Incandescence.Colour(start), Incandescence.Colour(critical))
                });
            }

            bands.Sort(delegate (Band a, Band b) { return b.DeltaE.CompareTo(a.DeltaE); });
            return bands;
        }

        /// <summary>
        /// ΔE76 between the colour of the coolest-rated block at its rating and the hottest-rated
        /// block at its own — the *cross-block* distinction, which is the one the design's own
        /// words are about.
        ///
        /// <para>
        /// **This is a different question from the band, and the two answers differ.** Within one
        /// block's hundred-kelvin band the colour barely moves; between two blocks rated hundreds
        /// of kelvin apart it moves a great deal, because the locus is walked over the whole of its
        /// range rather than over a sliver of it. *A decorative block dying dull red beside a
        /// thruster dying orange* is a claim about this number and not about the other one.
        /// </para>
        /// </summary>
        public static double AcrossBlocks(out float coolest, out float hottest)
        {
            coolest = float.PositiveInfinity;
            hottest = 0f;

            foreach (Band band in Bands())
            {
                if (band.CriticalKelvin < coolest) coolest = band.CriticalKelvin;
                if (band.CriticalKelvin > hottest) hottest = band.CriticalKelvin;
            }

            if (float.IsInfinity(coolest)) return 0d;

            return DeltaE(Incandescence.Colour(coolest), Incandescence.Colour(hottest));
        }

        /// <summary>
        /// The most colour any hundred-kelvin band anywhere in the table can carry, and where.
        ///
        /// **A property of the table rather than of the game's blocks**, so it bounds the channel
        /// for any block Keen might ship next — including one this catalogue does not have.
        /// </summary>
        public static double BestBand(out float atKelvin)
        {
            double best = 0d;
            atKelvin = 0f;

            for (float start = Incandescence.ColourFirstKelvin - 200f; start <= 3000f; start += 5f)
            {
                double delta = DeltaE(Incandescence.Colour(start),
                    Incandescence.Colour(start + Incandescence.GlowBandKelvin));

                if (delta <= best) continue;

                best = delta;
                atKelvin = start;
            }

            return best;
        }
    }
}
