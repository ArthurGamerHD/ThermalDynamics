using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The block layout of one grid, independent of any game type. The host mirrors its real
    /// grid into this; the harness builds one directly.
    /// </summary>
    public class GridModel : IBlockAdjacency
    {
        /// <summary>Edge length of one cell in metres. 2.5 for large grids, 0.5 for small.</summary>
        public readonly float GridSize;

        /// <summary>
        /// The block at each occupied cell, keyed on <see cref="GridMath.Key"/> rather than on the
        /// cell: a `long` hashes and compares inline where a `Vector3I` goes through a comparer
        /// object, and the difference is the whole cost of the neighbour query — 51 ns a probe
        /// against 9 on the same hull. The key is the one `BlockInstance.Key` already carries.
        /// See performance.md, Iteration 11.
        /// </summary>
        private readonly Dictionary<long, BlockInstance> blocksByCell = new Dictionary<long, BlockInstance>();

        private readonly List<BlockInstance> blocks = new List<BlockInstance>();

        /// <summary>
        /// Where each block sits in <see cref="blocks"/>, so a removal is a swap rather than a scan and
        /// a shift. Also the grid's only index by key: a second dictionary held the same keys and
        /// answered the same question one indirection sooner. See memory.md, 1e.
        /// </summary>
        private readonly Dictionary<long, int> blockSlots = new Dictionary<long, int>();

        /// <summary>
        /// Counts of blocks carrying coolant plumbing and blocks that are heat pumps.
        ///
        /// Maintained as blocks are placed, so establishing that a grid has neither is an integer
        /// test rather than a pass over every block. Most grids have neither.
        /// </summary>
        private int coolantBlocks;
        private int heatPumpBlocks;

        /// <summary>
        /// The doors, held separately so the room mapper can find every portal without walking every
        /// block. A large grid has tens of thousands of blocks and a few dozen doors.
        /// </summary>
        private readonly List<BlockInstance> stateDependent = new List<BlockInstance>();

        private Vector3I min = Vector3I.MaxValue;
        private Vector3I max = Vector3I.MinValue;
        private bool boundsDirty;

        public GridModel(float gridSize)
        {
            // ArgumentOutOfRangeException is not on the in-game script compiler's whitelist.
            if (gridSize <= 0f) throw new ArgumentException("gridSize must be positive", "gridSize");
            GridSize = gridSize;
        }

        /// <summary>Area of a single cell face, m^2.</summary>
        public float CellFaceArea
        {
            get { return GridSize * GridSize; }
        }

        public int BlockCount
        {
            get { return blocks.Count; }
        }

        public IList<BlockInstance> Blocks
        {
            get { return blocks; }
        }

        /// <summary>
        /// Blocks whose sealing depends on their own state, meaning doors. Every portal between two
        /// regions of the grid is one of these.
        /// </summary>
        public IList<BlockInstance> StateDependentBlocks
        {
            get { return stateDependent; }
        }

        /// <summary>Inclusive lower bound over every occupied cell.</summary>
        public Vector3I Min
        {
            get { RebuildBoundsIfNeeded(); return min; }
        }

        /// <summary>Inclusive upper bound over every occupied cell.</summary>
        public Vector3I Max
        {
            get { RebuildBoundsIfNeeded(); return max; }
        }

        /// <summary>
        /// The block occupying a cell, or null. A read-only probe: the solver walks its own link lists,
        /// so nothing on a hot path needs this.
        /// </summary>
        public BlockInstance At(Vector3I cell)
        {
            BlockInstance block;
            return blocksByCell.TryGetValue(GridMath.Key(cell), out block) ? block : null;
        }

        public BlockInstance Add(BlockInstance block)
        {
            if (block == null) throw new ArgumentNullException("block");

            Vector3I[] cells = block.Cells;
            for (int i = 0; i < cells.Length; i++)
            {
                if (blocksByCell.ContainsKey(GridMath.Key(cells[i])))
                {
                    throw new InvalidOperationException(
                        "Cell " + cells[i] + " is already occupied by " + blocksByCell[GridMath.Key(cells[i])].Name);
                }
            }

            for (int i = 0; i < cells.Length; i++)
            {
                blocksByCell[GridMath.Key(cells[i])] = block;
                Grow(cells[i]);
            }

            blockSlots[block.Key] = blocks.Count;
            blocks.Add(block);
            if (block.HasStateDependentSealing) stateDependent.Add(block);
            if (block.Model.Coolant != null) coolantBlocks++;
            if (block.Model.HeatPump != null) heatPumpBlocks++;
            return block;
        }

        /// <summary>Convenience overload that constructs the instance.</summary>
        public BlockInstance Add(BlockModel model, Vector3I min, BlockOrientation orientation)
        {
            return Add(new BlockInstance(model, min, orientation));
        }

        public BlockInstance Add(BlockModel model, Vector3I min)
        {
            return Add(new BlockInstance(model, min, BlockOrientation.Identity));
        }

        public bool Remove(BlockInstance block)
        {
            if (block == null || !blockSlots.ContainsKey(block.Key)) return false;

            Vector3I[] cells = block.Cells;
            for (int i = 0; i < cells.Length; i++)
            {
                BlockInstance occupant;
                long key = GridMath.Key(cells[i]);
                if (blocksByCell.TryGetValue(key, out occupant) && occupant == block)
                {
                    blocksByCell.Remove(key);
                }
            }

            RemoveSlot(block);
            if (block.HasStateDependentSealing) stateDependent.Remove(block);
            if (block.Model.Coolant != null) coolantBlocks--;
            if (block.Model.HeatPump != null) heatPumpBlocks--;
            boundsDirty = true;
            return true;
        }

        /// <summary>
        /// Removes a block from the flat list by moving the last entry into its slot.
        ///
        /// Nothing reads this list in order: the loop search, the heat pump search and the solver's
        /// registration all treat it as a set. The moved block's slot entry is rewritten before the
        /// list shrinks, so no stale slot remains.
        /// </summary>
        private void RemoveSlot(BlockInstance block)
        {
            int slot;
            if (!blockSlots.TryGetValue(block.Key, out slot))
            {
                // Should not occur, but a linear fallback is preferable to a corrupt list.
                blocks.Remove(block);
                return;
            }

            blockSlots.Remove(block.Key);

            int last = blocks.Count - 1;
            if (slot != last)
            {
                BlockInstance moved = blocks[last];
                blocks[slot] = moved;
                blockSlots[moved.Key] = slot;
            }

            blocks.RemoveAt(last);
        }

        /// <summary>Blocks on this grid carrying coolant plumbing. Zero on most grids.</summary>
        public int CoolantBlockCount
        {
            get { return coolantBlocks; }
        }

        /// <summary>Heat pumps on this grid.</summary>
        public int HeatPumpBlockCount
        {
            get { return heatPumpBlocks; }
        }

        public BlockInstance GetAtCell(Vector3I cell)
        {
            BlockInstance block;
            return blocksByCell.TryGetValue(GridMath.Key(cell), out block) ? block : null;
        }

        /// <summary>The block with this position key, or null when the grid does not carry it.</summary>
        public BlockInstance GetByKey(long key)
        {
            int slot;
            return blockSlots.TryGetValue(key, out slot) ? blocks[slot] : null;
        }

        public bool IsOccupied(Vector3I cell)
        {
            return blocksByCell.ContainsKey(GridMath.Key(cell));
        }

        /// <summary>
        /// Distinct blocks sharing at least one face with <paramref name="block"/>. A multi-cell
        /// neighbour appears once, no matter how many faces it touches.
        /// </summary>
        public List<BlockInstance> Neighbours(BlockInstance block)
        {
            List<BlockInstance> result = new List<BlockInstance>();
            GetNeighbours(block, result);
            return result;
        }

        /// <summary>
        /// Allocation-free neighbour query. The caller owns and clears <paramref name="results"/>.
        ///
        /// Walks only the block's boundary, never its interior: a neighbour must touch an outward
        /// face, so interior cells cannot contribute one. Cost is proportional to a block's surface
        /// rather than its volume.
        /// </summary>
        public void GetNeighbours(BlockInstance block, List<BlockInstance> results)
        {
            if (block == null || results == null) return;

            // A one-cell block — nearly every block on a hull — has six candidate cells, one per
            // face, and no two of them can hold the same neighbour: a neighbour is a box, and a box
            // touches a unit cube on at most one face. So the slab walk below collapses to six
            // probes in face order, with nothing to deduplicate. Same faces, same order, same
            // answers; GridModelAdjacencyTests holds the two paths together.
            // See performance.md, Pass 2, Iteration 2.
            if (block.CellCount == 1)
            {
                Vector3I cell = block.Min;
                for (int face = 0; face < Face.Count; face++)
                {
                    BlockInstance other = GetAtCell(cell + Face.Offsets[face]);
                    if (other == null || other == block) continue;
                    results.Add(other);
                }
                return;
            }

            GetNeighboursWalkingTheBoundary(block, results);
        }

        /// <summary>
        /// The general query: walks every cell on each face of the block's box and deduplicates,
        /// which is what a multi-cell block needs and a one-cell block does not. Public so a test
        /// can hold the one-cell path to it.
        /// </summary>
        public void GetNeighboursWalkingTheBoundary(BlockInstance block, List<BlockInstance> results)
        {
            if (block == null || results == null) return;

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

                        BlockInstance other = GetAtCell(cell + offset);
                        if (other == null || other == block) continue;
                        if (!results.Contains(other)) results.Add(other);
                    }
                }
            }
        }

        /// <summary>
        /// Number of cell faces shared between two blocks. This is the contact area, in cell
        /// faces, before mount-point filtering.
        /// </summary>
        public int SharedFaceCount(BlockInstance a, BlockInstance b)
        {
            if (a == null || b == null || a == b) return 0;

            int count = 0;
            Vector3I[] cells = a.Cells;
            for (int i = 0; i < cells.Length; i++)
            {
                for (int f = 0; f < Face.Count; f++)
                {
                    if (GetAtCell(cells[i] + Face.Offsets[f]) == b) count++;
                }
            }
            return count;
        }

        private void Grow(Vector3I cell)
        {
            if (boundsDirty) return;
            min = Vector3I.Min(min, cell);
            max = Vector3I.Max(max, cell);
        }

        private void RebuildBoundsIfNeeded()
        {
            if (!boundsDirty) return;

            min = Vector3I.MaxValue;
            max = Vector3I.MinValue;
            foreach (KeyValuePair<long, BlockInstance> entry in blocksByCell)
            {
                Vector3I cell = GridMath.FromKey(entry.Key);
                min = Vector3I.Min(min, cell);
                max = Vector3I.Max(max, cell);
            }
            boundsDirty = false;
        }
    }
}
