using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// How much of a real ship the base-variant defect was getting wrong, measured on the corpus
    /// rather than argued from the game's definition files.
    ///
    /// <para>
    /// The game leaves <c>SubtypeId</c> empty on thirteen definitions and eleven of them are not
    /// armour. <see cref="Blueprints"/> resolved every empty <c>SubtypeName</c> to a plain armour
    /// cube until 2026-08-25, so every vanilla oxygen generator, air vent, oxygen tank, gravity
    /// generator, door, hangar door, passage, ladder and large turret in the corpus was built as
    /// 500 kg of steel that draws no power and therefore makes no heat. Nothing about that looked
    /// wrong: the ship parsed, the block count was right, and the hull stayed vanilla.
    /// </para>
    ///
    /// <para>
    /// **This is a parse, not a simulation**, and that is what makes it cheap enough to run on
    /// demand. It asks what each ship's blocks *are* and what they would waste at full electrical
    /// load — the same basis the census's `waste_full_w` uses — and reports the difference between
    /// what the resolver builds now and the armour it used to build. It says nothing about where a
    /// hull settles; a temperature needs the census re-run, which is backlog.md `A13`.
    /// </para>
    /// </summary>
    public static class BaseVariantLab
    {
        /// <summary>One block type that a blueprint spells with an empty subtype.</summary>
        public class Row
        {
            public string TypeId;
            public bool Large;

            /// <summary>Blocks placed, across the sample.</summary>
            public int Blocks;

            /// <summary>Kilograms the sample gains: the real mass, less the armour it was.</summary>
            public float MassGainedKilograms;

            /// <summary>Watts of full-load waste the sample gains. Armour draws nothing.</summary>
            public float WasteGainedWatts;
        }

        public class Reading
        {
            public int ShipsRead;
            public int BlocksRead;

            /// <summary>Blocks in the sample whose identity the fix changes.</summary>
            public int BlocksCorrected;

            /// <summary>Files that would not parse, counted rather than dropped (`O5`).</summary>
            public int FilesUnread;

            /// <summary>Ships carrying at least one corrected block.</summary>
            public int ShipsAffected;

            /// <summary>Full-load waste the sample makes now, watts.</summary>
            public float WasteWatts;

            /// <summary>Full-load waste it would have made with those blocks as armour, watts.</summary>
            public float WasteAsArmourWatts;

            public readonly List<Row> Types = new List<Row>();
        }

        /// <summary>
        /// The armour cube every one of these blocks used to be, per grid size. Named here so the
        /// counterfactual is the same block the defect actually built rather than a stand-in.
        /// </summary>
        private const string LargeArmour = "LargeBlockArmorBlock";

        /// <summary>The small-grid half of <see cref="LargeArmour"/>.</summary>
        private const string SmallArmour = "SmallBlockArmorBlock";

        /// <summary>
        /// An evenly-strided sample of <paramref name="ships"/> blueprints from the corpus, or all
        /// of them for a count of zero or less.
        ///
        /// **A stride rather than the first N**, because the corpus is stored by workshop id and
        /// the first N by name is a sample of whatever a sort order happens to put first. A stride
        /// over a sorted list is reproducible and spans the population, which is the written rule
        /// a sample needs before it stands for anything (`P1`).
        /// </summary>
        public static List<string> Sample(string root, int ships)
        {
            List<string> all = Blueprints.Files(root ?? Blueprints.CorpusPath());
            all.Sort(StringComparer.Ordinal);

            if (ships <= 0 || ships >= all.Count) return all;

            List<string> taken = new List<string>(ships);
            for (int i = 0; i < ships; i++) taken.Add(all[(int)((long)i * all.Count / ships)]);
            return taken;
        }

        /// <summary>
        /// Walk <paramref name="files"/> and report what the base variants in them are worth.
        ///
        /// Streamed one file at a time and never held: a `Blueprints.Ship` carries every grid and
        /// every block, and parsing a corpus in one pass is what takes a machine down (`O5`). A
        /// blueprint that will not parse is skipped and the ship count says how many were read, so
        /// a sample that half-failed cannot report a clean number (`O5` again).
        /// </summary>
        public static Reading Walk(IList<string> files)
        {
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

                            float waste = FullLoadWatts(definition);
                            reading.WasteWatts += waste;

                            if (definition.SubtypeId.Length > 0)
                            {
                                reading.WasteAsArmourWatts += waste;
                                continue;
                            }

                            affected = true;
                            reading.BlocksCorrected++;

                            Row row = RowFor(byType, definition);
                            row.Blocks++;
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

        /// <summary>
        /// Every carrying ship's own share of full-load waste taken by <paramref name="typeId"/>,
        /// sorted — the statistic the census answers with `--type`, recomputed here because the
        /// census on disk was taken before the base-variant fix and cannot see the largest member
        /// of some types at all.
        /// </summary>
        public static List<float> ShareOf(IList<string> files, string typeId, out int shipsRead)
        {
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

        /// <summary>
        /// Watts of heat this block makes with everything on it running: what it produces through
        /// its producer fraction, what it draws through its consumer one. The census's own basis
        /// (`E3`) — a bound rather than a duty cycle.
        ///
        /// Read from the definition rather than from the placed block, because a block only
        /// carries watts after a scenario has been applied to it and this class does not step
        /// anything.
        ///
        /// <para>
        /// **Thrusters contribute nothing**, which is what makes this the census's *full
        /// electrical* basis rather than a ceiling — a burn is its own scenario there and is here
        /// too. A store is skipped for the census's reason: it rates both ways, is never doing
        /// both, and only makes heat for the share of a load the generators cannot cover, which
        /// needs both totals and a scenario.
        /// </para>
        /// </summary>
        private static float FullLoadWatts(GameBlocks.Definition definition)
        {
            if (definition.ThrustNewtons > 0f) return 0f;
            if (ShipLoad.IsStore(definition.TypeId)) return 0f;

            ShippedBlocks.Function function = ShippedBlocks.FunctionOf(definition.TypeId);

            return definition.PowerOutputWatts > 0f
                ? definition.PowerOutputWatts * function.ProducerWasteEnergy
                : definition.PowerDrawWatts * function.ConsumerWasteEnergy;
        }

        private static float ArmourMass(bool large)
        {
            Vanilla.Block plate = Vanilla.Find(large ? LargeArmour : SmallArmour);
            return plate == null ? 0f : plate.Mass;
        }

        /// <summary>One type's per-ship share, as a report.</summary>
        public static string ShareReport(IList<string> files, string typeId)
        {
            int ships;
            List<float> shares = ShareOf(files, typeId, out ships);

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

        public static string Report(IList<string> files)
        {
            Reading reading = Walk(files);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("BASE VARIANTS  (blocks a blueprint spells with an empty SubtypeName)");
            sb.AppendLine("  the resolver built every one of these as a plain armour cube until 2026-08-25:");
            sb.AppendLine("  wrong mass, wrong material, and no power draw — so no heat at all");
            sb.AppendLine();
            sb.AppendLine("read " + reading.ShipsRead.ToString("n0") + " ships, "
                + reading.BlocksRead.ToString("n0") + " blocks; "
                + reading.FilesUnread.ToString("n0") + " files would not parse");
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
