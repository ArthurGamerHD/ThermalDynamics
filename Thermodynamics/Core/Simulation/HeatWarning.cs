using System;

namespace Thermodynamics.Core
{
    /// <summary>Where a block is heading, and when it gets there.</summary>
    public struct HeatForecast
    {
        /// <summary>True when the block reaches the threshold at all.</summary>
        public bool WillCross;

        /// <summary>
        /// Seconds until it does, 0 when it already has, and meaningless unless
        /// <see cref="WillCross"/>.
        /// </summary>
        public float Seconds;

        /// <summary>
        /// The temperature it is heading for, K, or <see cref="float.PositiveInfinity"/> when the
        /// forecast could not find one.
        /// </summary>
        public float Settles;

        /// <summary>
        /// True when the block is heating faster than it was, so no equilibrium could be read off
        /// it and the straight-line fallback answered instead.
        /// </summary>
        public bool Accelerating;
    }

    /// <summary>
    /// When a block will cross a temperature, for a cue that has to arrive before it does.
    ///
    /// <para>
    /// **A straight line is the wrong answer and the reason this file exists.** A block warming
    /// toward an equilibrium slows down as it approaches it, so projecting its current rate
    /// forward crosses thresholds the block never reaches: every hot-running reactor in the game
    /// would be warned about, once, for as long as it was warming up. A cue that cries wolf is
    /// worse than no cue at all, so the projection has to account for the approach.
    /// </para>
    ///
    /// <para>
    /// It can, from the rate alone, because the approach has a shape. Heat flow out of a block
    /// grows with the gap between it and everything around it, so near any operating point the
    /// block obeys <c>dT/dt = (T∞ − T)/τ</c> and the rate decays by a fixed factor each interval.
    /// Two rates one interval apart therefore give both unknowns:
    /// </para>
    ///
    /// <code>
    ///   τ  = −Δt / ln(r₁ / r₀)
    ///   T∞ = T₁ + τ·r₁
    ///   t  = τ · ln((T∞ − T₁) / (T∞ − threshold))
    /// </code>
    ///
    /// <para>
    /// **`T∞ ≤ threshold` is the answer that matters** — it is the block that never gets there, and
    /// saying so is the whole difference between this and the straight line. The straight line is
    /// still what answers a block whose rate is *rising*, because there is no equilibrium to read
    /// off an accelerating block and a straight line under-estimates a runaway, which warns early.
    /// Early is the safe direction to be wrong in; late is not.
    /// </para>
    ///
    /// <para>
    /// Stateless apart from the previous rate the caller carries, and free of any game type, so it
    /// is checked against the solver itself rather than against its own arithmetic (`C5`, `E7`).
    /// </para>
    /// </summary>
    public static class HeatWarning
    {
        /// <summary>
        /// How long before the crossing the lead cue sounds, s of play. Long enough to be a warning
        /// and short enough that the forecast is still about the state the block is in.
        /// </summary>
        public const float LeadSeconds = 3f;

        /// <summary>
        /// How far the rate has to fall between samples before the decay is believed, as a share of
        /// the earlier rate.
        ///
        /// A ratio just under 1 is arithmetic noise as much as it is a decay, and it produces an
        /// enormous τ and an equilibrium far above anything real; taking the straight line there
        /// costs a cue that is early rather than one that is wrong.
        /// </summary>
        public const float DecayFloor = 0.999f;

        /// <summary>
        /// When the block at <paramref name="kelvin"/> reaches <paramref name="threshold"/>.
        ///
        /// <paramref name="rate"/> and <paramref name="previousRate"/> are K/s, sampled
        /// <paramref name="interval"/> seconds apart. A caller with only one sample passes 0 for
        /// the previous rate and gets the straight line.
        /// </summary>
        public static HeatForecast Forecast(float kelvin, float rate, float previousRate,
            float interval, float threshold)
        {
            HeatForecast forecast = new HeatForecast();
            forecast.Settles = float.PositiveInfinity;

            if (float.IsNaN(kelvin) || float.IsNaN(rate) || threshold <= 0f) return forecast;

            if (kelvin >= threshold)
            {
                forecast.WillCross = true;
                forecast.Seconds = 0f;
                return forecast;
            }

            // Not heating: nothing is coming, whatever the block did a moment ago.
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

            // The block levels off short of the threshold. This is the case a straight line gets
            // wrong, and it is the common one.
            if (settles <= threshold) return forecast;

            double seconds = tau * Math.Log((settles - kelvin) / (settles - threshold));
            if (double.IsNaN(seconds) || seconds < 0d) return forecast;

            forecast.WillCross = true;
            forecast.Seconds = (float)seconds;
            return forecast;
        }
    }
}
