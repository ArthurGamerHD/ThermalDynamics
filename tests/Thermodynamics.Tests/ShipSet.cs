using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// A committed list of corpus ships, read and resolved to blueprints — and the resume record a
    /// sweep over one keeps.
    ///
    /// <para>
    /// **There are two such lists and they answer different questions**: `panel.csv` is cut for
    /// spread because a dial sweep needs the largest lever it can find, and `typical.csv` is cut for
    /// the middle because a regression asks whether a change broke the ships people fly. What they
    /// share is everything mechanical — the same header shape, the same quoting, the same need to
    /// resolve a name and a workshop id to one blueprint — and that plumbing existed three times
    /// before this class, once per walk, which is the drift `D3` exists to prevent.
    /// </para>
    ///
    /// <para>
    /// **The paths matter more than they look.** Each list carries the blueprint each ship was
    /// measured from. Matching on name and id by walking the corpus instead means fully parsing all
    /// 9,981 blueprints — every worker free to open a quarter-gigabyte file at once — to find forty
    /// ships, which is exactly how earlier runs died.
    /// </para>
    /// </summary>
    internal static class ShipSet
    {
        /// <summary>One row of a committed set: the three columns every one of them starts with.</summary>
        public class Entry
        {
            public string Name;
            public string WorkshopId;

            /// <summary>The blueprint it was measured from, so a walk need not go looking.</summary>
            public string Path;

            /// <summary>The whole row, for a walk that reads further columns.</summary>
            public string[] Fields;
        }

        /// <summary>
        /// Reads a set. <paramref name="variable"/> names an environment variable that overrides
        /// <paramref name="fallback"/>, so one walk can be pointed at the other list without a code
        /// change.
        /// </summary>
        public static List<Entry> Read(string variable, string fallback)
        {
            List<Entry> entries = new List<Entry>();

            string path = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(path)) path = Find(fallback);
            if (path == null || !File.Exists(path)) return entries;

            string[] lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;

                List<string> fields = Split(lines[i]);
                if (fields.Count < 3) continue;

                entries.Add(new Entry
                {
                    Name = fields[0],
                    WorkshopId = fields[1],
                    Path = fields[2],
                    Fields = fields.ToArray(),
                });
            }

            return entries;
        }

        /// <summary>
        /// Resolves a set to ships, one blueprint each, matched on name and workshop id **together**
        /// — one blueprint can hold several ships and the corpus holds ships whose names collide, so
        /// the census and the survey join on the same pair.
        /// </summary>
        public static List<Blueprints.Ship> Load(List<Entry> entries, Action<string> progress)
        {
            List<Blueprints.Ship> found = new List<Blueprints.Ship>();
            object gate = new object();
            int unresolved = 0;

            GameBlocks.BySubtype();

            System.Threading.Tasks.ParallelOptions options =
                new System.Threading.Tasks.ParallelOptions
                { MaxDegreeOfParallelism = LabRun.Workers };

            System.Threading.Tasks.Parallel.ForEach(
                System.Collections.Concurrent.Partitioner.Create(0, entries.Count, 1),
                options,
                range =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        Entry wanted = entries[i];

                        if (string.IsNullOrEmpty(wanted.Path) || !File.Exists(wanted.Path))
                        {
                            lock (gate) unresolved++;
                            continue;
                        }

                        List<Blueprints.Ship> read;
                        try { read = Blueprints.Read(wanted.Path); }
                        catch { lock (gate) unresolved++; continue; }

                        bool matched = false;
                        foreach (Blueprints.Ship ship in read)
                        {
                            if (ship.Name != wanted.Name) continue;
                            if (ship.WorkshopId.ToString(CultureInfo.InvariantCulture)
                                != wanted.WorkshopId) continue;

                            lock (gate) found.Add(ship);
                            matched = true;
                            break;
                        }

                        if (!matched) lock (gate) unresolved++;
                    }
                });

            if (progress != null)
            {
                progress("set resolved " + found.Count + " of " + entries.Count + " ships, "
                    + unresolved + " unresolved");
            }

            return found;
        }

        /// <summary>Splits one CSV line, honouring the quoting <c>CorpusRecord.Text</c> writes.</summary>
        public static List<string> Split(string line)
        {
            return CsvLine.Split(line);
        }

        /// <summary>
        /// A repository-relative path.
        ///
        /// **Walking up from the assembly does not work.** `Directory.Build.props` sends build
        /// output to a sibling `ThermalDynamics.build/` so that artifacts never land in the
        /// published mod folder, so no ancestor of the running assembly is the repository. A first
        /// version of this walked up ten levels, found nothing, and a sweep returned green in 355 ms
        /// having measured exactly nothing. <see cref="ShippedBlocks.RepoRoot"/> already handles the
        /// relocated case by falling back to this source file's compiled-in path.
        /// </summary>
        public static string Find(string relative)
        {
            try
            {
                string candidate = Path.Combine(ShippedBlocks.RepoRoot(), relative);
                return File.Exists(candidate) ? candidate : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// What a sweep has already finished, kept beside its data.
        ///
        /// <para>
        /// A sweep is hours long and dies for reasons that have nothing to do with it (`O3`), so
        /// resuming is not a convenience. The record is exact because a mark is written *after* the
        /// rows it stands for — a mark that landed first would lose them. One file per sweep: two
        /// sweeps share a corpus and not a state, and one that inherited another's record would skip
        /// everything and report success.
        /// </para>
        /// </summary>
        public class Resume
        {
            private readonly string label;

            public Resume(string label)
            {
                this.label = label;
            }

            private string PathOrNull()
            {
                string directory = CorpusRecord.Directory();
                return directory == null ? null : Path.Combine(directory, "done-" + label + ".txt");
            }

            /// <summary>Marks an earlier run of this sweep finished with, read fresh.</summary>
            public HashSet<string> Done()
            {
                HashSet<string> done = new HashSet<string>(StringComparer.Ordinal);

                string path = PathOrNull();
                if (path == null || !File.Exists(path)) return done;

                try
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.Length > 0) done.Add(trimmed);
                    }
                }
                catch (IOException)
                {
                    // A resume that cannot read its own record starts over, which is slow and right.
                }

                return done;
            }

            /// <summary>Records one unit of work as finished. Call with the write lock held.</summary>
            public void Mark(string mark)
            {
                string path = PathOrNull();
                if (path == null) return;

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.AppendAllText(path, mark + Environment.NewLine);
                }
                catch (IOException)
                {
                    // Failing to record a finished ship costs a re-run, not a result.
                }
            }
        }

        /// <summary>Appends one line to the progress file, when a run named one.</summary>
        public static void Progress(string sweep, string line)
        {
            string path = Environment.GetEnvironmentVariable("THERMAL_CORPUS_PROGRESS");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                File.AppendAllText(path,
                    DateTime.Now.ToString("HH:mm:ss") + " " + sweep + " " + line
                    + Environment.NewLine);
            }
            catch
            {
                // Progress reporting must never be the reason a run fails.
            }
        }
    }
}
