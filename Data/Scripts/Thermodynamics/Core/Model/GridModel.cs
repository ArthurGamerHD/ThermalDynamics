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

        private readonly Dictionary<Vector3I, BlockInstance> blocksByCell =
            new Dictionary<Vector3I, BlockInstance>(Vector3I.Comparer);

        private readonly Dictionary<long, BlockInstance> blocksByKey = new Dictionary<long, BlockInstance>();

        private readonly List<BlockInstance> blocks = new List<BlockInstance>();

        /// <summary>
        /// Where each block sits in <see cref="blocks"/>, so removal does not have to search for
        /// it. <c>List.Remove</c> is a linear scan and then a shift of everything after the hole:
        /// grinding a block off a million-block grid walked the list twice for one block, and a
        /// section being shot away did it once per block destroyed.
        /// </summary>
        private readonly Dictionary<long, int> blockSlots = new Dictionary<long, int>();

        /// <summary>
        /// Blocks that carry coolant plumbing, and blocks that are heat pumps.
        ///
        /// Both searches used to walk every block on the grid asking a question almost every
        /// block answers no to. Counting them as they are placed turns "does this ship have any
        /// plumbing" from a pass over a million blocks into an integer test, and the overwhelming
        /// majority of grids have none of either.
        /// </summary>
        private int coolantBlocks;
        private int heatPumpBlocks;

        /// <summary>
        /// The doors, kept apart from the rest so that the room mapper can find every portal in
        /// the ship without walking every block. A capital ship has tens of thousands of blocks
        /// and perhaps thirty doors, and it is the doors that move.
        /// </summary>
        private readonly List<BlockInstance> stateDependent = new List<BlockInstance>();

        private Vector3I min = Vector3I.MaxValue;
        private Vector3I max = Vector3I.MinValue;
        private bool boundsDirty;

        public GridModel(float gridSize)
        {
            // ArgumentOutOfRangeException is not on the in-game script compiler's whitelist
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
        /// Blocks whose sealing depends on their own state — doors. Every portal between two
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
        /// Adds a block. Throws when any of its cells is already occupied, which would silently
        /// corrupt the surface map.
        /// </summary>
        public BlockInstance Add(BlockInstance block)
        {
            if (block == null) throw new ArgumentNullException("block");

            Vector3I[] cells = block.Cells;
            for (int i = 0; i < cells.Length; i++)
            {
                if (blocksByCell.ContainsKey(cells[i]))
                {
                    throw new InvalidOperationException(
                        "Cell " + cells[i] + " is already occupied by " + blocksByCell[cells[i]].Name);
                }
            }

            for (int i = 0; i < cells.Length; i++)
            {
                blocksByCell[cells[i]] = block;
                Grow(cells[i]);
            }

            blocksByKey[block.Key] = block;
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
            if (block == null || !blocksByKey.ContainsKey(block.Key)) return false;

            Vector3I[] cells = block.Cells;
            for (int i = 0; i < cells.Length; i++)
            {
                BlockInstance occupant;
                if (blocksByCell.TryGetValue(cells[i], out occupant) && occupant == block)
                {
                    blocksByCell.Remove(cells[i]);
                }
            }

            blocksByKey.Remove(block.Key);
            RemoveSlot(block);
            if (block.HasStateDependentSealing) stateDependent.Remove(block);
            if (block.Model.Coolant != null) coolantBlocks--;
            if (block.Model.HeatPump != null) heatPumpBlocks--;
            boundsDirty = true;
            return true;
        }

        /// <summary>
        /// Takes a block out of the flat list by moving the last one into its place.
        ///
        /// Nothing reads this list in order — the loop search, the heat pump search and the
        /// solver's own registration all treat it as a set — so the cheap removal is the correct
        /// one. What it must not do is leave a stale slot behind, which is why the moved block's
        /// entry is rewritten before the list shrinks.
        /// </summary>
        private void RemoveSlot(BlockInstance block)
        {
            int slot;
            if (!blockSlots.TryGetValue(block.Key, out slot))
            {
                // Should not happen, but a linear fallback is better than a corrupt list.
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

        /// <summary>Blocks on this grid carrying coolant plumbing. Zero on almost every grid.</summary>
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
            return blocksByCell.TryGetValue(cell, out block) ? block : null;
        }

        public BlockInstance GetByKey(long key)
        {
            BlockInstance block;
            return blocksByKey.TryGetValue(key, out block) ? block : null;
        }

        public bool IsOccupied(Vector3I cell)
        {
            return blocksByCell.ContainsKey(cell);
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
        /// Allocation-free neighbour query. The caller owns and clears
        /// <paramref name="results"/>.
        ///
        /// Only the block's boundary is walked, never its interior: a neighbour has to touch an
        /// outward face, so the cells inside the block cannot contribute one. That makes the
        /// cost proportional to a block's surface rather than its volume, which matters once
        /// blocks are large enough for the difference to be an order of magnitude.
        /// </summary>
        public void GetNeighbours(BlockInstance block, List<BlockInstance> results)
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
            foreach (KeyValuePair<Vector3I, BlockInstance> entry in blocksByCell)
            {
                min = Vector3I.Min(min, entry.Key);
                max = Vector3I.Max(max, entry.Key);
            }
            boundsDirty = false;
        }
    }
}
