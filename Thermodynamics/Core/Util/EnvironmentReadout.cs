using System;

namespace Thermodynamics.Core
{
    public static class EnvironmentReadout
    {
        public enum Heat
        {
            Cool,

            Warm,

            Hot,
        }

        public const float WarmShare = 0.5f;


        public static float Climb(float ambientKelvin, float hottestKelvin, float criticalKelvin)
        {
            if (float.IsNaN(ambientKelvin) || float.IsNaN(hottestKelvin)
                || float.IsNaN(criticalKelvin) || criticalKelvin <= 0f)
            {
                return 0f;
            }

            float glow = Incandescence.GlowStartKelvin(criticalKelvin);

            float span = glow - ambientKelvin;
            if (span <= 0f) return 0f;

            float above = hottestKelvin - ambientKelvin;
            return above <= 0f ? 0f : above / span;
        }


        public static Heat State(float ambientKelvin, float hottestKelvin, float criticalKelvin)
        {

            float climb = Climb(ambientKelvin, hottestKelvin, criticalKelvin);

            if (climb >= 1f) return Heat.Hot;
            return climb >= WarmShare ? Heat.Warm : Heat.Cool;
        }


        public static string Word(Heat heat)
        {
            switch (heat)
            {
                case Heat.Hot: return "hot";
                case Heat.Warm: return "warm";
                default: return "cool";
            }
        }


        public static string Line(float ambientKelvin, float hottestKelvin, float criticalKelvin)
        {
            string air = TemperatureScale.ToCelsiusString(ambientKelvin);

            if (criticalKelvin <= 0f || float.IsNaN(hottestKelvin) || float.IsNaN(criticalKelvin))
            {
                return air;
            }

            return air + "   hull " + Word(State(ambientKelvin, hottestKelvin, criticalKelvin));
        }
    }
}
