using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Temperature offset the surface material contributes to the air above it, K.
    ///
    /// The game reports which voxel material a grid is parked on; this table maps that name to a
    /// temperature offset and a day-night swing multiplier. Snow and ice are cold, sand is hot, and
    /// rock and grass are close to the planet's own figures.
    ///
    /// Matched on the name containing a keyword rather than on the exact subtype, since worlds and
    /// mods name their materials differently (Sand_02, SandDesert, Desert_Sand). An exact match
    /// would silently stop working on a third-party planet.
    ///
    /// These values are balance choices rather than measurements, which is why the whole table is
    /// scaled by one setting and can be disabled with it.
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

            public Ground(float offset, float swing)
            {
                Offset = offset;
                Swing = swing;
            }
        }

        private static readonly KeyValuePair<string, Ground>[] Grounds =
        {
            new KeyValuePair<string, Ground>("snow", new Ground(-14f, 0.7f)),
            new KeyValuePair<string, Ground>("ice", new Ground(-16f, 0.65f)),
            new KeyValuePair<string, Ground>("frozen", new Ground(-12f, 0.7f)),
            new KeyValuePair<string, Ground>("tundra", new Ground(-8f, 0.85f)),

            new KeyValuePair<string, Ground>("sand", new Ground(8f, 1.7f)),
            new KeyValuePair<string, Ground>("desert", new Ground(9f, 1.8f)),
            new KeyValuePair<string, Ground>("dune", new Ground(8f, 1.7f)),

            new KeyValuePair<string, Ground>("lava", new Ground(20f, 1.2f)),
            new KeyValuePair<string, Ground>("magma", new Ground(22f, 1.2f)),

            new KeyValuePair<string, Ground>("grass", new Ground(0f, 1f)),
            new KeyValuePair<string, Ground>("soil", new Ground(0f, 1f)),
            new KeyValuePair<string, Ground>("dirt", new Ground(1f, 1.1f)),
            new KeyValuePair<string, Ground>("rock", new Ground(-1f, 1.2f)),
            new KeyValuePair<string, Ground>("stone", new Ground(-1f, 1.2f)),
            new KeyValuePair<string, Ground>("woods", new Ground(-3f, 0.85f)),
            new KeyValuePair<string, Ground>("forest", new Ground(-3f, 0.85f)),
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
    }
}
