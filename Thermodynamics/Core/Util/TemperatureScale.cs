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
        /// <remarks>
        /// Each of the three divisions is guarded by the branch it sits in rather than by nudging
        /// the anchors apart beforehand. Nudging was the first attempt and it did not work: the
        /// nudge was <c>float.Epsilon</c>, the smallest denormal there is, and <c>6f +
        /// float.Epsilon</c> is exactly <c>6f</c>. A caller asking for a six-face ramp — which the
        /// exposed-faces overlay does — still divided zero by zero and got a NaN saturation for
        /// every fully exposed block. A guard has to be relative to the value it protects, or it
        /// has to be a branch; this is the branch.
        /// </remarks>
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
                // t < low and t >= 0, so low > 0.
                v = (1.5f * (t / low)) - 1f;
            }
            else if (t < high)
            {
                // low <= t < high, so high > low.
                h = (240f - ((t - low) / (high - low) * 240f)) / 360f;
            }
            else
            {
                h = 0f;
                s = max > high ? 1f - (2f * ((t - high) / (max - high))) : 1f;
            }

            return new Vector3(h, s, v);
        }

        public static string ToCelsiusString(float kelvin)
        {
            return ThermalConstants.KelvinToCelsius(kelvin).ToString("n2") + "°C";
        }
    }
}
