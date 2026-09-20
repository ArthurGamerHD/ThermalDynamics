using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Temperature offset and day-night swing the surface material contributes to the air above it.
    /// Matched on a keyword in the material name rather than the exact subtype, since an exact match
    /// stops working silently on a third-party planet. **Balance choices rather than measurements**,
    /// which is why one setting scales the whole table. See environment.md, Ambient temperature.
    /// </summary>
    public static class GroundTemperature
    {
        /// <summary>
        /// One material's effect on the air: an offset from the planet's own figure, and a multiplier
        /// on the day-night swing.
        ///
        /// Dry ground retains little heat overnight, so a desert is both hotter by day and colder by
        /// night; snow and water damp the swing instead.
        /// </summary>
        public struct Ground
        {
            public float Offset;
            public float Swing;

            /// <summary>
            /// Aerodynamic roughness length z₀, m — how much the ground drags on the wind above it.
            ///
            /// <para>
            /// **The one number in this struct that is not an opinion.** The offsets and the swing
            /// multipliers are figures chosen to look like Earth and are recorded as such
            /// (`C7`); roughness length is a standard wind-engineering quantity with a table
            /// behind it, and the ground material a grid is standing on is exactly what it varies
            /// with — open water 0.0002, snow 0.0005, sand 0.003, grassland 0.03, rock 0.05,
            /// scattered obstacles 0.1, forest 0.5.
            /// </para>
            ///
            /// <para>
            /// Zero means the table does not cover this ground, and the world's own
            /// `WindRoughnessLength` answers instead.
            /// </para>
            /// </summary>
            public float Roughness;

            public Ground(float offset, float swing, float roughness = 0f)
            {
                Offset = offset;
                Swing = swing;
                Roughness = roughness;
            }
        }

        private static readonly KeyValuePair<string, Ground>[] Grounds =
        {
            //                                          offset K   swing   z0 m
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

        /// <summary>
        /// The entry for a material name, or <see cref="Neutral"/> for any material the table does
        /// not cover.
        /// </summary>
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

        /// <summary>An uncovered material: the planet's own climate, unchanged.</summary>
        public static readonly Ground Neutral = new Ground(0f, 1f);

        /// <summary>The offset alone, for callers that do not need the swing multiplier.</summary>
        public static float OffsetFor(string material)
        {
            return For(material).Offset;
        }

        /// <summary>
        /// Roughness length for a material, or <paramref name="fallback"/> where the table does not
        /// cover the ground — which is the world's own setting, and is what an airless world, a
        /// modded voxel or a grid over no surface at all gets.
        /// </summary>
        public static float RoughnessFor(string material, float fallback)
        {
            float roughness = For(material).Roughness;
            return roughness > 0f ? roughness : fallback;
        }
    }
}
