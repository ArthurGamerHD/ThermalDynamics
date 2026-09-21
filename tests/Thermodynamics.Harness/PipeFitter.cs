using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class PipeFitter
    {
/// <summary>AllOrientations operation.</summary>
        public static IEnumerable<BlockOrientation> AllOrientations()
        {
            Array directions = Enum.GetValues(typeof(Base6Directions.Direction));
            foreach (Base6Directions.Direction forward in directions)
            {
                foreach (Base6Directions.Direction up in directions)
                {
                    Vector3 f = Base6Directions.GetVector(forward);
                    Vector3 u = Base6Directions.GetVector(up);
                    if (Math.Abs(Vector3.Dot(f, u)) > 0.001f) continue;
                    yield return new BlockOrientation(forward, up);
                }
            }
        }

/// <summary>TryOrient operation.</summary>
        public static bool TryOrient(BlockModel model, Vector3I a, Vector3I b, Vector3I requiredSink, out BlockOrientation orientation)
        {
            if (model != null && model.Coolant != null)
            {
                foreach (BlockOrientation candidate in AllOrientations())
                {
                    if (!LinksMatch(model, candidate, a, b)) continue;
                    if (requiredSink != Vector3I.Zero && !HasSinkToward(model, candidate, requiredSink)) continue;

                    orientation = candidate;
                    return true;
                }
            }

            orientation = BlockOrientation.Identity;
            return false;
        }

/// <summary>TryOrient operation.</summary>
        public static bool TryOrient(BlockModel model, Vector3I a, Vector3I b, out BlockOrientation orientation)
        {
/// <summary>TryOrient operation.</summary>
            return TryOrient(model, a, b, Vector3I.Zero, out orientation);
        }

/// <summary>Orient operation.</summary>
        public static BlockOrientation Orient(BlockModel model, Vector3I a, Vector3I b)
        {
/// <summary>Orient operation.</summary>
            return Orient(model, a, b, Vector3I.Zero);
        }

/// <summary>Orient operation.</summary>
        public static BlockOrientation Orient(BlockModel model, Vector3I a, Vector3I b, Vector3I requiredSink)
        {
            BlockOrientation orientation;
            if (!TryOrient(model, a, b, requiredSink, out orientation))
            {
                throw new InvalidOperationException(
                    "No orientation of " + model.Name + " links " + a + " and " + b
                    + (requiredSink == Vector3I.Zero ? "" : " with a sink toward " + requiredSink));
            }
            return orientation;
        }

/// <summary>TryOrientDirected operation.</summary>
        public static bool TryOrientDirected(BlockModel model, Vector3I inlet, Vector3I outlet,
            out BlockOrientation orientation)
        {
            if (model != null && model.Coolant != null && model.Coolant.LinkPorts.Length == 2)
            {
                CoolantPort[] ports = model.Coolant.LinkPorts;

                foreach (BlockOrientation candidate in AllOrientations())
                {
                    if (candidate.Rotate(ports[0].LocalDirection) != inlet) continue;
                    if (candidate.Rotate(ports[1].LocalDirection) != outlet) continue;

                    orientation = candidate;
                    return true;
                }
            }

            orientation = BlockOrientation.Identity;
            return false;
        }

/// <summary>OrientDirected operation.</summary>
        public static BlockOrientation OrientDirected(BlockModel model, Vector3I inlet, Vector3I outlet)
        {
            BlockOrientation orientation;
            if (!TryOrientDirected(model, inlet, outlet, out orientation))
            {
                throw new InvalidOperationException(
                    "No orientation of " + model.Name + " takes fluid in from " + inlet
                    + " and out toward " + outlet);
            }
            return orientation;
        }

/// <summary>LinksMatch operation.</summary>
        private static bool LinksMatch(BlockModel model, BlockOrientation orientation, Vector3I a, Vector3I b)
        {
            CoolantPort[] ports = model.Coolant.LinkPorts;
            if (ports.Length != 2) return false;

            Vector3I first = orientation.Rotate(ports[0].LocalDirection);
            Vector3I second = orientation.Rotate(ports[1].LocalDirection);

            return (first == a && second == b) || (first == b && second == a);
        }

/// <summary>HasSinkToward operation.</summary>
        private static bool HasSinkToward(BlockModel model, BlockOrientation orientation, Vector3I gridDirection)
        {
            CoolantPort[] sinks = model.Coolant.SinkPorts;
            for (int i = 0; i < sinks.Length; i++)
            {
                if (orientation.Rotate(sinks[i].LocalDirection) == gridDirection) return true;
            }
            return false;
        }

/// <summary>Builds the API method table.</summary>
        public static List<BlockInstance> BuildPumplessRing(GridBuilder builder, IList<Vector3I> cells)
        {
            if (builder == null) throw new ArgumentNullException("builder");
            if (cells == null || cells.Count < 4) throw new ArgumentException("A ring needs at least four cells");

/// <summary>List operation.</summary>
            List<BlockInstance> ring = new List<BlockInstance>();

            for (int i = 0; i < cells.Count; i++)
            {
                Vector3I cell = cells[i];
                Vector3I toPrevious = cells[(i - 1 + cells.Count) % cells.Count] - cell;
                Vector3I toNext = cells[(i + 1) % cells.Count] - cell;

                BlockModel model = toPrevious == -toNext
                    ? Catalog.CoolantPipeStraight()
                    : Catalog.CoolantPipeCorner();

                builder.Place(model, cell, Orient(model, toPrevious, toNext));
                ring.Add(builder.Last);
            }

            return ring;
        }

/// <summary>Builds the method table.</summary>
        public static List<BlockInstance> BuildRing(
            GridBuilder builder,
            IList<Vector3I> cells,
            int pumpIndex = -1,
            IDictionary<int, Vector3I> sinkDirections = null,
            bool reversePump = false)
        {
            if (builder == null) throw new ArgumentNullException("builder");
            if (cells == null || cells.Count < 4) throw new ArgumentException("A ring needs at least four cells");

            if (pumpIndex < 0) pumpIndex = FirstStraightIndexAvoiding(cells, sinkDirections);
            else if (sinkDirections != null && sinkDirections.ContainsKey(pumpIndex))
            {
                throw new ArgumentException(
                    "Ring index " + pumpIndex + " was given both the pump and a sink face. A pump has "
                    + "no sink ports, so the sink would be dropped and the ring would test nothing.");
            }

/// <summary>List operation.</summary>
            List<BlockInstance> ring = new List<BlockInstance>();

            for (int i = 0; i < cells.Count; i++)
            {
                Vector3I cell = cells[i];
                Vector3I previous = cells[(i - 1 + cells.Count) % cells.Count];
                Vector3I next = cells[(i + 1) % cells.Count];

                Vector3I toPrevious = previous - cell;
                Vector3I toNext = next - cell;

                if (!IsUnitStep(toPrevious) || !IsUnitStep(toNext))
                {
                    throw new ArgumentException("Ring cells must be adjacent: " + previous + " -> " + cell + " -> " + next);
                }

                bool isStraight = toPrevious == -toNext;

                Vector3I sink = Vector3I.Zero;
                bool wantsSink = sinkDirections != null && sinkDirections.TryGetValue(i, out sink);

                BlockModel model;
                if (i == pumpIndex)
                {
                    if (!isStraight) throw new ArgumentException("The pump cell must be a straight run");
                    model = Catalog.CoolantPump();
                    sink = Vector3I.Zero;

                    builder.Place(model, cell, reversePump
/// <summary>OrientDirected operation.</summary>
                        ? OrientDirected(model, toNext, toPrevious)
/// <summary>OrientDirected operation.</summary>
                        : OrientDirected(model, toPrevious, toNext));
                    ring.Add(builder.Last);
                    continue;
                }
/// <summary>if operation.</summary>
                else if (isStraight)
                {
                    model = wantsSink
                        ? Catalog.CoolantPipeStraight(Vector3I.Right)
                        : Catalog.CoolantPipeStraight();
                }
                else
                {
                    model = wantsSink
                        ? Catalog.CoolantPipeCorner(Vector3I.Up)
                        : Catalog.CoolantPipeCorner();
                }

/// <summary>Orient operation.</summary>
                BlockOrientation orientation = Orient(model, toPrevious, toNext, sink);
                builder.Place(model, cell, orientation);
                ring.Add(builder.Last);
            }

            return ring;
        }

/// <summary>IsUnitStep operation.</summary>
        private static bool IsUnitStep(Vector3I step)
        {
            return Face.IndexOf(step) >= 0;
        }

/// <summary>FirstStraightIndexAvoiding operation.</summary>
        public static int FirstStraightIndexAvoiding(IList<Vector3I> cells, IDictionary<int, Vector3I> sinks)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (sinks != null && sinks.ContainsKey(i)) continue;

                Vector3I toPrevious = cells[(i - 1 + cells.Count) % cells.Count] - cells[i];
                Vector3I toNext = cells[(i + 1) % cells.Count] - cells[i];
                if (toPrevious == -toNext) return i;
            }

            throw new ArgumentException(
                "This ring has no straight run left for a pump once every requested sink is placed");
        }

/// <summary>FirstStraightIndex operation.</summary>
        public static int FirstStraightIndex(IList<Vector3I> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                Vector3I toPrevious = cells[(i - 1 + cells.Count) % cells.Count] - cells[i];
                Vector3I toNext = cells[(i + 1) % cells.Count] - cells[i];
                if (toPrevious == -toNext) return i;
            }
            throw new ArgumentException("This ring has no straight run to put a pump on");
        }

/// <summary>RectangleXZ operation.</summary>
        public static List<Vector3I> RectangleXZ(Vector3I origin, int width, int depth)
        {
            if (width < 2 || depth < 2) throw new ArgumentException("A rectangle ring needs sides of at least two");

            return Rectangle(origin, width, depth, new Vector3I(1, 0, 0), new Vector3I(0, 0, 1));
        }

/// <summary>RectangleXY operation.</summary>
        public static List<Vector3I> RectangleXY(Vector3I origin, int width, int height)
        {
            return Rectangle(origin, width, height, new Vector3I(1, 0, 0), new Vector3I(0, 1, 0));
        }

/// <summary>RectangleYZ operation.</summary>
        public static List<Vector3I> RectangleYZ(Vector3I origin, int height, int depth)
        {
            return Rectangle(origin, height, depth, new Vector3I(0, 1, 0), new Vector3I(0, 0, 1));
        }

/// <summary>Rectangle operation.</summary>
        private static List<Vector3I> Rectangle(Vector3I origin, int along, int across,
            Vector3I first, Vector3I second)
        {
            if (along < 2 || across < 2) throw new ArgumentException("A rectangle ring needs sides of at least two");

/// <summary>List operation.</summary>
            List<Vector3I> cells = new List<Vector3I>();
            for (int a = 0; a < along; a++) cells.Add(origin + (first * a));
            for (int b = 1; b < across; b++) cells.Add(origin + (first * (along - 1)) + (second * b));
            for (int a = along - 2; a >= 0; a--) cells.Add(origin + (first * a) + (second * (across - 1)));
            for (int b = across - 2; b >= 1; b--) cells.Add(origin + (second * b));
            return cells;
        }
    }
}
