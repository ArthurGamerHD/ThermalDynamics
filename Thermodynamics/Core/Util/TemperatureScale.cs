using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public static class TemperatureScale
    {
        public const float DefaultMax = 1000f;
        public const float DefaultLow = 267f;
        public const float DefaultHigh = 500f;

/// <summary>ToHsv operation.</summary>
        public static Vector3 ToHsv(float value, float max = DefaultMax, float low = DefaultLow, float high = DefaultHigh)
        {
            if (!(max > 0f)) max = 1f;
            if (!(low > 0f)) low = 0f;
            if (high < low) high = low;
            if (max < high) max = high;

            float t = Math.Max(0f, Math.Min(max, value));

            float h = 240f / 360f;
            float s = 1f;
            float v = 0.5f;

            if (t < low)
            {
                v = (1.5f * (t / low)) - 1f;
            }
/// <summary>if operation.</summary>
            else if (t < high)
            {
                h = (240f - ((t - low) / (high - low) * 240f)) / 360f;
            }
            else
            {
                h = 0f;
                s = max > high ? 1f - (2f * ((t - high) / (max - high))) : 1f;
            }

            return new Vector3(h, s, v);
        }

/// <summary>ToCelsiusString operation.</summary>
        public static string ToCelsiusString(float kelvin)
        {
            return ThermalConstants.KelvinToCelsius(kelvin).ToString("n2") + "°C";
        }
    }
}
