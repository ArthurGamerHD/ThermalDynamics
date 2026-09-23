using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class GridShapes
    {


        public static HashSet<Vector3I> SolidBox(Vector3I origin, Vector3I extents)
        {

            HashSet<Vector3I> cells = new HashSet<Vector3I>(Vector3I.Comparer);
            for (int z = 0; z < extents.Z; z++)
                for (int y = 0; y < extents.Y; y++)
                    for (int x = 0; x < extents.X; x++)
                        cells.Add(origin + new Vector3I(x, y, z));
            return cells;
        }


        public static HashSet<Vector3I> HollowBox(Vector3I origin, Vector3I extents)
        {

            HashSet<Vector3I> cells = new HashSet<Vector3I>(Vector3I.Comparer);
            for (int z = 0; z < extents.Z; z++)
                for (int y = 0; y < extents.Y; y++)
                    for (int x = 0; x < extents.X; x++)
                    {
                        bool onSkin = x == 0 || y == 0 || z == 0
                            || x == extents.X - 1 || y == extents.Y - 1 || z == extents.Z - 1;
                        if (onSkin) cells.Add(origin + new Vector3I(x, y, z));
                    }
            return cells;
        }


        public static HashSet<Vector3I> Stick(int length)
        {
            return SolidBox(Vector3I.Zero, new Vector3I(1, 1, Math.Max(1, length)));
        }


        public static HashSet<Vector3I> Plate(int width, int length)
        {
            return SolidBox(Vector3I.Zero, new Vector3I(1, Math.Max(1, width), Math.Max(1, length)));
        }



        public static HashSet<Vector3I> Dumbbell(int massSide, int neckLength, int neckWidth = 1)
        {
            massSide = Math.Max(1, massSide);
            neckLength = Math.Max(1, neckLength);
            neckWidth = Math.Max(1, neckWidth);


            HashSet<Vector3I> cells = SolidBox(Vector3I.Zero, new Vector3I(massSide, massSide, massSide));

            int offset = (massSide - neckWidth) / 2;
            for (int z = 0; z < neckLength; z++)
                for (int y = 0; y < neckWidth; y++)
                    for (int x = 0; x < neckWidth; x++)
                        cells.Add(new Vector3I(offset + x, offset + y, massSide + z));

            cells.UnionWith(SolidBox(new Vector3I(0, 0, massSide + neckLength),

                new Vector3I(massSide, massSide, massSide)));
            return cells;
        }


        public static HashSet<Vector3I> LJunction(int armLength, int thickness = 3)
        {
            armLength = Math.Max(1, armLength);
            thickness = Math.Max(1, thickness);


            HashSet<Vector3I> cells = SolidBox(Vector3I.Zero, new Vector3I(thickness, thickness, armLength));
            cells.UnionWith(SolidBox(Vector3I.Zero, new Vector3I(armLength, thickness, thickness)));
            return cells;
        }


        public static HashSet<Vector3I> Truss(int length, int width = 4, int ringSpacing = 4)
        {
            length = Math.Max(1, length);
            width = Math.Max(1, width);
            ringSpacing = Math.Max(1, ringSpacing);


            HashSet<Vector3I> cells = new HashSet<Vector3I>(Vector3I.Comparer);

            for (int z = 0; z < length; z++)
                for (int a = 0; a <= 1; a++)
                    for (int b = 0; b <= 1; b++)
                        cells.Add(new Vector3I(a * width, b * width, z));

            for (int z = 0; z < length; z += ringSpacing)
                for (int i = 0; i <= width; i++)
                {
                    cells.Add(new Vector3I(i, 0, z));
                    cells.Add(new Vector3I(i, width, z));
                    cells.Add(new Vector3I(0, i, z));
                    cells.Add(new Vector3I(width, i, z));
                }

            return cells;
        }


        public static HashSet<Vector3I> Ship(
            int fuselageLength = 60,
            int fuselageWidth = 9,
            int bulkheadSpacing = 20)
        {
            fuselageLength = Math.Max(3, fuselageLength);
            fuselageWidth = Math.Max(3, fuselageWidth);
            bulkheadSpacing = Math.Max(2, bulkheadSpacing);


            HashSet<Vector3I> cells = new HashSet<Vector3I>(Vector3I.Comparer);
            int w = fuselageWidth;
            int mid = w / 2;

            for (int z = 0; z < fuselageLength; z++)
                for (int y = 0; y < w; y++)
                    for (int x = 0; x < w; x++)
                    {
                        bool skin = x == 0 || x == w - 1 || y == 0 || y == w - 1;
                        bool bulkhead = z % bulkheadSpacing == 0;
                        if (skin || bulkhead) cells.Add(new Vector3I(x, y, z));
                    }

            int wingSpan = w + 5;
            int wingRoot = fuselageLength / 3;
            int wingTip = Math.Min(fuselageLength - 5, wingRoot + fuselageLength / 2);
            for (int z = wingRoot; z < wingTip; z++)
                for (int y = mid - 1; y <= mid + 1; y++)
                {
                    for (int x = -wingSpan; x < 0; x++) cells.Add(new Vector3I(x, y, z));
                    for (int x = w; x < w + wingSpan; x++) cells.Add(new Vector3I(x, y, z));
                }

            int nacelleZ = Math.Max(0, fuselageLength - 14);
            int[] nacelleX = { -wingSpan + 2, -5, w + 3, w + wingSpan - 5 };
            for (int i = 0; i < nacelleX.Length; i++)
            {
                cells.UnionWith(SolidBox(new Vector3I(nacelleX[i], mid - 2, nacelleZ), new Vector3I(4, 4, 10)));
                for (int z = nacelleZ - 2; z < nacelleZ; z++)
                    cells.Add(new Vector3I(nacelleX[i] + 1, mid, z));
            }

            for (int z = fuselageLength - 12; z < fuselageLength - 2; z++)
                for (int y = w; y < w + 9; y++)
                    cells.Add(new Vector3I(mid, y, z));

            return cells;
        }


        public static HashSet<Vector3I> Station(
            Vector3I extents,
            Vector3I room,
            int wallThickness = 1)
        {

            extents = new Vector3I(Math.Max(3, extents.X), Math.Max(3, extents.Y), Math.Max(3, extents.Z));

            room = new Vector3I(Math.Max(1, room.X), Math.Max(1, room.Y), Math.Max(1, room.Z));
            wallThickness = Math.Max(1, wallThickness);


            HashSet<Vector3I> cells = SolidBox(Vector3I.Zero, extents);


            Vector3I pitch = room + new Vector3I(wallThickness, wallThickness, wallThickness);

            for (int z = wallThickness; z + room.Z + wallThickness <= extents.Z; z += pitch.Z)
                for (int y = wallThickness; y + room.Y + wallThickness <= extents.Y; y += pitch.Y)
                    for (int x = wallThickness; x + room.X + wallThickness <= extents.X; x += pitch.X)
                    {
                        for (int rz = 0; rz < room.Z; rz++)
                            for (int ry = 0; ry < room.Y; ry++)
                                for (int rx = 0; rx < room.X; rx++)
                                {
                                    cells.Remove(new Vector3I(x + rx, y + ry, z + rz));
                                }
                    }

            return cells;
        }


        public static HashSet<Vector3I> Accreted(int blockCount, int seed = 1)
        {
            blockCount = Math.Max(1, blockCount);


            HashSet<Vector3I> cells = new HashSet<Vector3I>(Vector3I.Comparer);

            List<Vector3I> frontier = new List<Vector3I>();

            cells.Add(Vector3I.Zero);
            frontier.Add(Vector3I.Zero);

            uint state = (uint)(seed * 2654435761u) | 1u;
            Func<int, int> next = bound =>
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return (int)(state % (uint)bound);
            };

            while (cells.Count < blockCount && frontier.Count > 0)
            {

                int pick = next(frontier.Count);
                Vector3I from = frontier[pick];
                Vector3I candidate = from + Face.Offsets[next(Face.Count)];

                if (!cells.Add(candidate))
                {
                    if (next(4) == 0) frontier.RemoveAt(pick);
                    continue;
                }

                frontier.Add(candidate);
            }

            return cells;
        }



        public static GridBuilder PlaceAll(this GridBuilder builder, BlockModel model, IEnumerable<Vector3I> cells)
        {
            if (builder == null) throw new ArgumentNullException("builder");
            if (model == null) throw new ArgumentNullException("model");
            if (cells == null) return builder;

            foreach (Vector3I cell in cells)
            {
                builder.Place(model, cell);
            }
            return builder;
        }


        public static IEnumerable<KeyValuePair<string, HashSet<Vector3I>>> Catalogue()
        {
            yield return Pair("solid-cube", SolidBox(Vector3I.Zero, new Vector3I(8, 8, 8)));
            yield return Pair("hollow-box", HollowBox(Vector3I.Zero, new Vector3I(11, 11, 11)));
            yield return Pair("stick", Stick(120));
            yield return Pair("plate", Plate(12, 24));
            yield return Pair("dumbbell", Dumbbell(5, 8));
            yield return Pair("l-junction", LJunction(24, 3));
            yield return Pair("truss", Truss(60));
            yield return Pair("ship", Ship(28, 7, 9));
            yield return Pair("station", Station(new Vector3I(13, 9, 13), new Vector3I(3, 3, 3)));
            yield return Pair("accreted", Accreted(400));
        }


        private static KeyValuePair<string, HashSet<Vector3I>> Pair(string name, HashSet<Vector3I> cells)
        {
            return new KeyValuePair<string, HashSet<Vector3I>>(name, cells);
        }
    }
}
