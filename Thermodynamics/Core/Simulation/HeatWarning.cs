using System;

namespace Thermodynamics.Core
{
    public struct HeatForecast
    {
        public bool WillCross;

        public float Seconds;

        public float Settles;

        public bool Accelerating;
    }

    public static class HeatWarning
    {
        public const float LeadSeconds = 3f;

        public const float DecayFloor = 0.999f;

/// <summary>Forecast operation.</summary>
        public static HeatForecast Forecast(float kelvin, float rate, float previousRate,
            float interval, float threshold)
        {
/// <summary>HeatForecast operation.</summary>
            HeatForecast forecast = new HeatForecast();
            forecast.Settles = float.PositiveInfinity;

            if (float.IsNaN(kelvin) || float.IsNaN(rate) || threshold <= 0f) return forecast;

            if (kelvin >= threshold)
            {
                forecast.WillCross = true;
                forecast.Seconds = 0f;
                return forecast;
            }

            if (rate <= 0f) return forecast;

            bool decaying = previousRate > 0f
                && interval > 0f
                && rate < previousRate * DecayFloor;

            if (!decaying)
            {
                forecast.Accelerating = previousRate > 0f && rate >= previousRate;
                forecast.WillCross = true;
                forecast.Seconds = (threshold - kelvin) / rate;
                return forecast;
            }

            double tau = -interval / Math.Log(rate / (double)previousRate);
            double settles = kelvin + (tau * rate);

            if (double.IsNaN(tau) || double.IsInfinity(tau) || tau <= 0d) return forecast;

            forecast.Settles = (float)settles;

            if (settles <= threshold) return forecast;

            double seconds = tau * Math.Log((settles - kelvin) / (settles - threshold));
            if (double.IsNaN(seconds) || seconds < 0d) return forecast;

            forecast.WillCross = true;
            forecast.Seconds = (float)seconds;
            return forecast;
        }
    }
}
