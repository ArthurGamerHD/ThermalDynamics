using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Step 3 of the balance lab: a panel of ships through the whole scenario battery, and the
    /// matrix that comes out.
    ///
    /// See [balance-lab.md](../../docs/balance-lab.md).
    /// </summary>
    public static class BatteryLab
    {
        public static List<ScenarioOutcome> Run(IList<Blueprints.Ship> ships,
            IList<Battery.Scenario> scenarios, ThermalSettings settings = null)
        {
            List<ScenarioOutcome> outcomes = new List<ScenarioOutcome>();

            for (int s = 0; s < ships.Count; s++)
            {
                for (int i = 0; i < scenarios.Count; i++)
                {
                    try
                    {
                        outcomes.Add(Battery.Run(ships[s], scenarios[i], settings));
                    }
                    catch (Exception)
                    {
                        // One ship failing one scenario is not a reason to lose the matrix.
                    }
                }
            }

            return outcomes;
        }

        public static string Report(string path, int panelSize)
        {
            StringBuilder sb = new StringBuilder();

            string root = path ?? Blueprints.DefaultPath();
            if (root == null)
            {
                sb.AppendLine("No blueprints. The corpus lives at " + Blueprints.CorpusPath());
                return sb.ToString();
            }

            CorpusLab.Summary corpus = CorpusLab.Scan(root);
            if (corpus.Usable.Count == 0)
            {
                sb.AppendLine("No usable ships under " + root);
                return sb.ToString();
            }

            // The battery is expensive, so it runs on specimens rather than on everything. The
            // panel is chosen to cover the feature space rather than to be typical of it.
            List<ShipProfile> profiles = ScreeningLab.Measure(corpus.Usable);
            List<Specimens.Scored> panel = Specimens.Select(profiles, panelSize > 0 ? panelSize : 6);

            Dictionary<string, Blueprints.Ship> byName = new Dictionary<string, Blueprints.Ship>();
            for (int i = 0; i < corpus.Usable.Count; i++) byName[corpus.Usable[i].Name] = corpus.Usable[i];

            List<Blueprints.Ship> ships = new List<Blueprints.Ship>();
            foreach (Specimens.Scored scored in panel)
            {
                Blueprints.Ship ship;
                if (byName.TryGetValue(scored.Ship.Name, out ship)) ships.Add(ship);
            }

            List<Battery.Scenario> scenarios = Battery.All();
            List<ScenarioOutcome> outcomes = Run(ships, scenarios);

            sb.AppendLine("SCENARIO BATTERY");
            sb.AppendLine();
            sb.Append("  ").AppendLine(root);
            sb.Append("  ").Append(ships.Count).Append(" specimens x ").Append(scenarios.Count)
                .Append(" scenarios = ").Append(outcomes.Count).AppendLine(" runs");
            sb.AppendLine();

            sb.AppendLine("  what each scenario is for");
            foreach (Battery.Scenario scenario in scenarios)
            {
                sb.Append("    ").Append(scenario.Name.PadRight(20)).AppendLine(scenario.Question);
            }
            sb.AppendLine();

            sb.AppendLine("ship / scenario                                peak K   mean K   hotspot  over  margin   made kW  vent kW  solar  frict  hottest block");
            foreach (ScenarioOutcome o in outcomes)
            {
                sb.Append(Trim(o.Ship, 22).PadRight(23));
                sb.Append(Trim(o.Scenario, 20).PadRight(21));
                sb.Append(o.PeakKelvin.ToString("n0").PadLeft(8));
                sb.Append(o.MeanKelvin.ToString("n0").PadLeft(9));
                sb.Append(o.HotSpotKelvin.ToString("n0").PadLeft(9));
                sb.Append(o.BlocksOverCritical.ToString("n0").PadLeft(6));
                sb.Append(o.MarginKelvin.ToString("n0").PadLeft(8));
                sb.Append((o.MadeWatts / 1000f).ToString("n0").PadLeft(10));
                sb.Append((o.VentedWatts / 1000f).ToString("n0").PadLeft(9));
                sb.Append((o.SolarWatts / 1000f).ToString("n0").PadLeft(7));
                sb.Append((o.FrictionWatts / 1000f).ToString("n0").PadLeft(7));
                sb.Append("  ").AppendLine(Trim(o.HottestBlock, 26));
            }

            sb.AppendLine();
            sb.AppendLine("  by scenario, across the panel");
            sb.AppendLine("  scenario              peak K    mean K   worst hotspot   ships over critical");

            foreach (Battery.Scenario scenario in scenarios)
            {
                float peak = 0f, mean = 0f, hotspot = 0f;
                int over = 0, count = 0;

                foreach (ScenarioOutcome o in outcomes)
                {
                    if (o.Scenario != scenario.Name) continue;
                    count++;
                    if (o.PeakKelvin > peak) peak = o.PeakKelvin;
                    mean += o.MeanKelvin;
                    if (o.HotSpotKelvin > hotspot) hotspot = o.HotSpotKelvin;
                    if (o.BlocksOverCritical > 0) over++;
                }

                if (count == 0) continue;

                sb.Append("  ").Append(scenario.Name.PadRight(20));
                sb.Append(peak.ToString("n0").PadLeft(8));
                sb.Append((mean / count).ToString("n0").PadLeft(10));
                sb.Append(hotspot.ToString("n0").PadLeft(16));
                sb.Append((over + " of " + count).PadLeft(22));
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string Trim(string text, int width)
        {
            if (text == null) return "";
            text = text.Replace('\n', ' ').Trim();
            return text.Length <= width ? text : text.Substring(0, width - 1) + "…";
        }
    }
}
