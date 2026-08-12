using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The result of a room mapping pass: which cells see open space, which are sealed
    /// structure, and which belong to an enclosed pocket.
    ///
    /// Immutable from the simulation's point of view — <see cref="RoomMapper"/> builds a fresh
    /// one and swaps it in only when the pass finishes, so readers never see a half-filled map.
    /// </summary>
    public class RoomMap
    {
        private readonly HashSet<Vector3I> external = new HashSet<Vector3I>(Vector3I.Comparer);
        private readonly HashSet<Vector3I> solid = new HashSet<Vector3I>(Vector3I.Comparer);
        private readonly List<HashSet<Vector3I>> rooms = new List<HashSet<Vector3I>>();
        private readonly Dictionary<Vector3I, int> roomIndexByCell = new Dictionary<Vector3I, int>(Vector3I.Comparer);

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
            get { return external.Count; }
        }

        public int SolidCellCount
        {
            get { return solid.Count; }
        }

        public IList<HashSet<Vector3I>> Rooms
        {
            get { return rooms; }
        }

        /// <summary>
        /// True when the cell can reach open space without crossing a seal. Cells the pass
        /// never visited — anything outside the grid's bounding box — are external by
        /// definition, so this answers by exclusion rather than by lookup.
        /// </summary>
        public bool IsExternal(Vector3I cell)
        {
            if (solid.Contains(cell)) return false;
            if (roomIndexByCell.ContainsKey(cell)) return false;
            return true;
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
            external.Add(cell);
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

        internal bool IsKnown(Vector3I cell)
        {
            return external.Contains(cell) || solid.Contains(cell) || roomIndexByCell.ContainsKey(cell);
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
