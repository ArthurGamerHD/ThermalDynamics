using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    public static class UnmappedRooms
    {
        public class Region
        {
            public Vector3I Anchor;

/// <summary>HashSet operation.</summary>
            public readonly HashSet<Vector3I> Cells = new HashSet<Vector3I>(Vector3I.Comparer);

/// <summary>List operation.</summary>
            public readonly List<Leak> Leaks = new List<Leak>();

            public int CellCount
            {
                get { return Cells.Count; }
            }
        }

        public struct Leak
        {
            public Vector3I Cell;

            public int Face;

            public Vector3I Neighbour;

/// <summary>Leak operation.</summary>
            public Leak(Vector3I cell, int face, Vector3I neighbour)
            {
                Cell = cell;
                Face = face;
                Neighbour = neighbour;
            }
        }

/// <summary>Find operation.</summary>
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

/// <summary>List operation.</summary>
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

/// <summary>HashSet operation.</summary>
            HashSet<Vector3I> pool = new HashSet<Vector3I>(candidates, Vector3I.Comparer);
/// <summary>HashSet operation.</summary>
            HashSet<Vector3I> taken = new HashSet<Vector3I>(Vector3I.Comparer);
/// <summary>Queue operation.</summary>
            Queue<Vector3I> frontier = new Queue<Vector3I>();

            for (int i = 0; i < candidates.Count; i++)
            {
                Vector3I start = candidates[i];
                if (taken.Contains(start)) continue;

/// <summary>Region operation.</summary>
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

            results.Sort(CompareRegions);
            return complete;
        }

/// <summary>CollectLeaks operation.</summary>
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

        public const int DefaultCellLimit = 200000;

/// <summary>CompareRegions operation.</summary>
        private static int CompareRegions(Region a, Region b)
        {
/// <summary>CompareCells operation.</summary>
            return CompareCells(a.Anchor, b.Anchor);
        }

/// <summary>CompareCells operation.</summary>
        private static int CompareCells(Vector3I a, Vector3I b)
        {
            if (a.X != b.X) return a.X < b.X ? -1 : 1;
            if (a.Y != b.Y) return a.Y < b.Y ? -1 : 1;
            if (a.Z != b.Z) return a.Z < b.Z ? -1 : 1;
            return 0;
        }
    }
}
