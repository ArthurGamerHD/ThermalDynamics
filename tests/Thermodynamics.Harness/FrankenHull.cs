using System;
using System.Collections.Generic;
using System.IO;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// One grid of a million blocks, welded together out of real workshop ships.
    ///
    /// <para>
    /// **Why it exists.** The scale bound is a single grid of 10⁶ blocks, and no published
    /// blueprint is one — the largest of 8,132 is 641,711 and the ninety-ninth percentile is
    /// 70,141 (`G5`). So every figure this repository has at that size was taken on
    /// <see cref="LoadShapes"/>' synthetic hull: census tiers dealt into a shape, which is a
    /// reasonable stand-in for stiffness and is not a ship. **A hull welded out of real ships is
    /// both a thing players build and a far better rig**, because the block *mixture* at scale is
    /// what a synthetic ladder cannot invent — a capital ship's proportion of armour to machinery,
    /// its conveyor runs, its thruster banks, its light fittings.
    /// </para>
    ///
    /// <para>
    /// **What it is not.** It is not a population sample and cannot be read as one: it is the
    /// biggest ships in the corpus, tiled, which is what a million-block grid would have to be made
    /// of and is nobody's median. Every figure taken on it is a figure about the stress bound.
    /// </para>
    ///
    /// <para>
    /// **Two properties of the tiling to read a figure against.** The lattice pitch is the largest
    /// bounding box in *each axis independently*, so a set of differently-shaped hulls leaves air
    /// between them and the grid's **bounding volume is far larger than its block count** — which
    /// the room map floods (`D2`), so the room and exposure columns describe the tiling as much as
    /// they describe the ships. And it stops within one hull of the target rather than on it,
    /// because a hull is placed whole; the manifest reports what was actually built and every figure
    /// is keyed on that rather than on what was asked for.
    /// </para>
    /// </summary>
    public static class FrankenHull
    {
        /// <summary>What the whole thing is built from, and what it cost to build.</summary>
        public class Manifest
        {
            /// <summary>Distinct blueprints used, largest first.</summary>
            public readonly List<string> Ships = new List<string>();

            /// <summary>How many copies of each were placed, in the same order.</summary>
            public readonly List<int> Copies = new List<int>();

            /// <summary>Blocks placed, which is the grid's own count.</summary>
            public int Blocks;

            /// <summary>Copies placed in total, across every ship.</summary>
            public int Placements;

            /// <summary>Subgrids dropped: a franken hull is one grid, so turrets and doors go.</summary>
            public int SubgridsDropped;

            /// <summary>Blocks in those subgrids, so what was dropped is a number rather than a word.</summary>
            public int SubgridBlocksDropped;

            /// <summary>The lattice the bounding boxes were tiled on.</summary>
            public Vector3I Lattice;

            /// <summary>One cell of that lattice, in grid cells.</summary>
            public Vector3I Pitch;

            public string Describe()
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append(Blocks.ToString("n0")).Append(" blocks from ")
                  .Append(Placements.ToString("n0")).Append(" copies of ")
                  .Append(Ships.Count).Append(" ships, tiled ")
                  .Append(Lattice.X).Append("x").Append(Lattice.Y).Append("x").Append(Lattice.Z)
                  .Append(" on a pitch of ")
                  .Append(Pitch.X).Append("x").Append(Pitch.Y).Append("x").Append(Pitch.Z)
                  .Append(" cells; dropped ").Append(SubgridsDropped)
                  .Append(" subgrids carrying ").Append(SubgridBlocksDropped.ToString("n0"))
                  .Append(" blocks");
                return sb.ToString();
            }
        }

        /// <summary>
        /// Reads the corpus's largest ships until they add up to <paramref name="targetBlocks"/>,
        /// and tiles their main grids into one grid.
        ///
        /// <para>
        /// **Largest first, and that is a choice with a reason.** Fewest copies means the mixture
        /// stays closest to the ships it came from rather than becoming one small ship repeated a
        /// thousand times, and a million-block grid is a capital-ship and station thing in the first
        /// place. It is also the cheapest: a thousand copies of a shuttle is a thousand parses.
        /// </para>
        ///
        /// <para>
        /// **The bounding boxes are tiled with no gap**, so neighbouring hulls touch and bolt where
        /// their mount points agree. Whether any two of them conduct is left to the model rather
        /// than forced — a welded-together hull genuinely has seams that carry heat and seams that
        /// do not, and inventing a bridge would be inventing the answer to what the rig is for.
        /// </para>
        /// </summary>
        /// <param name="ships">
        /// Blocks and blueprint path per ship, largest first, from <see cref="LargestFirst"/>.
        /// **A ship larger than the whole target is skipped**: largest-first is right for a million
        /// and absurd for forty thousand, where it would read a 641,711-block capital ship — a
        /// quarter-gigabyte file — to build a hull a sixteenth its size and overshoot by fifteen.
        /// </param>
        public static GridBuilder Build(IList<KeyValuePair<int, string>> ships, int targetBlocks,
            Manifest manifest, Action<string> log)
        {
            if (manifest == null) manifest = new Manifest();

            GameBlocks.BySubtype();

            List<Blueprints.Grid> parts = new List<Blueprints.Grid>();
            List<string> names = new List<string>();
            int have = 0;

            for (int i = 0; i < ships.Count && have < targetBlocks; i++)
            {
                if (ships[i].Key > targetBlocks) continue;

                string path = ships[i].Value;
                if (!File.Exists(path)) continue;

                List<Blueprints.Ship> read;
                try
                {
                    read = Blueprints.Read(path);
                }
                catch (Exception)
                {
                    // A blueprint the reader cannot take is one fewer ship, not a dead run: this
                    // rig is a pile of hulls and any of them will do.
                    continue;
                }

                for (int s = 0; s < read.Count && have < targetBlocks; s++)
                {
                    Blueprints.Ship ship = read[s];
                    if (!ship.IsVanilla || ship.Grids.Count == 0) continue;

                    Blueprints.Grid main = ship.Grids[0];
                    if (main.Blocks <= 0) continue;

                    manifest.SubgridsDropped += ship.Subgrids;
                    manifest.SubgridBlocksDropped += ship.Blocks - main.Blocks;

                    parts.Add(main);
                    names.Add(ship.Name);
                    have += main.Blocks;

                    if (log != null)
                    {
                        log(ship.Name + ": " + main.Blocks.ToString("n0") + " blocks, "
                            + have.ToString("n0") + " of " + targetBlocks.ToString("n0"));
                    }
                }
            }

            return Tile(parts, names, targetBlocks, manifest);
        }

        /// <summary>
        /// Tiles the parts into one grid, repeating the list until the target is reached.
        ///
        /// Split from <see cref="Build"/> so a test can hand it hulls it made itself and not need a
        /// corpus to run.
        /// </summary>
        public static GridBuilder Tile(IList<Blueprints.Grid> parts, IList<string> names,
            int targetBlocks, Manifest manifest)
        {
            if (manifest == null) manifest = new Manifest();

            GridBuilder target = GridBuilder.Large();
            if (parts == null || parts.Count == 0) return target;

            // One pitch for every part, from the largest bounding box there is, so the lattice is
            // regular and two neighbours cannot overlap whichever pair lands beside each other.
            Vector3I pitch = Vector3I.One;
            int perPass = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                Vector3I extents = Extents(parts[i]);
                pitch = new Vector3I(
                    Math.Max(pitch.X, extents.X),
                    Math.Max(pitch.Y, extents.Y),
                    Math.Max(pitch.Z, extents.Z));
                perPass += parts[i].Blocks;
            }

            manifest.Pitch = pitch;

            // A cube of lattice cells, so the hull is compact rather than a line: a grid's cost
            // depends on how many neighbours a block has, and a chain of ships would understate it.
            int passes = perPass <= 0 ? 1 : ((targetBlocks + perPass - 1) / perPass);
            int cells = parts.Count * Math.Max(1, passes);
            int side = (int)Math.Ceiling(Math.Pow(cells, 1d / 3d));
            if (side < 1) side = 1;

            manifest.Lattice = new Vector3I(side, side, side);

            int[] copies = new int[parts.Count];
            int placed = 0;
            int index = 0;

            for (int z = 0; z < side && manifest.Blocks < targetBlocks; z++)
            {
                for (int y = 0; y < side && manifest.Blocks < targetBlocks; y++)
                {
                    for (int x = 0; x < side && manifest.Blocks < targetBlocks; x++)
                    {
                        Blueprints.Grid part = parts[index % parts.Count];
                        Vector3I offset = new Vector3I(x * pitch.X, y * pitch.Y, z * pitch.Z)
                            - part.Builder.Grid.Min;

                        IList<BlockInstance> blocks = part.Builder.Placed;
                        for (int b = 0; b < blocks.Count; b++)
                        {
                            BlockInstance block = blocks[b];
                            target.Place(block.Model, block.Min + offset, block.Orientation);
                        }

                        copies[index % parts.Count]++;
                        manifest.Blocks += blocks.Count;
                        placed++;
                        index++;
                    }
                }
            }

            manifest.Placements = placed;
            for (int i = 0; i < parts.Count; i++)
            {
                if (copies[i] <= 0) continue;
                manifest.Ships.Add(names != null && i < names.Count ? names[i] : "part " + i);
                manifest.Copies.Add(copies[i]);
            }

            return target;
        }

        private static Vector3I Extents(Blueprints.Grid part)
        {
            GridModel grid = part.Builder.Grid;
            return (grid.Max - grid.Min) + Vector3I.One;
        }

        /// <summary>
        /// The corpus's blueprints, largest first, read from a survey's `ships.csv`.
        ///
        /// **From the survey rather than from a directory walk**, for the reason
        /// `tools/corpus/README.md` gives about the dial sweep: without a path per ship, finding the
        /// biggest hulls means parsing all 9,981 blueprints, and the largest of them are quarter-
        /// gigabyte files.
        /// </summary>
        public static List<KeyValuePair<int, string>> LargestFirst(string shipsCsv)
        {
            List<KeyValuePair<int, string>> rows = new List<KeyValuePair<int, string>>();
            if (!File.Exists(shipsCsv)) return rows;

            using (StreamReader reader = new StreamReader(shipsCsv))
            {
                string header = reader.ReadLine();
                if (header == null) return rows;

                string[] columns = header.Split(',');
                int blocksAt = Array.IndexOf(columns, "blocks");
                int pathAt = Array.IndexOf(columns, "path");
                if (blocksAt < 0 || pathAt < 0) return rows;

                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    List<string> cells = SplitCsv(line);
                    if (cells.Count <= blocksAt || cells.Count <= pathAt) continue;

                    int blocks;
                    if (!int.TryParse(cells[blocksAt], out blocks)) continue;

                    string path = cells[pathAt];
                    if (path.Length == 0 || !seen.Add(path)) continue;

                    rows.Add(new KeyValuePair<int, string>(blocks, path));
                }
            }

            rows.Sort(delegate (KeyValuePair<int, string> a, KeyValuePair<int, string> b)
            {
                return b.Key.CompareTo(a.Key);
            });

            return rows;
        }

        /// <summary>Quoted fields only, which is all `CorpusRecord` ever writes.</summary>
        private static List<string> SplitCsv(string line)
        {
            List<string> cells = new List<string>();
            System.Text.StringBuilder cell = new System.Text.StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') { quoted = !quoted; continue; }
                if (c == ',' && !quoted) { cells.Add(cell.ToString()); cell.Length = 0; continue; }
                cell.Append(c);
            }

            cells.Add(cell.ToString());
            return cells;
        }
    }
}
