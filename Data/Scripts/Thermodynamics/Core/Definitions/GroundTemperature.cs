using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// What the ground underfoot is worth to the air above it, K.
    ///
    /// A snowfield is not the same climate as a dune at the same latitude, and the game will say
    /// which one a grid is parked on. This is the table that turns that name into a number: snow
    /// and ice cold, sand hot, rock and grass close to whatever the planet already said.
    ///
    /// Matched on the name containing a word rather than on the exact subtype, because every world
    /// and every mod spells its materials differently — Sand_02, SandDesert, Desert_Sand — and the
    /// alternative is a table that silently stops working on somebody else's planet.
    ///
    /// These are opinions about feel, not measurements, which is why the whole table is scaled by
    /// one setting and can be turned off with it.
    /// </summary>
    public static class GroundTemperature
    {
        /// <summary>
        /// What a ground does to the air: how much warmer or colder than the planet's own figure,
        /// and how much more or less the day-night swing.
        ///
        /// The swing is the half of this people notice. A desert is not merely hot — it is hot by
        /// day and cold by night, because dry sand holds nothing overnight. Snow and water are the
        /// other way about: they hold what they have, and their days are flat.
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
        /// What this ground is worth. Neutral for anything the table has no opinion about, which
        /// is the right answer for a material nobody has thought about yet.
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

        /// <summary>Ground the table has nothing to say about: the planet's own climate, unchanged.</summary>
        public static readonly Ground Neutral = new Ground(0f, 1f);

        /// <summary>The offset alone, for callers that only want the shift.</summary>
        public static float OffsetFor(string material)
        {
            return For(material).Offset;
        }
    }
}
