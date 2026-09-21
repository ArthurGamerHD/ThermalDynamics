using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    public class RoomMap
    {
        private int externalCount;

        private Vector3I searchMin;
        private Vector3I searchMaxExclusive;
/// <summary>CellBitset operation.</summary>
        private readonly CellBitset solid = new CellBitset();
        private Vector3I[] roomCellStore = EmptyCells;
        private int[] roomStarts = EmptyRanges;
        private int[] roomLengths = EmptyRanges;
        private int roomCount;

        private static readonly Vector3I[] EmptyCells = new Vector3I[0];
        private static readonly int[] EmptyRanges = new int[0];
        private int roomCellCount;

/// <summary>CellBitset operation.</summary>
        private readonly CellBitset roomCells = new CellBitset();


        private int[] roomByRank = EmptyRanges;

        private bool frozen;

/// <summary>List operation.</summary>
        private readonly List<RoomPortal> portals = new List<RoomPortal>();

        private bool[] vented = new bool[0];

        private int[] parent = new int[0];

/// <summary>List operation.</summary>
        private readonly List<int> changedRooms = new List<int>();

        public const int ExternalRegion = -1;

/// <summary>RoomMap operation.</summary>
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

        public int RoomCellCapacity
        {
            get { return roomCellStore.Length; }
        }

        public int RoomCellCount
        {
            get { return roomCellCount; }
        }

        public bool IsEmpty
        {
            get { return externalCount == 0 && solid.Count == 0 && RoomCellCount == 0; }
        }

/// <summary>CellsOf operation.</summary>
        public RoomCells CellsOf(int roomIndex)
        {
            if (roomIndex < 0 || roomIndex >= roomCount) return new RoomCells(EmptyCells, 0, 0);
            return new RoomCells(roomCellStore, roomStarts[roomIndex], roomLengths[roomIndex]);
        }

/// <summary>CellsInRoom operation.</summary>
        public int CellsInRoom(int roomIndex)
        {
            if (roomIndex < 0 || roomIndex >= roomCount) return 0;
            return roomLengths[roomIndex];
        }

        public struct RoomCells
        {
            private readonly Vector3I[] store;
            private readonly int start;
            private readonly int count;

/// <summary>RoomCells operation.</summary>
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

/// <summary>Returns the enumerator.</summary>
            public Enumerator GetEnumerator()
            {
                return new Enumerator(store, start, count);
            }

            public struct Enumerator
            {
                private readonly Vector3I[] store;
                private readonly int start;
                private readonly int count;
                private int at;

/// <summary>Enumerator operation.</summary>
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

/// <summary>MoveNext operation.</summary>
                public bool MoveNext()
                {
                    at++;
                    return at < count;
                }
            }
        }

        public IEnumerable<Vector3I> ExternalCells
        {
/// <summary>EnumerateExternal operation.</summary>
            get { return EnumerateExternal(); }
        }

        public IList<RoomPortal> Portals
        {
            get { return portals; }
        }

/// <summary>IsVented operation.</summary>
        public bool IsVented(int roomIndex)
        {
            if (roomIndex < 0 || roomIndex >= vented.Length) return false;
            return vented[roomIndex];
        }

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

        public IList<int> ChangedRooms
        {
            get { return changedRooms; }
        }

/// <summary>RegionOf operation.</summary>
        public int RegionOf(Vector3I cell)
        {
/// <summary>RoomAt operation.</summary>
            int index = RoomAt(cell);
            return index >= 0 ? index : ExternalRegion;
        }

/// <summary>RoomAt operation.</summary>
        private int RoomAt(Vector3I cell)
        {
            return RoomAtIndex(roomCells.IndexOf(cell));
        }

/// <summary>RoomAtIndex operation.</summary>
        private int RoomAtIndex(long index)
        {
            if (!frozen) return -1;
            if (!roomCells.ContainsIndex(index)) return -1;

            int rank = roomCells.RankOfIndex(index);
            if (rank < 0 || rank >= roomCellCount) return -1;

            return roomByRank[rank];
        }

/// <summary>Freeze operation.</summary>
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

/// <summary>IsExternal operation.</summary>
        public bool IsExternal(Vector3I cell)
        {
            long index = solid.IndexOf(cell);
            if (solid.ContainsIndex(index)) return false;

            if (!roomCells.ContainsIndex(index)) return true;

/// <summary>RoomAtIndex operation.</summary>
            int room = RoomAtIndex(index);
            if (room < 0) return true;

/// <summary>IsVented operation.</summary>
            return IsVented(room);
        }

/// <summary>RoomIndexOf operation.</summary>
        public int RoomIndexOf(Vector3I cell)
        {
/// <summary>RoomAt operation.</summary>
            return RoomAt(cell);
        }

/// <summary>IsSolid operation.</summary>
        public bool IsSolid(Vector3I cell)
        {
            return solid.Contains(cell);
        }

/// <summary>Adds a external.</summary>
        internal void AddExternal(Vector3I cell)
        {
            externalCount++;
        }

/// <summary>Adds a externalrun.</summary>
        internal void AddExternalRun(int count)
        {
            externalCount += count;
        }

/// <summary>Reset operation.</summary>
        internal void Reset()
        {
            externalCount = 0;
            roomCount = 0;
            roomCellCount = 0;
            frozen = false;
            portals.Clear();
            changedRooms.Clear();
        }

/// <summary>Sets the searchbounds.</summary>
        internal void SetSearchBounds(Vector3I min, Vector3I maxExclusive)
        {
            searchMin = min;
            searchMaxExclusive = maxExclusive;
            solid.Reset(min, maxExclusive);
            roomCells.Reset(min, maxExclusive);
        }

/// <summary>EnumerateExternal operation.</summary>
        private IEnumerable<Vector3I> EnumerateExternal()
        {
            for (int z = searchMin.Z; z < searchMaxExclusive.Z; z++)
            {
                for (int y = searchMin.Y; y < searchMaxExclusive.Y; y++)
                {
                    for (int x = searchMin.X; x < searchMaxExclusive.X; x++)
                    {
/// <summary>Vector3I operation.</summary>
                        Vector3I cell = new Vector3I(x, y, z);
                        if (solid.Contains(cell)) continue;
                        if (RoomAt(cell) >= 0) continue;
                        yield return cell;
                    }
                }
            }
        }

/// <summary>Adds a solid.</summary>
        internal void AddSolid(Vector3I cell)
        {
            solid.Add(cell);
        }

/// <summary>BeginRoom operation.</summary>
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

/// <summary>HintRoomCells operation.</summary>
        internal void HintRoomCells(int expected)
        {
            if (roomCellCount != 0 || expected <= roomCellStore.Length) return;
            roomCellStore = new Vector3I[expected];
        }

/// <summary>Adds a toroom.</summary>
        internal void AddToRoom(int roomIndex, Vector3I cell)
        {
            frozen = false;

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

/// <summary>Adds a portal.</summary>
        internal void AddPortal(RoomPortal portal)
        {
            portals.Add(portal);
        }

/// <summary>RefreshVenting operation.</summary>
        public bool RefreshVenting()
        {
            changedRooms.Clear();

            int count = roomCount;
            if (vented.Length != count) vented = new bool[count];

            if (parent.Length != count + 1) parent = new int[count + 1];
            for (int i = 0; i <= count; i++) parent[i] = i;

            for (int p = 0; p < portals.Count; p++)
            {
                RoomPortal portal = portals[p];
                if (!portal.IsOpen) continue;

                Union(Slot(portal.RegionA, count), Slot(portal.RegionB, count));
            }

/// <summary>Find operation.</summary>
            int air = Find(count);
            bool changed = false;

            for (int i = 0; i < count; i++)
            {
/// <summary>Find operation.</summary>
                bool now = Find(i) == air;
                if (now == vented[i]) continue;

                vented[i] = now;
                changedRooms.Add(i);
                changed = true;
            }

            return changed;
        }

/// <summary>Slot operation.</summary>
        private static int Slot(int region, int roomCount)
        {
            return region == ExternalRegion ? roomCount : region;
        }

/// <summary>Find operation.</summary>
        private int Find(int node)
        {
            while (parent[node] != node)
            {
                parent[node] = parent[parent[node]];   // path halving
                node = parent[node];
            }
            return node;
        }

/// <summary>Union operation.</summary>
        private void Union(int a, int b)
        {
/// <summary>Find operation.</summary>
            int rootA = Find(a);
/// <summary>Find operation.</summary>
            int rootB = Find(b);
            if (rootA == rootB) return;

            parent[rootA] = rootB;
        }

/// <summary>IsKnown operation.</summary>
        internal bool IsKnown(Vector3I cell)
        {
            if (solid.Contains(cell) || RoomAt(cell) >= 0) return true;
            return GridMath.Contains(searchMin, searchMaxExclusive, cell);
        }

/// <summary>DropEmptyRooms operation.</summary>
        internal void DropEmptyRooms()
        {
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

            Freeze();
        }
    }
}
