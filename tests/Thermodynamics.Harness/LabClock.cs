using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    public static class LabClock
    {
        public const float TunedAt = 225f;

        public static float Shipped
        {
            get
            {
/// <summary>ThermalSettings operation.</summary>
                float clock = new ThermalSettings().HeatTimeScale;
                return clock > 0f ? clock : 1f;
            }
        }

        public static float Stretch
        {
            get { return TunedAt / Shipped; }
        }

/// <summary>Seconds operation.</summary>
        public static float Seconds(float seconds)
        {
            return seconds * Stretch;
        }

/// <summary>Steps operation.</summary>
        public static int Steps(int steps)
        {
            int stretched = (int)(steps * Stretch);
            return stretched < 1 ? 1 : stretched;
        }
    }
}
