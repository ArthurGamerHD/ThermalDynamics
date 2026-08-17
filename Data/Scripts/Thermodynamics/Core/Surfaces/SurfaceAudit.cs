using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Why a block face counts as exposed, or does not.
    ///
    /// <see cref="SurfaceMap.GetExposedFaces"/> returns a number, and a number cannot answer the
    /// only question anyone asks of it — "that face is open to the sky, why does the model say it
    /// is not?". Three separate rules can reject a cell face, and from the outside all three look
    /// identical. This records which one did it, per cell face, so the answer is read rather than
    /// guessed at.
    /// </summary>
    public struct FaceExposure
    {
        /// <summary>Cell faces on this side of the block. The rest of the counts sum to this.</summary>
        public int Cells;

        /// <summary>Cell faces open to the outside: what the model counts.</summary>
        public int Exposed;

        /// <summary>Rejected because something on the other side is airtight against this face.</summary>
        public int Sealed;

        /// <summary>Rejected because two mount surfaces are pressed together — bolted on.</summary>
        public int Mounted;

        /// <summary>Rejected because the space beyond is inside the ship rather than outdoors.</summary>
        public int Interior;

        public override string ToString()
        {
            return Exposed + "/" + Cells
                + " (sealed " + Sealed + ", mounted " + Mounted + ", interior " + Interior + ")";
        }
    }

    /// <summary>
    /// A block's six faces, explained.
    ///
    /// Deliberately a separate walk from the one the simulation uses rather than a flag threaded
    /// through it: the hot path stays a counting loop with no diagnostic branches, and this is only
    /// ever run by someone who has asked a question.
    /// </summary>
    public static class SurfaceAudit
    {
        /// <summary>
        /// Explains every face of one block. <paramref name="results"/> must hold six entries and
        /// is overwritten.
        /// </summary>
        public static void Explain(
            SurfaceMap surfaces, BlockInstance block, RoomMap rooms, FaceExposure[] results)
        {
            if (results == null || results.Length < Face.Count) return;

            for (int i = 0; i < Face.Count; i++)
            {
                results[i] = default(FaceExposure);
            }

            if (surfaces == null || block == null) return;

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

                FaceExposure result = default(FaceExposure);

                for (int a = BoxGeometry.Component(min, u); a < BoxGeometry.Component(maxExclusive, u); a++)
                {
                    for (int b = BoxGeometry.Component(min, v); b < BoxGeometry.Component(maxExclusive, v); b++)
                    {
                        Vector3I cell = BoxGeometry.WithComponent(Vector3I.Zero, axis, slab);
                        cell = BoxGeometry.WithComponent(cell, u, a);
                        cell = BoxGeometry.WithComponent(cell, v, b);

                        result.Cells++;

                        int state = surfaces.GetState(cell);
                        Vector3I neighbour = cell + offset;

                        // The order matters and mirrors GetExposedFaces exactly: a cell face
                        // rejected by two rules is reported against the first, so the counts sum
                        // to the number of cell faces rather than double-counting.
                        if (CellSurface.NeighbourAirtight(state, face))
                        {
                            result.Sealed++;
                            continue;
                        }

                        if (CellSurface.NeighbourMount(state, face) && CellSurface.SelfMount(state, face))
                        {
                            result.Mounted++;
                            continue;
                        }

                        if (rooms != null && !rooms.IsExternal(neighbour))
                        {
                            result.Interior++;
                            continue;
                        }

                        result.Exposed++;
                    }
                }

                results[face] = result;
            }
        }

        /// <summary>
        /// Explains every block on a grid, in model order. Allocates, and is meant for a report
        /// rather than for a step.
        /// </summary>
        public static List<BlockExposure> ExplainAll(
            SurfaceMap surfaces, GridModel grid, RoomMap rooms, int limit = int.MaxValue)
        {
            List<BlockExposure> results = new List<BlockExposure>();
            if (surfaces == null || grid == null) return results;

            IList<BlockInstance> blocks = grid.Blocks;
            FaceExposure[] faces = new FaceExposure[Face.Count];

            for (int i = 0; i < blocks.Count && results.Count < limit; i++)
            {
                Explain(surfaces, blocks[i], rooms, faces);

                BlockExposure entry = new BlockExposure();
                entry.Block = blocks[i];
                entry.Faces = (FaceExposure[])faces.Clone();
                results.Add(entry);
            }

            return results;
        }
    }

    /// <summary>One block and the explanation of its six faces.</summary>
    public struct BlockExposure
    {
        public BlockInstance Block;
        public FaceExposure[] Faces;

        /// <summary>Cell faces the model counts as open to the sky, over all six sides.</summary>
        public int TotalExposed
        {
            get
            {
                if (Faces == null) return 0;

                int total = 0;
                for (int i = 0; i < Faces.Length; i++) total += Faces[i].Exposed;
                return total;
            }
        }
    }
}
