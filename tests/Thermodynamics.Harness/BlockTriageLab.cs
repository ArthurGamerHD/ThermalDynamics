using System;
using System.Collections.Generic;
using System.Text;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **Which blocks a balance pass should look at, in the order it should look at them.**
    ///
    /// <para>
    /// A per-block pass that walks the game alphabetically spends most of itself on blocks nobody
    /// notices. Two facts already exist and had never been put together: <see cref="BlockHeatIndex"/>
    /// says whether a block can survive itself, from the definition alone and with no simulation at
    /// all; and a corpus census says how much of a real fleet's heat each type actually carries.
    /// **Severity says whether a block is wrong. Reach says whether being wrong matters.**
    /// </para>
    ///
    /// <para>
    /// So this ranks by the two together and prints both columns, because they disagree usefully:
    /// `LargePrototechReactor` is the worst block in the game by index and almost nobody builds one,
    /// while a jump drive is survivable and is three quarters of a loaded fleet's heat.
    /// </para>
    ///
    /// <para>
    /// **It is triage, not a verdict.** The index is a bound computed from a definition — every face
    /// radiating to deep space *and* every face bolted to armour held at ambient, both at once — so
    /// a block it clears is a block no arrangement can break, and a block it flags still has to be
    /// rigged to find out what it actually does. That is the point: it turns "simulate 1,503 blocks"
    /// into "simulate the twenty this names".
    /// </para>
    /// </summary>
    public static class BlockTriageLab
    {
        /// <summary>One block, with what is wrong with it and how much that matters.</summary>
        public class Row
        {
            public string Subtype;
            public string TypeId;
            public bool Large;

            /// <summary>Waste watts at full rating, from the definition.</summary>
            public float Watts;

            /// <summary>Heat made over the most it could shed. Above 1 the block is impossible.</summary>
            public float Index;

            /// <summary>The same against its own skin alone. Above 1 it cannot cool itself.</summary>
            public float SelfIndex;

            /// <summary>Where it settles bare, K, and what it is allowed, K.</summary>
            public float EquilibriumKelvin;

            public float CriticalKelvin;

            /// <summary>Share of a censused fleet's full-load waste this type carries, 0..1.</summary>
            public float FleetShare;

            /// <summary>Ships in the census carrying at least one.</summary>
            public int Carriers;

            /// <summary>
            /// Severity times reach, which is what the list is sorted on.
            ///
            /// **Severity is the index clamped at 1 rather than the index itself**, because a block
            /// at 17.9 is not eighteen times more worth fixing than one at 1.0 — both are
            /// impossible, and past the line the number stops carrying information about priority.
            /// Reach is the fleet share, floored so a block with no census row still sorts on its
            /// own severity rather than vanishing.
            /// </summary>
            public float Priority;
        }

        /// <summary>Reach below which a type is ranked on severity alone.</summary>
        private const float ReachFloor = 0.0001f;

        /// <summary>
        /// Every heat-making block the game defines, ranked. `composition` is a census's
        /// `composition.csv`; without one the reach column is empty and the ranking is severity
        /// alone, which is stated rather than silently assumed.
        /// </summary>
        public static List<Row> Rank(string composition)
        {
            Dictionary<string, float> share;
            Dictionary<string, int> carriers;
            Reach(composition, out share, out carriers);

            List<Row> rows = new List<Row>();

            foreach (BlockHeatIndex.Reading reading in BlockHeatIndex.All())
            {
                if (reading.Watts <= 0f) continue;

                float fleet;
                if (!share.TryGetValue(reading.TypeId, out fleet)) fleet = 0f;

                int ships;
                if (!carriers.TryGetValue(reading.TypeId, out ships)) ships = 0;

                float severity = reading.Index > 1f ? 1f : reading.Index;

                rows.Add(new Row
                {
                    Subtype = reading.Subtype,
                    TypeId = reading.TypeId,
                    Large = reading.Large,
                    Watts = reading.Watts,
                    Index = reading.Index,
                    SelfIndex = reading.SelfIndex,
                    EquilibriumKelvin = reading.EquilibriumKelvin,
                    CriticalKelvin = reading.CriticalKelvin,
                    FleetShare = fleet,
                    Carriers = ships,
                    Priority = severity * Math.Max(ReachFloor, fleet),
                });
            }

            rows.Sort(delegate (Row a, Row b) { return b.Priority.CompareTo(a.Priority); });
            return rows;
        }

        /// <summary>
        /// Each type's share of a census's full-load waste, and how many ships carry one.
        ///
        /// Read straight from the composition rather than through `provenance.py`, because this
        /// wants every type rather than only the ones whose fraction is an admitted opinion.
        /// </summary>
        private static void Reach(string composition,
            out Dictionary<string, float> share, out Dictionary<string, int> carriers)
        {
            share = new Dictionary<string, float>(StringComparer.Ordinal);
            carriers = new Dictionary<string, int>(StringComparer.Ordinal);

            if (composition == null || !System.IO.File.Exists(composition)) return;

            Dictionary<string, double> watts = new Dictionary<string, double>(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> ships =
                new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            double total = 0d;

            foreach (string line in System.IO.File.ReadLines(composition))
            {
                string[] fields = Split(line);
                if (fields.Length < 6 || fields[3] == "type_id") continue;

                float w;
                if (!float.TryParse(fields[5], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out w))
                {
                    continue;
                }

                string type = fields[3];
                double had;
                watts[type] = (watts.TryGetValue(type, out had) ? had : 0d) + w;
                total += w;

                HashSet<string> set;
                if (!ships.TryGetValue(type, out set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    ships[type] = set;
                }
                set.Add(fields[0] + "/" + fields[1]);
            }

            if (total <= 0d) return;

            foreach (KeyValuePair<string, double> entry in watts)
            {
                share[entry.Key] = (float)(entry.Value / total);
            }

            foreach (KeyValuePair<string, HashSet<string>> entry in ships)
            {
                carriers[entry.Key] = entry.Value.Count;
            }
        }

        /// <summary>A CSV line, respecting the quotes a ship name needs.</summary>
        private static string[] Split(string line)
        {
            List<string> fields = new List<string>();
            StringBuilder current = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') { quoted = !quoted; continue; }
                if (c == ',' && !quoted) { fields.Add(current.ToString()); current.Length = 0; continue; }
                current.Append(c);
            }

            fields.Add(current.ToString());
            return fields.ToArray();
        }

        /// <summary>The ranking as a table, for crossing against a population.</summary>
        public static string Csv(string composition)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("subtype,type_id,large,watts,index,self_index,settles_k,critical_k,fleet_share,carriers");

            foreach (Row row in Rank(composition))
            {
                sb.Append('"').Append(row.Subtype).Append("\",");
                sb.Append('"').Append(row.TypeId).Append("\",");
                sb.Append(row.Large ? "1," : "0,");
                sb.Append(row.Watts.ToString("r")).Append(',');
                sb.Append(row.Index.ToString("r")).Append(',');
                sb.Append(row.SelfIndex.ToString("r")).Append(',');
                sb.Append(row.EquilibriumKelvin.ToString("r")).Append(',');
                sb.Append(row.CriticalKelvin.ToString("r")).Append(',');
                sb.Append(row.FleetShare.ToString("r")).Append(',');
                sb.Append(row.Carriers.ToString());
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public static string Report(string composition, int take)
        {
            List<Row> rows = Rank(composition);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("BLOCK TRIAGE  (no simulation: an index from the definition, a reach from a census)");
            sb.AppendLine("  index      heat made over the most it could shed, every face radiating AND bolted");
            sb.AppendLine("             to armour at ambient. Above 1 the block is impossible and only the");
            sb.AppendLine("             definition can be changed.");
            sb.AppendLine("  self       the same against its own skin alone. Above 1 it lives on the hull.");
            sb.AppendLine("  fleet      share of a censused fleet's full-load waste this type carries.");
            sb.AppendLine();

            if (composition == null || !System.IO.File.Exists(composition))
            {
                sb.AppendLine("  no census given, so the ranking is severity alone and the fleet column is empty.");
                sb.AppendLine();
            }

            int impossible = 0;
            int cannotCoolItself = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Index > 1f) impossible++;
                if (rows[i].SelfIndex > 1f) cannotCoolItself++;
            }

            sb.AppendLine(rows.Count.ToString("n0") + " heat-making blocks; "
                + impossible.ToString("n0") + " impossible, "
                + cannotCoolItself.ToString("n0") + " cannot cool themselves");
            sb.AppendLine();
            sb.AppendLine("block                                  watts    index    self   settles  crit"
                + "    fleet   ships");

            int shown = take > 0 && take < rows.Count ? take : rows.Count;
            for (int i = 0; i < shown; i++)
            {
                Row row = rows[i];
                sb.Append(row.Subtype.PadRight(36).Substring(0, 36));
                sb.Append(row.Watts.ToString("n0").PadLeft(11));
                sb.Append(row.Index.ToString("n2").PadLeft(9));
                sb.Append(row.SelfIndex.ToString("n2").PadLeft(8));
                sb.Append(row.EquilibriumKelvin.ToString("n0").PadLeft(10));
                sb.Append(row.CriticalKelvin.ToString("n0").PadLeft(6));
                sb.Append((row.FleetShare * 100f).ToString("n2").PadLeft(8)).Append(" %");
                sb.Append(row.Carriers.ToString("n0").PadLeft(8));
                sb.AppendLine();
            }

            if (shown < rows.Count)
            {
                sb.AppendLine("  ... " + (rows.Count - shown).ToString("n0") + " more, lower priority");
            }

            return sb.ToString();
        }
    }
}
