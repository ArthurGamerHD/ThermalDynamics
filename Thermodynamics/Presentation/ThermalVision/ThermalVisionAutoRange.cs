using System;

namespace Thermodynamics.Presentation
{
    /// <summary>Shared scene exposure with fast expansion and slower contraction.</summary>
    public sealed class ThermalVisionAutoRange
    {
        public float Low { get; private set; }
        public float High { get; private set; }
        public bool HasSamples { get; private set; }
        private bool batchValid;
        private float targetLow, targetHigh;

        public void Reset() { HasSamples = false; BeginSamples(); }

        /// <summary>Starts a fresh scene sample set; an empty set preserves the previous exposure.</summary>
        public void BeginSamples() { batchValid = false; }

        public bool Observe(float kelvin)
        {
            if (float.IsNaN(kelvin) || float.IsInfinity(kelvin) || kelvin < 0 || kelvin > 100000f)
                return false;
            float low = Math.Max(0, (float)Math.Floor((kelvin - 10f) / 25f) * 25f);
            float high = Math.Max(low + 50f, (float)Math.Ceiling((kelvin + 10f) / 25f) * 25f);
            if (batchValid) { low = Math.Min(targetLow, low); high = Math.Max(targetHigh, high); }
            targetLow = low; targetHigh = high; batchValid = true;
            return true;
        }

        /// <summary>Uses real elapsed seconds, independent of draw frequency; first exposure initializes immediately.</summary>
        public bool Update(double seconds)
        {
            if (!batchValid) return false;
            if (!HasSamples)
            {
                Low = targetLow; High = targetHigh; HasSamples = true;
                return true;
            }
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return false;
            float oldLow = Low, oldHigh = High;
            // Each bound expands with a 0.4 s time constant and contracts with a 3 s constant.
            Low += (targetLow - Low) * (float)(1 - Math.Exp(-seconds / (targetLow < Low ? .4 : 3)));
            High += (targetHigh - High) * (float)(1 - Math.Exp(-seconds / (targetHigh > High ? .4 : 3)));
            High = Math.Max(High, Low + 50f);
            return Low != oldLow || High != oldHigh;
        }
    }
}
