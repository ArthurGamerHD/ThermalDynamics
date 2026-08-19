using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Rooms the game holds and this map does not.
    ///
    /// The room mapper decides sealing from the surface bits, which come from each block
    /// definition's pressurisation table read cell by cell. The game decides it from its own
    /// sealing test, which knows the true shape of a sloped block where this knows only a cell.
    /// Where the two disagree the model loses a compartment: the flood fill walks in from outside,
    /// the cells classify as external, no room is created, and nothing queries the game about a room
    /// that was never found — so the compartment appears in no overlay and no report.
    ///
    /// This locates them. Every cell the map calls external is offered to the game, the airtight
    /// ones are grouped into connected regions, and each region is a compartment the model lost.
    /// Diagnostic only: it is read by the report and the overlay, and the fix belongs in the
    /// surface bits.
    /// </summary>
    public static class UnmappedRooms
    {
        /// <summary>One compartment the game seals and the map does not.</summary>
        public class Region
        {
            /// <summary>
            /// Lexicographically smallest cell, matching <see cref="RoomAirNode.Anchor"/>. Regions are
            /// ordered by it, so a region keeps the same index across two dumps of an unchanged grid.
            /// </summary>
            public Vector3I Anchor;

            public readonly HashSet<Vector3I> Cells = new HashSet<Vector3I>(Vector3I.Comparer);

            /// <summary>
            /// Boundary faces leading out of the region that this model does not seal. These are the
            /// faces the game seals and this does not, so the blocks across them are the ones to fix.
            /// </summary>
            public readonly List<Leak> Leaks = new List<Leak>();

            public int CellCount
            {
                get { return Cells.Count; }
            }
        }

        /// <summary>One face where the game seals and this model does not.</summary>
        public struct Leak
        {
            /// <summary>The region cell the face belongs to, and which of its six faces.</summary>
            public Vector3I Cell;

            public int Face;

            /// <summary>The cell on the other side. The block failing to seal is in one of the two.</summary>
            public Vector3I Neighbour;

            public Leak(Vector3I cell, int face, Vector3I neighbour)
            {
                Cell = cell;
                Face = face;
                Neighbour = neighbour;
            }
        }

        /// <summary>Finds every compartment the game seals and <paramref name="map"/> does not.</summary>
        /// <param name="map">The published room map, whose external cells are the candidates.</param>
        /// <param name="surfaces">The surface bits, used to decide where this model lets air flow.</param>
        /// <param name="airtightHere">
        /// The game's verdict at a cell. Called once per external cell, so this runs on a dump or
        /// when the room overlay is up, never during a step.
        /// </param>
        /// <param name="results">Cleared and filled, ordered by anchor.</param>
        /// <param name="cellLimit">
        /// Ceiling on cells examined, bounding the cost on a large grid. The method returns false
        /// when the limit was reached, so a partial result is reported as partial.
        /// </param>
        public static bool Find(
            RoomMap map,
            SurfaceMap surfaces,
            Func<Vector3I, bool> airtightHere,
            List<Region> results,
            int cellLimit = DefaultCellLimit)
        {
            if (results == null) throw new ArgumentNullException("results");
            results.Clear();

            if (map == null || surfaces == null || airtightHere == null) return true;
            if (map.IsEmpty) return true;

            // Sorted, so the grouping below is deterministic and region indices are stable between
            // two runs over the same grid.
            List<Vector3I> candidates = new List<Vector3I>();
            IEnumerable<Vector3I> external = map.ExternalCells;

            int examined = 0;
            bool complete = true;

            foreach (Vector3I cell in external)
            {
                if (examined >= cellLimit)
                {
                    complete = false;
                    break;
                }

                examined++;
                if (airtightHere(cell)) candidates.Add(cell);
            }

            if (candidates.Count == 0) return complete;

            candidates.Sort(CompareCells);

            HashSet<Vector3I> pool = new HashSet<Vector3I>(candidates, Vector3I.Comparer);
            HashSet<Vector3I> taken = new HashSet<Vector3I>(Vector3I.Comparer);
            Queue<Vector3I> frontier = new Queue<Vector3I>();

            for (int i = 0; i < candidates.Count; i++)
            {
                Vector3I start = candidates[i];
                if (taken.Contains(start)) continue;

                Region region = new Region();
                region.Anchor = start;

                frontier.Clear();
                frontier.Enqueue(start);
                taken.Add(start);

                while (frontier.Count > 0)
                {
                    Vector3I cell = frontier.Dequeue();
                    region.Cells.Add(cell);

                    for (int face = 0; face < Face.Count; face++)
                    {
                        Vector3I next = cell + Face.Offsets[face];
                        if (!pool.Contains(next)) continue;
                        if (taken.Contains(next)) continue;

                        taken.Add(next);
                        frontier.Enqueue(next);
                    }
                }

                CollectLeaks(region, surfaces);
                results.Add(region);
            }

            // Ordered by anchor, so region 0 is the same region on the next run.
            results.Sort(CompareRegions);
            return complete;
        }

        /// <summary>
        /// The faces where this region ends and the model does not seal.
        ///
        /// A face leaving the region that this model calls sealed is not a leak: both models agree
        /// and the region simply ends there. A face leaving it that this model leaves open is the
        /// disagreement being measured.
        /// </summary>
        private static void CollectLeaks(Region region, SurfaceMap surfaces)
        {
            foreach (Vector3I cell in region.Cells)
            {
                for (int face = 0; face < Face.Count; face++)
                {
                    Vector3I neighbour = cell + Face.Offsets[face];
                    if (region.Cells.Contains(neighbour)) continue;
                    if (surfaces.IsFaceSealedStructurally(cell, face)) continue;

                    region.Leaks.Add(new Leak(cell, face, neighbour));
                }
            }
        }

        /// <summary>Cells examined before a scan gives up, roughly a large grid's bounding box.</summary>
        public const int DefaultCellLimit = 200000;

        private static int CompareRegions(Region a, Region b)
        {
            return CompareCells(a.Anchor, b.Anchor);
        }

        private static int CompareCells(Vector3I a, Vector3I b)
        {
            if (a.X != b.X) return a.X < b.X ? -1 : 1;
            if (a.Y != b.Y) return a.Y < b.Y ? -1 : 1;
            if (a.Z != b.Z) return a.Z < b.Z ? -1 : 1;
            return 0;
        }
    }
}
