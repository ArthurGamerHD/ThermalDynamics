namespace Thermodynamics.Presentation
{
    /// <summary>View-dependent shaping and work limits for the natural heat cue; see thermal-glow.md.</summary>
    public static class HeatGlowStyle
    {
        public const double DrawRange = 2000.0;
        public const int MaxQuads = 4000;
        public const int MaxLights = 32;
        public const float HaloScale = 1.12f;
        public const float SurfaceIntensity = 0.7f;

        /// <summary>Metres between the surface and glow plane, scaled for small and large grids.</summary>
        public static float StandOff(float gridSize)
        {
            return gridSize * 0.008f;
        }

        /// <summary>Continuous fade before a bounded draw distance; invalid inputs produce no light.</summary>
        public static float RangeFade(double distance, double range)
        {
            if (!(distance >= 0.0) || !(range > 0.0) || distance >= range) return 0f;
            return Smooth((float)((range - distance) / (range * 0.2)));
        }

        /// <summary>Suppress edge-on planes without darkening normal views; cosine uses the outward normal.</summary>
        public static float FacingFade(double cosine)
        {
            return Smooth((float)(cosine / 0.25));
        }

        private static float Smooth(float value)
        {
            if (!(value > 0f)) return 0f;
            if (value >= 1f) return 1f;
            return value * value * (3f - 2f * value);
        }
    }
}
