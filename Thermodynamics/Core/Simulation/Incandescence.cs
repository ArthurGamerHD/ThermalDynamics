using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Models thermal glow and incandescence of blocks as they heat up.
    /// Provides glow intensity calculation and color mapping based on temperature.
    /// Based on the Draper point (798K / 525C) where materials begin to glow visibly.
    /// </summary>
    public static class Incandescence
    {
        /// <summary>
        /// Temperature band width in Kelvin over which glow transitions from 0 to 1.
        /// Glow increases linearly over this range from GlowStartKelvin to CriticalTemperature.
        /// Default 100K means glow goes from 0 to 1 over a 100-degree range.
        /// </summary>
        public const float GlowBandKelvin = 100f;

        /// <summary>
        /// Draper point temperature in Kelvin (525°C = 798K).
        /// The temperature at which materials begin to emit visible light (red glow).
        /// Below this temperature, glow is zero regardless of critical threshold.
        /// </summary>
        public const float DraperKelvin = 798f;


        /// <summary>
        /// Calculates the temperature at which glow begins for a given critical temperature.
        /// Glow starts at critical temperature minus GlowBandKelvin, but not below Draper point.
        /// </summary>
        /// <param name="critical">Critical temperature in Kelvin at which block fails.</param>
        /// <returns>Temperature in Kelvin at which glow begins (0 if no glow).</returns>
        /// <remarks>
        /// Glow start = max(DraperKelvin, critical - GlowBandKelvin)
        /// 
        /// Examples:
        ///   critical = 1000K -> start = max(798, 900) = 900K
        ///   critical = 900K -> start = max(798, 800) = 800K
        ///   critical = 800K -> start = max(798, 700) = 798K
        ///   critical = 500K -> start = 0 (below Draper point)
        /// </remarks>
        public static float GlowStartKelvin(float critical)
        {
            if (critical <= 0f) return 0f;
            return critical > GlowBandKelvin ? critical - GlowBandKelvin : 0f;
        }


        /// <summary>
        /// Calculates the glow intensity for a block at a given temperature.
        /// Returns 0 if below glow start, 1 if at or above critical, and linear
        /// interpolation in between.
        /// </summary>
        /// <param name="kelvin">Current temperature in Kelvin.</param>
        /// <param name="critical">Critical temperature in Kelvin at which block fails.</param>
        /// <returns>Glow intensity (0-1). 0 = no glow, 1 = full incandescence.</returns>
        /// <remarks>
        /// Glow calculation:
        ///   start = GlowStartKelvin(critical)
        ///   if kelvin <= start: glow = 0
        ///   if kelvin >= critical: glow = 1
        ///   otherwise: glow = (kelvin - start) / (critical - start)
        ///
        /// This provides a smooth visual transition from dark (cold) to bright (critical).
        /// </remarks>
        public static float Glow(float kelvin, float critical)
        {
            if (float.IsNaN(kelvin) || float.IsNaN(critical) || critical <= 0f) return 0f;
            if (kelvin >= critical) return 1f;

            float start = GlowStartKelvin(critical);
            if (kelvin <= start) return 0f;

            // Linear interpolation within glow band
            return (kelvin - start) / (critical - start);
        }

        /// <summary>
        /// Starting temperature in Kelvin for color calculation.
        /// Colors are calculated relative to this baseline.
        /// </summary>
        public const float ColourFirstKelvin = 800f;

        /// <summary>
        /// Temperature step in Kelvin between color samples.
        /// Each sample represents a color at a specific temperature interval.
        /// </summary>
        public const float ColourStepKelvin = 200f;

        /// <summary>
        /// Pre-calculated color locus points in RGB format.
        /// Each color sample is 3 consecutive floats (R, G, B).
        /// Values represent the color of a block at different temperatures:
        /// - Index 0 (800K): Bright red
        /// - Index 1 (1000K): Orange-red
        /// - Index 2 (1200K): Orange
        /// - Index 3 (1400K): Yellow-orange
        /// - Index 4 (1600K): Yellow
        /// - Index 5 (1800K): Yellow-white
        /// - Index 6 (2000K): White
        /// - Index 7 (2200K): Bright white
        /// - Index 8 (2400K): Blue-white
        /// - Index 9 (2600K): Blue-white
        /// - Index 10 (2800K): Blue-white
        /// - Index 11 (3000K): Blue-white
        /// </summary>
        private static readonly float[] Locus = new float[]
        {
            // R,    G,    B
            1.0000f, 0.0000f, 0.0000f,  // 800K  - bright red
            1.0000f, 0.1853f, 0.0000f,  // 1000K - orange-red
            1.0000f, 0.2999f, 0.0000f,  // 1200K - orange
            1.0000f, 0.3824f, 0.0000f,  // 1400K - yellow-orange
            1.0000f, 0.4488f, 0.0000f,  // 1600K - yellow
            1.0000f, 0.5047f, 0.0000f,  // 1800K - yellow-white
            1.0000f, 0.5529f, 0.0838f,  // 2000K - white
            1.0000f, 0.5953f, 0.1831f,  // 2200K - bright white
            1.0000f, 0.6329f, 0.2562f,  // 2400K - bluish white
            1.0000f, 0.6666f, 0.3194f,  // 2600K - white-blue
            1.0000f, 0.6971f, 0.3766f,  // 2800K - white-blue
            1.0000f, 0.7247f, 0.4295f,  // 3000K - white-blue
        };

        /// <summary>
        /// Number of color samples in the Locus array.
        /// Each sample is 3 floats (RGB), so array length / 3 = sample count.
        /// </summary>
        public static int ColourSamples
        {
            get { return Locus.Length / 3; }
        }


        /// <summary>
        /// Gets the incandescence color for a given temperature.
        /// Uses linear interpolation between color samples for smooth color transitions.
        /// </summary>
        /// <param name="kelvin">Temperature in Kelvin.</param>
        /// <returns>RGB color vector representing the incandescence color.</returns>
        /// <remarks>
        /// Color calculation:
        ///   position = (kelvin - ColourFirstKelvin) / ColourStepKelvin
        ///   
        /// If position <= 0: return first color sample (800K)
        /// If position >= last: return last color sample
        /// Otherwise: linearly interpolate between floor(position) and ceil(position)
        ///
        /// Example temperatures and colors:
        ///   798K - Draper point (beginning of visible glow)
        ///   800K - Bright red (1, 0, 0)
        ///   1000K - Orange-red
        ///   1500K - Yellow-orange
        ///   2000K - White
        ///   3000K+ - Blue-white (very hot)
        /// </remarks>
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

            // Linear interpolation between samples
            return a + ((b - a) * blend);
        }


        /// <summary>
        /// Gets a single color sample from the Locus array.
        /// </summary>
        /// <param name="index">Sample index (0 to ColourSamples-1).</param>
        /// <returns>RGB color vector for the sample.</returns>
        public static Vector3 Sample(int index)
        {
            int i = index * 3;
            return new Vector3(Locus[i], Locus[i + 1], Locus[i + 2]);
        }
    }
}
