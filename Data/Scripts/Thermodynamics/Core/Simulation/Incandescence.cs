using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// What a hot block looks like: how brightly it glows, and what colour.
    ///
    /// <para>
    /// **The two halves answer different questions, and that is the design.** Brightness says *how
    /// close this block is to failing* — nothing at temperatures a person would be comfortable in,
    /// rising as the block heats, full at its own critical temperature and above. Colour says *how
    /// hot it actually is* — the Planckian locus, absolute, so a block glowing at 500 K is deep red
    /// and one at 2,000 K is orange whatever either is rated for. A player reads danger off the
    /// brightness and temperature off the colour, and neither has to be calibrated against the
    /// other.
    /// </para>
    ///
    /// <para>
    /// **Brightness is deliberately not incandescence.** A real solid emits no visible light below
    /// the Draper point at 798 K, and 26 % of shipped block types are rated below that — a quarter
    /// of the game would fail with no visual warning at all. The
    /// [governing prior](../../../../../docs/document-of-intent.md#the-governing-prior-game-mod-first)
    /// settles it: the cost of the strict form is real and paid by the player, so legibility wins
    /// and the ramp is keyed to the block's own rating. What stays physical is the colour, where
    /// nothing is lost by being right.
    /// </para>
    ///
    /// <para>
    /// Free of any game type but <c>Vector3</c>, and a pure function of its arguments, so all of it
    /// is checked outside a session — the colour against a numerical integration of Planck's law
    /// (`C5`, `E7`).
    /// </para>
    /// </summary>
    public static class Incandescence
    {
        /// <summary>
        /// The warmest a block can be and still not glow at all, K.
        ///
        /// **The temperature the mod already calls comfortable**, so the two statements cannot
        /// drift apart: it is what <see cref="SuitThermal.ComfortKelvin"/> holds a player at. A
        /// hull sitting in an ordinary climate is below it everywhere, so an ordinary ship is dark.
        /// </summary>
        public const float ComfortableKelvin = SuitThermal.ComfortKelvin;

        /// <summary>
        /// The Draper point, K: where a real solid first glows visibly, dull red, in the dark.
        ///
        /// Not what the brightness ramp uses — see the summary — and kept because it is what the
        /// colour table starts at, below which the locus has no visible colour to report.
        /// </summary>
        public const float DraperKelvin = 798f;

        /// <summary>
        /// How sharply the ramp bends, as the exponent on the share of the way from
        /// <see cref="ComfortableKelvin"/> to a block's own rating.
        ///
        /// <para>
        /// **A straight line spends most of its length faintly lit, which is both wrong and
        /// expensive.** Wrong because a real hot thing brightens far faster than its temperature
        /// rises, and expensive because a barely-glowing block still costs a render write every
        /// pass — on a hot planet that is every block on the ship. A cube keeps the low end dark
        /// and the top end unchanged: a block a quarter of the way up reads 1.6 %, half way reads
        /// 12 %, and at its rating it is full.
        /// </para>
        /// </summary>
        public const float GlowExponent = 3f;

        /// <summary>
        /// The faintest glow worth writing, below which a block is left alone.
        ///
        /// Two per cent of full on an emissive surface is not a thing an eye picks out, and this is
        /// what makes <see cref="GlowStartKelvin"/> a real floor rather than a formality — without
        /// it every block warmer than a person would like carries a glow nobody can see and a
        /// render message every pass.
        /// </summary>
        public const float MinimumVisibleGlow = 0.02f;

        /// <summary>
        /// How brightly a block at <paramref name="kelvin"/> rated for
        /// <paramref name="critical"/> glows: 0 at <see cref="ComfortableKelvin"/> and below, 1 at
        /// its rating and above.
        /// </summary>
        public static float Glow(float kelvin, float critical)
        {
            if (float.IsNaN(kelvin) || float.IsNaN(critical)) return 0f;

            // A block with no rating has nothing to be a share of, and is not a glow question.
            if (critical <= 0f) return 0f;
            if (critical <= ComfortableKelvin) return kelvin >= critical ? 1f : 0f;
            if (kelvin >= critical) return 1f;
            if (kelvin <= ComfortableKelvin) return 0f;

            float share = (kelvin - ComfortableKelvin) / (critical - ComfortableKelvin);
            float glow = (float)Math.Pow(share, GlowExponent);

            return glow < MinimumVisibleGlow ? 0f : glow;
        }

        /// <summary>
        /// The coolest a block rated for <paramref name="critical"/> can be and still be worth
        /// drawing, K. Below this <see cref="Glow"/> is zero, which is what lets a grid decide in
        /// one comparison whether anything on it is worth a pass.
        /// </summary>
        public static float GlowStartKelvin(float critical)
        {
            if (critical <= ComfortableKelvin) return critical;
            return ComfortableKelvin + ((critical - ComfortableKelvin) * GlowStartShare);
        }

        /// <summary>
        /// The share of the way to its rating at which a block starts being worth drawing, which is
        /// <see cref="MinimumVisibleGlow"/> undone by <see cref="GlowExponent"/>: **0.271**.
        ///
        /// Held rather than computed because the per-node test that decides whether a hull is worth
        /// scanning at all runs it, and a <c>Math.Pow</c> per block on a cold hull is exactly the
        /// cost this feature is not allowed to have.
        /// </summary>
        public static readonly float GlowStartShare =
            (float)Math.Pow(MinimumVisibleGlow, 1d / GlowExponent);

        /// <summary>
        /// Temperatures the colour table is sampled at, K, and the step between them.
        /// </summary>
        public const float ColourFirstKelvin = 800f;

        public const float ColourStepKelvin = 200f;

        /// <summary>
        /// The colour of a black body at each of those temperatures, sRGB 0..1, normalised so the
        /// brightest channel is full — brightness is <see cref="Glow"/>'s job and carrying it twice
        /// would square it.
        ///
        /// <para>
        /// Computed from Planck's law against the CIE 1931 observer, through the sRGB matrix and
        /// its transfer curve, and regenerated by `IncandescenceTests` rather than trusted. A table
        /// rather than the integral because the integral is four hundred terms and this is read per
        /// hot block per frame; twelve entries at 200 K spacing hold the locus to under a
        /// perceptible step, which the same test checks against the integral at the midpoints.
        /// </para>
        /// </summary>
        private static readonly float[] Locus = new float[]
        {
            1.0000f, 0.0000f, 0.0000f,   //   800 K
            1.0000f, 0.1853f, 0.0000f,   // 1,000 K
            1.0000f, 0.2999f, 0.0000f,   // 1,200 K
            1.0000f, 0.3824f, 0.0000f,   // 1,400 K
            1.0000f, 0.4488f, 0.0000f,   // 1,600 K
            1.0000f, 0.5047f, 0.0000f,   // 1,800 K
            1.0000f, 0.5529f, 0.0838f,   // 2,000 K
            1.0000f, 0.5953f, 0.1831f,   // 2,200 K
            1.0000f, 0.6329f, 0.2562f,   // 2,400 K
            1.0000f, 0.6666f, 0.3194f,   // 2,600 K
            1.0000f, 0.6971f, 0.3766f,   // 2,800 K
            1.0000f, 0.7247f, 0.4295f,   // 3,000 K
        };

        /// <summary>Number of temperatures the table holds.</summary>
        public static int ColourSamples
        {
            get { return Locus.Length / 3; }
        }

        /// <summary>
        /// The colour of a black body at <paramref name="kelvin"/>, sRGB 0..1. Clamped at both ends
        /// of the table: below 800 K nothing is visible anyway, and above 3,000 K no block survives
        /// long enough for the difference to be seen.
        /// </summary>
        public static Vector3 Colour(float kelvin)
        {
            float position = (kelvin - ColourFirstKelvin) / ColourStepKelvin;
            int last = ColourSamples - 1;

            if (position <= 0f || float.IsNaN(position)) return Sample(0);
            if (position >= last) return Sample(last);

            int low = (int)position;
            float blend = position - low;
            Vector3 a = Sample(low);
            Vector3 b = Sample(low + 1);

            return a + ((b - a) * blend);
        }

        /// <summary>The table entry at <paramref name="index"/>.</summary>
        public static Vector3 Sample(int index)
        {
            int i = index * 3;
            return new Vector3(Locus[i], Locus[i + 1], Locus[i + 2]);
        }
    }
}
