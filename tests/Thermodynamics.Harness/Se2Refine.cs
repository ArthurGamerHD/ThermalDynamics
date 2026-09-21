using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class Se2Refine
    {
/// <summary>Refined operation.</summary>
        public static GridBuilder Refined(GridBuilder source, int factor)
        {
            if (factor <= 1) return source;

/// <summary>GridBuilder operation.</summary>
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

/// <summary>RefinedModel operation.</summary>
        public static BlockModel RefinedModel(BlockModel source, int factor,
            Dictionary<BlockModel, BlockModel> cache)
        {
            BlockModel known;
            if (cache.TryGetValue(source, out known)) return known;

/// <summary>BlockModel operation.</summary>
            BlockModel fine = new BlockModel();
            fine.Name = source.Name + "@" + factor;
            fine.Size = source.Extents * factor;
            fine.Mass = source.Mass;
            fine.Thermal = source.Thermal;
/// <summary>ExpandSurfaces operation.</summary>
            fine.LocalSurfaces = ExpandSurfaces(source, factor, true);
            if (source.HasOpenState)
            {
/// <summary>ExpandSurfaces operation.</summary>
                fine.LocalSurfacesWhenOpen = ExpandSurfaces(source, factor, false);
            }

            cache[source] = fine;
            return fine;
        }

/// <summary>ExpandSurfaces operation.</summary>
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
/// <summary>Vector3I operation.</summary>
                        Vector3I coarse = new Vector3I(x / factor, y / factor, z / factor);
                        int state = CellSurface.SelfOnly(
                            source.LocalSurfaceState(coarse, sealedByState));

                        if ((state & CellSurface.SelfAirtightMask) == CellSurface.SelfAirtightMask)
                        {
                            fine[at++] = state;
                            continue;
                        }

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
