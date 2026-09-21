using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    public static class BaseVariantLab
    {
        public class Row
        {
            public string TypeId;
            public bool Large;

            public int Blocks;

            public float MassGainedKilograms;

            public float WasteGainedWatts;
        }

        public class Reading
        {
            public int ShipsRead;
            public int BlocksRead;

            public int BlocksCorrected;

            public int FilesUnread;

            public int ShipsRejected;

            public int ShipsAffected;

            public int BlocksAmbiguous;

            public float WasteWatts;

            public float WasteAsArmourWatts;

/// <summary>List operation.</summary>
            public readonly List<Row> Types = new List<Row>();
        }

        private const string LargeArmour = "LargeBlockArmorBlock";

        private const string SmallArmour = "SmallBlockArmorBlock";

/// <summary>Sample operation.</summary>
        public static List<string> Sample(string root, int ships)
        {
            List<string> all = Blueprints.Files(root ?? Blueprints.CorpusPath());
            all.Sort(StringComparer.Ordinal);

            if (ships <= 0 || ships >= all.Count) return all;

/// <summary>List operation.</summary>
            List<string> taken = new List<string>(ships);
            for (int i = 0; i < ships; i++) taken.Add(all[(int)((long)i * all.Count / ships)]);
            return taken;
        }

/// <summary>Walk operation.</summary>
        public static Reading Walk(IList<string> files)
        {
/// <summary>Reading operation.</summary>
            Reading reading = new Reading();
            Dictionary<string, Row> byType = new Dictionary<string, Row>(StringComparer.Ordinal);

            foreach (string file in files)
            {
                List<Blueprints.Ship> ships;
                try { ships = Blueprints.Read(file); }
                catch (Exception) { reading.FilesUnread++; continue; }

                foreach (Blueprints.Ship ship in ships)
                {
                    reading.ShipsRead++;
                    reading.BlocksAmbiguous += ship.AmbiguousBlocks;
                    if (!ship.IsVanilla) reading.ShipsRejected++;
                    bool affected = false;

                    foreach (Blueprints.Grid grid in ship.Grids)
                    {
                        reading.BlocksRead += grid.Blocks;

                        foreach (BlockInstance placed in grid.Builder.Placed)
                        {
                            GameBlocks.Definition definition;
                            if (!GameBlocks.ByModelName().TryGetValue(placed.Model.Name, out definition))
                            {
                                continue;
                            }

/// <summary>FullLoadWatts operation.</summary>
                            float waste = FullLoadWatts(definition);
                            reading.WasteWatts += waste;

                            if (definition.SubtypeId.Length > 0)
                            {
                                reading.WasteAsArmourWatts += waste;
                                continue;
                            }

                            affected = true;
                            reading.BlocksCorrected++;

/// <summary>RowFor operation.</summary>
                            Row row = RowFor(byType, definition);
                            row.Blocks++;
/// <summary>ArmourMass operation.</summary>
                            row.MassGainedKilograms += definition.Mass - ArmourMass(definition.Large);
                            row.WasteGainedWatts += waste;
                        }
                    }

                    if (affected) reading.ShipsAffected++;
                }
            }

            foreach (Row row in byType.Values) reading.Types.Add(row);

            reading.Types.Sort((a, b) => b.WasteGainedWatts.CompareTo(a.WasteGainedWatts));

            return reading;
        }

/// <summary>ShareOf operation.</summary>
        public static List<float> ShareOf(IList<string> files, string typeId, out int shipsRead)
        {
/// <summary>List operation.</summary>
            List<float> shares = new List<float>();
            shipsRead = 0;

            foreach (string file in files)
            {
                List<Blueprints.Ship> ships;
                try { ships = Blueprints.Read(file); }
                catch (Exception) { continue; }

                foreach (Blueprints.Ship ship in ships)
                {
                    shipsRead++;
                    float total = 0f;
                    float mine = 0f;

                    foreach (Blueprints.Grid grid in ship.Grids)
                    {
                        foreach (BlockInstance placed in grid.Builder.Placed)
                        {
                            GameBlocks.Definition definition;
                            if (!GameBlocks.ByModelName().TryGetValue(placed.Model.Name, out definition))
                            {
                                continue;
                            }

/// <summary>FullLoadWatts operation.</summary>
                            float waste = FullLoadWatts(definition);
                            total += waste;
                            if (definition.TypeId == typeId) mine += waste;
                        }
                    }

                    if (mine > 0f && total > 0f) shares.Add(mine / total);
                }
            }

            shares.Sort();
            return shares;
        }

/// <summary>RowFor operation.</summary>
        private static Row RowFor(Dictionary<string, Row> byType, GameBlocks.Definition definition)
        {
            string key = GameBlocks.BaseVariantKey(definition.TypeId, definition.Large);

            Row row;
            if (!byType.TryGetValue(key, out row))
            {
                row = new Row { TypeId = definition.TypeId, Large = definition.Large };
                byType[key] = row;
            }

            return row;
        }

/// <summary>FullLoadWatts operation.</summary>
        private static float FullLoadWatts(GameBlocks.Definition definition)
        {
            if (definition.ThrustNewtons > 0f) return 0f;
            if (ShipLoad.IsStore(definition.TypeId)) return 0f;

            ShippedBlocks.Function function = ShippedBlocks.FunctionOf(definition.TypeId);

            return definition.PowerOutputWatts > 0f
                ? definition.PowerOutputWatts * function.ProducerWasteEnergy
                : definition.PowerDrawWatts * function.ConsumerWasteEnergy;
        }

/// <summary>ArmourMass operation.</summary>
        private static float ArmourMass(bool large)
        {
            Vanilla.Block plate = Vanilla.Find(large ? LargeArmour : SmallArmour);
            return plate == null ? 0f : plate.Mass;
        }

/// <summary>ShareReport operation.</summary>
        public static string ShareReport(IList<string> files, string typeId)
        {
            int ships;
/// <summary>ShareOf operation.</summary>
            List<float> shares = ShareOf(files, typeId, out ships);

/// <summary>StringBuilder operation.</summary>
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(typeId + "  share of the full-load waste of the ships that carry one");
            sb.AppendLine("  basis: full electrical load, no thrust, stores held in reserve");
            sb.AppendLine();

            if (shares.Count == 0)
            {
                sb.AppendLine("  no ship of the " + ships.ToString("n0") + " read carries one");
                return sb.ToString();
            }

            sb.AppendLine("  " + shares.Count.ToString("n0") + " of " + ships.ToString("n0")
/// <summary>one operation.</summary>
                + " ships carry one ("
                + ((float)shares.Count / ships * 100f).ToString("n1") + " %)");

            float[] points = { 0.5f, 0.75f, 0.9f, 0.99f };
            string[] names = { "median", "p75", "p90", "p99" };

            for (int i = 0; i < points.Length; i++)
            {
                int at = Math.Min(shares.Count - 1, (int)(points[i] * shares.Count));
                sb.AppendLine("    " + names[i].PadLeft(6) + " "
                    + (shares[at] * 100f).ToString("n1").PadLeft(6) + " %");
            }

            sb.AppendLine("    " + "max".PadLeft(6) + " "
                + (shares[shares.Count - 1] * 100f).ToString("n1").PadLeft(6) + " %");
            return sb.ToString();
        }

/// <summary>Report operation.</summary>
        public static string Report(IList<string> files)
        {
/// <summary>Walk operation.</summary>
            Reading reading = Walk(files);

/// <summary>StringBuilder operation.</summary>
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("BASE VARIANTS  (blocks a blueprint spells with an empty SubtypeName)");
            sb.AppendLine("  the resolver built every one of these as a plain armour cube until 2026-08-25:");
            sb.AppendLine("  wrong mass, wrong material, and no power draw — so no heat at all");
            sb.AppendLine();
            sb.AppendLine("read " + reading.ShipsRead.ToString("n0") + " ships, "
                + reading.BlocksRead.ToString("n0") + " blocks; "
                + reading.FilesUnread.ToString("n0") + " files would not parse");
            sb.AppendLine(reading.ShipsRejected.ToString("n0")
                + " ships hold a block that resolves to nothing, so the corpus filter rejects them");
            sb.AppendLine(reading.BlocksAmbiguous.ToString("n0")
                + " blocks named a type and subtype no definition has, and fell back to the subtype");
            sb.AppendLine("corrected " + reading.BlocksCorrected.ToString("n0") + " blocks on "
                + reading.ShipsAffected.ToString("n0") + " ships");
            sb.AppendLine();
            sb.AppendLine("full-load waste, this sample:");
            sb.AppendLine("  as built now      " + reading.WasteWatts.ToString("n0") + " W");
            sb.AppendLine("  as armour, before " + reading.WasteAsArmourWatts.ToString("n0") + " W");

            if (reading.WasteAsArmourWatts > 0f)
            {
                sb.AppendLine("  understated by    "
                    + ((reading.WasteWatts / reading.WasteAsArmourWatts - 1f) * 100f).ToString("n2") + " %");
            }

            sb.AppendLine();
            sb.AppendLine("type                             grid    blocks     kg gained        W gained");

            foreach (Row row in reading.Types)
            {
                sb.Append(row.TypeId.PadRight(33));
                sb.Append((row.Large ? "large" : "small").PadRight(8));
                sb.Append(row.Blocks.ToString("n0").PadLeft(6));
                sb.Append(row.MassGainedKilograms.ToString("n0").PadLeft(14));
                sb.Append(row.WasteGainedWatts.ToString("n0").PadLeft(16));
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
