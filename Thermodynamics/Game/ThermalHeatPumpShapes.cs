using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics
{
    public static class ThermalHeatPumpShapes
    {
        private class Rating
        {
            public float Watts;
            public float Power;
        }

        private static readonly Dictionary<string, Rating> Ratings = new Dictionary<string, Rating>
        {
            { "Gauge_LG_HeatPump", new Rating { Watts = 60000f, Power = 20000f } },
            { "Gauge_SG_HeatPump", new Rating { Watts = 12000f, Power = 4000f } },
        };

        private static readonly Dictionary<string, HeatPumpShape> Cache = new Dictionary<string, HeatPumpShape>();

/// <summary>object operation.</summary>
        private static readonly object CacheLock = new object();

/// <summary>Returns the .</summary>
        public static HeatPumpShape Get(string subtype, Vector3I size)
        {
            if (string.IsNullOrEmpty(subtype)) return null;

            lock (CacheLock)
            {
                HeatPumpShape cached;
                if (Cache.TryGetValue(subtype, out cached)) return cached;

                Rating rating;
                HeatPumpShape shape = Ratings.TryGetValue(subtype, out rating)
                    ? HeatPumpShape.Centred(Vector3I.Forward, size, rating.Watts, rating.Power)
                    : null;

                Cache[subtype] = shape;
                return shape;
            }
        }

/// <summary>MaxPowerWatts operation.</summary>
        public static float MaxPowerWatts(string subtype)
        {
            Rating rating;
            if (string.IsNullOrEmpty(subtype) || !Ratings.TryGetValue(subtype, out rating)) return 0f;
            return rating.Power;
        }

/// <summary>IsHeatPump operation.</summary>
        public static bool IsHeatPump(string subtype)
        {
            return !string.IsNullOrEmpty(subtype) && Ratings.ContainsKey(subtype);
        }

/// <summary>Clear operation.</summary>
        public static void Clear()
        {
            lock (CacheLock)
            {
                Cache.Clear();
            }
        }
    }
}
