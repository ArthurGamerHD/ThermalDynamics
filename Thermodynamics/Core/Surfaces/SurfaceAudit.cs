using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Why a block face counts as exposed, or does not.
    ///
    /// <see cref="SurfaceMap.GetExposedFaces"/> returns only a count, which cannot say why a face
    /// that looks open to the sky was not counted. Two separate rules can reject a cell face and
    /// both produce the same count. This records which rule applied, per cell face, and counts
    /// mount joints alongside rather than as a rejection.
    /// </summary>
    public struct FaceExposure
    {
        /// <summary>
        /// Cell faces on this side of the block. <see cref="Exposed"/>, <see cref="Sealed"/> and
        /// <see cref="Interior"/> sum to this; <see cref="Mounted"/> is a subset of the first.
        /// </summary>
        public int Cells;

        /// <summary>Cell faces open to the outside: what the model counts.</summary>
        public int Exposed;

        /// <summary>Rejected because something on the other side is airtight against this face.</summary>
        public int Sealed;

        /// <summary>
        /// Exposed cell faces that also carry a mount-to-mount joint — a panel with a grating or a
        /// catwalk bolted flat against it. Not a rejection: a subset of <see cref="Exposed"/>,
        /// reported so the population that conducts *and* radiates through the same face can be
        /// counted on a real ship.
        /// </summary>
        public int Mounted;

        /// <summary>Rejected because the space beyond is inside the ship rather than outdoors.</summary>
        public int Interior;

        public override string ToString()
        {
            return Exposed + "/" + Cells
                + " (sealed " + Sealed + ", interior " + Interior + ", of which bolted " + Mounted + ")";
        }
    }

    /// <summary>
    /// A block's six faces, explained.
    ///
    /// A separate walk from the simulation's rather than a flag threaded through it, so the hot path
    /// stays a counting loop with no diagnostic branches and this runs only when something asks.
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
                BoxGeometry.FaceSpan span = BoxGeometry.Span(min, maxExclusive, face);
                Vector3I offset = span.Offset;
                int axis = span.Axis;
                bool positive = span.Positive;
                int slab = span.Slab;
                int u = span.U;
                int v = span.V;

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

                        // The order mirrors GetExposedFaces exactly: a cell face rejected by two
                        // rules is reported against the first, so the rejection counts sum to the
                        // number of cell faces rather than double-counting.
                        if (CellSurface.NeighbourAirtight(state, face))
                        {
                            result.Sealed++;
                            continue;
                        }

                        if (rooms != null && !rooms.IsExternal(neighbour))
                        {
                            result.Interior++;
                            continue;
                        }

                        result.Exposed++;

                        // Counted beside the exposure rather than instead of it. A face bolted to
                        // something that does not seal both conducts through the joint and sees
                        // the sky, and this is how much of a ship is in that state.
                        if (CellSurface.NeighbourMount(state, face) && CellSurface.SelfMount(state, face))
                        {
                            result.Mounted++;
                        }
                    }
                }

                results[face] = result;
            }
        }

        /// <summary>
        /// Explains every block on a grid, in model order. Allocates; intended for a report rather
        /// than a step.
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
