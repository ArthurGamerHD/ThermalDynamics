using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// The heat-pump hardware of the blocks this mod ships, keyed by subtype. Same shape of table as
    /// <see cref="ThermalCoolantShapes"/>, and for the same reason: a block's material properties
    /// belong in its definition, but its port geometry must be supplied to the simulation.
    ///
    /// The two ratings here describe the hardware. Conversion efficiency follows the Carnot relation
    /// and is tuned once for the whole mod in
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
        /// Large grid moves 60 kW for at most 20 kW drawn; small grid a fifth of both. The small block
        /// is an eighth of the volume but is sized to cool a small grid's single reactor, so it is
        /// not scaled by volume.
        /// </summary>
        private static readonly Dictionary<string, Rating> Ratings = new Dictionary<string, Rating>
        {
            { "Gauge_LG_HeatPump", new Rating { Watts = 60000f, Power = 20000f } },
            { "Gauge_SG_HeatPump", new Rating { Watts = 12000f, Power = 4000f } },
        };

        private static readonly Dictionary<string, HeatPumpShape> Cache = new Dictionary<string, HeatPumpShape>();

        /// <summary>
        /// Guards <see cref="Cache"/>. See the note on <c>ThermalCoolantShapes.CacheLock</c>: this
        /// dictionary is written from <c>ThermalBlockCatalog.Build</c>, which runs outside the
        /// catalogue's own lock and so concurrently with itself on the game's worker threads.
        /// </summary>
        private static readonly object CacheLock = new object();

        /// <summary>
        /// The heat-pump hardware for a subtype, or null when the block does not pump heat.
        ///
        /// The cold face is the block's forward and the hot face its backward, along the same axis
        /// the coolant blocks run, so both families orient identically when placed identically.
        /// </summary>
        /// <param name="size">
        /// Block size in cells. The small-grid pump is three by three, so its faces sit at the centre
        /// of its end caps rather than at a corner.
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
        /// Read from the table rather than from a shape: a shape depends on the block's size, so a
        /// caller holding only a subtype name would cache a one-cell shape under the name of a block
        /// three cells across.
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
