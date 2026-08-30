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

        /// <summary>
        /// One bit per cell of the padded bounding box, set where a block stands. Built on demand
        /// and thrown away whenever the grid changes, which <see cref="version"/> tracks.
        ///
        /// <para>
        /// **What it is for is the probes that find nothing.** A caller walking the six faces of
        /// every cell of every room — the air rebuild, the room-side exposure refresh — asks
        /// `blocksByCell` about 1.6 million faces on a 126,731-block hull and **8.5 %** of them hold
        /// a block. The other 91.5 % are a hash and a bucket chase to be told "nothing", against a
        /// bit test here. See performance.md, Pass 4, Iteration 6.
        /// </para>
        ///
        /// <para>
        /// Building it costs one bit-set per occupied cell, once per change to the grid, which is
        /// once per room-mapping pass in practice — against nine million probes saved on the hull
        /// above. It is not maintained incrementally: a placement invalidates it and the next
        /// caller pays for the rebuild, so a load that places half a million blocks builds it once
        /// at the end rather than half a million times on the way.
        /// </para>
        /// </summary>
        private readonly CellBitset occupied = new CellBitset();
        private int occupancyVersion = -1;
        private int version;

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

        /// <summary>
        /// Where the blocks are, as one bit a cell over the padded bounding box, current as of this
        /// call. See <see cref="occupied"/> for why it exists and what it costs.
        ///
        /// <para>
        /// The box is padded by one so that every neighbour of every cell a block occupies is
        /// inside it, which is what lets a caller step <see cref="CellBitset.IndexStep"/> from cell
        /// to neighbour without a bounds test.
        /// </para>
        /// </summary>
        public CellBitset Occupancy()
        {
            if (occupancyVersion == version) return occupied;

            occupied.Reset(Min - Vector3I.One, Max + new Vector3I(2, 2, 2));

            for (int i = 0; i < blocks.Count; i++)
            {
                Vector3I[] cells = blocks[i].Cells;
                for (int c = 0; c < cells.Length; c++) occupied.Add(cells[c]);
            }

            occupancyVersion = version;
            return occupied;
        }

        public BlockInstance Add(BlockInstance block)
        {
            if (block == null) throw new ArgumentNullException("block");

            version++;

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

            block.GridSlot = blocks.Count;
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
            if (block == null || !Holds(block)) return false;

            version++;

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
            if (!Holds(block))
            {
                // Should not occur, but a linear fallback is preferable to a corrupt list.
                int found = blocks.IndexOf(block);
                if (found < 0) return;
                block.GridSlot = found;
            }

            int slot = block.GridSlot;
            block.GridSlot = -1;

            int last = blocks.Count - 1;
            if (slot != last)
            {
                BlockInstance moved = blocks[last];
                blocks[slot] = moved;
                moved.GridSlot = slot;
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
            return GetAtKey(GridMath.Key(cell));
        }

        /// <summary>
        /// The block occupying the cell with this key, or null. The same question as
        /// <see cref="GetAtCell"/> for a caller that already holds the key — and since a key is a
        /// *sum* of the components, a caller walking the six neighbours of a cell holds all six
        /// keys the moment it holds one: `key + GridMath.KeyByFace[face]`. That is one addition a
        /// face where converting the neighbouring cell is two multiplies and three adds.
        ///
        /// <para>
        /// Not to be confused with <see cref="GetByKey"/>, which answers only for a block's
        /// *lowest* cell and so returns null for the other cells of a multi-cell block.
        /// </para>
        /// </summary>
        public BlockInstance GetAtKey(long key)
        {
            BlockInstance block;
            return blocksByCell.TryGetValue(key, out block) ? block : null;
        }

        /// <summary>The block with this position key, or null when the grid does not carry it.</summary>
        public BlockInstance GetByKey(long key)
        {
            // A block's key is the key of its lowest cell, and that cell is one this block
            // occupies — a block fills its own bounding box — so the cell index answers this.
            BlockInstance block;
            if (!blocksByCell.TryGetValue(key, out block)) return null;
            return block.Key == key ? block : null;
        }

        /// <summary>Whether this grid's list really holds the block at the slot the block claims.</summary>
        private bool Holds(BlockInstance block)
        {
            int slot = block.GridSlot;
            return slot >= 0 && slot < blocks.Count && ReferenceEquals(blocks[slot], block);
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
            GetNeighbours(block, results, null);
        }

        /// <summary>
        /// The same query, also reporting the grid-space face of <paramref name="block"/> each
        /// neighbour was found across — which the walk knows, and which the link builder otherwise
        /// asks <c>ConductionBuilder.ContactFace</c> to work out again from two boxes. A box touches
        /// another on at most one face, so the two answers are the same one;
        /// `GridModelAdjacencyTests` holds them together. Pass null to ignore the faces.
        /// See performance.md, Pass 3, Iteration 9.
        /// </summary>
        public void GetNeighbours(BlockInstance block, List<BlockInstance> results, List<int> faces)
        {
            GetNeighbours(block, results, faces, null);
        }

        /// <summary>
        /// The same, with a bit consulted before the block table.
        ///
        /// <para>
        /// **Only for a caller that walks the whole grid at once.** A block's six candidate cells
        /// hold a block about five times in eight on a hull, so three probes in eight are a hash
        /// and a bucket chase to be told nothing — worth a bit test in front. The set is built on
        /// demand and dropped whenever the grid changes, so a caller that walked it *per placement*
        /// would rebuild it per placement and turn a load into quadratic work. `RebuildLinks` takes
        /// it once and hands it down; the incremental path does not ask for it.
        /// See performance.md, Pass 5, Iteration 6.
        /// </para>
        /// </summary>
        public void GetNeighbours(BlockInstance block, List<BlockInstance> results, List<int> faces,
            CellBitset occupied)
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
                // And a neighbour's key is this cell's key plus a per-face constant, so the six
                // candidates cost one conversion rather than six.
                // See performance.md, Pass 3, Iteration 6 and Pass 4, Iteration 10.
                Vector3I cell = block.Min;
                long key = GridMath.Key(cell);
                long slot = occupied == null ? -1L : occupied.IndexOf(cell);

                for (int face = 0; face < Face.Count; face++)
                {
                    if (occupied != null && !occupied.ContainsIndex(slot + occupied.IndexStep(face)))
                    {
                        continue;
                    }

                    BlockInstance other = GetAtKey(key + GridMath.KeyByFace[face]);
                    if (other == null || other == block) continue;
                    results.Add(other);
                    if (faces != null) faces.Add(face);
                }
                return;
            }

            GetNeighboursWalkingTheBoundary(block, results, faces);
        }

        /// <summary>
        /// Whether the index step from a cell to its neighbour is meaningful here: it is not on the
        /// box's own boundary, where stepping −X from the first column lands in the previous row.
        /// The occupancy box is padded by one, and a block only ever occupies cells inside that
        /// padding, so every block's cell is safe — but a caller that has not built the set at all
        /// gets nothing.
        /// </summary>

        /// <summary>
        /// The general query: walks every cell on each face of the block's box and deduplicates,
        /// which is what a multi-cell block needs and a one-cell block does not. Public so a test
        /// can hold the one-cell path to it.
        /// </summary>
        public void GetNeighboursWalkingTheBoundary(BlockInstance block, List<BlockInstance> results)
        {
            GetNeighboursWalkingTheBoundary(block, results, null);
        }

        /// <summary>The boundary walk, also reporting the face each neighbour was found across.</summary>
        public void GetNeighboursWalkingTheBoundary(BlockInstance block, List<BlockInstance> results, List<int> faces)
        {
            if (block == null || results == null) return;

            Vector3I min = block.Min;
            Vector3I maxExclusive = block.MaxExclusive;

            for (int face = 0; face < Face.Count; face++)
            {
                BoxGeometry.FaceSpan span = BoxGeometry.Span(min, maxExclusive, face);
                int axis = span.Axis;
                bool positive = span.Positive;
                int slab = span.Slab;
                int u = span.U;
                int v = span.V;

                for (int a = BoxGeometry.Component(min, u); a < BoxGeometry.Component(maxExclusive, u); a++)
                {
                    for (int b = BoxGeometry.Component(min, v); b < BoxGeometry.Component(maxExclusive, v); b++)
                    {
                        Vector3I cell = BoxGeometry.WithComponent(Vector3I.Zero, axis, slab);
                        cell = BoxGeometry.WithComponent(cell, u, a);
                        cell = BoxGeometry.WithComponent(cell, v, b);

                        BlockInstance other = GetAtKey(GridMath.Key(cell) + GridMath.KeyByFace[face]);
                        if (other == null || other == block) continue;
                        if (results.Contains(other)) continue;

                        results.Add(other);
                        if (faces != null) faces.Add(face);
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
