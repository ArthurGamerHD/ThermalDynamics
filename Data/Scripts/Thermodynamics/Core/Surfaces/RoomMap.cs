using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The result of a room mapping pass: which cells see open space, which are sealed
    /// structure, and which belong to an enclosed pocket.
    ///
    /// Pocket boundaries are structural: doors count as shut whatever their state, so a room is a
    /// property of how the grid is built and survives its doors being used. A door's open state is
    /// recorded as a <see cref="RoomPortal"/>, and <see cref="RefreshVenting"/> resolves the
    /// portals into which rooms currently reach open air — a walk over the doors, not the grid.
    ///
    /// The geometry is immutable to the simulation: <see cref="RoomMapper"/> builds a fresh map and
    /// swaps it in only when a pass completes, so readers never see a partial one. Venting is the
    /// one field that changes in place, since it must respond within the frame a door is operated.
    /// </summary>
    public class RoomMap
    {
        /// <summary>
        /// Count of cells the pass classified as open air, and the box it classified them in.
        ///
        /// The cells themselves are not stored. Most of a grid's bounding box is open air — 1.3 of
        /// 1.5 million cells on a 127,000-block grid — and a cell inside the box that is neither
        /// solid nor in a room is external by definition, so storing them duplicates the other two
        /// sets at about forty bytes each.
        /// </summary>
        private int externalCount;

        private Vector3I searchMin;
        private Vector3I searchMaxExclusive;
        private readonly HashSet<Vector3I> solid = new HashSet<Vector3I>(Vector3I.Comparer);
        /// <summary>
        /// The cells of each room, in the order the flood reached them.
        ///
        /// Lists rather than sets. Nothing asks a room whether it contains a cell — that question
        /// goes to <see cref="roomIndexByCell"/>, which answers it for every room at once — so the
        /// only things asked of these are their length and their contents in turn, and a set pays
        /// about seventeen bytes a cell over a list for a lookup nobody performs. The flood cannot
        /// offer the same cell twice: every <c>AddToRoom</c> is behind a visited bitset that was
        /// tested and set in the same breath, so there is nothing for a set to deduplicate either.
        /// </summary>
        private readonly List<List<Vector3I>> rooms = new List<List<Vector3I>>();
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
        /// An empty map, treating every cell as external. The safe default before the first pass
        /// completes: blocks radiate rather than accumulating heat unnoticed.
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
        /// True when nothing has been classified: <see cref="AllExternal"/>, or a map whose pass has
        /// not run. Queries still answer external, but as a default rather than a measurement, which
        /// readers must be able to distinguish.
        /// </summary>
        public bool IsEmpty
        {
            get { return externalCount == 0 && solid.Count == 0 && roomIndexByCell.Count == 0; }
        }

        /// <summary>The cells of each room. Read in order; never searched.</summary>
        public IList<List<Vector3I>> Rooms
        {
            get { return rooms; }
        }

        /// <summary>
        /// Every cell the pass reached from outside. Read only by <see cref="UnmappedRooms"/>, which
        /// offers each to the game to find compartments this map lost.
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
        /// True when the room currently reaches open air, directly through an open door or through a
        /// chain of open doors and other rooms.
        ///
        /// A vented room is still a room; it holds no air, so the surfaces facing it see outdoors,
        /// which <see cref="IsExternal"/> reports.
        /// </summary>
        public bool IsVented(int roomIndex)
        {
            if (roomIndex < 0 || roomIndex >= vented.Length) return false;
            return vented[roomIndex];
        }

        /// <summary>Rooms currently sealed off from open air.</summary>
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
        /// The region a cell belongs to: a room index, or <see cref="ExternalRegion"/> for open air.
        /// Solid structure has no region and also returns <see cref="ExternalRegion"/>, so callers
        /// that need to distinguish it must test <see cref="IsSolid"/> first.
        /// </summary>
        public int RegionOf(Vector3I cell)
        {
            int index;
            return roomIndexByCell.TryGetValue(cell, out index) ? index : ExternalRegion;
        }

        /// <summary>
        /// True when the cell can reach open space without crossing a seal. Cells the pass never
        /// visited, meaning anything outside the grid's bounding box, are external by definition, so
        /// this answers by exclusion rather than by lookup.
        /// </summary>
        public bool IsExternal(Vector3I cell)
        {
            if (solid.Contains(cell)) return false;

            int room;
            if (!roomIndexByCell.TryGetValue(cell, out room)) return true;

            // A room standing open through a door holds no air, so what faces it faces outdoors. The
            // room still exists: only its venting flag changed, not the map.
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
        /// The cells classified as open air, enumerated rather than stored.
        ///
        /// Yielded in scan order, which is deterministic and repeatable. Read only by the room-leak
        /// audit, which stops at a cell limit and runs only when something is reading it.
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
            rooms.Add(new List<Vector3I>());
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
        /// Recomputes which rooms reach open air from the doors' current states.
        ///
        /// Union-find over the rooms plus one node for open air: every open portal merges the two
        /// regions it joins, and any room in open air's set is vented. Cost is the number of doors
        /// rather than the number of cells, which is why the map is built on structure rather than
        /// on current door state.
        /// </summary>
        /// <returns>True when any room changed state.</returns>
        public bool RefreshVenting()
        {
            changedRooms.Clear();

            int count = rooms.Count;
            if (vented.Length != count) vented = new bool[count];

            // One slot per room, plus a final slot representing open air.
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
        /// Whether a completed pass has an answer for this cell, which after a pass means whether the
        /// cell lies inside the box it walked.
        /// </summary>
        internal bool IsKnown(Vector3I cell)
        {
            if (solid.Contains(cell) || roomIndexByCell.ContainsKey(cell)) return true;
            return GridMath.Contains(searchMin, searchMaxExclusive, cell);
        }

        /// <summary>
        /// Called once when a pass completes. Drops the rooms the flood opened and never filled,
        /// then hands back the spare capacity in the rest.
        ///
        /// A list doubles as it grows, so a finished room carries up to as much empty capacity as
        /// it does cells — on a large hull that is tens of megabytes of nothing, held for as long
        /// as the grid exists. The map is immutable from here, so the trim can never be undone by
        /// a later add.
        ///
        /// Assigning <c>Capacity</c> rather than calling <c>TrimExcess</c>, which declines to do
        /// anything unless the list is under ninety per cent full and therefore leaves the common
        /// case — a room that stopped just past a doubling — carrying its slack.
        /// </summary>
        internal void DropEmptyRooms()
        {
            for (int i = rooms.Count - 1; i >= 0; i--)
            {
                if (rooms[i].Count != 0)
                {
                    rooms[i].Capacity = rooms[i].Count;
                    continue;
                }

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
