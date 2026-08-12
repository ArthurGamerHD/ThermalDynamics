using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The temperature to colour ramp used by every debug view and by the HUD.
    /// </summary>
    public static class TemperatureScale
    {
        public const float DefaultMax = 1000f;
        public const float DefaultLow = 267f;
        public const float DefaultHigh = 500f;

        /// <summary>
        /// Maps a value onto an HSV colour: black below <paramref name="low"/>, blue at
        /// <paramref name="low"/>, sweeping through to red at <paramref name="high"/>, then
        /// desaturating to white at <paramref name="max"/>.
        /// </summary>
        public static Vector3 ToHsv(float value, float max = DefaultMax, float low = DefaultLow, float high = DefaultHigh)
        {
            if (max <= 0f) max = 1f;
            if (low <= 0f) low = float.Epsilon;
            if (high <= low) high = low + float.Epsilon;
            if (max <= high) max = high + float.Epsilon;

            float t = Math.Max(0f, Math.Min(max, value));

            float h = 240f / 360f;
            float s = 1f;
            float v = 0.5f;

            if (t < low)
            {
                v = (1.5f * (t / low)) - 1f;
            }
            else if (t < high)
            {
                h = (240f - ((t - low) / (high - low) * 240f)) / 360f;
            }
            else
            {
                h = 0f;
                s = 1f - (2f * ((t - high) / (max - high)));
            }

            return new Vector3(h, s, v);
        }

        public static string ToCelsiusString(float kelvin)
        {
            return ThermalConstants.KelvinToCelsius(kelvin).ToString("n2") + "°C";
        }

        public static string ToFahrenheitString(float kelvin)
        {
            return ThermalConstants.KelvinToFahrenheit(kelvin).ToString("n2") + "°F";
        }
    }
}
