using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// What a blueprint corpus contains, before any of it is simulated.
    ///
    /// The first question of the balance lab is not "how hot does this get" but "can this be read
    /// at all": a corpus is only a measurement of vanilla balance to the extent that its ships
    /// resolve to vanilla blocks. This reports the yield — how many files parsed, how many ships
    /// came out, how many are unmodded — and the size distribution of what survives, which is what
    /// decides how the expensive passes are sampled.
    /// </summary>
    public static class CorpusLab
    {
        /// <summary>Ships below this are cockpits, doors and test rigs rather than designs.</summary>
        public const int MinimumBlocks = 25;

        public class Summary
        {
            public int Files;
            public int Ships;
            public int Vanilla;
            public int Modded;
            public int TooSmall;
            public long Blocks;

            /// <summary>Usable ships, largest first.</summary>
            public List<Blueprints.Ship> Usable = new List<Blueprints.Ship>();

            /// <summary>Subtypes that failed to resolve, by how many ships they disqualified.</summary>
            public Dictionary<string, int> Missing = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        public static Summary Scan(string root)
        {
            Summary summary = new Summary();

            foreach (string file in Blueprints.Files(root))
            {
                summary.Files++;

                foreach (Blueprints.Ship ship in Blueprints.Read(file))
                {
                    summary.Ships++;

                    if (!ship.IsVanilla)
                    {
                        summary.Modded++;
                        foreach (string subtype in ship.UnknownSubtypes)
                        {
                            int count;
                            summary.Missing.TryGetValue(subtype, out count);
                            summary.Missing[subtype] = count + 1;
                        }
                        continue;
                    }

                    if (ship.Blocks < MinimumBlocks)
                    {
                        summary.TooSmall++;
                        continue;
                    }

                    summary.Vanilla++;
                    summary.Blocks += ship.Blocks;
                    summary.Usable.Add(ship);
                }
            }

            summary.Usable.Sort(delegate (Blueprints.Ship a, Blueprints.Ship b)
            {
                return b.Blocks.CompareTo(a.Blocks);
            });

            return summary;
        }

        public static string Report(string path)
        {
            StringBuilder sb = new StringBuilder();

            string root = path ?? Blueprints.WorkshopPath();
            if (root == null)
            {
                sb.AppendLine("No blueprint directory. Pass --path, or install the game and");
                sb.AppendLine("subscribe to some blueprints so Steam unpacks them.");
                return sb.ToString();
            }

            Summary summary = Scan(root);

            sb.AppendLine("BLUEPRINT CORPUS");
            sb.AppendLine();
            sb.Append("  ").AppendLine(root);
            sb.AppendLine();
            sb.Append("  files parsed        ").AppendLine(summary.Files.ToString("n0"));
            sb.Append("  ships found         ").AppendLine(summary.Ships.ToString("n0"));
            sb.Append("  modded, rejected    ").AppendLine(summary.Modded.ToString("n0"));
            sb.Append("  under ").Append(MinimumBlocks).Append(" blocks     ")
                .AppendLine(summary.TooSmall.ToString("n0"));
            sb.Append("  usable              ").AppendLine(summary.Vanilla.ToString("n0"));
            sb.Append("  blocks in corpus    ").AppendLine(summary.Blocks.ToString("n0"));
            sb.AppendLine();

            if (summary.Usable.Count > 0)
            {
                sb.AppendLine("  size distribution, blocks per ship");
                foreach (KeyValuePair<string, int> band in Percentiles(summary.Usable))
                {
                    sb.Append("    ").Append(band.Key.PadRight(6))
                        .AppendLine(band.Value.ToString("n0"));
                }
                sb.AppendLine();

                sb.AppendLine("  largest ships");
                int take = summary.Usable.Count < 10 ? summary.Usable.Count : 10;
                for (int i = 0; i < take; i++)
                {
                    Blueprints.Ship ship = summary.Usable[i];
                    sb.Append("    ").Append(ship.Blocks.ToString("n0").PadLeft(7)).Append("  ")
                        .Append(ship.Large ? "LG  " : "SG  ").AppendLine(Trim(ship.Name, 48));
                }
                sb.AppendLine();
            }

            if (summary.Missing.Count > 0)
            {
                List<KeyValuePair<string, int>> missing =
                    new List<KeyValuePair<string, int>>(summary.Missing);
                missing.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
                {
                    return b.Value.CompareTo(a.Value);
                });

                sb.AppendLine("  unresolved subtypes, by ships disqualified");
                int take = missing.Count < 12 ? missing.Count : 12;
                for (int i = 0; i < take; i++)
                {
                    sb.Append("    ").Append(missing[i].Value.ToString().PadLeft(5)).Append("  ")
                        .AppendLine(Trim(missing[i].Key, 56));
                }
            }

            return sb.ToString();
        }

        private static List<KeyValuePair<string, int>> Percentiles(List<Blueprints.Ship> ships)
        {
            // Usable is sorted largest first, so the percentile index counts from the end.
            List<KeyValuePair<string, int>> bands = new List<KeyValuePair<string, int>>();
            int last = ships.Count - 1;

            bands.Add(new KeyValuePair<string, int>("min", ships[last].Blocks));
            bands.Add(new KeyValuePair<string, int>("p25", ships[last - (last * 25 / 100)].Blocks));
            bands.Add(new KeyValuePair<string, int>("p50", ships[last - (last * 50 / 100)].Blocks));
            bands.Add(new KeyValuePair<string, int>("p75", ships[last - (last * 75 / 100)].Blocks));
            bands.Add(new KeyValuePair<string, int>("p95", ships[last - (last * 95 / 100)].Blocks));
            bands.Add(new KeyValuePair<string, int>("max", ships[0].Blocks));
            return bands;
        }

        private static string Trim(string text, int width)
        {
            if (text == null) return "";
            text = text.Replace('\n', ' ').Trim();
            return text.Length <= width ? text : text.Substring(0, width - 1) + "…";
        }
    }
}
