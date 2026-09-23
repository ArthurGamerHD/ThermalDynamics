using System;
using System.Collections.Generic;
using System.Text;

namespace Thermodynamics.Harness
{
    public static class BlockTriageLab
    {
        public class Row
        {
            public string Subtype;
            public string TypeId;
            public bool Large;

            public float Watts;

            public float Index;

            public float SelfIndex;

            public float EquilibriumKelvin;

            public float CriticalKelvin;

            public float FleetShare;

            public int Carriers;

            public float Priority;
        }

        private const float ReachFloor = 0.0001f;


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


        private static string[] Split(string line)
        {
            return CsvLine.Split(line).ToArray();
        }


        public static string Levers(string composition, float target, float minimumReach)
        {

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("LEVERS TO A SELF INDEX OF " + target.ToString("n1")
                + "  (blocks a censused fleet actually carries)");
            sb.AppendLine("  waste / emis / area   the factor that lever must move by; emissivity is capped at 1");
            sb.AppendLine("  rating                the same as a temperature, which is the fourth root of it");
            sb.AppendLine();
            sb.AppendLine("block                                self   waste   emis   area   rating       to K"
                + "   emis now");

            int shown = 0;
            foreach (Row row in Rank(composition))
            {
                if (row.SelfIndex <= target) continue;
                if (row.FleetShare < minimumReach) continue;

                float k = row.SelfIndex / target;
                float rating = (float)Math.Pow(k, 0.25);

                BlockHeatIndex.Reading reading = null;
                foreach (BlockHeatIndex.Reading candidate in BlockHeatIndex.All())
                {
                    if (candidate.Subtype != row.Subtype) continue;
                    reading = candidate;
                    break;
                }

                float emissivity = reading == null ? 0f : reading.Emissivity;

                sb.Append(row.Subtype.PadRight(34).Substring(0, 34));
                sb.Append(row.SelfIndex.ToString("n1").PadLeft(7));
                sb.Append(("/" + k.ToString("n1") + "x").PadLeft(8));
                sb.Append(("x" + k.ToString("n1")).PadLeft(7));
                sb.Append(("x" + k.ToString("n1")).PadLeft(7));
                sb.Append(("x" + rating.ToString("n2")).PadLeft(9));
                sb.Append((row.CriticalKelvin * rating).ToString("n0").PadLeft(11));
                sb.Append(emissivity.ToString("n2").PadLeft(11));

                if (emissivity > 0f && emissivity * k > 1f) sb.Append("   (emis impossible)");

                sb.AppendLine();
                shown++;
            }

            if (shown == 0)
            {
                sb.AppendLine("  nothing a fleet carries is above the target.");
            }

            return sb.ToString();
        }


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
