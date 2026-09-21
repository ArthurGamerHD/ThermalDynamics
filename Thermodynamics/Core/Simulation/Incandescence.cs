using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public static class Incandescence
    {
        public const float GlowBandKelvin = 100f;

        public const float DraperKelvin = 798f;

/// <summary>GlowStartKelvin operation.</summary>
        public static float GlowStartKelvin(float critical)
        {
            if (critical <= 0f) return 0f;
            return critical > GlowBandKelvin ? critical - GlowBandKelvin : 0f;
        }

/// <summary>Glow operation.</summary>
        public static float Glow(float kelvin, float critical)
        {
            if (float.IsNaN(kelvin) || float.IsNaN(critical) || critical <= 0f) return 0f;
            if (kelvin >= critical) return 1f;

/// <summary>GlowStartKelvin operation.</summary>
            float start = GlowStartKelvin(critical);
            if (kelvin <= start) return 0f;

            return (kelvin - start) / (critical - start);
        }

        public const float ColourFirstKelvin = 800f;

        public const float ColourStepKelvin = 200f;

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

        public static int ColourSamples
        {
            get { return Locus.Length / 3; }
        }

/// <summary>Colour operation.</summary>
        public static Vector3 Colour(float kelvin)
        {
            float position = (kelvin - ColourFirstKelvin) / ColourStepKelvin;
            int last = ColourSamples - 1;

            if (position <= 0f || float.IsNaN(position)) return Sample(0);
            if (position >= last) return Sample(last);

            int low = (int)position;
            float blend = position - low;
/// <summary>Sample operation.</summary>
            Vector3 a = Sample(low);
/// <summary>Sample operation.</summary>
            Vector3 b = Sample(low + 1);

            return a + ((b - a) * blend);
        }

/// <summary>Sample operation.</summary>
        public static Vector3 Sample(int index)
        {
            int i = index * 3;
            return new Vector3(Locus[i], Locus[i + 1], Locus[i + 2]);
        }
    }
}
