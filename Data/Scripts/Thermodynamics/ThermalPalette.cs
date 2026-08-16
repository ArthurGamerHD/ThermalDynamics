using System;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// Maps a temperature onto the colour a thermal camera would show for it.
    ///
    /// A real sensor has no absolute colours. It has a **span** — a low and a high temperature —
    /// and it stretches its whole palette across that span, which is why an operator adjusts the
    /// range to see anything useful. This does the same: <see cref="Normalise"/> takes the two
    /// bounds from the settings, and everything outside them clips to black or to white.
    ///
    /// Two palettes, both real ones. White-hot is a straight luminance ramp and is what most
    /// footage is shot in. Ironbow spends more of its range in the middle, where the interesting
    /// differences on a ship are, and is easier to read a gradient in.
    /// </summary>
    public static class ThermalPalette
    {
        /// <summary>Ironbow stops, cold to hot. Chosen to match the classic sensor palette.</summary>
        private static readonly Vector3[] IronbowStops =
        {
            new Vector3(0.00f, 0.00f, 0.00f),   // black
            new Vector3(0.11f, 0.00f, 0.31f),   // deep violet
            new Vector3(0.42f, 0.00f, 0.44f),   // magenta
            new Vector3(0.75f, 0.13f, 0.20f),   // red
            new Vector3(0.94f, 0.45f, 0.00f),   // orange
            new Vector3(1.00f, 0.82f, 0.20f),   // yellow
            new Vector3(1.00f, 1.00f, 1.00f),   // white hot
        };

        /// <summary>Position of a temperature in the current span, 0..1.</summary>
        public static float Normalise(float kelvin, float minKelvin, float maxKelvin)
        {
            float span = maxKelvin - minKelvin;
            if (span <= 0f) return 0f;

            float t = (kelvin - minKelvin) / span;
            if (t < 0f) return 0f;
            if (t > 1f) return 1f;
            return t;
        }

        /// <summary>Colour for a position in the span.</summary>
        public static Vector3 Sample(float t, bool greyscale)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            if (greyscale) return new Vector3(t, t, t);

            float scaled = t * (IronbowStops.Length - 1);
            int index = (int)scaled;
            if (index >= IronbowStops.Length - 1) return IronbowStops[IronbowStops.Length - 1];

            return Vector3.Lerp(IronbowStops[index], IronbowStops[index + 1], scaled - index);
        }

        /// <summary>
        /// Colour for a temperature, scaled by an intensity.
        ///
        /// The result is meant for additive blending, where the colour is light being added to the
        /// frame rather than paint being laid over it. That is what gives a hot object its bloom
        /// and lets two warm things overlapping read as hotter than either.
        /// </summary>
        public static Color Colour(float kelvin, float minKelvin, float maxKelvin, bool greyscale, float intensity)
        {
            Vector3 rgb = Sample(Normalise(kelvin, minKelvin, maxKelvin), greyscale) * intensity;
            return new Color(Clamp01(rgb.X), Clamp01(rgb.Y), Clamp01(rgb.Z), 1f);
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
