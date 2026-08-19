using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Works out how to rotate a coolant block so its ports face where you need them, and lays
    /// complete rings out from a list of cells.
    ///
    /// Building rings by hand means deriving rotations by hand, which is exactly the kind of
    /// thing that produces a scenario that silently tests nothing.
    /// </summary>
    public static class PipeFitter
    {
        /// <summary>The 24 legal block orientations.</summary>
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

        /// <summary>
        /// Finds an orientation whose link ports face <paramref name="a"/> and
        /// <paramref name="b"/> in grid space, and — when <paramref name="requiredSink"/> is not
        /// zero — that also puts a sink port on that grid direction.
        /// </summary>
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

        public static bool TryOrient(BlockModel model, Vector3I a, Vector3I b, out BlockOrientation orientation)
        {
            return TryOrient(model, a, b, Vector3I.Zero, out orientation);
        }

        public static BlockOrientation Orient(BlockModel model, Vector3I a, Vector3I b)
        {
            return Orient(model, a, b, Vector3I.Zero);
        }

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

        private static bool LinksMatch(BlockModel model, BlockOrientation orientation, Vector3I a, Vector3I b)
        {
            CoolantPort[] ports = model.Coolant.LinkPorts;
            if (ports.Length != 2) return false;

            Vector3I first = orientation.Rotate(ports[0].LocalDirection);
            Vector3I second = orientation.Rotate(ports[1].LocalDirection);

            return (first == a && second == b) || (first == b && second == a);
        }

        private static bool HasSinkToward(BlockModel model, BlockOrientation orientation, Vector3I gridDirection)
        {
            CoolantPort[] sinks = model.Coolant.SinkPorts;
            for (int i = 0; i < sinks.Length; i++)
            {
                if (orientation.Rotate(sinks[i].LocalDirection) == gridDirection) return true;
            }
            return false;
        }

        /// <summary>
        /// Lays a closed ring of coolant blocks through <paramref name="cells"/>, in order,
        /// wrapping from the last cell back to the first.
        ///
        /// Each cell gets a straight or a corner depending on whether its two neighbours are
        /// opposite or perpendicular, rotated to fit. The cell at
        /// <paramref name="pumpIndex"/> gets a pump, which requires a straight run.
        /// </summary>
        /// <param name="pumpIndex">
        /// Ring index for the pump, or -1 to use the first straight run.
        /// </param>
        /// <param name="sinkDirections">
        /// Optional grid-space sink direction per ring index. Only straight runs can carry one.
        /// </param>
        public static List<BlockInstance> BuildRing(
            GridBuilder builder,
            IList<Vector3I> cells,
            int pumpIndex = -1,
            IDictionary<int, Vector3I> sinkDirections = null)
        {
            if (builder == null) throw new ArgumentNullException("builder");
            if (cells == null || cells.Count < 4) throw new ArgumentException("A ring needs at least four cells");

            // A pump carries no sink ports, so a sink asked for on the pump's cell cannot exist.
            // Silently dropping it is how a scenario ends up testing nothing — the exact failure
            // this class was written to prevent — so an automatic choice steps aside and an explicit
            // one is an error.
            if (pumpIndex < 0) pumpIndex = FirstStraightIndexAvoiding(cells, sinkDirections);
            else if (sinkDirections != null && sinkDirections.ContainsKey(pumpIndex))
            {
                throw new ArgumentException(
                    "Ring index " + pumpIndex + " was given both the pump and a sink face. A pump has "
                    + "no sink ports, so the sink would be dropped and the ring would test nothing.");
            }

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
                }
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

                BlockOrientation orientation = Orient(model, toPrevious, toNext, sink);
                builder.Place(model, cell, orientation);
                ring.Add(builder.Last);
            }

            return ring;
        }

        private static bool IsUnitStep(Vector3I step)
        {
            return Face.IndexOf(step) >= 0;
        }

        /// <summary>
        /// As <see cref="FirstStraightIndex"/>, skipping any cell the caller asked for a sink face
        /// on. Every rectangle's first straight run is index 1, so a caller asking for a sink there —
        /// which every scenario in this repository did — would otherwise lose it to the pump.
        /// </summary>
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

        /// <summary>
        /// Index of the first cell whose neighbours are directly opposite each other, which is
        /// where a straight block — and therefore a pump — can go.
        /// </summary>
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

        /// <summary>
        /// A rectangular ring in the XZ plane with one corner at <paramref name="origin"/>.
        /// </summary>
        public static List<Vector3I> RectangleXZ(Vector3I origin, int width, int depth)
        {
            if (width < 2 || depth < 2) throw new ArgumentException("A rectangle ring needs sides of at least two");

            List<Vector3I> cells = new List<Vector3I>();
            for (int x = 0; x < width; x++) cells.Add(origin + new Vector3I(x, 0, 0));
            for (int z = 1; z < depth; z++) cells.Add(origin + new Vector3I(width - 1, 0, z));
            for (int x = width - 2; x >= 0; x--) cells.Add(origin + new Vector3I(x, 0, depth - 1));
            for (int z = depth - 2; z >= 1; z--) cells.Add(origin + new Vector3I(0, 0, z));
            return cells;
        }
    }
}
