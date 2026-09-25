using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class LoadShapes
    {
        public const int BulkheadSpacing = 6;


        public static HashSet<Vector3I> Ship(int targetCells)
        {
            int width;
            int length;
            SolveShip(targetCells, out width, out length);
            HashSet<Vector3I> cells = GridShapes.Ship(length, width, BulkheadSpacing);

            if (cells.Count > 0 && Math.Abs(cells.Count - targetCells) > targetCells / 20)
            {
                int corrected = (int)Math.Round(targetCells * (double)targetCells / cells.Count);
                SolveShip(corrected, out width, out length);
                cells = GridShapes.Ship(length, width, BulkheadSpacing);
            }

            return cells;
        }


        public static void SolveShip(int targetCells, out int width, out int length)
        {
            targetCells = Math.Max(64, targetCells);

            int bestWidth = 7;
            int bestLength = 3;
            double bestError = double.MaxValue;

            for (int w = 7; w <= 201; w += 2)
            {
                double perLength = (4d * w - 4d) + ((double)w * w / BulkheadSpacing);
                int l = (int)Math.Round(targetCells / perLength);
                if (l < 3) break;

                double error = Math.Abs(l - (6d * w));
                if (error >= bestError) continue;

                bestError = error;
                bestWidth = w;
                bestLength = l;
            }

            width = bestWidth;
            length = bestLength;
        }


        public static HashSet<Vector3I> Cube(int targetCells)
        {
            int side = Math.Max(2, (int)Math.Round(Math.Pow(Math.Max(8, targetCells), 1d / 3d)));
            return GridShapes.SolidBox(Vector3I.Zero, new Vector3I(side, side, side));
        }


        public static HashSet<Vector3I> Truss(int targetCells)
        {
            const int width = 4;
            const int ringSpacing = 4;
            double perLength = 4d + (4d * (width + 1) / ringSpacing);
            int length = Math.Max(4, (int)Math.Round(Math.Max(64, targetCells) / perLength));
            return GridShapes.Truss(length, width, ringSpacing);
        }


        public static HashSet<Vector3I> Build(string shape, int targetCells)
        {
            switch (shape)
            {

                case "ship": return Ship(targetCells);

                case "cube": return Cube(targetCells);

                case "truss": return Truss(targetCells);
                default:
                    throw new ArgumentException("Unknown load shape: " + shape);
            }
        }

        public static readonly string[] Names = { "ship", "cube", "truss" };
    }
}
