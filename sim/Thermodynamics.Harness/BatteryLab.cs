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
        /// <summary>One run: a ship in a state.</summary>
        private class Job
        {
            public Blueprints.Ship Ship;
            public Battery.Scenario Scenario;
        }

        /// <summary>
        /// Every ship through every scenario.
        ///
        /// **The run is the unit of parallelism**, not the ship. Every simulation built from one
        /// <c>Ship</c> shares its <c>BlockInstance</c> objects and the load is written onto them,
        /// so each job reads the blueprint again for grid state of its own — see
        /// <c>Blueprints.Ship.Reload</c>. Parsing is a fraction of the settling run it frees, and
        /// without it a panel of six ships would use six cores of however many there are.
        /// </summary>
        public static List<ScenarioOutcome> Run(IList<Blueprints.Ship> ships,
            IList<Battery.Scenario> scenarios, ThermalSettings settings = null,
            LabMode mode = LabMode.Parallel)
        {
            GameBlocks.Warm();

            List<Job> jobs = new List<Job>(ships.Count * scenarios.Count);
            for (int s = 0; s < ships.Count; s++)
            {
                for (int i = 0; i < scenarios.Count; i++)
                {
                    jobs.Add(new Job { Ship = ships[s], Scenario = scenarios[i] });
                }
            }

            return LabRun.Map(jobs, job =>
            {
                Blueprints.Ship own = mode == LabMode.Parallel ? job.Ship.Reload() : job.Ship;
                return Battery.Run(own, job.Scenario, settings);
            }, mode);
        }

        public static string Report(string path, int panelSize, LabMode mode = LabMode.Parallel)
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
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            List<ScenarioOutcome> outcomes = Run(ships, scenarios, null, mode);
            clock.Stop();

            sb.AppendLine("SCENARIO BATTERY");
            sb.AppendLine();
            sb.Append("  ").AppendLine(root);
            sb.Append("  ").Append(ships.Count).Append(" specimens x ").Append(scenarios.Count)
                .Append(" scenarios = ").Append(outcomes.Count).AppendLine(" runs");
            sb.Append("  ").AppendLine(LabRun.Describe(mode));
            sb.Append("  ").Append(clock.Elapsed.TotalSeconds.ToString("n1")).Append(" s, ")
                .Append((clock.Elapsed.TotalSeconds / Math.Max(1, outcomes.Count)).ToString("n2"))
                .AppendLine(" s a run");
            sb.AppendLine();

            sb.AppendLine("  what each scenario is for");
            foreach (Battery.Scenario scenario in scenarios)
            {
                sb.Append("    ").Append(scenario.Name.PadRight(20)).AppendLine(scenario.Question);
            }
            sb.AppendLine();

            sb.AppendLine("ship / scenario                                peak K   mean K   hotspot  over   made kW  vent kW  frict kW   demand  granted  settled  hottest block");
            foreach (ScenarioOutcome o in outcomes)
            {
                sb.Append(Trim(o.Ship, 22).PadRight(23));
                sb.Append(Trim(o.Scenario, 20).PadRight(21));
                sb.Append(o.PeakKelvin.ToString("n0").PadLeft(8));
                sb.Append(o.MeanKelvin.ToString("n0").PadLeft(9));
                sb.Append(o.HotSpotKelvin.ToString("n0").PadLeft(9));
                sb.Append(o.BlocksOverCritical.ToString("n0").PadLeft(6));
                sb.Append((o.MadeWatts / 1000f).ToString("n0").PadLeft(10));
                sb.Append((o.VentedWatts / 1000f).ToString("n0").PadLeft(9));
                sb.Append((o.FrictionWatts / 1000f).ToString("n0").PadLeft(10));
                sb.Append(o.SubstepsDemanded.ToString("n1").PadLeft(9));
                sb.Append(o.SubstepsGranted.ToString("n0").PadLeft(9));
                sb.Append((o.SecondsToSettle >= 0f ? o.SecondsToSettle.ToString("n0") : "never").PadLeft(9));
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
