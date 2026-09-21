using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    public struct CoolantPort
    {
        public Vector3I LocalCell;

        public Vector3I LocalDirection;

/// <summary>CoolantPort operation.</summary>
        public CoolantPort(Vector3I localCell, Vector3I localDirection)
        {
            LocalCell = localCell;
            LocalDirection = localDirection;
        }

/// <summary>CoolantPort operation.</summary>
        public CoolantPort(Vector3I localDirection)
        {
            LocalCell = Vector3I.Zero;
            LocalDirection = localDirection;
        }
    }

    public class CoolantShape
    {
        public CoolantPort[] LinkPorts = new CoolantPort[0];

        public CoolantPort[] SinkPorts = new CoolantPort[0];

        public bool IsPump;

        public float MaxPowerWatts;

/// <summary>Pipe operation.</summary>
        public static CoolantShape Pipe(Vector3I inDirection, Vector3I outDirection, params Vector3I[] sinkDirections)
        {
/// <summary>CoolantShape operation.</summary>
            CoolantShape s = new CoolantShape();
/// <summary>CoolantPort operation.</summary>
            s.LinkPorts = new CoolantPort[] { new CoolantPort(inDirection), new CoolantPort(outDirection) };
/// <summary>ToPorts operation.</summary>
            s.SinkPorts = ToPorts(sinkDirections);
            return s;
        }

/// <summary>Pump operation.</summary>
        public static CoolantShape Pump(Vector3I inDirection, Vector3I outDirection, int length,
            float maxPowerWatts = 0f)
        {
/// <summary>CoolantShape operation.</summary>
            CoolantShape s = new CoolantShape();
            s.IsPump = true;
            s.MaxPowerWatts = maxPowerWatts > 0f ? maxPowerWatts : 0f;

            Vector3I farCell = Vector3I.Abs(outDirection) * Math.Max(0, length - 1);
            s.LinkPorts = new CoolantPort[]
            {
/// <summary>CoolantPort operation.</summary>
                new CoolantPort(Vector3I.Zero, inDirection),
/// <summary>CoolantPort operation.</summary>
                new CoolantPort(farCell, outDirection),
            };
            return s;
        }

/// <summary>ToPorts operation.</summary>
        private static CoolantPort[] ToPorts(Vector3I[] directions)
        {
            if (directions == null) return new CoolantPort[0];
            CoolantPort[] ports = new CoolantPort[directions.Length];
            for (int i = 0; i < directions.Length; i++)
            {
/// <summary>CoolantPort operation.</summary>
                ports[i] = new CoolantPort(directions[i]);
            }
            return ports;
        }
    }
}
