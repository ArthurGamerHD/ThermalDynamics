using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// The coolant plumbing of the blocks this mod ships, keyed by subtype. A
    /// <see cref="CoolantShape"/> states which cell of the block each port sits on, so a pump of any
    /// length is derived from its declared size rather than from a per-subtype special case.
    /// </summary>
    public static class ThermalCoolantShapes
    {
        private class Plumbing
        {
            public Vector3I[] Link;
            public Vector3I[] Sink;
            public bool IsPump;
        }

        private static readonly Vector3I[] None = new Vector3I[0];

        private static readonly Vector3I[] StraightLink = new Vector3I[] { Vector3I.Forward, Vector3I.Backward };
        private static readonly Vector3I[] CornerLink = new Vector3I[] { Vector3I.Forward, Vector3I.Left };

        private static readonly Dictionary<string, Plumbing> Shapes = new Dictionary<string, Plumbing>
        {
            { "CoolantPipe_Straight", Pipe(StraightLink, None) },
            { "CoolantPipe_Straight_DoubleSink", Pipe(StraightLink, new Vector3I[] { Vector3I.Left, Vector3I.Right }) },
            { "CoolantPipe_Straight_SingleSink", Pipe(StraightLink, new Vector3I[] { Vector3I.Right }) },
            { "CoolantPipe_Corner", Pipe(CornerLink, None) },
            { "CoolantPipe_Corner_DoubleSink", Pipe(CornerLink, new Vector3I[] { Vector3I.Backward, Vector3I.Right }) },
            { "CoolantPipe_Corner_SingleSink", Pipe(CornerLink, new Vector3I[] { Vector3I.Up }) },
            { "CoolantPump", Pump(StraightLink) },
        };

        private static readonly Dictionary<string, CoolantShape> Cache = new Dictionary<string, CoolantShape>();

        /// <summary>
        /// Guards <see cref="Cache"/>. <c>ThermalBlockCatalog</c> deliberately builds outside its
        /// own lock, so this is reached from the worker threads the game pastes grids on.
        /// See known-issues.md, Block placement is not a main-thread-only path.
        /// </summary>
        private static readonly object CacheLock = new object();

        /// <summary>The plumbing for a subtype, or null when the block is not part of the coolant system.</summary>
        /// <param name="subtype">Full subtype name, e.g. <c>Gauge_LG_CoolantPump</c>.</param>
        /// <param name="size">Block size in cells, used to place a long pump's far port.</param>
        public static CoolantShape Get(string subtype, Vector3I size)
        {
            if (string.IsNullOrEmpty(subtype)) return null;

            lock (CacheLock)
            {
                CoolantShape cached;
                if (Cache.TryGetValue(subtype, out cached)) return cached;
            }

            // Built outside the lock, as the catalogue builds its models outside its own: two
            // threads racing on one subtype build the same shape twice and one copy is discarded.
            CoolantShape shape = Build(subtype, size);

            lock (CacheLock)
            {
                CoolantShape existing;
                if (Cache.TryGetValue(subtype, out existing)) return existing;

                Cache[subtype] = shape;
                return shape;
            }
        }

        public static void Clear()
        {
            lock (CacheLock)
            {
                Cache.Clear();
            }
        }

        /// <summary>
        /// Most electricity a large-grid coolant pump draws at full speed, W.
        ///
        /// <para>
        /// Derived from the loop the model already describes rather than chosen: a large-grid ring
        /// moves 200 kg/s of coolant — four two-and-a-half-metre parcels a second at 50 kg each —
        /// and pushing that against two bar of head at seventy per cent efficiency is
        /// <c>ṁ ΔP / (ρ η)</c> = 57 kW. Fifty, rounded.
        /// </para>
        ///
        /// <para>
        /// It is small against what a ring carries — 680 kW per kelvin of difference around it —
        /// which is the point: a circulator is not a refrigerator, and the mod's heat pump pays a
        /// third of what it moves. Both blocks exist because those two prices are different, and
        /// that was not expressible while one of them was free. See document-of-intent.md, A cooling system
        /// costs power, and makes heat doing it.
        /// </para>
        /// </summary>
        public const float LargeGridPumpWatts = 50000f;

        /// <summary>
        /// The same for a small grid: a fifth, on the same reasoning the heat pump's rating uses.
        /// The block is an eighth of the volume and is sized for the loop a small ship builds, so
        /// it is not scaled by volume.
        /// </summary>
        public const float SmallGridPumpWatts = 10000f;

        private static CoolantShape Build(string subtype, Vector3I size)
        {
            Plumbing plumbing;
            if (!Shapes.TryGetValue(StripGridPrefix(subtype), out plumbing)) return null;

            if (plumbing.IsPump)
            {
                // The pump's second port sits at the far end of the block, whatever its length.
                Vector3I axis = Vector3I.Abs(plumbing.Link[1]);
                int length = (axis.X * size.X) + (axis.Y * size.Y) + (axis.Z * size.Z);

                float power = subtype != null && subtype.StartsWith("Gauge_SG_")
                    ? SmallGridPumpWatts
                    : LargeGridPumpWatts;

                return CoolantShape.Pump(
                    plumbing.Link[0], plumbing.Link[1], Math.Max(1, length), power);
            }

            return CoolantShape.Pipe(plumbing.Link[0], plumbing.Link[1], plumbing.Sink);
        }

        /// <summary>
        /// Drops the <c>Gauge_LG_</c> / <c>Gauge_SG_</c> prefix, so one table entry covers both grid
        /// sizes of a block rather than two that can diverge.
        /// </summary>
        public static string StripGridPrefix(string subtype)
        {
            if (subtype == null) return "";
            if (subtype.StartsWith("Gauge_LG_")) return subtype.Substring(9);
            if (subtype.StartsWith("Gauge_SG_")) return subtype.Substring(9);
            return subtype;
        }

        private static Plumbing Pipe(Vector3I[] link, Vector3I[] sink)
        {
            return new Plumbing { Link = link, Sink = sink, IsPump = false };
        }

        private static Plumbing Pump(Vector3I[] link)
        {
            return new Plumbing { Link = link, Sink = None, IsPump = true };
        }
    }
}
