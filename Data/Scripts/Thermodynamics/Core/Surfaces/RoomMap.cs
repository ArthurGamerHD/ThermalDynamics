using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The result of a room mapping pass: which cells see open space, which are sealed structure, and
    /// which belong to an enclosed pocket. Boundaries are structural — a door counts as shut whatever
    /// its state — and venting is the one field that changes in place, because it must answer within
    /// the frame a door is operated. See thermal-model.md, Portals and venting.
    /// </summary>
    public class RoomMap
    {
        /// <summary>
        /// Count of cells the pass classified as open air, and the box it classified them in. The
        /// cells are derivable — inside the box, neither solid nor in a room — so they are counted
        /// rather than stored. See memory.md, 1b.
        /// </summary>
        private int externalCount;

        private Vector3I searchMin;
        private Vector3I searchMaxExclusive;
        /// <summary>
        /// The cells the pass classified as sealed structure, one bit each over the search box.
        /// A set the region is dense in — a hull is a third to three quarters structure — so a
        /// bitset holds it at an eighth of a byte a cell where a hash set held about forty bytes
        /// a member, and every exposure face that asks <see cref="IsExternal"/> reads a bit rather
        /// than hashing. Sized by <see cref="SetSearchBounds"/>, which every pass calls before it
        /// adds a cell; a map with no bounds holds nothing, which is what <see cref="AllExternal"/>
        /// is. See performance.md, Iteration 7.
        /// </summary>
        private readonly CellBitset solid = new CellBitset();
        /// <summary>
        /// The cells of every room, in the order the flood reached them, in **one array** with a
        /// start and a length per room.
        ///
        /// <para>
        /// The flood fills one room to exhaustion before it opens the next — only
        /// <c>currentRoom</c> is ever added to — so a room's cells are contiguous by construction,
        /// and <see cref="AddToRoom"/> refuses any other index rather than corrupting a range
        /// quietly. What that buys over a list per room is not the room objects, of which a hull
        /// this size has a few hundred: it is that a *rebuild* is handed the last pass's cell count
        /// (<see cref="HintRoomCells"/>) and so allocates its store once, at the right size, with
        /// no doubling copies to abandon and no trim copy at the end.
        /// See performance.md, Pass 4, Iteration 2.
        /// </para>
        ///
        /// An array rather than a set: containment goes to the frozen lookup these cells are the
        /// source of, and the flood cannot offer a cell twice because every add is behind a visited
        /// bitset. See memory.md, 4.
        /// </summary>
        private Vector3I[] roomCellStore = EmptyCells;
        private int[] roomStarts = EmptyRanges;
        private int[] roomLengths = EmptyRanges;
        private int roomCount;

        private static readonly Vector3I[] EmptyCells = new Vector3I[0];
        private static readonly int[] EmptyRanges = new int[0];
        /// <summary>
        /// Cells across every room, counted rather than held.
        ///
        /// <para>
        /// **The cell-to-room dictionary is gone.** It was written once per room cell during the
        /// flood, read by nobody while the pass ran — a working map is private until it is
        /// published — and enumerated once at the end to build the frozen arrays, which
        /// <see cref="roomCellStore"/> can supply directly since it holds the same cells with their room
        /// already known. At half a million blocks that was 1.5 million hash inserts and something
        /// like ninety megabytes of the two hundred and fifty the pass allocated.
        /// See performance.md, Pass 4, Iteration 1.
        /// </para>
        /// </summary>
        private int roomCellCount;

        /// <summary>
        /// Whether each cell of the search box belongs to some room, one bit each — the question
        /// <see cref="IsExternal"/> asks first, and answers *no* to for nearly every face it is
        /// asked about, since a face onto open space is what exposure is looking for.
        ///
        /// <para>
        /// Without it that answer costs a binary search over every room cell on the grid: about
        /// twenty dependent loads through 1.5 million keys at half a million blocks, per unsealed
        /// face, per block, on every exposure refresh. The search stays for the callers that need
        /// the room's *index*; this is the membership test in front of it, at an eighth of a byte a
        /// bounding cell beside the solid set it sits with.
        /// See performance.md, Pass 3, Iteration 3.
        /// </para>
        /// </summary>
        private readonly CellBitset roomCells = new CellBitset();


        /// <summary>
        /// Which room each room cell belongs to, once a pass has completed: one entry per member of
        /// <see cref="roomCells"/>, in the box's index order, read through that set's rank index.
        ///
        /// <para>
        /// **A map is written once and then read for the life of the grid.** The first form of this
        /// was a `Dictionary&lt;Vector3I, int&gt;` at about 31 bytes a cell to hold twelve bytes of
        /// answer. The second was a sorted `long[]` of keys and a parallel `int[]` of rooms —
        /// twelve bytes a cell and a binary search, which is twenty dependent loads through twelve
        /// megabytes at half a million blocks, and which had to be *sorted* on the tick a player is
        /// waiting on.
        /// </para>
        ///
        /// <para>
        /// This is the third and it is neither. The membership set already knows which cells are in
        /// rooms and already orders them; ranking it says *which* member a cell is, so the room can
        /// be an array lookup at that rank. Four bytes a cell, no keys held at all, no sort, and a
        /// query that is two loads and a popcount rather than a search.
        /// See performance.md, Pass 4, Iteration 3.
        /// </para>
        /// </summary>
        private int[] roomByRank = EmptyRanges;

        /// <summary>
        /// Whether a pass has completed and published its answers. A working map is private: it
        /// returns "no room" to every query until it is frozen, because a half-filled flood has no
        /// answer that will still be true when it finishes.
        /// </summary>
        private bool frozen;

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
            get { return roomCount; }
        }

        public int ExternalCellCount
        {
            get { return externalCount; }
        }

        public int SolidCellCount
        {
            get { return solid.Count; }
        }

        /// <summary>
        /// Cells the room store has room for. Equal to <see cref="RoomCellCount"/> after a
        /// completed pass, which is the property that says the store is carrying no doubling slack
        /// into the life of the grid; larger than it only while a pass is running. Reported by the
        /// memory benchmark beside the cells themselves, because slack is the difference between
        /// what the row measures and what the map needs.
        /// </summary>
        public int RoomCellCapacity
        {
            get { return roomCellStore.Length; }
        }

        /// <summary>Cells belonging to some enclosed room, across every room.</summary>
        public int RoomCellCount
        {
            get { return roomCellCount; }
        }

        /// <summary>
        /// True when nothing has been classified: <see cref="AllExternal"/>, or a map whose pass has
        /// not run. Queries still answer external, but as a default rather than a measurement, which
        /// readers must be able to distinguish.
        /// </summary>
        public bool IsEmpty
        {
            get { return externalCount == 0 && solid.Count == 0 && RoomCellCount == 0; }
        }

        /// <summary>
        /// One room's cells, as a window onto the shared store. Read in order; never searched. The
        /// window is a struct and enumerates through a struct, so walking a room allocates nothing —
        /// which matters because the solver walks every room's cells on every air rebuild.
        /// </summary>
        public RoomCells CellsOf(int roomIndex)
        {
            if (roomIndex < 0 || roomIndex >= roomCount) return new RoomCells(EmptyCells, 0, 0);
            return new RoomCells(roomCellStore, roomStarts[roomIndex], roomLengths[roomIndex]);
        }

        /// <summary>Cells in one room, without building the window.</summary>
        public int CellsInRoom(int roomIndex)
        {
            if (roomIndex < 0 || roomIndex >= roomCount) return 0;
            return roomLengths[roomIndex];
        }

        /// <summary>
        /// A room's cells: the shared array, and the half-open range within it that is this room's.
        /// </summary>
        public struct RoomCells
        {
            private readonly Vector3I[] store;
            private readonly int start;
            private readonly int count;

            internal RoomCells(Vector3I[] store, int start, int count)
            {
                this.store = store;
                this.start = start;
                this.count = count;
            }

            public int Count
            {
                get { return count; }
            }

            public Vector3I this[int index]
            {
                get { return store[start + index]; }
            }

            public Enumerator GetEnumerator()
            {
                return new Enumerator(store, start, count);
            }

            /// <summary>A struct enumerator, so <c>foreach</c> over a room boxes nothing.</summary>
            public struct Enumerator
            {
                private readonly Vector3I[] store;
                private readonly int start;
                private readonly int count;
                private int at;

                internal Enumerator(Vector3I[] store, int start, int count)
                {
                    this.store = store;
                    this.start = start;
                    this.count = count;
                    at = -1;
                }

                public Vector3I Current
                {
                    get { return store[start + at]; }
                }

                public bool MoveNext()
                {
                    at++;
                    return at < count;
                }
            }
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
                for (int i = 0; i < roomCount; i++)
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
            int index = RoomAt(cell);
            return index >= 0 ? index : ExternalRegion;
        }

        /// <summary>
        /// The room a cell belongs to, or -1. The one place that knows whether this map is frozen.
        /// </summary>
        private int RoomAt(Vector3I cell)
        {
            return RoomAtIndex(roomCells.IndexOf(cell));
        }

        /// <summary>
        /// The same answer for a cell whose box index the caller already has, which
        /// <see cref="IsExternal"/> does — the solid set and the room set cover the same box, so
        /// one index serves both.
        /// </summary>
        private int RoomAtIndex(long index)
        {
            // Only a published map answers this: a pass in flight has no ranks built, because
            // nothing outside it can ask and its own flood asks the bits rather than the ranks.
            if (!frozen) return -1;
            if (!roomCells.ContainsIndex(index)) return -1;

            int rank = roomCells.RankOfIndex(index);
            if (rank < 0 || rank >= roomCellCount) return -1;

            return roomByRank[rank];
        }

        /// <summary>
        /// Builds the answer <see cref="RoomAt"/> reads, from the rooms themselves. Called once,
        /// when a pass completes and the map stops being written to.
        ///
        /// <para>
        /// Two walks and no comparisons: one over the membership set's words to rank them — a
        /// sixty-fourth of the box — and one over the room cells to write each one's room at its
        /// rank. Where this used to sort 1.5 million keys on the tick that publishes the map, it
        /// now touches each room cell once, in the order the rooms hold them.
        /// </para>
        /// </summary>
        private void Freeze()
        {
            roomCells.BuildRanks();

            if (roomByRank.Length < roomCellCount) roomByRank = new int[roomCellCount];

            for (int r = 0; r < roomCount; r++)
            {
                int start = roomStarts[r];
                int end = start + roomLengths[r];
                for (int i = start; i < end; i++)
                {
                    int rank = roomCells.RankOfIndex(roomCells.IndexOf(roomCellStore[i]));
                    if (rank >= 0 && rank < roomCellCount) roomByRank[rank] = r;
                }
            }

            frozen = true;
        }

        /// <summary>
        /// True when the cell can reach open space without crossing a seal. Cells the pass never
        /// visited, meaning anything outside the grid's bounding box, are external by definition, so
        /// this answers by exclusion rather than by lookup.
        /// </summary>
        public bool IsExternal(Vector3I cell)
        {
            // Both sets cover the same box, so the cell's place in it is derived once and read
            // twice — three subtractions, six compares and two multiplies that exposure was
            // paying per face, twice. See performance.md, Pass 3, Iteration 5.
            long index = solid.IndexOf(cell);
            if (solid.ContainsIndex(index)) return false;

            // The common answer, in one bit rather than in a search: a cell in no room is outside.
            if (!roomCells.ContainsIndex(index)) return true;

            int room = RoomAtIndex(index);
            if (room < 0) return true;

            // A room standing open through a door holds no air, so what faces it faces outdoors. The
            // room still exists: only its venting flag changed, not the map.
            return IsVented(room);
        }

        /// <summary>The cell's room, in the numbering <see cref="CellsOf"/> reads, or -1 when it is in none.</summary>
        public int RoomIndexOf(Vector3I cell)
        {
            return RoomAt(cell);
        }

        public bool IsSolid(Vector3I cell)
        {
            return solid.Contains(cell);
        }

        internal void AddExternal(Vector3I cell)
        {
            externalCount++;
        }

        /// <summary>
        /// The same, for a whole run of open-air cells at once. External cells are counted rather
        /// than stored (see <see cref="externalCount"/>), so a run costs one addition.
        /// </summary>
        internal void AddExternalRun(int count)
        {
            externalCount += count;
        }

        /// <summary>Records the box the pass classified, so open air can be enumerated from it.</summary>
        internal void SetSearchBounds(Vector3I min, Vector3I maxExclusive)
        {
            searchMin = min;
            searchMaxExclusive = maxExclusive;
            solid.Reset(min, maxExclusive);
            roomCells.Reset(min, maxExclusive);
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
                        if (RoomAt(cell) >= 0) continue;
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
            if (roomCount == roomStarts.Length)
            {
                int size = roomStarts.Length == 0 ? 16 : roomStarts.Length * 2;
                int[] starts = new int[size];
                int[] lengths = new int[size];
                Array.Copy(roomStarts, starts, roomCount);
                Array.Copy(roomLengths, lengths, roomCount);
                roomStarts = starts;
                roomLengths = lengths;
            }

            roomStarts[roomCount] = roomCellCount;
            roomLengths[roomCount] = 0;
            roomCount++;
            return roomCount - 1;
        }

        /// <summary>
        /// Sizes the cell store for a pass expected to find about this many room cells. A rebuild
        /// knows that number: the pass before it found one. Called before the first cell, or
        /// ignored — a store with cells in it is not resized under them.
        /// </summary>
        internal void HintRoomCells(int expected)
        {
            if (roomCellCount != 0 || expected <= roomCellStore.Length) return;
            roomCellStore = new Vector3I[expected];
        }

        internal void AddToRoom(int roomIndex, Vector3I cell)
        {
            // A pass that resumed after a freeze would be answering queries from ranks that no
            // longer match its cells. It cannot happen — a map is filled once — and saying so is
            // cheaper than finding out. (The set drops its own ranks on the add below; this is the
            // half that stops the map answering from them.)
            frozen = false;

            // Contiguity is the whole design: a room owns a range, so only the room the flood is
            // currently filling can grow. Refusing here is how that stays true, because the failure
            // it prevents is silent — a cell filed under the wrong room's range.
            if (roomIndex != roomCount - 1)
            {
                throw new InvalidOperationException(
                    "a room map is filled one room at a time; room " + roomIndex +
                    " cannot grow while room " + (roomCount - 1) + " is open");
            }

            if (roomCellCount == roomCellStore.Length)
            {
                int size = roomCellStore.Length == 0 ? 1024 : roomCellStore.Length * 2;
                Vector3I[] grown = new Vector3I[size];
                Array.Copy(roomCellStore, grown, roomCellCount);
                roomCellStore = grown;
            }

            roomCellStore[roomCellCount] = cell;
            roomLengths[roomIndex]++;
            roomCells.Add(cell);
            roomCellCount++;
        }

        internal void AddPortal(RoomPortal portal)
        {
            portals.Add(portal);
        }

        /// <summary>
        /// Recomputes which rooms reach open air from the doors' current states: union-find over the
        /// rooms plus one node for open air, costing the number of doors rather than of cells.
        /// </summary>
        /// <returns>True when any room changed state.</returns>
        public bool RefreshVenting()
        {
            changedRooms.Clear();

            int count = roomCount;
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
            if (solid.Contains(cell) || RoomAt(cell) >= 0) return true;
            return GridMath.Contains(searchMin, searchMaxExclusive, cell);
        }

        /// <summary>
        /// Called once when a pass completes: drops the rooms the flood opened and never filled, then
        /// hands back whatever doubling slack the cell store is carrying, which on a large hull is
        /// tens of megabytes. A pass that was hinted correctly has none and skips the copy.
        /// </summary>
        internal void DropEmptyRooms()
        {
            // Ranges compact; cells do not move. An empty room owns no cells, so removing its range
            // leaves every surviving room's start and length exactly as they were, and nothing needs
            // renumbering: the freeze below reads the surviving ranges in order.
            int kept = 0;
            for (int i = 0; i < roomCount; i++)
            {
                if (roomLengths[i] == 0) continue;

                roomStarts[kept] = roomStarts[i];
                roomLengths[kept] = roomLengths[i];
                kept++;
            }
            roomCount = kept;

            if (roomCellStore.Length > roomCellCount)
            {
                Vector3I[] exact = new Vector3I[roomCellCount];
                Array.Copy(roomCellStore, exact, roomCellCount);
                roomCellStore = exact;
            }

            // The map is written once and read for the life of the grid, so this is where its
            // lookup is built — from the rooms, in one pass over each.
            Freeze();
        }
    }
}
