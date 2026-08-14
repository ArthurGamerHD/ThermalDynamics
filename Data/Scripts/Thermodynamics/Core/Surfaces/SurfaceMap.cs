using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Per-cell surface state for a grid: which faces seal and which carry mount surfaces, for
    /// the cell itself and for whatever sits next to it.
    ///
    /// Neighbour bits are always derived, never authored, so the map cannot drift out of sync
    /// with itself the way two independently-written halves can.
    /// </summary>
    public class SurfaceMap
    {
        private readonly Dictionary<Vector3I, int> states = new Dictionary<Vector3I, int>(Vector3I.Comparer);

        /// <summary>
        /// The same cells as <see cref="states"/>, with every block read as if its state were
        /// sealing — a door taken as shut.
        ///
        /// Two layers because two callers mean different things. Exposure asks what is sealing
        /// <em>now</em>, because an open doorway does radiate. The room mapper asks what the grid
        /// is <em>built</em> like, because a door swinging must not change the shape of the ship
        /// and force a new flood fill. The pair is kept in step by writing both from the same
        /// block in the same call, never independently.
        /// </summary>
        private readonly Dictionary<Vector3I, int> structure = new Dictionary<Vector3I, int>(Vector3I.Comparer);

        public int CellCount
        {
            get { return states.Count; }
        }

        public IEnumerable<Vector3I> Cells
        {
            get { return states.Keys; }
        }

        /// <summary>Surface state of a cell, or 0 when the cell is empty.</summary>
        public int GetState(Vector3I cell)
        {
            int state;
            return states.TryGetValue(cell, out state) ? state : 0;
        }

        public bool HasCell(Vector3I cell)
        {
            return states.ContainsKey(cell);
        }

        /// <summary>Writes a block's cells into the map and refreshes the affected neighbours.</summary>
        public void AddBlock(BlockInstance block)
        {
            if (block == null) return;

            Vector3I[] cells = block.Cells;
            int[] self = block.SelfSurfaces;

            int[] structural = block.StructuralSurfaces;

            for (int i = 0; i < cells.Length; i++)
            {
                states[cells[i]] = CellSurface.SelfOnly(self[i]);
                structure[cells[i]] = CellSurface.SelfOnly(structural == null ? self[i] : structural[i]);
            }

            for (int i = 0; i < cells.Length; i++)
            {
                RefreshCell(cells[i]);
                RefreshNeighboursOf(cells[i]);
            }
        }

        /// <summary>Removes a block's cells and refreshes what was touching them.</summary>
        public void RemoveBlock(BlockInstance block)
        {
            if (block == null) return;

            Vector3I[] cells = block.Cells;
            for (int i = 0; i < cells.Length; i++)
            {
                states.Remove(cells[i]);
                structure.Remove(cells[i]);
            }

            for (int i = 0; i < cells.Length; i++)
            {
                RefreshNeighboursOf(cells[i]);
            }
        }

        /// <summary>
        /// Rebuilds the whole map from a grid. Cheaper and more predictable than a long chain of
        /// incremental edits when many blocks change at once.
        /// </summary>
        public void Rebuild(GridModel grid)
        {
            states.Clear();
            structure.Clear();
            if (grid == null) return;

            IList<BlockInstance> blocks = grid.Blocks;
            for (int b = 0; b < blocks.Count; b++)
            {
                BlockInstance block = blocks[b];
                Vector3I[] cells = block.Cells;
                int[] self = block.SelfSurfaces;
                int[] structural = block.StructuralSurfaces;
                for (int i = 0; i < cells.Length; i++)
                {
                    states[cells[i]] = CellSurface.SelfOnly(self[i]);
                    structure[cells[i]] = CellSurface.SelfOnly(structural == null ? self[i] : structural[i]);
                }
            }

            List<Vector3I> keys = new List<Vector3I>(states.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                RefreshCell(keys[i]);
            }
        }

        /// <summary>Recomputes the derived neighbour half of one cell.</summary>
        public void RefreshCell(Vector3I cell)
        {
            Refresh(states, cell);
            Refresh(structure, cell);
        }

        private static void Refresh(Dictionary<Vector3I, int> layer, Vector3I cell)
        {
            int state;
            if (!layer.TryGetValue(cell, out state)) return;

            state = CellSurface.SelfOnly(state);
            for (int face = 0; face < Face.Count; face++)
            {
                int neighbourState;
                if (layer.TryGetValue(cell + Face.Offsets[face], out neighbourState))
                {
                    state |= CellSurface.NeighbourContribution(neighbourState, face);
                }
            }
            layer[cell] = state;
        }

        private void RefreshNeighboursOf(Vector3I cell)
        {
            for (int face = 0; face < Face.Count; face++)
            {
                RefreshCell(cell + Face.Offsets[face]);
            }
        }

        /// <summary>
        /// True when nothing can pass between two adjacent cells: either side sealing is enough.
        /// This is the connectivity rule the room mapper walks.
        /// </summary>
        public bool IsFaceSealed(Vector3I cell, int face)
        {
            int state = GetState(cell);
            if (CellSurface.SelfAirtight(state, face)) return true;

            int neighbourState = GetState(cell + Face.Offsets[face]);
            return CellSurface.SelfAirtight(neighbourState, Face.Opposite(face));
        }

        /// <summary>True when every face of the cell seals — solid structure, not a gap.</summary>
        public bool IsFullySealed(Vector3I cell)
        {
            return CellSurface.IsFullySealed(GetState(cell));
        }

        /// <summary>Structural state of a cell — every door read as shut — or 0 when empty.</summary>
        public int GetStructuralState(Vector3I cell)
        {
            int state;
            return structure.TryGetValue(cell, out state) ? state : 0;
        }

        /// <summary>
        /// <see cref="IsFaceSealed"/> against the structure layer. This is the connectivity rule
        /// the room mapper walks, so that the rooms it finds are a property of how the ship is
        /// built and not of which doors happen to be open.
        /// </summary>
        public bool IsFaceSealedStructurally(Vector3I cell, int face)
        {
            int state = GetStructuralState(cell);
            if (CellSurface.SelfAirtight(state, face)) return true;

            int neighbourState = GetStructuralState(cell + Face.Offsets[face]);
            return CellSurface.SelfAirtight(neighbourState, Face.Opposite(face));
        }

        public bool IsFullySealedStructurally(Vector3I cell)
        {
            return CellSurface.IsFullySealed(GetStructuralState(cell));
        }

        /// <summary>
        /// Counts, per face direction, how many of a block's cell faces are open to the outside.
        ///
        /// A face counts when it is on the block's boundary, the space beyond is external, the
        /// neighbour does not seal against it, and it is not a mount-to-mount contact with
        /// another block.
        /// </summary>
        public void GetExposedFaces(BlockInstance block, RoomMap rooms, int[] resultsByFace)
        {
            if (resultsByFace == null || resultsByFace.Length < Face.Count)
            {
                throw new ArgumentException("resultsByFace must have at least six entries");
            }

            Array.Clear(resultsByFace, 0, Face.Count);
            if (block == null) return;

            Vector3I min = block.Min;
            Vector3I maxExclusive = block.MaxExclusive;

            // Only the block's boundary is walked. A face that radiates has to be on the
            // outside, so the interior cells of a multi-cell block cannot contribute one — the
            // old volume walk visited them only to reject them, at a cost that grows as the
            // cube of block size rather than the square.
            for (int face = 0; face < Face.Count; face++)
            {
                Vector3I offset = Face.Offsets[face];
                int axis = Face.Axis(face);
                bool positive = BoxGeometry.Component(offset, axis) > 0;

                int slab = positive
                    ? BoxGeometry.Component(maxExclusive, axis) - 1
                    : BoxGeometry.Component(min, axis);

                int u = (axis + 1) % 3;
                int v = (axis + 2) % 3;

                int count = 0;

                for (int a = BoxGeometry.Component(min, u); a < BoxGeometry.Component(maxExclusive, u); a++)
                {
                    for (int b = BoxGeometry.Component(min, v); b < BoxGeometry.Component(maxExclusive, v); b++)
                    {
                        Vector3I cell = BoxGeometry.WithComponent(Vector3I.Zero, axis, slab);
                        cell = BoxGeometry.WithComponent(cell, u, a);
                        cell = BoxGeometry.WithComponent(cell, v, b);

                        int state = GetState(cell);
                        Vector3I neighbour = cell + offset;

                        // something on the other side seals this face off
                        if (CellSurface.NeighbourAirtight(state, face)) continue;

                        // two mount surfaces pressed together: bolted on, not exposed
                        if (CellSurface.NeighbourMount(state, face) && CellSurface.SelfMount(state, face)) continue;

                        // must actually reach the outside world
                        if (rooms != null && !rooms.IsExternal(neighbour)) continue;

                        count++;
                    }
                }

                resultsByFace[face] = count;
            }
        }

        /// <summary>
        /// Counts, per room, how many of a block's cell faces look into that room's air.
        ///
        /// The test is deliberately different from <see cref="GetExposedFaces"/>. A face is
        /// exposed when it can radiate to the sky; a face is in contact with room air when there
        /// is air on the other side of it, sealed or not — the inner skin of a bulkhead seals the
        /// compartment and warms it at the same time. So this asks only that the neighbouring
        /// cell hold no block and belong to a room that is currently holding its air in.
        ///
        /// The caller owns and clears <paramref name="results"/>. Rooms are few per block — a
        /// block bounds one or two — so a short list beats a dictionary.
        /// </summary>
        public void GetRoomContacts(BlockInstance block, RoomMap rooms, List<RoomContact> results)
        {
            if (results == null || block == null || rooms == null) return;

            Vector3I min = block.Min;
            Vector3I maxExclusive = block.MaxExclusive;

            for (int face = 0; face < Face.Count; face++)
            {
                Vector3I offset = Face.Offsets[face];
                int axis = Face.Axis(face);
                bool positive = BoxGeometry.Component(offset, axis) > 0;

                int slab = positive
                    ? BoxGeometry.Component(maxExclusive, axis) - 1
                    : BoxGeometry.Component(min, axis);

                int u = (axis + 1) % 3;
                int v = (axis + 2) % 3;

                for (int a = BoxGeometry.Component(min, u); a < BoxGeometry.Component(maxExclusive, u); a++)
                {
                    for (int b = BoxGeometry.Component(min, v); b < BoxGeometry.Component(maxExclusive, v); b++)
                    {
                        Vector3I cell = BoxGeometry.WithComponent(Vector3I.Zero, axis, slab);
                        cell = BoxGeometry.WithComponent(cell, u, a);
                        cell = BoxGeometry.WithComponent(cell, v, b);

                        Vector3I neighbour = cell + offset;

                        // Another block on the far side is a conduction joint, not air.
                        if (HasCell(neighbour)) continue;

                        int room = rooms.RoomIndexOf(neighbour);
                        if (room < 0 || rooms.IsVented(room)) continue;

                        Accumulate(results, room);
                    }
                }
            }
        }

        private static void Accumulate(List<RoomContact> results, int room)
        {
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].RoomIndex != room) continue;
                results[i] = new RoomContact(room, results[i].Faces + 1);
                return;
            }
            results.Add(new RoomContact(room, 1));
        }

        /// <summary>Convenience wrapper allocating the result array.</summary>
        public int[] GetExposedFaces(BlockInstance block, RoomMap rooms)
        {
            int[] result = new int[Face.Count];
            GetExposedFaces(block, rooms, result);
            return result;
        }

        public void Clear()
        {
            states.Clear();
            structure.Clear();
        }
    }
}
