using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// What a hot block looks like, from Planck's law rather than from a design choice.
    ///
    /// <para>
    /// **This is not a warning and it is not keyed to anything the block is rated for.** Hot matter
    /// glows because it is hot, at a temperature that is the same for a jump drive and for a piece
    /// of armour, and the whole value of the channel is that a player learns to read a colour the
    /// way they read one on a real forge. Both halves — brightness and colour — are functions of
    /// temperature alone.
    /// </para>
    ///
    /// <para>
    /// **A ramp keyed to each block's rating was tried and withdrawn.** It reads as a fuel gauge
    /// for failure rather than as a hot object, and the game already tells a player that a block is
    /// in trouble: damaged blocks carry the engine's own damage effects, and how close a given
    /// block is to its limit is something a player works out from playing. Making the glow say it
    /// twice buys nothing and costs the one thing the channel is for, which is that the same
    /// temperature looks the same everywhere. See
    /// [document-of-intent.md](../../../../../docs/document-of-intent.md), Natural feedback.
    /// </para>
    ///
    /// <para>
    /// The consequence is a real limit and it is why the glow cannot be the only channel: the
    /// shipped definitions run from 500 K to 1,522 K of critical temperature, and **26 % of block
    /// types are rated below the Draper point**, so a quarter of the game fails before it ever
    /// glows. The audio cue, which *is* keyed to a block's own rating, is what covers those.
    /// </para>
    ///
    /// <para>
    /// Free of any game type but <c>Vector3</c>, and a pure function of temperature, so all of it
    /// is checked outside a session against a numerical integration of Planck's law (`C5`, `E7`).
    /// </para>
    /// </summary>
    public static class Incandescence
    {
        /// <summary>
        /// The Draper point, K: the temperature at which a solid first glows visibly, dull red, in
        /// the dark. Below this the glow is nothing, which is a measurement rather than a cutoff —
        /// the luminance here is already four decades under the one at
        /// <see cref="FullGlowKelvin"/>.
        /// </summary>
        public const float DraperKelvin = 798f;

        /// <summary>
        /// Where the ramp reaches full, K. Forge-welding heat, bright orange, and above the
        /// critical temperature of 92 % of shipped block types — so a block that reaches it is one
        /// the game is about to take away.
        /// </summary>
        public const float FullGlowKelvin = 1500f;

        /// <summary>
        /// The two constants of the visible-band luminance of a black body,
        /// <c>L(T) ∝ T^p · exp(−b/T)</c>.
        ///
        /// <para>
        /// **Measured, not chosen.** Both were fitted to a numerical integration of Planck's law
        /// against the CIE photopic response over 700–1,900 K, and the fit holds to 2.9 % over that
        /// whole range — far inside anything an eye resolves. `IncandescenceTests` re-runs that
        /// integration and fails if either constant drifts from it.
        /// </para>
        ///
        /// <para>
        /// <see cref="LuminanceWienKelvin"/> is Wien's <c>c₂/λ</c> and says which wavelength is
        /// doing the work: 0.0143878 / 21,735 = **662 nm**, the deep red end of the band. That is
        /// the cross-check that the fit is physics and not curve drawing — a dull-hot body is seen
        /// almost entirely in the red, which is why it looks red.
        /// </para>
        /// </summary>
        public const float LuminanceExponent = 1.7929f;

        public const float LuminanceWienKelvin = 21735f;

        /// <summary>
        /// The exponent that turns luminance into brightness: CIE lightness, <c>L*</c> ∝
        /// <c>Y^(1/3)</c>.
        ///
        /// <para>
        /// **The eye's own transfer function, and it is required rather than decorative.** The
        /// luminance of a black body rises by six decades between the Draper point and
        /// <see cref="FullGlowKelvin"/>; handed to a renderer as a 0..1 intensity that would leave
        /// everything under about 1,300 K at zero, which is not what a hot bar looks like. The
        /// output here is display-referred, so the display-referred transfer belongs in it.
        /// </para>
        /// </summary>
        public const float LightnessExponent = 1f / 3f;

        /// <summary>
        /// How brightly a block at <paramref name="kelvin"/> glows, 0 at the Draper point and 1 at
        /// <see cref="FullGlowKelvin"/> and above.
        /// </summary>
        public static float Glow(float kelvin)
        {
            if (kelvin <= DraperKelvin || float.IsNaN(kelvin)) return 0f;
            if (kelvin >= FullGlowKelvin) return 1f;

            double ratio = Luminance(kelvin) / Luminance(FullGlowKelvin);
            if (ratio <= 0d) return 0f;

            float glow = (float)Math.Pow(ratio, LightnessExponent);
            if (glow < 0f) return 0f;
            if (glow > 1f) return 1f;
            return glow;
        }

        /// <summary>
        /// Visible-band luminance in arbitrary units. Only ratios of it mean anything, which is why
        /// the fit carries no scale.
        /// </summary>
        public static double Luminance(double kelvin)
        {
            if (kelvin <= 0d) return 0d;
            return Math.Pow(kelvin, LuminanceExponent) * Math.Exp(-LuminanceWienKelvin / kelvin);
        }

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
