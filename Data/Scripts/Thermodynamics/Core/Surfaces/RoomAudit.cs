using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// A cross-check of one completed room mapping pass against the grid that produced it.
    ///
    /// The mapper's counters report what it decided but not whether the decision was correct. This
    /// walks the placed blocks and asks the published map how it classified each of their cells. A
    /// cell holding a block should be structure; one the flood fill walked into is a leak, which is
    /// why an apparently sealed room can map as no room at all.
    /// </summary>
    public struct RoomAudit
    {
        /// <summary>Cells in the padded search box the pass covered.</summary>
        public int SearchVolume;

        public int RoomCount;

        /// <summary>
        /// Rooms currently standing open to the outside through a door. Still rooms, but holding no
        /// air, so surfaces facing them radiate.
        /// </summary>
        public int VentedRooms;

        /// <summary>Doors the map knows as a way between two regions.</summary>
        public int Portals;

        /// <summary>Portals currently open.</summary>
        public int OpenPortals;
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
        /// Block cells the map treats as open space. Not a fault in itself — a reactor, a lattice and
        /// the way through a door are all open in the game too — but this is the figure that
        /// explains a room that will not seal, and the examples name the blocks responsible.
        /// </summary>
        public int OpenBlockCells;

        /// <summary>
        /// Faces of block cells that do not seal, counting only faces the block itself owns. Zero for
        /// a hull of solid armour; every open door and lattice adds to it.
        /// </summary>
        public int UnsealedBlockFaces;

        /// <summary>Blocks whose cells seal on no face at all.</summary>
        public int BlocksSealingNothing;

        /// <summary>
        /// Cell count of each room found, largest first, bounded as the examples are. A room count
        /// establishes that a room exists; this gives its size.
        /// </summary>
        public List<int> RoomSizes;

        /// <summary>
        /// Human-readable description of the first few block cells left outdoors. Never null.
        /// </summary>
        public List<string> Examples;

        /// <summary>
        /// False when the map is the empty default rather than the result of a pass.
        ///
        /// A grid that closes before its first pass — a paste preview, a short-lived subgrid — still
        /// has a published map, since the mapper serves an all-external one until it has built a
        /// real one. Auditing against that reports every block as unaccounted for.
        /// </summary>
        public bool MapBuilt;

        /// <summary>True when the map contradicts the grid: the fill reached sealed structure.</summary>
        public bool HasLeak
        {
            get { return LeakedCells > 0; }
        }
    }

    public static class RoomAuditor
    {
        public const int DefaultExampleLimit = 8;

        /// <summary>Audits a published map against the grid it was built from.</summary>
        /// <param name="exampleLimit">
        /// How many leaking cells to describe. Descriptions build strings, so this bounds the output
        /// for a grid where many cells leak.
        /// </param>
        public static RoomAudit Audit(GridModel grid, SurfaceMap surfaces, RoomMap map, int exampleLimit = DefaultExampleLimit)
        {
            RoomAudit audit = new RoomAudit();
            audit.Examples = new List<string>();
            audit.RoomSizes = new List<int>();

            if (map != null)
            {
                audit.MapBuilt = !map.IsEmpty;
                audit.RoomCount = map.RoomCount;
                audit.VentedRooms = map.RoomCount - map.AirtightRoomCount;
                audit.Portals = map.Portals.Count;

                for (int i = 0; i < map.Portals.Count; i++)
                {
                    if (map.Portals[i].IsOpen) audit.OpenPortals++;
                }

                audit.ExternalCells = map.ExternalCellCount;
                audit.SolidCells = map.SolidCellCount;
                audit.RoomCells = map.RoomCellCount;

                CollectRoomSizes(map, audit.RoomSizes, exampleLimit);
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

                    if (!audit.MapBuilt) continue;
                    if (map.IsSolid(cells[i])) continue;

                    // A cell sealed on every face cannot be reached across any of them, so the fill
                    // could only have entered if the map does not describe this grid.
                    if (CellSurface.IsFullySealed(state)) audit.LeakedCells++;

                    // A cell inside a room is open space the mapper accounted for. The reportable
                    // cases are those left outdoors.
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

        /// <summary>
        /// The largest rooms by cell count, at most <paramref name="limit"/> of them. Bounded because
        /// a grid can have hundreds of compartments and this becomes one line of a report.
        /// </summary>
        private static void CollectRoomSizes(RoomMap map, List<int> sizes, int limit)
        {
            for (int i = 0; i < map.RoomCount; i++)
            {
                sizes.Add(map.CellsInRoom(i));
            }

            sizes.Sort();
            sizes.Reverse();

            if (limit > 0 && sizes.Count > limit) sizes.RemoveRange(limit, sizes.Count - limit);
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
            // Most blocks in this list belong there: a reactor, a lattice or a window frame is not
            // airtight in the game either, so the description states that rather than implying a
            // fault.
            // Door state first: a block held open is a more specific explanation than the surface
            // bits it is left with.
            string verdict;
            if (!block.IsSealedByDoorState) verdict = " (open door)";
            else if ((selfState & CellSurface.SelfAirtightMask) == 0) verdict = " (not airtight: expected outdoors)";
            else verdict = " (partly sealing)";

            string text = block.Name + " " + cell + verdict +
                " " + CellSurface.Describe(selfState);

            if (surfaces == null) return text;

            // Which faces the fill entered through. A cell can only be reached across a face neither
            // side seals, so naming those faces identifies the block that is not sealing.
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
