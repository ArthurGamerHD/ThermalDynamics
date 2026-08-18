using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Grid shapes sized to a target block count, for the load benchmarks.
    ///
    /// <see cref="GridShapes"/> generators take structural parameters — a fuselage length, a
    /// truss spacing — because that is what makes one shape different from another. A load
    /// benchmark wants the opposite: the same shape at 10 000, 100 000 and 1 000 000 blocks, so
    /// the cost curve is a function of size alone and not of size tangled up with proportion.
    ///
    /// Every generator here therefore solves for its own parameters. The block count that comes
    /// back is close to the target rather than equal to it — a hull is built out of whole rings,
    /// not out of fractions of one — so every benchmark reports the count it actually built
    /// rather than the count it was asked for.
    /// </summary>
    public static class LoadShapes
    {
        /// <summary>Bulkhead spacing used by every scaled hull, in cells.</summary>
        public const int BulkheadSpacing = 6;

        /// <summary>
        /// A ship of roughly <paramref name="targetCells"/> blocks, kept at a plausible
        /// proportion rather than stretched into a needle.
        ///
        /// Proportion matters to the cost, not just to the look. The room mapper floods the
        /// bounding box, so a hull of a given block count is cheap to map when it is stubby and
        /// expensive when it is long and thin; picking the width that keeps the ship about six
        /// times longer than it is wide holds that ratio steady as the size climbs, which is what
        /// makes two rows of the ladder comparable.
        /// </summary>
        public static HashSet<Vector3I> Ship(int targetCells)
        {
            int width;
            int length;
            SolveShip(targetCells, out width, out length);
            HashSet<Vector3I> cells = GridShapes.Ship(length, width, BulkheadSpacing);

            // The closed form below counts the fuselage and nothing else, and a ship is also two
            // wings, four nacelles and a fin — about half as many blocks again. Rather than carry
            // a second approximation for those, build once, measure the miss and rebuild scaled
            // by it. One extra generation is nothing against the simulation that follows, and it
            // is what makes a rung of the ladder land on the size it claims.
            if (cells.Count > 0 && Math.Abs(cells.Count - targetCells) > targetCells / 20)
            {
                int corrected = (int)Math.Round(targetCells * (double)targetCells / cells.Count);
                SolveShip(corrected, out width, out length);
                cells = GridShapes.Ship(length, width, BulkheadSpacing);
            }

            return cells;
        }

        /// <summary>
        /// The fuselage width and length a <see cref="Ship"/> of this size is built from.
        /// Exposed so a benchmark can name the shape it measured.
        /// </summary>
        public static void SolveShip(int targetCells, out int width, out int length)
        {
            targetCells = Math.Max(64, targetCells);

            int bestWidth = 7;
            int bestLength = 3;
            double bestError = double.MaxValue;

            // A hull is a skin ring per cell of length, plus a solid bulkhead every few cells.
            // That is enough to solve for length given a width; the wings, nacelles and fin are
            // a small fraction on top and are absorbed by reporting the real count afterwards.
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

        /// <summary>
        /// A solid cube of roughly <paramref name="targetCells"/> blocks. The best case on every
        /// axis the simulation cares about, kept in the ladder as the floor a shape-sensitive
        /// regression would show up against.
        /// </summary>
        public static HashSet<Vector3I> Cube(int targetCells)
        {
            int side = Math.Max(2, (int)Math.Round(Math.Pow(Math.Max(8, targetCells), 1d / 3d)));
            return GridShapes.SolidBox(Vector3I.Zero, new Vector3I(side, side, side));
        }

        /// <summary>
        /// A station spine of roughly <paramref name="targetCells"/> blocks: the worst case for
        /// anything that hopes to skip settled regions, because a transient needs one hop per
        /// ring to cross it and the structure is nearly all skin.
        /// </summary>
        public static HashSet<Vector3I> Truss(int targetCells)
        {
            // Four rails plus a ring every few cells: 4 per cell of length, and 4*(width+1) more
            // on a ring cell.
            const int width = 4;
            const int ringSpacing = 4;
            double perLength = 4d + (4d * (width + 1) / ringSpacing);
            int length = Math.Max(4, (int)Math.Round(Math.Max(64, targetCells) / perLength));
            return GridShapes.Truss(length, width, ringSpacing);
        }

        /// <summary>The named scalable shapes, in the order the ladder reports them.</summary>
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
