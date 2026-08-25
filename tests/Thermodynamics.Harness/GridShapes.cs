using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Grid shapes for scenarios and tests.
    ///
    /// The box shapes that <see cref="GridBuilder"/> offers — a solid <c>Fill</c> and a hollow
    /// <c>Shell</c> — are convenient but unrepresentative. A solid cube is the best case in
    /// almost every dimension the simulation cares about: it has the smallest possible exposed
    /// fraction, a bounding volume it fills completely, and the shortest conduction path for its
    /// block count. Real grids are ships: irregular assemblies of boxes, mostly skin, with long
    /// thin structures and narrow joints between heavy masses.
    ///
    /// Every generator here is deterministic, so a scenario built from one is reproducible.
    /// See <see cref="GridMetrics"/> for the numbers that separate these shapes.
    /// </summary>
    public static class GridShapes
    {
        // ---- primitives --------------------------------------------------------------------

        /// <summary>Solid box. The classic benchmark shape, and the least representative one.</summary>
        public static HashSet<Vector3I> SolidBox(Vector3I origin, Vector3I extents)
        {
            HashSet<Vector3I> cells = new HashSet<Vector3I>(Vector3I.Comparer);
            for (int z = 0; z < extents.Z; z++)
                for (int y = 0; y < extents.Y; y++)
                    for (int x = 0; x < extents.X; x++)
                        cells.Add(origin + new Vector3I(x, y, z));
            return cells;
        }

        /// <summary>Hollow box: skin only, nothing inside. Every block is exposed.</summary>
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

        /// <summary>
        /// A 1 x 1 x n line. Degenerate on purpose: conduction graph diameter equals the block
        /// count, so a transient at one end takes n hops to reach the other.
        /// </summary>
        public static HashSet<Vector3I> Stick(int length)
        {
            return SolidBox(Vector3I.Zero, new Vector3I(1, 1, Math.Max(1, length)));
        }

        /// <summary>A one-block-thick plate. Wing, solar panel, hull panel.</summary>
        public static HashSet<Vector3I> Plate(int width, int length)
        {
            return SolidBox(Vector3I.Zero, new Vector3I(1, Math.Max(1, width), Math.Max(1, length)));
        }

        // ---- assemblies --------------------------------------------------------------------

        /// <summary>
        /// Two solid masses joined by a thin neck. The neck is an articulation point: all heat
        /// between the ends must funnel through it, which is where a conduction model is under
        /// the most stress and where an asymmetric-joint error shows up fastest.
        /// </summary>
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

        /// <summary>
        /// Two arms meeting at a right angle. The simplest shape whose bounding box is mostly
        /// empty, which is what makes a bounding-volume flood fill expensive.
        /// </summary>
        public static HashSet<Vector3I> LJunction(int armLength, int thickness = 3)
        {
            armLength = Math.Max(1, armLength);
            thickness = Math.Max(1, thickness);

            HashSet<Vector3I> cells = SolidBox(Vector3I.Zero, new Vector3I(thickness, thickness, armLength));
            cells.UnionWith(SolidBox(Vector3I.Zero, new Vector3I(armLength, thickness, thickness)));
            return cells;
        }

        /// <summary>
        /// An open truss: four longitudinal rails with periodic ring frames. Very low link
        /// density and a very long conduction path for the block count — a station spine.
        /// </summary>
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

        /// <summary>
        /// A ship: hollow fuselage with internal bulkheads, two wings, four engine nacelles on
        /// thin pylons, and a dorsal fin.
        ///
        /// This is the representative shape. It is mostly skin, its bounding box is around
        /// nine tenths empty, and it has several thin connections between heavy masses — all
        /// three of the properties a solid cube lacks.
        /// </summary>
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

            // fuselage: skin plus periodic bulkheads
            for (int z = 0; z < fuselageLength; z++)
                for (int y = 0; y < w; y++)
                    for (int x = 0; x < w; x++)
                    {
                        bool skin = x == 0 || x == w - 1 || y == 0 || y == w - 1;
                        bool bulkhead = z % bulkheadSpacing == 0;
                        if (skin || bulkhead) cells.Add(new Vector3I(x, y, z));
                    }

            // wings
            int wingSpan = w + 5;
            int wingRoot = fuselageLength / 3;
            int wingTip = Math.Min(fuselageLength - 5, wingRoot + fuselageLength / 2);
            for (int z = wingRoot; z < wingTip; z++)
                for (int y = mid - 1; y <= mid + 1; y++)
                {
                    for (int x = -wingSpan; x < 0; x++) cells.Add(new Vector3I(x, y, z));
                    for (int x = w; x < w + wingSpan; x++) cells.Add(new Vector3I(x, y, z));
                }

            // nacelles on pylons
            int nacelleZ = Math.Max(0, fuselageLength - 14);
            int[] nacelleX = { -wingSpan + 2, -5, w + 3, w + wingSpan - 5 };
            for (int i = 0; i < nacelleX.Length; i++)
            {
                cells.UnionWith(SolidBox(new Vector3I(nacelleX[i], mid - 2, nacelleZ), new Vector3I(4, 4, 10)));
                for (int z = nacelleZ - 2; z < nacelleZ; z++)
                    cells.Add(new Vector3I(nacelleX[i] + 1, mid, z));
            }

            // dorsal fin
            for (int z = fuselageLength - 12; z < fuselageLength - 2; z++)
                for (int y = w; y < w + 9; y++)
                    cells.Add(new Vector3I(mid, y, z));

            return cells;
        }

        /// <summary>
        /// A station: a solid block of structure with sealed compartments cut out of it.
        ///
        /// <para>
        /// **The shape a base takes, and the opposite of <see cref="Ship"/> in the one way that
        /// decides cooling.** A hull is skin plus bulkheads and is mostly exposed; a station is
        /// built outward from what it contains and is mostly interior, so at the same cell count it
        /// has far less area to shed through (backlog.md `F27`). The compartments are what make it a
        /// station rather than a solid cube: a base's most elaborate machinery — room air,
        /// pressurisation, the flood fill that finds compartments — is the machinery that carries
        /// the least evidence in this model.
        /// </para>
        ///
        /// <para>
        /// Compartments are laid on a lattice with <paramref name="wallThickness"/> of structure
        /// between them, each hollowed to leave its own walls, so the result is sealed rooms rather
        /// than one connected void. The cells this returns are structure; the holes are the rooms.
        /// </para>
        /// </summary>
        /// <param name="extents">Outside size of the block, in cells.</param>
        /// <param name="room">Inside size of one compartment, in cells.</param>
        /// <param name="wallThickness">Cells of structure between one compartment and the next.</param>
        public static HashSet<Vector3I> Station(
            Vector3I extents,
            Vector3I room,
            int wallThickness = 1)
        {
            extents = new Vector3I(Math.Max(3, extents.X), Math.Max(3, extents.Y), Math.Max(3, extents.Z));
            room = new Vector3I(Math.Max(1, room.X), Math.Max(1, room.Y), Math.Max(1, room.Z));
            wallThickness = Math.Max(1, wallThickness);

            HashSet<Vector3I> cells = SolidBox(Vector3I.Zero, extents);

            // The pitch is a compartment plus the wall that follows it, and the first wall is the
            // outer skin — so a compartment never opens onto the outside, which would make it a
            // bay rather than a room.
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

        /// <summary>
        /// Pseudo-random accreted growth from a seed cell — the shape a grid takes when someone
        /// builds without a plan. Deterministic for a given seed.
        /// </summary>
        public static HashSet<Vector3I> Accreted(int blockCount, int seed = 1)
        {
            blockCount = Math.Max(1, blockCount);

            HashSet<Vector3I> cells = new HashSet<Vector3I>(Vector3I.Comparer);
            List<Vector3I> frontier = new List<Vector3I>();

            cells.Add(Vector3I.Zero);
            frontier.Add(Vector3I.Zero);

            // xorshift, so the sequence does not depend on the host's Random implementation
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
                    // that direction was taken; retire the cell occasionally so the frontier
                    // does not stall on a fully enclosed block
                    if (next(4) == 0) frontier.RemoveAt(pick);
                    continue;
                }

                frontier.Add(candidate);
            }

            return cells;
        }

        // ---- building ----------------------------------------------------------------------

        /// <summary>Places one model at every cell of a shape.</summary>
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

        /// <summary>Every named shape, at roughly comparable block counts. For cross-shape tests.</summary>
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
