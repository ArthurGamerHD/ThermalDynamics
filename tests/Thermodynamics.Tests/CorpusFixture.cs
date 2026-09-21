using System;
using System.Collections.Generic;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    internal static class CorpusFixture
    {
/// <summary>object operation.</summary>
        private static readonly object Gate = new object();
        private static List<string> cached;

/// <summary>Files operation.</summary>
        public static List<string> Files()
        {
            lock (Gate)
            {
                if (cached != null) return cached;

/// <summary>Find operation.</summary>
                cached = Find();
                return cached;
            }
        }

/// <summary>OptedIn operation.</summary>
        public static bool OptedIn(string configured)
        {
            if (string.IsNullOrEmpty(configured)) return false;

            string value = configured.Trim().ToLowerInvariant();
            return value != "0" && value != "no" && value != "off" && value != "false";
        }

/// <summary>OptedIn operation.</summary>
        public static bool OptedIn()
        {
            return OptedIn(Environment.GetEnvironmentVariable("THERMAL_CORPUS_TESTS"));
        }

/// <summary>Find operation.</summary>
        private static List<string> Find()
        {
            if (!OptedIn()) return new List<string>();

            if (!GameBlocks.IsInstalled) return new List<string>();

            string root = Blueprints.DefaultPath();
            if (root == null) return new List<string>();

            List<string> files = Blueprints.Files(root);

            files.Sort(delegate (string a, string b)
            {
                return Length(b).CompareTo(Length(a));
            });

/// <summary>Ceiling operation.</summary>
            long ceiling = Ceiling();
/// <summary>List operation.</summary>
            List<string> oversized = new List<string>();

            for (int i = files.Count - 1; i >= 0; i--)
            {
                if (Length(files[i]) <= ceiling) continue;

                oversized.Add(files[i]);
                files.RemoveAt(i);
            }

            excluded = oversized;
            if (oversized.Count > 0)
            {
                Note("corpus excluded " + oversized.Count + " oversized blueprints");
                foreach (string file in oversized)
                {
                    Note("excluded " + System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(file))
                        + " at " + (Length(file) / (1024L * 1024L)) + " MB");
                }
            }

            Assert.True(files.Count > 0,
                "the corpus at " + root + " holds no blueprint files.");

            return files;
        }

/// <summary>DonePath operation.</summary>
        private static string DonePath(string label)
        {
            string directory = CorpusRecord.Directory();
            if (directory == null || string.IsNullOrEmpty(label)) return null;

            return System.IO.Path.Combine(directory, "done-" + label + ".txt");
        }

/// <summary>Done operation.</summary>
        private static HashSet<string> Done(string label)
        {
/// <summary>HashSet operation.</summary>
            HashSet<string> done = new HashSet<string>(StringComparer.Ordinal);

/// <summary>DonePath operation.</summary>
            string path = DonePath(label);
            if (path == null || !System.IO.File.Exists(path)) return done;

            try
            {
                foreach (string line in System.IO.File.ReadAllLines(path))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length > 0) done.Add(trimmed);
                }
            }
            catch (System.IO.IOException)
            {
            }

            return done;
        }

/// <summary>Remaining operation.</summary>
        public static List<string> Remaining(IList<string> corpus, ICollection<string> done)
        {
/// <summary>List operation.</summary>
            List<string> remaining = new List<string>(corpus.Count);

            for (int i = 0; i < corpus.Count; i++)
            {
                if (done == null || !done.Contains(corpus[i])) remaining.Add(corpus[i]);
            }

            return remaining;
        }

/// <summary>MarkDone operation.</summary>
        private static void MarkDone(string label, string blueprint)
        {
/// <summary>DonePath operation.</summary>
            string path = DonePath(label);
            if (path == null) return;

            try
            {
                System.IO.File.AppendAllText(path, blueprint + System.Environment.NewLine);
            }
            catch (System.IO.IOException)
            {
            }
        }

/// <summary>Sample operation.</summary>
        public static List<string> Sample(string label = null)
        {
/// <summary>Files operation.</summary>
            List<string> corpus = Files();

/// <summary>Only operation.</summary>
            corpus = Only(corpus);

/// <summary>Done operation.</summary>
            HashSet<string> done = Done(label);
            if (done.Count > 0)
            {
/// <summary>Remaining operation.</summary>
                List<string> remaining = Remaining(corpus, done);
                Note(label + ": resuming, " + done.Count + " blueprints already finished and "
                    + remaining.Count + " to go");
                corpus = remaining;
            }

            int cap;
            string configured = Environment.GetEnvironmentVariable("THERMAL_CORPUS_SHIPS");
            if (string.IsNullOrEmpty(configured) || !int.TryParse(configured, out cap) || cap <= 0)
            {
                return corpus;
            }

            if (cap >= corpus.Count) return corpus;

/// <summary>List operation.</summary>
            List<string> sample = new List<string>();
            int stride = corpus.Count / cap;
            if (stride < 1) stride = 1;

            for (int i = 0; i < corpus.Count && sample.Count < cap; i += stride)
            {
                sample.Add(corpus[i]);
            }

            return sample;
        }

/// <summary>Only operation.</summary>
        public static List<string> Only(List<string> corpus)
        {
            string path = Environment.GetEnvironmentVariable("THERMAL_CORPUS_ONLY");
            if (string.IsNullOrEmpty(path)) return corpus;

            Assert.True(System.IO.File.Exists(path),
                "THERMAL_CORPUS_ONLY names " + path + ", which does not exist");

/// <summary>HashSet operation.</summary>
            HashSet<string> wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in System.IO.File.ReadAllLines(path))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;

                wanted.Add(trimmed);
            }

            Assert.True(wanted.Count > 0, "THERMAL_CORPUS_ONLY at " + path + " names no blueprints");

/// <summary>List operation.</summary>
            List<string> chosen = new List<string>();
/// <summary>HashSet operation.</summary>
            HashSet<string> found = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < corpus.Count; i++)
            {
                if (!wanted.Contains(corpus[i])) continue;

                chosen.Add(corpus[i]);
                found.Add(corpus[i]);
            }

            Assert.True(found.Count == wanted.Count,
                "THERMAL_CORPUS_ONLY names " + wanted.Count + " blueprints and the corpus holds "
                + found.Count + " of them, so this list was built against a different corpus");

            Note("walking " + chosen.Count + " blueprints named by " + path);
            return chosen;
        }

/// <summary>Spread operation.</summary>
        public static List<Blueprints.Ship> Spread(int count)
        {
/// <summary>Files operation.</summary>
            List<string> corpus = Files();
/// <summary>List operation.</summary>
            List<Blueprints.Ship> ships = new List<Blueprints.Ship>();
            if (corpus.Count == 0 || count <= 0) return ships;

            GameBlocks.BySubtype();

            int stride = corpus.Count / count;
            if (stride < 1) stride = 1;

/// <summary>List operation.</summary>
            List<string> paths = new List<string>();
            for (int i = 0; i < corpus.Count && paths.Count < count; i += stride) paths.Add(corpus[i]);

            foreach (List<Blueprints.Ship> read in LabRun.Map(paths, Blueprints.Read, LabMode.Parallel))
            {
                foreach (Blueprints.Ship ship in read)
                {
                    if (!ship.IsVanilla) continue;
                    if (ship.Blocks < CorpusLab.MinimumBlocks) continue;
                    ships.Add(ship);
                }
            }

            return ships;
        }
/// <summary>Report operation.</summary>
        private static void Report(string label, int batch, int batches, int ships, int files)
        {
            string path = Environment.GetEnvironmentVariable("THERMAL_CORPUS_PROGRESS");
            if (string.IsNullOrEmpty(path)) return;

            string line = DateTime.Now.ToString("HH:mm:ss") + " " + label + " batch " + batch
                + "/" + batches + " ships " + ships + " files " + files + Environment.NewLine;

            try
            {
                lock (Gate)
                {
                    System.IO.File.AppendAllText(path, line);
                }
            }
            catch
            {
            }
        }

/// <summary>List operation.</summary>
        private static List<string> excluded = new List<string>();

/// <summary>Excluded operation.</summary>
        public static List<string> Excluded()
        {
            Files();
            return excluded;
        }

/// <summary>Ceiling operation.</summary>
        private static long Ceiling()
        {
            int megabytes;
            string configured = Environment.GetEnvironmentVariable("THERMAL_CORPUS_MAX_MB");
            if (string.IsNullOrEmpty(configured) || !int.TryParse(configured, out megabytes))
            {
                megabytes = 256;
            }

            return megabytes <= 0 ? long.MaxValue : megabytes * 1024L * 1024L;
        }

/// <summary>Note operation.</summary>
        private static void Note(string line)
        {
            string path = Environment.GetEnvironmentVariable("THERMAL_CORPUS_PROGRESS");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                lock (Gate)
                {
                    System.IO.File.AppendAllText(path,
                        DateTime.Now.ToString("HH:mm:ss") + " " + line + Environment.NewLine);
                }
            }
            catch
            {
            }
        }

        private const long HeavyFileBytes = 48L * 1024L * 1024L;

        private const int ReportEvery = 10;

/// <summary>Sweep operation.</summary>
        public static List<T> Sweep<T>(string label, Func<Blueprints.Ship, T> work) where T : class
        {
/// <summary>Sample operation.</summary>
            List<string> paths = Sample(label);
/// <summary>List operation.</summary>
            List<T> results = new List<T>();
            if (paths.Count == 0) return results;


            GameBlocks.BySubtype();

            CorpusRecord.Provenance(label);

/// <summary>object operation.</summary>
            object collect = new object();
            int files = 0;
            int ships = 0;

            System.Threading.Tasks.ParallelOptions options =
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = LabRun.Workers };

            System.Threading.Tasks.Parallel.ForEach(
                System.Collections.Concurrent.Partitioner.Create(0, paths.Count, 1),
                options,
                range =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                    {
/// <summary>Visit operation.</summary>
                        List<T> mine = Visit(paths[i], work);

                        lock (collect)
                        {
                            results.AddRange(mine);
                            MarkDone(label, paths[i]);
                            files++;
                            ships += mine.Count;

                            if (files % ReportEvery == 0 || files == paths.Count)
                            {
                                Report(label, files, paths.Count, ships, files);
                            }
                        }
                    }
                });

            Report(label, files, paths.Count, ships, files);
            return results;
        }

/// <summary>Visit operation.</summary>
        private static List<T> Visit<T>(string path, Func<Blueprints.Ship, T> work) where T : class
        {
/// <summary>List operation.</summary>
            List<T> results = new List<T>();

/// <summary>Length operation.</summary>
            bool heavy = Length(path) > HeavyFileBytes;
            if (heavy) ParseSlots.Wait();

            List<Blueprints.Ship> ships;
            try
            {
                ships = Blueprints.Read(path);
            }
            catch
            {
                return results;
            }
            finally
            {
                if (heavy) ParseSlots.Release();
            }

            foreach (Blueprints.Ship ship in ships)
            {
                if (!ship.IsVanilla) continue;
                if (ship.Blocks < CorpusLab.MinimumBlocks) continue;

                try
                {
/// <summary>work operation.</summary>
                    T result = work(ship);
                    if (result != null) results.Add(result);
                }
                catch
                {
                }
            }

            return results;
        }

/// <summary>Length operation.</summary>
        private static long Length(string path)
        {
            try
            {
                return new System.IO.FileInfo(path).Length;
            }
            catch
            {
                return 0L;
            }
        }

        private static readonly System.Threading.SemaphoreSlim ParseSlots =
            new System.Threading.SemaphoreSlim(4, 4);
    }
}
