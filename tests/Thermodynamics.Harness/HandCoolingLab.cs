using System;
using System.Collections.Generic;

namespace Thermodynamics.Harness
{
    public static class HandCoolingLab
    {
        public const float BottleKilograms = 5f;

        public const float BottleJoulesPerKilogram = 660000f;

        public const float BottleSeconds = 20f;

        public static float BottleJoules
        {
            get { return BottleKilograms * BottleJoulesPerKilogram; }
        }

        public static float BottleWatts
        {
            get { return BottleJoules / BottleSeconds; }
        }

        public class Price
        {
            public string Subtype;

            public float CriticalKelvin;

            public float WasteWatts;

            public float HoldWatts;

            public float ReturnJoules;

            public float WindowSeconds;

            public float BottlesToHoldTheWindow
            {
                get
                {
                    if (HoldWatts <= 0f) return 0f;
                    if (float.IsInfinity(WindowSeconds)) return float.PositiveInfinity;

                    return HoldWatts * WindowSeconds / BottleJoules;
                }
            }

            public float BottlesToReturn
            {
                get { return ReturnJoules / BottleJoules; }
            }

            public bool OneBottleHolds
            {
                get { return HoldWatts <= BottleWatts; }
            }

            public float ReturnWattsInWindow
            {
                get
                {
                    if (float.IsInfinity(WindowSeconds) || WindowSeconds <= 0f) return HoldWatts;
                    return HoldWatts + (ReturnJoules / WindowSeconds);
                }
            }

            public float BottlesAtOnce
            {
                get { return ReturnWattsInWindow / BottleWatts; }
            }
        }


        public static List<Price> All()
        {

            List<Price> prices = new List<Price>();

            foreach (BlockHeatIndex.Reading reading in BlockHeatIndex.All())
            {
                if (float.IsInfinity(reading.SecondsToCritical)) continue;
                if (reading.Watts <= 0f) continue;

                float shed = reading.RadiatedWatts + reading.ConductedWatts;
                float hold = reading.Watts - shed;
                if (hold < 0f) hold = 0f;

                float physical = reading.HeatCapacity * BlockHeatIndex.PaceHeatTimeScale;

                prices.Add(new Price
                {
                    Subtype = reading.Subtype,
                    CriticalKelvin = reading.CriticalKelvin,
                    WasteWatts = reading.Watts,
                    HoldWatts = hold,
                    ReturnJoules = physical
                        * (reading.CriticalKelvin - BlockHeatIndex.AmbientKelvin),
                    WindowSeconds = reading.SecondsCriticalToLoss
                });
            }

            prices.Sort(delegate (Price a, Price b) { return b.HoldWatts.CompareTo(a.HoldWatts); });
            return prices;
        }


        public static float Quantile(List<float> ascending, double quantile)
        {
            if (ascending.Count == 0) return 0f;

            int index = (int)Math.Ceiling(quantile * ascending.Count) - 1;
            if (index < 0) index = 0;
            if (index >= ascending.Count) index = ascending.Count - 1;

            return ascending[index];
        }
    }
}
