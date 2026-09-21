using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics
{
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
/// <summary>Pipe operation.</summary>
            { "CoolantPipe_Straight", Pipe(StraightLink, None) },
/// <summary>Pipe operation.</summary>
            { "CoolantPipe_Straight_DoubleSink", Pipe(StraightLink, new Vector3I[] { Vector3I.Left, Vector3I.Right }) },
/// <summary>Pipe operation.</summary>
            { "CoolantPipe_Straight_SingleSink", Pipe(StraightLink, new Vector3I[] { Vector3I.Right }) },
/// <summary>Pipe operation.</summary>
            { "CoolantPipe_Corner", Pipe(CornerLink, None) },
/// <summary>Pipe operation.</summary>
            { "CoolantPipe_Corner_DoubleSink", Pipe(CornerLink, new Vector3I[] { Vector3I.Backward, Vector3I.Right }) },
/// <summary>Pipe operation.</summary>
            { "CoolantPipe_Corner_SingleSink", Pipe(CornerLink, new Vector3I[] { Vector3I.Up }) },
/// <summary>Pump operation.</summary>
            { "CoolantPump", Pump(StraightLink) },
        };

        private static readonly Dictionary<string, CoolantShape> Cache = new Dictionary<string, CoolantShape>();

/// <summary>object operation.</summary>
        private static readonly object CacheLock = new object();

/// <summary>Returns the .</summary>
        public static CoolantShape Get(string subtype, Vector3I size)
        {
            if (string.IsNullOrEmpty(subtype)) return null;

            lock (CacheLock)
            {
                CoolantShape cached;
                if (Cache.TryGetValue(subtype, out cached)) return cached;
            }

/// <summary>Builds the method table.</summary>
            CoolantShape shape = Build(subtype, size);

            lock (CacheLock)
            {
                CoolantShape existing;
                if (Cache.TryGetValue(subtype, out existing)) return existing;

                Cache[subtype] = shape;
                return shape;
            }
        }

/// <summary>Clear operation.</summary>
        public static void Clear()
        {
            lock (CacheLock)
            {
                Cache.Clear();
            }
        }

        public const float LargeGridPumpWatts = 50000f;

        public const float SmallGridPumpWatts = 10000f;

/// <summary>Builds the API method table.</summary>
        private static CoolantShape Build(string subtype, Vector3I size)
        {
            Plumbing plumbing;
            if (!Shapes.TryGetValue(StripGridPrefix(subtype), out plumbing)) return null;

            if (plumbing.IsPump)
            {
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

/// <summary>StripGridPrefix operation.</summary>
        public static string StripGridPrefix(string subtype)
        {
            if (subtype == null) return "";
            if (subtype.StartsWith("Gauge_LG_")) return subtype.Substring(9);
            if (subtype.StartsWith("Gauge_SG_")) return subtype.Substring(9);
            return subtype;
        }

/// <summary>Pipe operation.</summary>
        private static Plumbing Pipe(Vector3I[] link, Vector3I[] sink)
        {
            return new Plumbing { Link = link, Sink = sink, IsPump = false };
        }

/// <summary>Pump operation.</summary>
        private static Plumbing Pump(Vector3I[] link)
        {
            return new Plumbing { Link = link, Sink = None, IsPump = true };
        }
    }
}
