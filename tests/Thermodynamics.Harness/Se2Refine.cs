using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Re-expresses a dealt hull on a lattice several times finer, holding the blocks still: the
    /// same ship SE2 sees.
    ///
    /// <para>
    /// **SE2 multiplies cells, not blocks.** Its unified grid places 2.5 m blocks on a 25 cm
    /// lattice, so the hull that is one node per block either way owns a thousand times the cells
    /// for the same nodes. Everything in this model that is keyed per cell — the block table, the
    /// surface map, the room flood's bounding volume — inherits that factor, and everything keyed
    /// per node or per link does not. This transform is what lets a lab measure which is which:
    /// every block keeps its model's thermal properties, its mass, its power figures and its
    /// place, and only the lattice under it divides.
    /// </para>
    ///
    /// <para>
    /// A block's per-cell surface bits are expanded so that a fine face carries its source cell's
    /// bit where crossing it crosses a source-cell boundary, and is open where it is interior to
    /// one source cell. That preserves passability exactly — every fine crossing seals where and
    /// only where the coarse crossing it subdivides sealed, and a source cell's fine interior
    /// stays one connected volume rather than shearing into unreachable layers behind its sealed
    /// faces — so room count is invariant under refinement and every room's cell count multiplies
    /// by the factor cubed, which `Se2RefineTests` asserts. What it does not model is SE2's
    /// sub-block shape resolution (a slope as a staircase of cells); for the structures these
    /// labs price, the block-level topology is the load-bearing part.
    /// </para>
    ///
    /// <para>
    /// Coolant and heat-pump port geometry is deliberately not carried: ports are cell-indexed
    /// plumbing whose refinement is a design question, not a measurement one, and a hull refined
    /// without them still prices every structure these labs ask about.
    /// </para>
    /// </summary>
    public static class Se2Refine
    {
        /// <summary>
        /// The source hull on a lattice <paramref name="factor"/> times finer per axis. A factor
        /// of one hands the source back untouched.
        /// </summary>
        public static GridBuilder Refined(GridBuilder source, int factor)
        {
            if (factor <= 1) return source;

            GridBuilder fine = new GridBuilder(source.Grid.GridSize / factor);
            fine.Grid.EnsureCellCapacity(source.Grid.BlockCount * factor * factor * factor);

            Dictionary<BlockModel, BlockModel> models = new Dictionary<BlockModel, BlockModel>();

            IList<BlockInstance> placed = source.Placed;
            for (int i = 0; i < placed.Count; i++)
            {
                BlockInstance block = placed[i];
                fine.Place(RefinedModel(block.Model, factor, models),
                    block.Min * factor, block.Orientation);

                BlockInstance copy = fine.Last;
                copy.PowerProducedWatts = block.PowerProducedWatts;
                copy.PowerConsumedWatts = block.PowerConsumedWatts;
                copy.ThrustWatts = block.ThrustWatts;
                copy.IsSealedByDoorState = block.IsSealedByDoorState;
            }

            return fine;
        }

        /// <summary>
        /// One block type expanded onto the finer lattice, cached so a hull of a hundred thousand
        /// armour blocks builds one refined model rather than a hundred thousand.
        /// </summary>
        public static BlockModel RefinedModel(BlockModel source, int factor,
            Dictionary<BlockModel, BlockModel> cache)
        {
            BlockModel known;
            if (cache.TryGetValue(source, out known)) return known;

            BlockModel fine = new BlockModel();
            fine.Name = source.Name + "@" + factor;
            fine.Size = source.Extents * factor;
            fine.Mass = source.Mass;
            fine.Thermal = source.Thermal;
            fine.LocalSurfaces = ExpandSurfaces(source, factor, true);
            if (source.HasOpenState)
            {
                fine.LocalSurfacesWhenOpen = ExpandSurfaces(source, factor, false);
            }

            cache[source] = fine;
            return fine;
        }

        private static int[] ExpandSurfaces(BlockModel source, int factor, bool sealedByState)
        {
            Vector3I extents = source.Extents * factor;
            int[] fine = new int[extents.X * extents.Y * extents.Z];

            int at = 0;
            for (int z = 0; z < extents.Z; z++)
            {
                for (int y = 0; y < extents.Y; y++)
                {
                    for (int x = 0; x < extents.X; x++)
                    {
                        Vector3I coarse = new Vector3I(x / factor, y / factor, z / factor);
                        int state = CellSurface.SelfOnly(
                            source.LocalSurfaceState(coarse, sealedByState));

                        // A fully sealed cell is structure and stays structure at every fine
                        // cell: stripping its interior faces would leave a hollow the room scan
                        // reads as a phantom room inside solid armour.
                        if ((state & CellSurface.SelfAirtightMask) == CellSurface.SelfAirtightMask)
                        {
                            fine[at++] = state;
                            continue;
                        }

                        // Otherwise a face interior to one source cell is open: the bits describe
                        // the source cell's boundary, and leaving them on interior faces would
                        // wall a partially sealed cell's inside off from its own open face.
                        int kept = 0;
                        for (int face = 0; face < Face.Count; face++)
                        {
                            Vector3I offset = Face.Offsets[face];
                            bool boundary =
                                (offset.X > 0 && x % factor == factor - 1)
                                || (offset.X < 0 && x % factor == 0)
                                || (offset.Y > 0 && y % factor == factor - 1)
                                || (offset.Y < 0 && y % factor == 0)
                                || (offset.Z > 0 && z % factor == factor - 1)
                                || (offset.Z < 0 && z % factor == 0);
                            if (!boundary) continue;

                            int faceBits = (1 << (CellSurface.SelfAirtightShift + face))
                                | (1 << (CellSurface.SelfMountShift + face));
                            kept |= state & faceBits;
                        }

                        fine[at++] = kept;
                    }
                }
            }

            return fine;
        }
    }
}
