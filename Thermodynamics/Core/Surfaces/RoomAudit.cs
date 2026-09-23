using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    public struct RoomAudit
    {
        public int SearchVolume;

        public int RoomCount;

        public int VentedRooms;

        public int Portals;

        public int OpenPortals;
        public int ExternalCells;
        public int SolidCells;
        public int RoomCells;

        public int BlockCells;

        public int LeakedCells;

        public int OpenBlockCells;

        public int UnsealedBlockFaces;

        public int BlocksSealingNothing;

        public List<int> RoomSizes;

        public List<string> Examples;

        public bool MapBuilt;

        public bool HasLeak
        {
            get { return LeakedCells > 0; }
        }
    }

    public static class RoomAuditor
    {
        public const int DefaultExampleLimit = 8;


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

                    if (CellSurface.IsFullySealed(state)) audit.LeakedCells++;

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


        public static int SearchVolumeOf(GridModel grid)
        {
            if (grid == null || grid.BlockCount == 0) return 0;

            Vector3I extents = (grid.Max - grid.Min) + new Vector3I(3, 3, 3);
            return Math.Max(0, extents.X) * Math.Max(0, extents.Y) * Math.Max(0, extents.Z);
        }


        private static string Describe(BlockInstance block, Vector3I cell, SurfaceMap surfaces, int selfState)
        {
            string verdict;
            if (!block.IsSealedByDoorState) verdict = " (open door)";
            else if ((selfState & CellSurface.SelfAirtightMask) == 0) verdict = " (not airtight: expected outdoors)";
            else verdict = " (partly sealing)";

            string text = block.Name + " " + cell + verdict +
                " " + CellSurface.Describe(selfState);

            if (surfaces == null) return text;

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
