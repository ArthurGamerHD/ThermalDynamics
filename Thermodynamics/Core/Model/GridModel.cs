using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    public class GridModel : IBlockAdjacency
    {
        public readonly float GridSize;

        private Dictionary<long, BlockInstance> blocksByCell = new Dictionary<long, BlockInstance>();

/// <summary>List operation.</summary>
        private readonly List<BlockInstance> blocks = new List<BlockInstance>();


        private int coolantBlocks;
        private int heatPumpBlocks;

/// <summary>List operation.</summary>
        private readonly List<BlockInstance> stateDependent = new List<BlockInstance>();

        private Vector3I min = Vector3I.MaxValue;
        private Vector3I max = Vector3I.MinValue;
        private bool boundsDirty;

/// <summary>CellBitset operation.</summary>
        private readonly CellBitset occupied = new CellBitset();
        private int occupancyVersion = -1;
        private int version;

/// <summary>GridModel operation.</summary>
        public GridModel(float gridSize)
        {
            if (gridSize <= 0f) throw new ArgumentException("gridSize must be positive", "gridSize");
            GridSize = gridSize;
        }

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

        public IList<BlockInstance> StateDependentBlocks
        {
            get { return stateDependent; }
        }

        public Vector3I Min
        {
/// <summary>RebuildBoundsIfNeeded operation.</summary>
            get { RebuildBoundsIfNeeded(); return min; }
        }

        public Vector3I Max
        {
/// <summary>RebuildBoundsIfNeeded operation.</summary>
            get { RebuildBoundsIfNeeded(); return max; }
        }

/// <summary>At operation.</summary>
        public BlockInstance At(Vector3I cell)
        {
            BlockInstance block;
            return blocksByCell.TryGetValue(GridMath.Key(cell), out block) ? block : null;
        }

        public int Version
        {
            get { return version; }
        }

/// <summary>Occupancy operation.</summary>
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

/// <summary>EnsureCellCapacity operation.</summary>
        public void EnsureCellCapacity(int cells)
        {
            if (cells <= 0 || blocksByCell.Count > 0) return;

            blocksByCell = new Dictionary<long, BlockInstance>(cells);
            if (blocks.Capacity < cells) blocks.Capacity = cells;
        }

/// <summary>Adds a .</summary>
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

/// <summary>Adds a .</summary>
        public BlockInstance Add(BlockModel model, Vector3I min, BlockOrientation orientation)
        {
            return Add(new BlockInstance(model, min, orientation));
        }

/// <summary>Adds a .</summary>
        public BlockInstance Add(BlockModel model, Vector3I min)
        {
            return Add(new BlockInstance(model, min, BlockOrientation.Identity));
        }

/// <summary>Removes the .</summary>
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

/// <summary>Removes the slot.</summary>
        private void RemoveSlot(BlockInstance block)
        {
            if (!Holds(block))
            {
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

        public int CoolantBlockCount
        {
            get { return coolantBlocks; }
        }

        public int HeatPumpBlockCount
        {
            get { return heatPumpBlocks; }
        }

/// <summary>Returns the atcell.</summary>
        public BlockInstance GetAtCell(Vector3I cell)
        {
            return GetAtKey(GridMath.Key(cell));
        }

/// <summary>Returns the atkey.</summary>
        public BlockInstance GetAtKey(long key)
        {
            BlockInstance block;
            return blocksByCell.TryGetValue(key, out block) ? block : null;
        }

/// <summary>Returns the bykey.</summary>
        public BlockInstance GetByKey(long key)
        {
            BlockInstance block;
            if (!blocksByCell.TryGetValue(key, out block)) return null;
            return block.Key == key ? block : null;
        }

/// <summary>Holds operation.</summary>
        private bool Holds(BlockInstance block)
        {
            int slot = block.GridSlot;
            return slot >= 0 && slot < blocks.Count && ReferenceEquals(blocks[slot], block);
        }

/// <summary>IsOccupied operation.</summary>
        public bool IsOccupied(Vector3I cell)
        {
            return blocksByCell.ContainsKey(GridMath.Key(cell));
        }

/// <summary>Neighbours operation.</summary>
        public List<BlockInstance> Neighbours(BlockInstance block)
        {
/// <summary>List operation.</summary>
            List<BlockInstance> result = new List<BlockInstance>();
            GetNeighbours(block, result);
            return result;
        }

/// <summary>Returns the neighbours.</summary>
        public void GetNeighbours(BlockInstance block, List<BlockInstance> results)
        {
            GetNeighbours(block, results, null);
        }

/// <summary>Returns the neighbours.</summary>
        public void GetNeighbours(BlockInstance block, List<BlockInstance> results, List<int> faces)
        {
            GetNeighbours(block, results, faces, null);
        }

/// <summary>Returns the neighbours.</summary>
        public void GetNeighbours(BlockInstance block, List<BlockInstance> results, List<int> faces,
            CellBitset occupied)
        {
            if (block == null || results == null) return;

            if (block.CellCount == 1)
            {
                Vector3I cell = block.Min;
                long key = GridMath.Key(cell);
                long slot = occupied == null ? -1L : occupied.IndexOf(cell);

                for (int face = 0; face < Face.Count; face++)
                {
                    if (occupied != null && !occupied.ContainsIndex(slot + occupied.IndexStep(face)))
                    {
                        continue;
                    }

/// <summary>Returns the atkey.</summary>
                    BlockInstance other = GetAtKey(key + GridMath.KeyByFace[face]);
                    if (other == null || other == block) continue;
                    results.Add(other);
                    if (faces != null) faces.Add(face);
                }
                return;
            }

            GetNeighboursWalkingTheBoundary(block, results, faces);
        }


/// <summary>Returns the neighbourswalkingtheboundary.</summary>
        public void GetNeighboursWalkingTheBoundary(BlockInstance block, List<BlockInstance> results)
        {
            GetNeighboursWalkingTheBoundary(block, results, null);
        }

/// <summary>Returns the neighbourswalkingtheboundary.</summary>
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

/// <summary>Returns the atkey.</summary>
                        BlockInstance other = GetAtKey(GridMath.Key(cell) + GridMath.KeyByFace[face]);
                        if (other == null || other == block) continue;
                        if (results.Contains(other)) continue;

                        results.Add(other);
                        if (faces != null) faces.Add(face);
                    }
                }
            }
        }

/// <summary>SharedFaceCount operation.</summary>
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

/// <summary>Grow operation.</summary>
        private void Grow(Vector3I cell)
        {
            if (boundsDirty) return;
            min = Vector3I.Min(min, cell);
            max = Vector3I.Max(max, cell);
        }

/// <summary>RebuildBoundsIfNeeded operation.</summary>
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
