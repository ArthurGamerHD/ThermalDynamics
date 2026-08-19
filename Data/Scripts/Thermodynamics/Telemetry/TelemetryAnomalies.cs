namespace Thermodynamics
{
    /// <summary>A class of value the simulation should not have produced.</summary>
    public enum TelemetryAnomalyKind
    {
        None = 0,

        /// <summary>A temperature that is not a number at all.</summary>
        NotANumber,

        /// <summary>A temperature that has run away to infinity.</summary>
        Infinite,

        /// <summary>Finite, but far above anything the model should reach.</summary>
        Implausible,

        /// <summary>
        /// The solver's floor at zero absorbed a negative excursion. The signature of an unstable
        /// step: without the clamp the value would go negative and then oscillate with growing
        /// amplitude.
        /// </summary>
        ClampedToZero
    }

    /// <summary>
    /// Classifies a cell's post-update state as an anomaly or not.
    ///
    /// Free of any Space Engineers type, so the classification can be tested outside the game, on
    /// the same boundary the simulation core draws.
    /// </summary>
    public static class TelemetryAnomalies
    {
        /// <param name="temperature">The cell temperature after the update and after the clamp.</param>
        /// <param name="lastTemperature">Its temperature at the start of the update.</param>
        /// <param name="implausible">The threshold above which a finite value is suspect.</param>
        public static TelemetryAnomalyKind Classify(float temperature, float lastTemperature, float implausible)
        {
            if (float.IsNaN(temperature)) return TelemetryAnomalyKind.NotANumber;
            if (float.IsInfinity(temperature)) return TelemetryAnomalyKind.Infinite;
            if (temperature > implausible) return TelemetryAnomalyKind.Implausible;

            // Only reported when the cell had heat to lose: a cell already at zero that stayed there
            // is the normal state of an unsimulated block.
            if (temperature == 0f && lastTemperature > 0f) return TelemetryAnomalyKind.ClampedToZero;

            return TelemetryAnomalyKind.None;
        }

        /// <summary>
        /// The name a kind is aggregated under in the report. Stable, since it is the dictionary key
        /// grouping every occurrence of the same problem.
        /// </summary>
        public static string Name(TelemetryAnomalyKind kind, float implausible)
        {
            switch (kind)
            {
                case TelemetryAnomalyKind.NotANumber: return "temperature is NaN";
                case TelemetryAnomalyKind.Infinite: return "temperature is infinite";
                case TelemetryAnomalyKind.Implausible: return "temperature above " + TelemetryFormat.Number(implausible) + "K";
                case TelemetryAnomalyKind.ClampedToZero: return "temperature clamped to zero";
                default: return "none";
            }
        }
    }

    /// <summary>
    /// Admits one call in every N, so the expensive half of the per-cell data collection runs on
    /// a fraction of updates. The first call is always admitted, so a short session still
    /// produces samples.
    /// </summary>
    public class SampleGate
    {
        private int _stride = 1;
        private int _countdown;

        public int Stride
        {
            get { return _stride; }
            set { _stride = value < 1 ? 1 : value; }
        }

        public bool Admit()
        {
            if (--_countdown > 0) return false;

            _countdown = _stride;
            return true;
        }

        public void Reset()
        {
            _countdown = 0;
        }
    }
}
