using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    public struct FaceExposure
    {
        public int Cells;

        public int Exposed;

        public int Sealed;

        public int Mounted;

        public int Interior;


        public override string ToString()
        {
            return Exposed + "/" + Cells
                + " (sealed " + Sealed + ", interior " + Interior + ", of which bolted " + Mounted + ")";
        }
    }

    public static class SurfaceAudit
    {

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

                        if (CellSurface.NeighbourMount(state, face) && CellSurface.SelfMount(state, face))
                        {
                            result.Mounted++;
                        }
                    }
                }

                results[face] = result;
            }
        }


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

    public struct BlockExposure
    {
        public BlockInstance Block;
        public FaceExposure[] Faces;

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
