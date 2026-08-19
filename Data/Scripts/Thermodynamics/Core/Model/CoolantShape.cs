using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// One connection point on a coolant block: which cell of the block it sits on, and which
    /// way it faces. Both are in block-local space, so a rotated block needs no special cases.
    /// </summary>
    public struct CoolantPort
    {
        /// <summary>Cell within the block, relative to the block's local minimum.</summary>
        public Vector3I LocalCell;

        /// <summary>Direction the port faces, in block-local space.</summary>
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

    /// <summary>
    /// The coolant plumbing a block type provides. Each port carries the block-local cell it sits
    /// on, so a block of any size works without subtype-name special cases.
    /// </summary>
    public class CoolantShape
    {
        /// <summary>Where the loop enters and leaves. A valid pipe has exactly two.</summary>
        public CoolantPort[] LinkPorts = new CoolantPort[0];

        /// <summary>Faces that exchange heat with whatever block is mounted against them.</summary>
        public CoolantPort[] SinkPorts = new CoolantPort[0];

        /// <summary>A loop needs at least one pump to circulate.</summary>
        public bool IsPump;

        public static CoolantShape Pipe(Vector3I inDirection, Vector3I outDirection, params Vector3I[] sinkDirections)
        {
            CoolantShape s = new CoolantShape();
            s.LinkPorts = new CoolantPort[] { new CoolantPort(inDirection), new CoolantPort(outDirection) };
            s.SinkPorts = ToPorts(sinkDirections);
            return s;
        }

        /// <summary>
        /// A pump whose two ports sit on opposite ends of a block that is
        /// <paramref name="length"/> cells long along the link axis.
        /// </summary>
        public static CoolantShape Pump(Vector3I inDirection, Vector3I outDirection, int length)
        {
            CoolantShape s = new CoolantShape();
            s.IsPump = true;

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
