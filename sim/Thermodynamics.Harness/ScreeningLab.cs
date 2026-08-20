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
    /// See [balance-lab.md](../../docs/balance-lab.md).
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

        public static List<ShipProfile> Measure(IList<Blueprints.Ship> ships, ThermalSettings settings = null)
        {
            List<ShipProfile> profiles = new List<ShipProfile>();

            for (int i = 0; i < ships.Count; i++)
            {
                try
                {
                    profiles.Add(ShipProfile.Measure(ships[i], settings));
                }
                catch (Exception)
                {
                    // A corpus of ten thousand will contain ships this model cannot build. Losing
                    // one is not a reason to lose the pass; the yield is reported instead.
                }
            }

            return profiles;
        }

        public static string Report(string path, int panelSize)
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

            List<ShipProfile> profiles = Measure(corpus.Usable);

            sb.AppendLine("SCREENING  (every ship measured, nothing stepped)");
            sb.AppendLine();
            sb.Append("  ").AppendLine(root);
            sb.Append("  ").Append(profiles.Count).Append(" of ").Append(corpus.Usable.Count)
                .AppendLine(" usable ships measured");
            sb.AppendLine();

            sb.AppendLine("ship                              blocks    mass t   area m2  exp%    waste kW   W/m2   Teq K  stiff  stiffest block");
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
                sb.Append("  ").AppendLine(Trim(p.StiffestBlock, 28));
            }

            sb.AppendLine();
            sb.AppendLine("  distributions across the corpus");
            Band(sb, "blocks", profiles, delegate (ShipProfile p) { return p.Blocks; });
            Band(sb, "W per m2", profiles, delegate (ShipProfile p) { return p.ThermalStress; });
            Band(sb, "substep demand", profiles, delegate (ShipProfile p) { return p.PeakSubstepDemand; });
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

        private static void Band(StringBuilder sb, string label, List<ShipProfile> profiles,
            Func<ShipProfile, float> property)
        {
            List<float> values = new List<float>(profiles.Count);
            for (int i = 0; i < profiles.Count; i++) values.Add(property(profiles[i]));
            values.Sort();

            sb.Append("    ").Append(label.PadRight(16));
            sb.Append("min ").Append(At(values, 0f).ToString("n1").PadLeft(10));
            sb.Append("   p50 ").Append(At(values, 0.5f).ToString("n1").PadLeft(10));
            sb.Append("   p95 ").Append(At(values, 0.95f).ToString("n1").PadLeft(10));
            sb.Append("   max ").Append(At(values, 1f).ToString("n1").PadLeft(10));
            sb.AppendLine();
        }

        private static float At(List<float> sorted, float fraction)
        {
            if (sorted.Count == 0) return 0f;
            return sorted[(int)(fraction * (sorted.Count - 1))];
        }

        private static string Trim(string text, int width)
        {
            if (text == null) return "";
            text = text.Replace('\n', ' ').Trim();
            return text.Length <= width ? text : text.Substring(0, width - 1) + "…";
        }
    }
}
