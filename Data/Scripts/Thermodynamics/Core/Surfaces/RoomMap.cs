using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The result of a room mapping pass: which cells see open space, which are sealed
    /// structure, and which belong to an enclosed pocket.
    ///
    /// The pocket boundaries are <em>structural</em> — doors count as shut whatever they are
    /// doing — so a room is a property of how the ship is built and survives its doors being
    /// used. What a door does when it opens is recorded as a <see cref="RoomPortal"/>, and
    /// <see cref="RefreshVenting"/> resolves the portals into which rooms currently reach open
    /// air. That is a walk over the doors, not over the grid.
    ///
    /// The geometry is immutable from the simulation's point of view — <see cref="RoomMapper"/>
    /// builds a fresh map and swaps it in only when the pass finishes, so readers never see a
    /// half-filled one. Venting is the one thing that changes in place, because it has to be able
    /// to change in the same frame a player presses a button.
    /// </summary>
    public class RoomMap
    {
        /// <summary>
        /// How many cells the pass classified as open air, and the box it classified them in.
        ///
        /// The cells themselves are not stored. On a hull nine tenths of the bounding box is open
        /// air — 1.3 million cells of 1.5 million on a 127,000-block ship — and every one of them
        /// was held in a hash set at about forty bytes, to record the absence of anything. It is
        /// the default: a cell inside the box that is neither solid nor in a room is external, by
        /// definition, so the set was storing what the other two already implied.
        /// </summary>
        private int externalCount;

        private Vector3I searchMin;
        private Vector3I searchMaxExclusive;
        private readonly HashSet<Vector3I> solid = new HashSet<Vector3I>(Vector3I.Comparer);
        private readonly List<HashSet<Vector3I>> rooms = new List<HashSet<Vector3I>>();
        private readonly Dictionary<Vector3I, int> roomIndexByCell = new Dictionary<Vector3I, int>(Vector3I.Comparer);

        private readonly List<RoomPortal> portals = new List<RoomPortal>();

        /// <summary>Per room: true when it currently reaches open air through open doors.</summary>
        private bool[] vented = new bool[0];

        /// <summary>Union-find parent per region, reused between refreshes to avoid allocating.</summary>
        private int[] parent = new int[0];

        /// <summary>Rooms whose venting changed in the last <see cref="RefreshVenting"/>.</summary>
        private readonly List<int> changedRooms = new List<int>();

        /// <summary>The region index standing for open air, as opposed to a room.</summary>
        public const int ExternalRegion = -1;

        /// <summary>
        /// An empty map treats everything as external, which is the safe default before the
        /// first pass completes: blocks radiate rather than silently cooking.
        /// </summary>
        public static readonly RoomMap AllExternal = new RoomMap();

        public int RoomCount
        {
            get { return rooms.Count; }
        }

        public int ExternalCellCount
        {
            get { return externalCount; }
        }

        public int SolidCellCount
        {
            get { return solid.Count; }
        }

        /// <summary>Cells belonging to some enclosed room, across every room.</summary>
        public int RoomCellCount
        {
            get { return roomIndexByCell.Count; }
        }

        /// <summary>
        /// True when nothing has been classified at all — <see cref="AllExternal"/>, or a map
        /// whose pass has not run. Every query still answers "external", which is the safe
        /// default, but it is a default and not a measurement, and a reader has to be able to
        /// tell the two apart.
        /// </summary>
        public bool IsEmpty
        {
            get { return externalCount == 0 && solid.Count == 0 && roomIndexByCell.Count == 0; }
        }

        public IList<HashSet<Vector3I>> Rooms
        {
            get { return rooms; }
        }

        /// <summary>
        /// Every cell the pass reached from outside. Exposed for
        /// <see cref="UnmappedRooms"/>, which offers each one to the game to find the
        /// compartments this map lost — a diagnostic, and the only thing that reads it.
        /// </summary>
        public IEnumerable<Vector3I> ExternalCells
        {
            get { return EnumerateExternal(); }
        }

        /// <summary>Every door that opens onto one of these rooms, or onto open air.</summary>
        public IList<RoomPortal> Portals
        {
            get { return portals; }
        }

        /// <summary>
        /// True when the room currently reaches open air — directly through an open door, or
        /// through a chain of open doors and other rooms.
        ///
        /// A vented room is still a room. It is simply not holding anything in, so the surfaces
        /// facing it see outdoors, which is what <see cref="IsExternal"/> reports.
        /// </summary>
        public bool IsVented(int roomIndex)
        {
            if (roomIndex < 0 || roomIndex >= vented.Length) return false;
            return vented[roomIndex];
        }

        /// <summary>Rooms sealed off from open air right now.</summary>
        public int AirtightRoomCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < rooms.Count; i++)
                {
                    if (!IsVented(i)) count++;
                }
                return count;
            }
        }

        /// <summary>
        /// Rooms whose venting changed during the last <see cref="RefreshVenting"/>. Only the
        /// blocks facing these need their exposure recomputed.
        /// </summary>
        public IList<int> ChangedRooms
        {
            get { return changedRooms; }
        }

        /// <summary>
        /// The region a cell belongs to: a room index, or <see cref="ExternalRegion"/> for open
        /// air. Solid structure has no region and answers <see cref="ExternalRegion"/> too, so
        /// callers that care must ask <see cref="IsSolid"/> first.
        /// </summary>
        public int RegionOf(Vector3I cell)
        {
            int index;
            return roomIndexByCell.TryGetValue(cell, out index) ? index : ExternalRegion;
        }

        /// <summary>
        /// True when the cell can reach open space without crossing a seal. Cells the pass
        /// never visited — anything outside the grid's bounding box — are external by
        /// definition, so this answers by exclusion rather than by lookup.
        /// </summary>
        public bool IsExternal(Vector3I cell)
        {
            if (solid.Contains(cell)) return false;

            int room;
            if (!roomIndexByCell.TryGetValue(cell, out room)) return true;

            // A room standing open through a door is not holding anything in, so what faces it
            // faces outdoors. The room still exists; it is the venting that changed, and that is
            // a flag rather than a rebuild.
            return IsVented(room);
        }

        /// <summary>Index into <see cref="Rooms"/>, or -1 when the cell is not in a room.</summary>
        public int RoomIndexOf(Vector3I cell)
        {
            int index;
            return roomIndexByCell.TryGetValue(cell, out index) ? index : -1;
        }

        public bool IsSolid(Vector3I cell)
        {
            return solid.Contains(cell);
        }

        internal void AddExternal(Vector3I cell)
        {
            externalCount++;
        }

        /// <summary>Records the box the pass classified, so open air can be enumerated from it.</summary>
        internal void SetSearchBounds(Vector3I min, Vector3I maxExclusive)
        {
            searchMin = min;
            searchMaxExclusive = maxExclusive;
        }

        /// <summary>
        /// The cells classified as open air, walked rather than stored.
        ///
        /// Scan order, which is deterministic and repeatable — better for a diagnostic than a hash
        /// set's ordering was. Only the room-leak audit wants these, it stops at a cell limit, and
        /// it runs when something is asking.
        /// </summary>
        private IEnumerable<Vector3I> EnumerateExternal()
        {
            for (int z = searchMin.Z; z < searchMaxExclusive.Z; z++)
            {
                for (int y = searchMin.Y; y < searchMaxExclusive.Y; y++)
                {
                    for (int x = searchMin.X; x < searchMaxExclusive.X; x++)
                    {
                        Vector3I cell = new Vector3I(x, y, z);
                        if (solid.Contains(cell)) continue;
                        if (roomIndexByCell.ContainsKey(cell)) continue;
                        yield return cell;
                    }
                }
            }
        }

        internal void AddSolid(Vector3I cell)
        {
            solid.Add(cell);
        }

        internal int BeginRoom()
        {
            rooms.Add(new HashSet<Vector3I>(Vector3I.Comparer));
            return rooms.Count - 1;
        }

        internal void AddToRoom(int roomIndex, Vector3I cell)
        {
            rooms[roomIndex].Add(cell);
            roomIndexByCell[cell] = roomIndex;
        }

        internal void AddPortal(RoomPortal portal)
        {
            portals.Add(portal);
        }

        /// <summary>
        /// Recomputes which rooms reach open air, from the doors' current states.
        ///
        /// Union-find over the rooms and one extra node for open air: every open portal merges
        /// the two regions it joins, and any room that ends up in open air's set is vented. The
        /// cost is the number of doors, not the number of cells, which is the entire reason the
        /// room map is built on structure rather than on what is currently shut.
        /// </summary>
        /// <returns>True when any room changed state.</returns>
        public bool RefreshVenting()
        {
            changedRooms.Clear();

            int count = rooms.Count;
            if (vented.Length != count) vented = new bool[count];

            // one slot per room plus a final slot standing for open air
            if (parent.Length != count + 1) parent = new int[count + 1];
            for (int i = 0; i <= count; i++) parent[i] = i;

            for (int p = 0; p < portals.Count; p++)
            {
                RoomPortal portal = portals[p];
                if (!portal.IsOpen) continue;

                Union(Slot(portal.RegionA, count), Slot(portal.RegionB, count));
            }

            int air = Find(count);
            bool changed = false;

            for (int i = 0; i < count; i++)
            {
                bool now = Find(i) == air;
                if (now == vented[i]) continue;

                vented[i] = now;
                changedRooms.Add(i);
                changed = true;
            }

            return changed;
        }

        private static int Slot(int region, int roomCount)
        {
            return region == ExternalRegion ? roomCount : region;
        }

        private int Find(int node)
        {
            while (parent[node] != node)
            {
                parent[node] = parent[parent[node]];   // path halving
                node = parent[node];
            }
            return node;
        }

        private void Union(int a, int b)
        {
            int rootA = Find(a);
            int rootB = Find(b);
            if (rootA == rootB) return;

            parent[rootA] = rootB;
        }

        /// <summary>
        /// Whether a completed pass has an answer for this cell — which, once it has run, means
        /// whether the cell is inside the box it walked.
        /// </summary>
        internal bool IsKnown(Vector3I cell)
        {
            if (solid.Contains(cell) || roomIndexByCell.ContainsKey(cell)) return true;
            return GridMath.Contains(searchMin, searchMaxExclusive, cell);
        }

        internal void DropEmptyRooms()
        {
            for (int i = rooms.Count - 1; i >= 0; i--)
            {
                if (rooms[i].Count != 0) continue;

                rooms.RemoveAt(i);
                List<Vector3I> affected = new List<Vector3I>();
                foreach (KeyValuePair<Vector3I, int> entry in roomIndexByCell)
                {
                    if (entry.Value > i) affected.Add(entry.Key);
                }
                for (int a = 0; a < affected.Count; a++)
                {
                    roomIndexByCell[affected[a]] = roomIndexByCell[affected[a]] - 1;
                }
            }
        }
    }
}
