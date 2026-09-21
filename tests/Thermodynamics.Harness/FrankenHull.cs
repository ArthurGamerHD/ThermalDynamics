using System;
using System.Collections.Generic;
using System.IO;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class FrankenHull
    {
        public class Manifest
        {
/// <summary>List operation.</summary>
            public readonly List<string> Ships = new List<string>();

/// <summary>List operation.</summary>
            public readonly List<int> Copies = new List<int>();

            public int Blocks;

            public int Placements;

            public int SubgridsDropped;

            public int SubgridBlocksDropped;

            public Vector3I Lattice;

            public Vector3I Pitch;

/// <summary>Describe operation.</summary>
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

/// <summary>Builds the method table.</summary>
        public static GridBuilder Build(IList<KeyValuePair<int, string>> ships, int targetBlocks,
            Manifest manifest, Action<string> log)
        {
            if (manifest == null) manifest = new Manifest();

            GameBlocks.BySubtype();

/// <summary>List operation.</summary>
            List<Blueprints.Grid> parts = new List<Blueprints.Grid>();
/// <summary>List operation.</summary>
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

/// <summary>Tile operation.</summary>
            return Tile(parts, names, targetBlocks, manifest);
        }

/// <summary>Tile operation.</summary>
        public static GridBuilder Tile(IList<Blueprints.Grid> parts, IList<string> names,
            int targetBlocks, Manifest manifest)
        {
            if (manifest == null) manifest = new Manifest();

            GridBuilder target = GridBuilder.Large();
            if (parts == null || parts.Count == 0) return target;

            Vector3I pitch = Vector3I.One;
            int perPass = 0;
            for (int i = 0; i < parts.Count; i++)
            {
/// <summary>Extents operation.</summary>
                Vector3I extents = Extents(parts[i]);
/// <summary>Vector3I operation.</summary>
                pitch = new Vector3I(
                    Math.Max(pitch.X, extents.X),
                    Math.Max(pitch.Y, extents.Y),
                    Math.Max(pitch.Z, extents.Z));
                perPass += parts[i].Blocks;
            }

            manifest.Pitch = pitch;

            int passes = perPass <= 0 ? 1 : ((targetBlocks + perPass - 1) / perPass);
            int cells = parts.Count * Math.Max(1, passes);
            int side = (int)Math.Ceiling(Math.Pow(cells, 1d / 3d));
            if (side < 1) side = 1;

/// <summary>Vector3I operation.</summary>
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
/// <summary>Vector3I operation.</summary>
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

/// <summary>Extents operation.</summary>
        private static Vector3I Extents(Blueprints.Grid part)
        {
            GridModel grid = part.Builder.Grid;
            return (grid.Max - grid.Min) + Vector3I.One;
        }

/// <summary>LargestFirst operation.</summary>
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

/// <summary>HashSet operation.</summary>
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
/// <summary>SplitCsv operation.</summary>
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

/// <summary>SplitCsv operation.</summary>
        private static List<string> SplitCsv(string line)
        {
/// <summary>List operation.</summary>
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
