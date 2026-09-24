using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Represents thermal characteristics of planetary ground surfaces.
    /// Used to calculate ground temperature offsets and wind roughness for surface blocks.
    /// </summary>
    public static class GroundTemperature
    {
        /// <summary>
        /// Configuration for ground surface thermal properties.
        /// Contains temperature offset, swing multiplier, and roughness coefficient.
        /// </summary>
        public struct Ground
        {
            /// <summary>
            /// Temperature offset in Kelvin applied to ground-based blocks.
            /// Negative values cool the surface, positive values warm it.
            /// </summary>
            public float Offset;
            
            /// <summary>
            /// Temperature swing multiplier for diurnal (day/night) temperature variation.
            /// Higher values mean greater temperature differences between day and night.
            /// </summary>
            public float Swing;

            /// <summary>
            /// Surface roughness coefficient for wind calculations.
            /// Used to compute wind profile multipliers (higher = more turbulence).
            /// </summary>
            public float Roughness;

            /// <summary>
            /// Creates a new Ground configuration.
            /// </summary>
            public Ground(float offset, float swing, float roughness = 0f)
            {
                Offset = offset;
                Swing = swing;
                Roughness = roughness;
            }
        }

        /// <summary>
        /// Database of ground types with their thermal properties.
        /// Maps material keywords to Ground configurations for fast lookup.
        /// </summary>
        private static readonly KeyValuePair<string, Ground>[] Grounds =
        {
            new KeyValuePair<string, Ground>("snow", new Ground(-14f, 0.7f, 0.0005f)),
            new KeyValuePair<string, Ground>("ice", new Ground(-16f, 0.65f, 0.0002f)),
            new KeyValuePair<string, Ground>("frozen", new Ground(-12f, 0.7f, 0.001f)),
            new KeyValuePair<string, Ground>("tundra", new Ground(-8f, 0.85f, 0.03f)),

            new KeyValuePair<string, Ground>("sand", new Ground(8f, 1.7f, 0.003f)),
            new KeyValuePair<string, Ground>("desert", new Ground(9f, 1.8f, 0.003f)),
            new KeyValuePair<string, Ground>("dune", new Ground(8f, 1.7f, 0.005f)),

            new KeyValuePair<string, Ground>("lava", new Ground(20f, 1.2f, 0.1f)),
            new KeyValuePair<string, Ground>("magma", new Ground(22f, 1.2f, 0.05f)),

            new KeyValuePair<string, Ground>("grass", new Ground(0f, 1f, 0.03f)),
            new KeyValuePair<string, Ground>("soil", new Ground(0f, 1f, 0.01f)),
            new KeyValuePair<string, Ground>("dirt", new Ground(1f, 1.1f, 0.01f)),
            new KeyValuePair<string, Ground>("rock", new Ground(-1f, 1.2f, 0.05f)),
            new KeyValuePair<string, Ground>("stone", new Ground(-1f, 1.2f, 0.05f)),
            new KeyValuePair<string, Ground>("woods", new Ground(-3f, 0.85f, 0.5f)),
            new KeyValuePair<string, Ground>("forest", new Ground(-3f, 0.85f, 0.5f)),
        };


        public static Ground For(string material)
        {
            if (string.IsNullOrEmpty(material)) return Neutral;

            string lowered = material.ToLowerInvariant();

            for (int i = 0; i < Grounds.Length; i++)
            {
                if (lowered.Contains(Grounds[i].Key)) return Grounds[i].Value;
            }

            return Neutral;
        }


        public static readonly Ground Neutral = new Ground(0f, 1f);


        public static float OffsetFor(string material)
        {
            return For(material).Offset;
        }


        public static float RoughnessFor(string material, float fallback)
        {

            float roughness = For(material).Roughness;
            return roughness > 0f ? roughness : fallback;
        }
    }
}
