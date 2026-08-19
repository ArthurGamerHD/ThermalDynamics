using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// The heat-pump hardware of the blocks this mod ships, keyed by subtype — the same shape of
    /// table as <see cref="ThermalCoolantShapes"/>, and for the same reason: what a block is made
    /// of belongs in the definition, but what it is plumbed like is geometry the simulation has to
    /// be told.
    ///
    /// The two ratings here are the block's hardware, not its physics. How efficiently it converts
    /// electricity into lifted heat is Carnot's business and is tuned once for the whole mod in
    /// <see cref="ThermalSettings.HeatPumpCarnotFraction"/>.
    /// </summary>
    public static class ThermalHeatPumpShapes
    {
        private class Rating
        {
            public float Watts;
            public float Power;
        }

        /// <summary>
        /// Large grid moves 60 kW for at most 20 kW drawn; small grid a fifth of both. The small
        /// block is an eighth of the volume but is expected to cool a small ship's single reactor,
        /// so it is deliberately not scaled by volume.
        /// </summary>
        private static readonly Dictionary<string, Rating> Ratings = new Dictionary<string, Rating>
        {
            { "Gauge_LG_HeatPump", new Rating { Watts = 60000f, Power = 20000f } },
            { "Gauge_SG_HeatPump", new Rating { Watts = 12000f, Power = 4000f } },
        };

        private static readonly Dictionary<string, HeatPumpShape> Cache = new Dictionary<string, HeatPumpShape>();

        /// <summary>
        /// Guards <see cref="Cache"/>. See the note on <c>ThermalCoolantShapes.CacheLock</c>:
        /// this dictionary is written from <c>ThermalBlockCatalog.Build</c>, which runs outside
        /// the catalogue's own lock and therefore concurrently with itself on the worker threads
        /// the game pastes grids on.
        /// </summary>
        private static readonly object CacheLock = new object();

        /// <summary>
        /// The heat-pump hardware for a subtype, or null when the block does not pump heat.
        ///
        /// Cold face is the block's forward, hot face its backward — the same axis the coolant
        /// blocks run along, so the two families point the same way when placed the same way.
        /// </summary>
        /// <param name="size">
        /// Block size in cells. The small-grid pump is three by three, so its faces sit on the
        /// middle of its end caps rather than on a corner.
        /// </param>
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

        /// <summary>
        /// Most electricity a subtype's pump can draw, W, or zero when it is not a pump.
        ///
        /// Read straight out of the table rather than off a shape: the shape depends on the
        /// block's size, and a caller that has only a subtype name would otherwise cache a
        /// one-cell shape under the name of a block that is three cells across.
        /// </summary>
        public static float MaxPowerWatts(string subtype)
        {
            Rating rating;
            if (string.IsNullOrEmpty(subtype) || !Ratings.TryGetValue(subtype, out rating)) return 0f;
            return rating.Power;
        }

        /// <summary>True when a subtype is one of the heat pumps.</summary>
        public static bool IsHeatPump(string subtype)
        {
            return !string.IsNullOrEmpty(subtype) && Ratings.ContainsKey(subtype);
        }

        public static void Clear()
        {
            lock (CacheLock)
            {
                Cache.Clear();
            }
        }
    }
}
