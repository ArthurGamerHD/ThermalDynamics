using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// A cross-check of one completed room mapping pass against the grid that produced it.
    ///
    /// The mapper's own counters say what it decided; they cannot say whether the decision was
    /// right. This does: it walks the placed blocks and asks the published map what it made of
    /// each of their cells. A cell holding a block should be structure. One the flood fill walked
    /// into instead is a leak, and a leak is why an obviously sealed room maps as no room at all.
    /// </summary>
    public struct RoomAudit
    {
        /// <summary>Cells in the padded search box the pass covered.</summary>
        public int SearchVolume;

        public int RoomCount;
        public int ExternalCells;
        public int SolidCells;
        public int RoomCells;

        /// <summary>Cells occupied by a block.</summary>
        public int BlockCells;

        /// <summary>
        /// Cells that seal on all six faces and yet are not structure in the map. Nothing can
        /// reach such a cell, so this is always zero unless the map and the grid disagree.
        /// </summary>
        public int LeakedCells;

        /// <summary>
        /// Block cells the map treats as open space. Not a fault on its own — a reactor, a
        /// lattice and the way through a door are all open in the game too — but it is the number
        /// that explains a room that will not seal, and the examples name which blocks they are.
        /// </summary>
        public int OpenBlockCells;

        /// <summary>
        /// Faces of block cells that do not seal, counting only faces the block itself owns.
        /// A hull of armour is zero; every open door and lattice adds to it.
        /// </summary>
        public int UnsealedBlockFaces;

        /// <summary>Blocks whose cells seal on no face at all.</summary>
        public int BlocksSealingNothing;

        /// <summary>
        /// Human-readable description of the first few block cells left outdoors. Never null.
        /// </summary>
        public List<string> Examples;

        /// <summary>The map contradicts the grid: sealed structure the fill got into.</summary>
        public bool HasLeak
        {
            get { return LeakedCells > 0; }
        }
    }

    public static class RoomAuditor
    {
        public const int DefaultExampleLimit = 8;

        /// <summary>
        /// Audits a published map against the grid it was built from.
        /// </summary>
        /// <param name="exampleLimit">
        /// How many leaking cells to describe. Descriptions build strings, so this is what keeps
        /// a grid that has gone badly wrong from writing a line per cell.
        /// </param>
        public static RoomAudit Audit(GridModel grid, SurfaceMap surfaces, RoomMap map, int exampleLimit = DefaultExampleLimit)
        {
            RoomAudit audit = new RoomAudit();
            audit.Examples = new List<string>();

            if (map != null)
            {
                audit.RoomCount = map.RoomCount;
                audit.ExternalCells = map.ExternalCellCount;
                audit.SolidCells = map.SolidCellCount;
                audit.RoomCells = map.RoomCellCount;
            }

            if (grid == null || map == null) return audit;

            audit.SearchVolume = SearchVolumeOf(grid);

            IList<BlockInstance> blocks = grid.Blocks;
            for (int b = 0; b < blocks.Count; b++)
            {
                BlockInstance block = blocks[b];
                Vector3I[] cells = block.Cells;
                int[] states = block.SelfSurfaces;

                bool sealsSomething = false;

                for (int i = 0; i < cells.Length; i++)
                {
                    audit.BlockCells++;

                    int state = states == null ? 0 : states[i];
                    for (int face = 0; face < Face.Count; face++)
                    {
                        if (CellSurface.SelfAirtight(state, face)) sealsSomething = true;
                        else audit.UnsealedBlockFaces++;
                    }

                    if (map.IsSolid(cells[i])) continue;

                    // A cell sealed on every face cannot be reached across any of them, so the
                    // fill can only have got in if the map is not the map of this grid.
                    if (CellSurface.IsFullySealed(state)) audit.LeakedCells++;

                    // A cell inside a room is open space, but it is open space the mapper
                    // accounted for. The interesting ones are those left outdoors.
                    if (map.RoomIndexOf(cells[i]) >= 0) continue;

                    audit.OpenBlockCells++;
                    if (audit.Examples.Count < exampleLimit)
                    {
                        audit.Examples.Add(Describe(block, cells[i], surfaces, state));
                    }
                }

                if (!sealsSomething) audit.BlocksSealingNothing++;
            }

            return audit;
        }

        /// <summary>The cell count of the padded box the mapper searches for this grid.</summary>
        public static int SearchVolumeOf(GridModel grid)
        {
            if (grid == null || grid.BlockCount == 0) return 0;

            Vector3I extents = (grid.Max - grid.Min) + new Vector3I(3, 3, 3);
            return Math.Max(0, extents.X) * Math.Max(0, extents.Y) * Math.Max(0, extents.Z);
        }

        private static string Describe(BlockInstance block, Vector3I cell, SurfaceMap surfaces, int selfState)
        {
            string text = block.Name + " " + cell +
                " sealed:" + (block.IsSealedByDoorState ? "yes" : "no (door)") +
                " " + CellSurface.Describe(selfState);

            if (surfaces == null) return text;

            // Which way the fill came in. A cell can only be reached across a face neither side
            // seals, so naming those faces points straight at the block that is not sealing.
            string open = "";
            for (int face = 0; face < Face.Count; face++)
            {
                if (surfaces.IsFaceSealed(cell, face)) continue;
                if (open.Length > 0) open += ",";
                open += Face.Name(face);
            }

            return text + " open:[" + (open.Length == 0 ? "-" : open) + "]";
        }
    }
}
