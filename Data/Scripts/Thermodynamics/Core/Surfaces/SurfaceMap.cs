using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Per-cell surface state for a grid: which faces seal and which carry mount surfaces, for
    /// the cell itself and for whatever sits next to it.
    ///
    /// Neighbour bits are always derived, never authored, so the two halves cannot drift out of
    /// sync with each other.
    /// </summary>
    public class SurfaceMap
    {
        /// <summary>
        /// Both layers of every occupied cell in one entry: the live state in the low 32 bits and the
        /// structural state — every door read as shut — in the high 32. One dictionary rather than
        /// two, because the two were keyed on the same cells and refreshed in the same call, so
        /// every probe was made twice and every entry held twice. A cell's two states are still
        /// written from the same block in the same call, never independently.
        /// See thermal-model.md, Two layers, and performance.md, Iteration 5.
        /// </summary>
        private readonly Dictionary<long, long> cells = new Dictionary<long, long>();

        /// <summary>The table is keyed on <see cref="GridMath.Key"/>, as the grid's is; see performance.md, Iteration 11.</summary>
        private static long KeyOf(Vector3I cell)
        {
            return GridMath.Key(cell);
        }

        private const int StructuralShift = 32;
        private const long LiveMask = 0xFFFFFFFFL;

        private static long Pack(int live, int structural)
        {
            return (live & LiveMask) | ((long)structural << StructuralShift);
        }

        private static int Live(long packed)
        {
            return (int)(packed & LiveMask);
        }

        private static int Structural(long packed)
        {
            return (int)(packed >> StructuralShift);
        }

        public int CellCount
        {
            get { return cells.Count; }
        }

        public IEnumerable<Vector3I> Cells
        {
            get
            {
                foreach (long key in cells.Keys) yield return GridMath.FromKey(key);
            }
        }

        /// <summary>Surface state of a cell, or 0 when the cell is empty.</summary>
        public int GetState(Vector3I cell)
        {
            long packed;
            return cells.TryGetValue(KeyOf(cell), out packed) ? Live(packed) : 0;
        }

        public bool HasCell(Vector3I cell)
        {
            return cells.ContainsKey(KeyOf(cell));
        }

        /// <summary>Writes a block's cells into the map and refreshes the affected neighbours.</summary>
        public void AddBlock(BlockInstance block)
        {
            if (block == null) return;

            Vector3I[] blockCells = block.Cells;
            int[] self = block.SelfSurfaces;

            int[] structural = block.StructuralSurfaces;

            for (int i = 0; i < blockCells.Length; i++)
            {
                cells[KeyOf(blockCells[i])] = Pack(
                    CellSurface.SelfOnly(self[i]),
                    CellSurface.SelfOnly(structural == null ? self[i] : structural[i]));
            }

            for (int i = 0; i < blockCells.Length; i++)
            {
                RefreshCell(blockCells[i]);
                RefreshNeighboursOf(blockCells[i]);
            }
        }

        /// <summary>Removes a block's cells and refreshes what was touching them.</summary>
        public void RemoveBlock(BlockInstance block)
        {
            if (block == null) return;

            Vector3I[] blockCells = block.Cells;
            for (int i = 0; i < blockCells.Length; i++)
            {
                cells.Remove(KeyOf(blockCells[i]));
            }

            for (int i = 0; i < blockCells.Length; i++)
            {
                RefreshNeighboursOf(blockCells[i]);
            }
        }

        /// <summary>
        /// Rebuilds the whole map from a grid. Cheaper and more predictable than a long chain of
        /// incremental edits when many blocks change at once.
        /// </summary>
        public void Rebuild(GridModel grid)
        {
            cells.Clear();
            if (grid == null) return;

            IList<BlockInstance> blocks = grid.Blocks;
            for (int b = 0; b < blocks.Count; b++)
            {
                BlockInstance block = blocks[b];
                Vector3I[] blockCells = block.Cells;
                int[] self = block.SelfSurfaces;
                int[] structural = block.StructuralSurfaces;
                for (int i = 0; i < blockCells.Length; i++)
                {
                    cells[KeyOf(blockCells[i])] = Pack(
                        CellSurface.SelfOnly(self[i]),
                        CellSurface.SelfOnly(structural == null ? self[i] : structural[i]));
                }
            }

            // Snapshotted as keys *and* self states, so the derived half of every cell is computed
            // from the array rather than by asking the dictionary for what was just put in it, and
            // written back exactly once. A neighbour's key is this cell's plus a constant
            // (`GridMath.KeyByFace`), so no cell is converted to a key or back inside the loop.
            int count = cells.Count;
            long[] keys = new long[count];
            long[] selves = new long[count];
            int at = 0;
            foreach (KeyValuePair<long, long> entry in cells)
            {
                keys[at] = entry.Key;
                selves[at] = entry.Value;
                at++;
            }

            long[] byFace = GridMath.KeyByFace;
            for (int i = 0; i < count; i++)
            {
                long key = keys[i];
                int live = CellSurface.SelfOnly(Live(selves[i]));
                int structural = CellSurface.SelfOnly(Structural(selves[i]));

                for (int face = 0; face < Face.Count; face++)
                {
                    long neighbour;
                    if (!cells.TryGetValue(key + byFace[face], out neighbour)) continue;

                    live |= CellSurface.NeighbourContribution(Live(neighbour), face);
                    structural |= CellSurface.NeighbourContribution(Structural(neighbour), face);
                }

                cells[key] = Pack(live, structural);
            }
        }

        /// <summary>Recomputes the derived neighbour half of one cell, in both layers, from one probe per neighbour.</summary>
        public void RefreshCell(Vector3I cell)
        {
            long packed;
            long key = KeyOf(cell);
            if (!cells.TryGetValue(key, out packed)) return;

            int live = CellSurface.SelfOnly(Live(packed));
            int structural = CellSurface.SelfOnly(Structural(packed));

            long[] byFace = GridMath.KeyByFace;
            for (int face = 0; face < Face.Count; face++)
            {
                long neighbour;
                if (cells.TryGetValue(key + byFace[face], out neighbour))
                {
                    live |= CellSurface.NeighbourContribution(Live(neighbour), face);
                    structural |= CellSurface.NeighbourContribution(Structural(neighbour), face);
                }
            }

            cells[key] = Pack(live, structural);
        }

        private void RefreshNeighboursOf(Vector3I cell)
        {
            for (int face = 0; face < Face.Count; face++)
            {
                RefreshCell(cell + Face.Offsets[face]);
            }
        }

        /// <summary>
        /// True when nothing can pass between two adjacent cells. Either side sealing is sufficient.
        /// </summary>
        public bool IsFaceSealed(Vector3I cell, int face)
        {
            int state = GetState(cell);
            if (CellSurface.SelfAirtight(state, face)) return true;

            int neighbourState = GetState(cell + Face.Offsets[face]);
            return CellSurface.SelfAirtight(neighbourState, Face.Opposite(face));
        }

        /// <summary>True when every face of the cell seals, meaning solid structure rather than a gap.</summary>
        public bool IsFullySealed(Vector3I cell)
        {
            return CellSurface.IsFullySealed(GetState(cell));
        }

        /// <summary>Structural state of a cell, with every door read as shut, or 0 when empty.</summary>
        public int GetStructuralState(Vector3I cell)
        {
            long packed;
            return cells.TryGetValue(KeyOf(cell), out packed) ? Structural(packed) : 0;
        }

        /// <summary>
        /// <see cref="IsFaceSealed"/> against the structure layer. This is the connectivity rule the
        /// room mapper walks, so the rooms it finds depend on how the grid is built rather than on
        /// which doors are open.
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
        /// Writes each occupied cell's six structural self-airtight bits into a dense box, one byte a
        /// cell, indexed <c>((z * sizeY) + y) * sizeX + x</c> from <paramref name="min"/>. Cells outside
        /// the box are skipped and empty cells stay zero, which is what <see cref="GetStructuralState"/>
        /// answers for them. The room mapper walks this instead of probing the dictionary twice per
        /// face of every cell in the bounding volume. See performance.md, Iteration 4.
        /// </summary>
        public void CopyStructuralSealing(Vector3I min, Vector3I maxExclusive, byte[] sealing)
        {
            if (sealing == null) return;

            int sizeX = maxExclusive.X - min.X;
            int sizeY = maxExclusive.Y - min.Y;
            int sizeZ = maxExclusive.Z - min.Z;
            if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0) return;

            foreach (KeyValuePair<long, long> entry in cells)
            {
                Vector3I at = GridMath.FromKey(entry.Key);
                int x = at.X - min.X;
                int y = at.Y - min.Y;
                int z = at.Z - min.Z;
                if (x < 0 || x >= sizeX || y < 0 || y >= sizeY || z < 0 || z >= sizeZ) continue;

                long index = (((long)z * sizeY) + y) * sizeX + x;
                if (index >= sealing.Length) continue;

                sealing[index] = (byte)(Structural(entry.Value) & CellSurface.SelfAirtightMask);
            }
        }

        /// <summary>
        /// Counts, per face direction, how many of a block's cell faces are open to the outside: on the
        /// boundary, external beyond, and not sealed against. A mount joint is deliberately not a
        /// rejection. See thermal-model.md, Exposure.
        /// </summary>
        public void GetExposedFaces(BlockInstance block, RoomMap rooms, int[] resultsByFace)
        {
            if (resultsByFace == null || resultsByFace.Length < Face.Count)
            {
                throw new ArgumentException("resultsByFace must have at least six entries");
            }

            Array.Clear(resultsByFace, 0, Face.Count);
            if (block == null) return;

            // A one-cell block has one cell face per face, so the slab walk below is six tests on
            // one cell state, read once rather than once a face. Same faces, same two questions in
            // the same order; ExposureFastPathTests holds the two paths together.
            // See performance.md, Pass 2, Iteration 3.
            if (block.CellCount == 1)
            {
                Vector3I cell = block.Min;
                int state = GetState(cell);
                for (int face = 0; face < Face.Count; face++)
                {
                    if (CellSurface.NeighbourAirtight(state, face)) continue;
                    if (rooms != null && !rooms.IsExternal(cell + Face.Offsets[face])) continue;
                    resultsByFace[face] = 1;
                }
                return;
            }

            GetExposedFacesWalkingTheBoundary(block, rooms, resultsByFace);
        }

        /// <summary>
        /// The general count: every cell on each face of the block's box, which a multi-cell block
        /// needs and a one-cell block does not. Public so a test can hold the one-cell path to it.
        /// The caller has cleared <paramref name="resultsByFace"/>.
        /// </summary>
        public void GetExposedFacesWalkingTheBoundary(BlockInstance block, RoomMap rooms, int[] resultsByFace)
        {
            if (block == null || resultsByFace == null) return;

            Vector3I min = block.Min;
            Vector3I maxExclusive = block.MaxExclusive;

            // Only the block's boundary is walked. A radiating face must be on the outside, so the
            // interior cells of a multi-cell block cannot contribute one; walking the volume would
            // cost the cube of block size rather than the square.
            for (int face = 0; face < Face.Count; face++)
            {
                BoxGeometry.FaceSpan span = BoxGeometry.Span(min, maxExclusive, face);
                Vector3I offset = span.Offset;
                int axis = span.Axis;
                bool positive = span.Positive;
                int slab = span.Slab;
                int u = span.U;
                int v = span.V;

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

                        // Something on the other side seals this face off.
                        if (CellSurface.NeighbourAirtight(state, face)) continue;

                        // The space beyond must reach the outside. A mount joint is deliberately not
                        // tested: the sealing test above already took every joint that buries a face,
                        // so a mount test could only reach a hull panel under a catwalk.
                        if (rooms != null && !rooms.IsExternal(neighbour)) continue;

                        count++;
                    }
                }

                resultsByFace[face] = count;
            }
        }

        /// <summary>
        /// Counts, per room, how many of a block's cell faces look into that room's air — a different
        /// test from <see cref="GetExposedFaces"/>, since a bulkhead's inner skin both seals the
        /// compartment and warms it. The caller owns and clears <paramref name="results"/>.
        /// </summary>
        public void GetRoomContacts(BlockInstance block, RoomMap rooms, List<RoomContact> results)
        {
            if (results == null || block == null || rooms == null) return;

            Vector3I min = block.Min;
            Vector3I maxExclusive = block.MaxExclusive;

            for (int face = 0; face < Face.Count; face++)
            {
                BoxGeometry.FaceSpan span = BoxGeometry.Span(min, maxExclusive, face);
                Vector3I offset = span.Offset;
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
            cells.Clear();
        }
    }
}
