using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    public struct CoolantPort
    {
        public Vector3I LocalCell;

        public Vector3I LocalDirection;


        public CoolantPort(Vector3I localCell, Vector3I localDirection)
        {
            LocalCell = localCell;
            LocalDirection = localDirection;
        }


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


        public static CoolantShape Pipe(Vector3I inDirection, Vector3I outDirection, params Vector3I[] sinkDirections)
        {

            CoolantShape s = new CoolantShape();

            s.LinkPorts = new CoolantPort[] { new CoolantPort(inDirection), new CoolantPort(outDirection) };

            s.SinkPorts = ToPorts(sinkDirections);
            return s;
        }


        public static CoolantShape Pump(Vector3I inDirection, Vector3I outDirection, int length,
            float maxPowerWatts = 0f)
        {

            CoolantShape s = new CoolantShape();
            s.IsPump = true;
            s.MaxPowerWatts = maxPowerWatts > 0f ? maxPowerWatts : 0f;

            Vector3I farCell = Vector3I.Abs(outDirection) * Math.Max(0, length - 1);
            s.LinkPorts = new CoolantPort[]
            {

                new CoolantPort(Vector3I.Zero, inDirection),

                new CoolantPort(farCell, outDirection),
            };
            return s;
        }


        private static CoolantPort[] ToPorts(Vector3I[] directions)
        {
            if (directions == null) return new CoolantPort[0];
            CoolantPort[] ports = new CoolantPort[directions.Length];
            for (int i = 0; i < directions.Length; i++)
            {

                ports[i] = new CoolantPort(directions[i]);
            }
            return ports;
        }
    }
}
