using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Forecast of when a block's temperature will reach a critical threshold.
    /// Used for early warning systems to predict overheating events.
    /// </summary>
    public struct HeatForecast
    {
        /// <summary>
        /// True if the temperature will cross the threshold within the forecast period.
        /// </summary>
        public bool WillCross;

        /// <summary>
        /// Estimated time in seconds until threshold crossing.
        /// float.PositiveInfinity if the temperature will not cross (stable or cooling).
        /// </summary>
        public float Seconds;

        /// <summary>
        /// Predicted equilibrium temperature if the current trend continues.
        /// For exponentially decaying systems, this is the asymptotic temperature.
        /// float.PositiveInfinity if the system is accelerating toward infinity.
        /// </summary>
        public float Settles;

        /// <summary>
        /// True if the temperature change rate is increasing (heating is accelerating).
        /// Indicates potentially dangerous thermal runaway conditions.
        /// </summary>
        public bool Accelerating;
    }

    /// <summary>
    /// Provides heat warning and forecasting functionality.
    /// Analyzes temperature trends to predict when blocks will reach critical thresholds.
    /// </summary>
    public static class HeatWarning
    {
        /// <summary>
        /// Lead time in seconds before threshold crossing to generate warnings.
        /// If threshold will be crossed within this time, an alert is raised.
        /// </summary>
        public const float LeadSeconds = 3f;

        /// <summary>
        /// Decay threshold factor for detecting cooling trends.
        /// Rate must be below previousRate * DecayFloor to be considered decaying.
        /// Default 0.999 means rate must decrease by at least 0.1% per interval.
        /// </summary>
        public const float DecayFloor = 0.999f;


        /// <summary>
        /// Forecasts when a block's temperature will reach a critical threshold.
        /// Analyzes current temperature, rate of change, and trend to predict
        /// whether and when the threshold will be crossed.
        /// </summary>
        /// <param name="kelvin">Current temperature in Kelvin.</param>
        /// <param name="rate">Current rate of temperature change in K/second.</param>
        /// <param name="previousRate">Rate of temperature change from the previous step.</param>
        /// <param name="interval">Time interval between measurements in seconds.</param>
        /// <param name="threshold">Critical temperature threshold in Kelvin.</param>
        /// <returns>HeatForecast with crossing prediction and timing.</returns>
        /// <remarks>
        /// Forecast logic:
        /// 1. If already at or above threshold -> immediate crossing (0 seconds)
        /// 2. If rate <= 0 -> no crossing (cooling or stable)
        /// 3. If rate is increasing or stable -> linear projection
        ///    Time = (threshold - current) / rate
        /// 4. If rate is decaying exponentially -> use tau (time constant) model
        ///    Settles = current + tau * rate
        ///    If settles > threshold -> solve for crossing time
        ///    tau = -interval / ln(rate/previousRate)
        /// 
        /// The exponential model is appropriate for systems with thermal inertia
        /// where heating rate decreases as temperature approaches equilibrium.
        /// </remarks>
        public static HeatForecast Forecast(float kelvin, float rate, float previousRate,
            float interval, float threshold)
        {
            HeatForecast forecast = new HeatForecast();
            forecast.Settles = float.PositiveInfinity;

            // Validate inputs
            if (float.IsNaN(kelvin) || float.IsNaN(rate) || threshold <= 0f) return forecast;

            // Already at or above threshold
            if (kelvin >= threshold)
            {
                forecast.WillCross = true;
                forecast.Seconds = 0f;
                return forecast;
            }

            // Not heating, will never cross
            if (rate <= 0f) return forecast;

            // Check if heating rate is decaying (exponential approach to equilibrium)
            bool decaying = previousRate > 0f
                && interval > 0f
                && rate < previousRate * DecayFloor;

            if (!decaying)
            {
                // Linear heating projection
                forecast.Accelerating = previousRate > 0f && rate >= previousRate;
                forecast.WillCross = true;
                forecast.Seconds = (threshold - kelvin) / rate;
                return forecast;
            }

            // Exponential decay model
            // tau = time constant, where after tau seconds, system reaches ~63% of way to target
            double tau = -interval / Math.Log(rate / (double)previousRate);
            // Settles = asymptotic temperature (where heating rate goes to zero)
            double settles = kelvin + (tau * rate);

            if (double.IsNaN(tau) || double.IsInfinity(tau) || tau <= 0d) return forecast;

            forecast.Settles = (float)settles;

            // If equilibrium temperature is below threshold, we'll never cross
            if (settles <= threshold) return forecast;

            // Solve for time when temperature reaches threshold in exponential approach
            // T(t) = settles - (settles - current) * e^(-t/tau)
            // threshold = settles - (settles - current) * e^(-seconds/tau)
            // e^(-seconds/tau) = (settles - threshold) / (settles - current)
            // -seconds/tau = ln((settles - threshold) / (settles - current))
            // seconds = -tau * ln((settles - threshold) / (settles - current))
            //         = tau * ln((settles - current) / (settles - threshold))
            double seconds = tau * Math.Log((settles - kelvin) / (settles - threshold));
            if (double.IsNaN(seconds) || seconds < 0d) return forecast;

            forecast.WillCross = true;
            forecast.Seconds = (float)seconds;
            return forecast;
        }
    }
}
