using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Step 2 of the balance lab: every ship in a corpus measured without running it forward, and
    /// the panel of specimens that population reduces to.
    ///
    /// See balance-lab.md.
    /// </summary>
    public static class ScreeningLab
    {
        /// <summary>Ships a panel is cut down to when none is asked for.</summary>
        public const int DefaultPanel = 24;

        /// <summary>
        /// Feature-space distance within which two ships are treated as saying the same thing.
        ///
        /// A guess until the scenario battery can check it. It is deliberately generous: calling
        /// two ships duplicates when they are not throws away information, while keeping a
        /// duplicate only costs a run.
        /// </summary>
        public const double RedundantWithin = 0.08d;

        /// <summary>
        /// Measures every ship. Ships are independent, so this goes as wide as it is allowed to; a
        /// ship this model cannot build is dropped and shows up as yield rather than losing the pass.
        /// </summary>
        public static List<ShipProfile> Measure(IList<Blueprints.Ship> ships,
            ThermalSettings settings = null, LabMode mode = LabMode.Parallel)
        {
            GameBlocks.Warm();
            return LabRun.Map(ships, ship => ShipProfile.Measure(ship, settings), mode);
        }

        public static string Report(string path, int panelSize, LabMode mode = LabMode.Parallel)
        {
            StringBuilder sb = new StringBuilder();

            string root = path ?? Blueprints.DefaultPath();
            if (root == null)
            {
                sb.AppendLine("No blueprints anywhere. The corpus lives at:");
                sb.AppendLine("  " + Blueprints.CorpusPath());
                return sb.ToString();
            }

            CorpusLab.Summary corpus = CorpusLab.Scan(root);
            if (corpus.Usable.Count == 0)
            {
                sb.AppendLine("No usable ships under " + root);
                return sb.ToString();
            }

            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            List<ShipProfile> profiles = Measure(corpus.Usable, null, mode);
            clock.Stop();

            sb.AppendLine("SCREENING  (every ship measured, nothing stepped)");
            sb.AppendLine();
            sb.Append("  ").AppendLine(root);
            sb.Append("  ").Append(profiles.Count).Append(" of ").Append(corpus.Usable.Count)
                .Append(" usable ships measured, ").Append(LabRun.Describe(mode))
                .Append(", in ").Append(clock.Elapsed.TotalSeconds.ToString("n1")).AppendLine(" s");
            sb.AppendLine();

            sb.AppendLine("  `stiff` is the vacuum figure; `air` is the same ship at sea level, where");
            sb.AppendLine("  convection over a block's exposed area rather than conduction sets it.");
            sb.AppendLine();
            sb.AppendLine("ship                              blocks    mass t   area m2  exp%    waste kW   W/m2   Teq K  stiff    air  stiffest block (in air)");
            profiles.Sort(delegate (ShipProfile a, ShipProfile b)
            {
                return b.ThermalStress.CompareTo(a.ThermalStress);
            });

            foreach (ShipProfile p in profiles)
            {
                sb.Append(Trim(p.Name, 32).PadRight(33));
                sb.Append(p.Blocks.ToString("n0").PadLeft(7));
                sb.Append((p.Mass / 1000f).ToString("n0").PadLeft(10));
                sb.Append(p.ExposedArea.ToString("n0").PadLeft(10));
                sb.Append((p.ExposedFraction * 100f).ToString("n0").PadLeft(6));
                sb.Append((p.WasteWatts / 1000f).ToString("n0").PadLeft(12));
                sb.Append(p.ThermalStress.ToString("n0").PadLeft(7));
                sb.Append(p.EquilibriumKelvin().ToString("n0").PadLeft(8));
                sb.Append(p.PeakSubstepDemand.ToString("n1").PadLeft(7));
                sb.Append(p.PeakSubstepDemandInAir.ToString("n1").PadLeft(7));
                sb.Append("  ").AppendLine(Trim(p.StiffestBlockInAir, 28));
            }

            sb.AppendLine();
            sb.AppendLine("  distributions across the corpus");
            Band(sb, "blocks", profiles, delegate (ShipProfile p) { return p.Blocks; });
            Band(sb, "W per m2", profiles, delegate (ShipProfile p) { return p.ThermalStress; });
            Band(sb, "substep demand", profiles, delegate (ShipProfile p) { return p.PeakSubstepDemand; });
            Band(sb, "substep demand, air", profiles, delegate (ShipProfile p) { return p.PeakSubstepDemandInAir; });
            sb.AppendLine();
            StiffestBlocks(sb, profiles);
            sb.AppendLine();

            // ---- the panel ----------------------------------------------------------------------

            int size = panelSize > 0 ? panelSize : DefaultPanel;
            List<Specimens.Scored> panel = Specimens.Select(profiles, size);
            double fidelity = Specimens.Fidelity(profiles, panel);
            List<KeyValuePair<ShipProfile, double>> redundant =
                Specimens.Redundant(profiles, RedundantWithin);

            sb.Append("SPECIMENS  (").Append(panel.Count).Append(" of ").Append(profiles.Count)
                .AppendLine(" ships, chosen to cover the space rather than to be typical)");
            sb.AppendLine();

            foreach (Specimens.Scored scored in panel)
            {
                sb.Append("  ").Append(Trim(scored.Ship.Name, 34).PadRight(35));
                sb.Append(scored.Ship.Blocks.ToString("n0").PadLeft(7));
                sb.Append(scored.Ship.ThermalStress.ToString("n0").PadLeft(8)).Append(" W/m2  ");
                sb.AppendLine(scored.Reason);
            }

            sb.AppendLine();
            sb.Append("  worst-served ship sits ").Append(fidelity.ToString("n3"))
                .AppendLine(" from its nearest specimen");
            sb.Append("  ").Append(redundant.Count).Append(" of ").Append(profiles.Count)
                .Append(" ships are within ").Append(RedundantWithin.ToString("n2"))
                .AppendLine(" of another, i.e. say nothing new");

            if (redundant.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  closest pairs, i.e. what a corpus stops buying as it grows");
                int take = redundant.Count < 6 ? redundant.Count : 6;
                for (int i = 0; i < take; i++)
                {
                    sb.Append("    ").Append(redundant[i].Value.ToString("n3")).Append("  ")
                        .AppendLine(Trim(redundant[i].Key.Name, 56));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// What sets each ship's substep count in air, counted over the corpus.
        ///
        /// The desk equivalent of a telemetry dump's stiffness table, and the reason the air
        /// distribution has two humps in it rather than a tail: a ship's demand is set by whichever
        /// single block on it is lightest for its exposed area, so the population divides by which
        /// fitting the builder happened to use rather than by anything about the hull.
        /// </summary>
        private static void StiffestBlocks(StringBuilder sb, List<ShipProfile> profiles)
        {
            Dictionary<string, List<float>> byBlock = new Dictionary<string, List<float>>();

            for (int i = 0; i < profiles.Count; i++)
            {
                string name = profiles[i].StiffestBlockInAir;
                if (string.IsNullOrEmpty(name)) continue;

                List<float> demands;
                if (!byBlock.TryGetValue(name, out demands))
                {
                    demands = new List<float>();
                    byBlock[name] = demands;
                }

                demands.Add(profiles[i].PeakSubstepDemandInAir);
            }

            List<KeyValuePair<string, List<float>>> ranked =
                new List<KeyValuePair<string, List<float>>>(byBlock);
            ranked.Sort(delegate (KeyValuePair<string, List<float>> a, KeyValuePair<string, List<float>> b)
            {
                return b.Value.Count.CompareTo(a.Value.Count);
            });

            sb.AppendLine("  what sets each ship's substep count in air");
            sb.AppendLine("    block                              ships   share   median   worst");

            int shown = ranked.Count < TopStiffestBlocks ? ranked.Count : TopStiffestBlocks;
            for (int i = 0; i < shown; i++)
            {
                List<float> demands = ranked[i].Value;
                demands.Sort();

                sb.Append("    ").Append(Trim(ranked[i].Key, 33).PadRight(34));
                sb.Append(demands.Count.ToString("n0").PadLeft(7));
                sb.Append((100f * demands.Count / profiles.Count).ToString("n1").PadLeft(7)).Append(" %");
                sb.Append(At(demands, 0.5f).ToString("n1").PadLeft(8));
                sb.Append(At(demands, 1f).ToString("n1").PadLeft(8));
                sb.AppendLine();
            }

            sb.AppendLine();
        }

        /// <summary>How many block subtypes the table above names before it stops.</summary>
        private const int TopStiffestBlocks = 12;

        private static void Band(StringBuilder sb, string label, List<ShipProfile> profiles,
            Func<ShipProfile, float> property)
        {
            List<float> values = new List<float>(profiles.Count);
            for (int i = 0; i < profiles.Count; i++) values.Add(property(profiles[i]));
            values.Sort();

            sb.Append("    ").Append(label.PadRight(22));
            sb.Append("min ").Append(At(values, 0f).ToString("n1").PadLeft(10));
            sb.Append("   p50 ").Append(At(values, 0.5f).ToString("n1").PadLeft(10));
            sb.Append("   p95 ").Append(At(values, 0.95f).ToString("n1").PadLeft(10));
            sb.Append("   max ").Append(At(values, 1f).ToString("n1").PadLeft(10));
            sb.AppendLine();
        }

        private static float At(List<float> sorted, float fraction)
        {
            return LabStats.PercentileOfSorted(sorted, fraction);
        }

        private static string Trim(string text, int width)
        {
            return LabText.Trim(text, width);
        }
    }
}
