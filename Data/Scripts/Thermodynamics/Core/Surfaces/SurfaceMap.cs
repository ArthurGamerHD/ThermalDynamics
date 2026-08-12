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

            for (int i = 0; i < cells.Length; i++)
            {
                states[cells[i]] = CellSurface.SelfOnly(self[i]);
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
            if (grid == null) return;

            IList<BlockInstance> blocks = grid.Blocks;
            for (int b = 0; b < blocks.Count; b++)
            {
                BlockInstance block = blocks[b];
                Vector3I[] cells = block.Cells;
                int[] self = block.SelfSurfaces;
                for (int i = 0; i < cells.Length; i++)
                {
                    states[cells[i]] = CellSurface.SelfOnly(self[i]);
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
            int state;
            if (!states.TryGetValue(cell, out state)) return;

            state = CellSurface.SelfOnly(state);
            for (int face = 0; face < Face.Count; face++)
            {
                int neighbourState;
                if (states.TryGetValue(cell + Face.Offsets[face], out neighbourState))
                {
                    state |= CellSurface.NeighbourContribution(neighbourState, face);
                }
            }
            states[cell] = state;
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
        }
    }
}
