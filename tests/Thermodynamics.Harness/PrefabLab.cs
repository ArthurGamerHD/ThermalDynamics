using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// `G7`: does a ship the game spawns survive arriving?
    ///
    /// <para>
    /// The stated compatibility floor, and until now the only goal this mod holds itself to that
    /// had never been measured. The corpus is ships players chose to upload; these are the 705 the
    /// game puts in front of a player whether they want them or not — cargo ships, drones,
    /// encounters, unknown signals, respawn pods — and a mod that destroys them as they arrive is
    /// broken however good its physics is.
    /// </para>
    ///
    /// <para>
    /// **The criterion was written down before this ran** and is in
    /// balance-lab.md: idle, in the environment the prefab's own
    /// category spawns into, for five simulated minutes, losing no block. Every term of it is a
    /// decision argued there rather than a convenience taken here (`E11`).
    /// </para>
    /// </summary>
    public static class PrefabLab
    {
        /// <summary>Simulated seconds a prefab is watched for. See `G7`.</summary>
        public const float WatchSeconds = 300f;

        /// <summary>What one prefab did.</summary>
        public class Outcome
        {
            public string Prefab;
            public string Category;
            public string Path;

            public int Blocks;
            public int Grids;

            /// <summary>Blocks the game could not identify, which are not simulated.</summary>
            public int UnknownBlocks;

            public float PeakKelvin;
            public float MeanKelvin;

            /// <summary>Blocks past their own rating at the end. Not the same as blocks lost.</summary>
            public int OverCritical;

            /// <summary>Simulated seconds to the first crossing, or -1.</summary>
            public float SecondsToCritical = -1f;

            /// <summary>Simulated seconds to the first block destroyed, or -1. **This is `G7`.**</summary>
            public float SecondsToFirstLoss = -1f;

            /// <summary>Why this prefab was not measured, or null.</summary>
            public string Skipped;

            public bool Lost
            {
                get { return SecondsToFirstLoss >= 0f; }
            }
        }

        /// <summary>
        /// Where a prefab of this category is put. Planetary encounters stand on a planet; nothing
        /// else the game spawns does, so everything else is measured in sunlit vacuum.
        /// </summary>
        public static Func<float, EnvironmentSample> Environment(string category)
        {
            if (category == "PlanetaryEncounters") return t => Worlds.PlanetSurface(1f, 0.5f);
            return t => Worlds.Space(new VRageMath.Vector3(0.3f, 0.9f, 0.2f));
        }

        /// <summary>
        /// Runs one prefab file. Never throws; a file that cannot be read says so.
        ///
        /// <paramref name="load"/> is `Idle` for `G7`, which is about arrival. The other states are
        /// the control: a criterion that can only ever pass has not been tested, and running the
        /// same prefabs under load is how this one is shown to be able to fail (`E8`).
        /// </summary>
        public static Outcome Measure(string path, ThermalSettings settings = null,
            ShipLoad.State load = null)
        {
            Outcome outcome = new Outcome
            {
                Path = path,
                Category = Blueprints.PrefabCategory(path),
                Prefab = System.IO.Path.GetFileNameWithoutExtension(path),
            };

            List<Blueprints.Ship> read = Blueprints.Read(path);
            if (read.Count == 0)
            {
                outcome.Skipped = "no grid the parser could read";
                return outcome;
            }

            Blueprints.Ship ship = read[0];
            if (!string.IsNullOrEmpty(ship.Name)) outcome.Prefab = ship.Name;

            outcome.UnknownBlocks = ship.UnknownBlocks;
            outcome.Grids = ship.Grids.Count;

            ShipAssembly assembly = ship.Build(settings ?? new ThermalSettings().Derive());
            outcome.Blocks = assembly.NodeCount;

            if (outcome.Blocks == 0)
            {
                outcome.Skipped = "no simulated block";
                return outcome;
            }

            ShipLoad.Apply(assembly, load ?? ShipLoad.State.Idle);

            AssemblyRunner runner = new AssemblyRunner(assembly);
            runner.Environment = Environment(outcome.Category);
            runner.Integrity = GameBlocks.IntegrityOf;
            runner.Run(WatchSeconds);

            outcome.SecondsToCritical = runner.SecondsToCritical;
            outcome.SecondsToFirstLoss = runner.SecondsToFirstLoss;

            ScenarioOutcome read2 = ScenarioOutcome.Read(assembly, outcome.Prefab, "prefab-idle");
            outcome.PeakKelvin = read2.PeakKelvin;
            outcome.MeanKelvin = read2.MeanKelvin;
            outcome.OverCritical = read2.BlocksOverCritical;

            return outcome;
        }

        /// <summary>Every prefab the game ships, measured. Parallel across files.</summary>
        public static List<Outcome> Run(ThermalSettings settings = null, int limit = 0,
            ShipLoad.State load = null)
        {
            List<string> files = Blueprints.PrefabFiles();
            if (limit > 0 && files.Count > limit) files = files.GetRange(0, limit);

            ThermalSettings world = settings;
            return LabRun.Map(files, path => Measure(path, world, load), LabMode.Parallel);
        }

        /// <summary>The verdict, and the shape of what was measured.</summary>
        public static string Report(IList<Outcome> outcomes)
        {
            StringBuilder text = new StringBuilder();

            int measured = 0, skipped = 0, lost = 0, crossed = 0;
            long blocks = 0, unknown = 0;
            Dictionary<string, int> lostByCategory = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, int> byCategory = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < outcomes.Count; i++)
            {
                Outcome o = outcomes[i];
                if (o.Skipped != null) { skipped++; continue; }

                measured++;
                blocks += o.Blocks;
                unknown += o.UnknownBlocks;

                int seen;
                byCategory.TryGetValue(o.Category, out seen);
                byCategory[o.Category] = seen + 1;

                if (o.SecondsToCritical >= 0f) crossed++;
                if (!o.Lost) continue;

                lost++;
                int had;
                lostByCategory.TryGetValue(o.Category, out had);
                lostByCategory[o.Category] = had + 1;
            }

            text.AppendLine("G7 — a ship the game spawns survives arrival");
            text.AppendLine();
            text.AppendLine("  " + measured.ToString("n0") + " prefabs measured, " + skipped
                + " unreadable, " + blocks.ToString("n0") + " blocks ("
                + unknown.ToString("n0") + " the parser could not identify)");
            text.AppendLine("  in the environment each category spawns into, for "
                + WatchSeconds.ToString("n0") + " simulated seconds");
            text.AppendLine();
            text.AppendLine("  crossed critical:  " + crossed.ToString("n0"));
            text.AppendLine("  LOST A BLOCK:      " + lost.ToString("n0"));
            text.AppendLine();
            text.AppendLine("  " + (lost == 0 ? "G7 HOLDS" : "G7 FAILS"));

            if (lost > 0)
            {
                text.AppendLine();
                text.AppendLine("  by category");
                foreach (KeyValuePair<string, int> entry in byCategory)
                {
                    int failed;
                    lostByCategory.TryGetValue(entry.Key, out failed);
                    if (failed == 0) continue;

                    text.AppendLine("    " + entry.Key.PadRight(22) + failed + " of " + entry.Value);
                }

                text.AppendLine();
                text.AppendLine("  worst, by how soon it lost one");
                List<Outcome> failures = new List<Outcome>();
                for (int i = 0; i < outcomes.Count; i++)
                {
                    if (outcomes[i].Skipped == null && outcomes[i].Lost) failures.Add(outcomes[i]);
                }

                failures.Sort(delegate (Outcome a, Outcome b)
                {
                    return a.SecondsToFirstLoss.CompareTo(b.SecondsToFirstLoss);
                });

                for (int i = 0; i < failures.Count && i < 20; i++)
                {
                    Outcome o = failures[i];
                    text.AppendLine(string.Format("    {0,-40}{1,8:n0} s  peak {2,9:n0} K  {3}",
                        o.Prefab, o.SecondsToFirstLoss, o.PeakKelvin, o.Category));
                }
            }

            return text.ToString();
        }

        /// <summary>One row per prefab.</summary>
        public static string Csv(IList<Outcome> outcomes)
        {
            StringBuilder csv = new StringBuilder();
            csv.AppendLine("prefab,category,blocks,grids,unknown_blocks,peak_k,mean_k,"
                + "over_critical,seconds_to_critical,seconds_to_first_loss,skipped");

            for (int i = 0; i < outcomes.Count; i++)
            {
                Outcome o = outcomes[i];
                csv.Append('"').Append((o.Prefab ?? "").Replace("\"", "\"\"")).Append("\",")
                   .Append(o.Category).Append(',')
                   .Append(o.Blocks).Append(',').Append(o.Grids).Append(',')
                   .Append(o.UnknownBlocks).Append(',')
                   .Append(o.PeakKelvin.ToString("0.###")).Append(',')
                   .Append(o.MeanKelvin.ToString("0.###")).Append(',')
                   .Append(o.OverCritical).Append(',')
                   .Append(o.SecondsToCritical.ToString("0.###")).Append(',')
                   .Append(o.SecondsToFirstLoss.ToString("0.###")).Append(',')
                   .Append('"').Append(o.Skipped ?? "").Append('"')
                   .AppendLine();
            }

            return csv.ToString();
        }
    }
}
