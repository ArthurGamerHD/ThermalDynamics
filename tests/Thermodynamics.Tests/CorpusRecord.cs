using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// What a corpus pass **learned**, written down: the invariants are pass or fail, and every figure
    /// the run measured to answer them used to die with the assertion. Written down once, the same
    /// pass names the specimens that answer the question in minutes afterwards.
    ///
    /// <para>
    /// Off unless <c>THERMAL_CORPUS_DATA</c> names a directory. One file per kind of row, appended a
    /// batch at a time, because thirty workers opening a file per ship is its own bottleneck.
    /// See balance.md, The datasets.
    /// </para>
    /// </summary>
    internal static class CorpusRecord
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<string> Started = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// The directory to write into, or null when recording is off.
        ///
        /// <para>
        /// **A relative path is resolved against the repository root, not the process's working
        /// directory.** The test host runs from its own `bin/Debug` folder, so
        /// `THERMAL_CORPUS_DATA=../out/census-2026-08-25` wrote a census three directories away
        /// from where it was asked for — the run passed, the named directory stayed empty, and the
        /// dataset had to be hunted for. A person typing that variable means it relative to the
        /// repository, which is the anchor everything else in the harness already uses.
        /// </para>
        /// </summary>
        public static string Directory()
        {
            string path = Environment.GetEnvironmentVariable("THERMAL_CORPUS_DATA");
            if (string.IsNullOrEmpty(path)) return null;

            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(Root(), path));
        }

        public static bool On
        {
            get { return Directory() != null; }
        }

        /// <summary>
        /// The definition files whose contents decide what a walk measures. Named rather than
        /// globbed, so a file that is added and not listed shows up as a gap in this list rather
        /// than as a hash that quietly appears one day and not the next.
        /// </summary>
        private static readonly string[] Definitions = { "Cubes.xml", "Loops.xml", "Planets.xml" };

        /// <summary>
        /// **What build a dataset was collected on, written beside it.**
        ///
        /// <para>
        /// A walk is hours long and its output outlives the tree it came from. On 2026-08-25 the
        /// 2026-08-24 air walk was found to have finished **one minute after** a commit that moved
        /// twenty-seven waste fractions — it had loaded the old ones at process start — and the only
        /// way to establish that was to compare its rows with a later walk and then read the git log
        /// for the window between them. A dataset that records its own build makes that a lookup
        /// (`P1`, `E5`).
        /// </para>
        ///
        /// <para>
        /// It records the commit and a hash of each definition file a walk's numbers depend on,
        /// because a clean commit says nothing about `Cubes.xml` being edited and not committed,
        /// which is exactly how a walk ends up measuring a world that never existed.
        /// </para>
        ///
        /// <para>
        /// Written once per run, on the first batch, and never overwritten: a resumed walk appends
        /// a second block, so a dataset assembled across two builds says so rather than claiming
        /// the second.
        /// </para>
        /// </summary>
        public static void Provenance(string walk)
        {
            string directory = Directory();
            if (directory == null) return;

            lock (Gate)
            {
                if (!Started.Add("provenance:" + walk)) return;
            }

            StringBuilder text = new StringBuilder();
            text.Append("walk ").Append(walk)
                .Append(" started ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                .AppendLine();
            text.Append("commit ").AppendLine(Commit());
            foreach (string definition in Definitions)
            {
                text.Append(definition).Append(' ')
                    .AppendLine(HashOf(Path.Combine(Root(), "Data", definition)));
            }

            try
            {
                // **The directory first, because this runs before the first batch is written.**
                // Without it `AppendAllText` throws `DirectoryNotFoundException` — an `IOException`,
                // so the catch below swallowed it — and **no dataset with a fresh output directory
                // ever recorded its build**, which is every dataset. Found on 2026-08-25 when the
                // `A13` census re-take had no `provenance.txt` and the question *which fractions
                // was this taken at* had to be answered from the git log again, which is the exact
                // thing this method exists to make a lookup.
                System.IO.Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "provenance.txt"), text.ToString());
            }
            catch (IOException)
            {
                // Provenance must never be the reason a walk fails.
            }
        }

        private static string Root()
        {
            return ShippedBlocks.RepoRoot();
        }

        /// <summary>
        /// The commit `HEAD` points at, read from `.git` rather than by running git — the harness
        /// has no process to spawn and a walk must not depend on one being available.
        ///
        /// **Says `unknown` rather than guessing.** It does not say whether the tree is clean: that
        /// needs the index and every file's stat, which is a `git status` and not a file read. What
        /// stands in for it is the definition hashes beside the commit, which are what decides what
        /// a walk measures. This summary used to claim a `dirty` marker that no line of the method
        /// produced.
        ///
        /// <para>
        /// **`.git` is a directory in a clone and a file in a worktree**, and the file names the
        /// directory to read instead. Following it matters because a walk launched from a worktree
        /// otherwise recorded `unknown` and looked exactly like a walk on a machine with no
        /// repository at all — the silent half of the failure this record exists to prevent. Loose
        /// refs live in the *common* directory a worktree shares with its clone, so a reference is
        /// looked for there as well before `packed-refs` is tried.
        /// </para>
        /// </summary>
        private static string Commit()
        {
            try
            {
                string git = GitDirectory();
                if (git == null) return "unknown";

                string common = CommonDirectory(git);
                string head = File.ReadAllText(Path.Combine(git, "HEAD")).Trim();

                string hash;
                if (head.StartsWith("ref:", StringComparison.Ordinal))
                {
                    string reference = head.Substring(4).Trim();
                    string relative = reference.Replace('/', Path.DirectorySeparatorChar);

                    string path = Path.Combine(git, relative);
                    if (!File.Exists(path)) path = Path.Combine(common, relative);

                    hash = File.Exists(path)
                        ? File.ReadAllText(path).Trim()
                        : Packed(common, reference);
                }
                else
                {
                    hash = head;
                }

                return string.IsNullOrEmpty(hash) ? "unknown" : hash;
            }
            catch (IOException)
            {
                return "unknown";
            }
        }

        /// <summary>
        /// The directory holding this checkout's `HEAD`: `.git` itself in a clone, and whatever the
        /// `gitdir:` line names in a worktree, where `.git` is a file. Null when neither is there.
        /// </summary>
        private static string GitDirectory()
        {
            // `System.IO.` spelled out: this class has a `Directory()` of its own — the dataset
            // directory — and the unqualified name resolves to it.
            string root = Root();
            string git = Path.Combine(root, ".git");

            if (System.IO.Directory.Exists(git)) return git;
            if (!File.Exists(git)) return null;

            string text = File.ReadAllText(git).Trim();
            if (!text.StartsWith("gitdir:", StringComparison.Ordinal)) return null;

            string named = text.Substring("gitdir:".Length).Trim();
            if (named.Length == 0) return null;

            // Git writes it absolute here and is allowed to write it relative, which is resolved
            // against the directory the `.git` file is in.
            string resolved = Path.IsPathRooted(named) ? named : Path.Combine(root, named);
            return System.IO.Directory.Exists(resolved) ? resolved : null;
        }

        /// <summary>
        /// The directory a worktree shares with its clone — refs, objects and `packed-refs` all live
        /// there. Named by a `commondir` file beside `HEAD`; without one, this *is* the clone.
        /// </summary>
        private static string CommonDirectory(string git)
        {
            string marker = Path.Combine(git, "commondir");
            if (!File.Exists(marker)) return git;

            string named = File.ReadAllText(marker).Trim();
            if (named.Length == 0) return git;

            string resolved = Path.IsPathRooted(named) ? named : Path.Combine(git, named);
            return System.IO.Directory.Exists(resolved) ? resolved : git;
        }

        /// <summary>A reference that lives in `packed-refs` rather than as a loose file.</summary>
        private static string Packed(string git, string reference)
        {
            string path = Path.Combine(git, "packed-refs");
            if (!File.Exists(path)) return null;

            foreach (string line in File.ReadAllLines(path))
            {
                if (!line.EndsWith(" " + reference, StringComparison.Ordinal)) continue;

                return line.Substring(0, line.IndexOf(' '));
            }

            return null;
        }

        /// <summary>
        /// A stable digest of a file, or `missing` — the definitions decide what a walk measures,
        /// and a commit hash says nothing about one edited and not committed.
        /// </summary>
        private static string HashOf(string path)
        {
            try
            {
                if (!File.Exists(path)) return "missing";

                using (System.Security.Cryptography.SHA256 sha =
                    System.Security.Cryptography.SHA256.Create())
                using (FileStream stream = File.OpenRead(path))
                {
                    return BitConverter.ToString(sha.ComputeHash(stream))
                        .Replace("-", "").ToLowerInvariant().Substring(0, 16);
                }
            }
            catch (IOException)
            {
                return "unreadable";
            }
        }

        /// <summary>
        /// Appends rows to <paramref name="name"/>.csv, writing the header the first time that file
        /// is touched in this run.
        /// </summary>
        public static void Write(string name, string header, List<string> rows)
        {
            string directory = Directory();
            if (directory == null || rows.Count == 0) return;

            try
            {
                lock (Gate)
                {
                    System.IO.Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, name + ".csv");

                    StringBuilder text = new StringBuilder();
                    if (Started.Add(name) && !File.Exists(path)) text.AppendLine(header);
                    foreach (string row in rows) text.AppendLine(row);

                    File.AppendAllText(path, text.ToString());
                }
            }
            catch
            {
                // Recording must never be the reason a run fails.
            }
        }

        /// <summary>A CSV field: quoted, with any quotes doubled.</summary>
        public static string Text(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>A number, invariant and with no thousands separators.</summary>
        public static string Num(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "";
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        // ---- the scenario matrix ------------------------------------------------------------

        public const string OutcomeHeader =
            "walk,ship,workshop_id,scenario,blocks,grids,joints,peak_k,mean_k,median_k,p95_k,min_k,"
            + "gradient_k,hotspot_k,hotspot_blocks,over_critical,over_share,margin_k,"
            + "seconds_to_settle,seconds_to_critical,peak_rate_k_per_s,thermal_mass_j_per_k,"
            + "bulk_drift_w,made_w,vented_w,radiation_w,convection_w,solar_w,friction_w,"
            + "generation_w,substeps_demanded,substeps_granted,hottest_block,seconds_to_first_loss,"
            + "links,substep_cost,run_seconds,cap,floored";

        /// <summary>Everything one run of one ship measured, as a row.</summary>
        public static string Row(string walk, ScenarioOutcome o)
        {
            StringBuilder row = new StringBuilder();
            row.Append(Text(walk)).Append(',');
            row.Append(Text(o.Ship)).Append(',');
            row.Append(o.WorkshopId.ToString(CultureInfo.InvariantCulture)).Append(',');
            row.Append(Text(o.Scenario)).Append(',');
            row.Append(o.Blocks).Append(',');
            row.Append(o.Grids).Append(',');
            row.Append(o.Joints).Append(',');
            row.Append(Num(o.PeakKelvin)).Append(',');
            row.Append(Num(o.MeanKelvin)).Append(',');
            row.Append(Num(o.MedianKelvin)).Append(',');
            row.Append(Num(o.P95Kelvin)).Append(',');
            row.Append(Num(o.MinKelvin)).Append(',');
            row.Append(Num(o.GradientKelvin)).Append(',');
            row.Append(Num(o.HotSpotKelvin)).Append(',');
            row.Append(o.HotSpotBlocks).Append(',');
            row.Append(o.BlocksOverCritical).Append(',');
            row.Append(Num(o.OverCriticalShare)).Append(',');
            row.Append(Num(o.MarginKelvin)).Append(',');
            row.Append(Num(o.SecondsToSettle)).Append(',');
            row.Append(Num(o.SecondsToCritical)).Append(',');
            row.Append(Num(o.PeakRateKelvinPerSecond)).Append(',');
            row.Append(Num(o.ThermalMass)).Append(',');
            row.Append(Num(o.BulkDriftWatts)).Append(',');
            row.Append(Num(o.MadeWatts)).Append(',');
            row.Append(Num(o.VentedWatts)).Append(',');
            row.Append(Num(o.RadiationWatts)).Append(',');
            row.Append(Num(o.ConvectionWatts)).Append(',');
            row.Append(Num(o.SolarWatts)).Append(',');
            row.Append(Num(o.FrictionWatts)).Append(',');
            row.Append(Num(o.GenerationWatts)).Append(',');
            row.Append(Num(o.SubstepsDemanded)).Append(',');
            row.Append(o.SubstepsGranted).Append(',');
            row.Append(Text(o.HottestBlock)).Append(',');
            row.Append(Num(o.SecondsToFirstLoss)).Append(',');
            row.Append(o.Links).Append(',');
            row.Append(o.SubstepCost).Append(',');
            row.Append(Num(o.RunSeconds)).Append(',');
            row.Append(o.SubstepsPerBlockCap).Append(',');
            row.Append(o.FlooredNodes);
            return row.ToString();
        }

        /// <summary>Records a whole matrix of outcomes under one walk's name.</summary>
        public static void Outcomes(string walk, List<ScenarioOutcome> outcomes)
        {
            if (!On) return;

            List<string> rows = new List<string>(outcomes.Count);
            foreach (ScenarioOutcome outcome in outcomes) rows.Add(Row(walk, outcome));

            Write("outcomes", OutcomeHeader, rows);
        }

        // ---- what a ship is, before anything is stepped --------------------------------------

        public const string ShipHeader =
            "ship,workshop_id,path,large,blocks,nodes,grids,joints,rooms,sealed_blocks,"
            + "stepped,accounted";

        public static string ShipRow(Blueprints.Ship ship, int nodes, int joints, int rooms,
            long sealedBlocks, bool stepped, bool accounted)
        {
            StringBuilder row = new StringBuilder();
            row.Append(Text(ship.Name)).Append(',');
            row.Append(ship.WorkshopId.ToString(CultureInfo.InvariantCulture)).Append(',');
            row.Append(Text(ship.Path)).Append(',');
            row.Append(ship.Large ? 1 : 0).Append(',');
            row.Append(ship.Blocks).Append(',');
            row.Append(nodes).Append(',');
            row.Append(ship.Grids.Count).Append(',');
            row.Append(joints).Append(',');
            row.Append(rooms).Append(',');
            row.Append(sealedBlocks).Append(',');
            row.Append(stepped ? 1 : 0).Append(',');
            row.Append(accounted ? 1 : 0);
            return row.ToString();
        }
    }
}
