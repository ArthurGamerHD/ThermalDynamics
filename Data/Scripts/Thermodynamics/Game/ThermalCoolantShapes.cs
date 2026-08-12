using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// The coolant plumbing of the blocks this mod ships, keyed by subtype.
    ///
    /// The old crawler carried two parallel dictionaries and a hard-coded "multiply the offset
    /// by three for the small-grid pump" rule. A <see cref="CoolantShape"/> states which cell of
    /// the block each port sits on, so a pump of any length works from its declared size and
    /// there is no special case left to keep in sync.
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
        /// The plumbing for a subtype, or null when the block is not part of the coolant system.
        /// </summary>
        /// <param name="subtype">Full subtype name, e.g. <c>Gauge_LG_CoolantPump</c>.</param>
        /// <param name="size">Block size in cells, used to place a long pump's far port.</param>
        public static CoolantShape Get(string subtype, Vector3I size)
        {
            if (string.IsNullOrEmpty(subtype)) return null;

            CoolantShape cached;
            if (Cache.TryGetValue(subtype, out cached)) return cached;

            CoolantShape shape = Build(subtype, size);
            Cache[subtype] = shape;
            return shape;
        }

        public static void Clear()
        {
            Cache.Clear();
        }

        private static CoolantShape Build(string subtype, Vector3I size)
        {
            Plumbing plumbing;
            if (!Shapes.TryGetValue(StripGridPrefix(subtype), out plumbing)) return null;

            if (plumbing.IsPump)
            {
                // The pump's second port sits at the far end of however long the block is.
                Vector3I axis = Vector3I.Abs(plumbing.Link[1]);
                int length = (axis.X * size.X) + (axis.Y * size.Y) + (axis.Z * size.Z);
                return CoolantShape.Pump(plumbing.Link[0], plumbing.Link[1], Math.Max(1, length));
            }

            return CoolantShape.Pipe(plumbing.Link[0], plumbing.Link[1], plumbing.Sink);
        }

        /// <summary>
        /// Drops the <c>Gauge_LG_</c> / <c>Gauge_SG_</c> prefix, so one table entry covers both
        /// grid sizes of a block instead of two that can drift apart.
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
