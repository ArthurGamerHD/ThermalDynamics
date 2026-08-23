using System;
using System.Collections.Generic;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The blueprint corpus, read once and shared by every test that wants it. **Opt-in**, behind
    /// <c>THERMAL_CORPUS_TESTS</c>, because it reads gigabytes that live outside the repository and
    /// takes minutes.
    ///
    /// <code>
    ///     THERMAL_CORPUS_TESTS=1 dotnet test --filter LabInvariantTests
    /// </code>
    /// </summary>
    internal static class CorpusFixture
    {
        private static readonly object Gate = new object();
        private static List<string> cached;

        /// <summary>
        /// Every blueprint file in the corpus, or an empty list when the opt-in was not taken and
        /// there is nothing to read.
        ///
        /// <para>
        /// **Paths, not ships.** A parsed ship carries its whole grid model — every block, every
        /// cell — and the corpus runs to nearly six thousand of them. Holding them all at once is
        /// not a large allocation, it is an impossible one: a scan of the full corpus reached a
        /// sixteen gigabyte ceiling and was killed there. So the list that lives for the length of
        /// the run is the cheap one, and the ships are read a batch at a time and let go.
        /// </para>
        /// </summary>
        public static List<string> Files()
        {
            lock (Gate)
            {
                if (cached != null) return cached;

                cached = Find();
                return cached;
            }
        }

        /// <summary>
        /// Whether the corpus walks are opted in to.
        ///
        /// <para>
        /// **Off has to be spellable** (`C8`, `P8`). Testing the variable against null read an
        /// empty value as opted *in*, because on Linux `THERMAL_CORPUS_TESTS=` is a variable that
        /// exists and holds nothing — so the obvious way to write "off" turned every corpus walk
        /// on. The values a person reaches for when they mean off are all off here, and anything
        /// else is on.
        /// </para>
        /// </summary>
        public static bool OptedIn(string configured)
        {
            if (string.IsNullOrEmpty(configured)) return false;

            string value = configured.Trim().ToLowerInvariant();
            return value != "0" && value != "no" && value != "off" && value != "false";
        }

        /// <summary>Whether this process is opted in to the corpus walks.</summary>
        public static bool OptedIn()
        {
            return OptedIn(Environment.GetEnvironmentVariable("THERMAL_CORPUS_TESTS"));
        }

        private static List<string> Find()
        {
            if (!OptedIn()) return new List<string>();

            if (!GameBlocks.IsInstalled) return new List<string>();

            string root = Blueprints.DefaultPath();
            if (root == null) return new List<string>();

            List<string> files = Blueprints.Files(root);

            // **Biggest first.** Two things turn on this and both were wrong before.
            //
            // The obvious one is scheduling: batches are barriers, and a batch holding one capital
            // hull and a hundred and nineteen fighters costs what the capital hull costs while the
            // workers that finished the fighters wait. Sorting by size makes the ships within a
            // batch alike, so the slowest is close to the average and almost nothing is wasted
            // waiting. Largest first also means the longest jobs start earliest, which is the one
            // ordering that reliably shortens the tail.
            //
            // The other is that this list was in whatever order the filesystem returned it, while
            // the code downstream said in comments that it was sorted largest first and took
            // strides through it believing it was sampling across the size range. It was sampling
            // arbitrarily. CorpusLab does sort, but nothing here ever called CorpusLab.
            files.Sort(delegate (string a, string b)
            {
                return Length(b).CompareTo(Length(a));
            });

            // **The megastructures come out, and they are named on the way.**
            //
            // The size distribution has a tail that is not a ship: a median blueprint is a
            // megabyte, and eight of the ten thousand are over a quarter of a gigabyte, one of them
            // 1.85 GB. An XML document costs several times its file on the heap, so a single one of
            // those wants more memory than most machines have — and with every walk reading the
            // corpus in the same order, they all reach it together. Two runs died on exactly this.
            //
            // Dropping eight files loses 0.08 % of the population and about a tenth of the bytes.
            // Keeping them costs the other 9,981. The ceiling is a setting rather than a constant
            // so a machine with room can raise it, and the excluded files are recorded rather than
            // quietly skipped, because a corpus that silently is not the population it claims to be
            // is the failure this whole class exists to prevent.
            long ceiling = Ceiling();
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

            // Past this point the opt-in was taken, the game is installed and a corpus directory
            // was found, so a corpus with nothing in it is a fault rather than a reason to stand
            // down. Every caller returns quietly on an empty list, which is the right behaviour
            // for a machine that has no corpus and a silent catastrophe for one that does.
            Assert.True(files.Count > 0,
                "the corpus at " + root + " holds no blueprint files.");

            return files;
        }

        /// <summary>
        /// The file a sweep records its finished blueprints in, or null when it is not recording.
        ///
        /// A sweep is hours long and dies for reasons that have nothing to do with it, so resuming
        /// is not a convenience (`O3`). This is what makes the resume *exact*: a file is written
        /// here when <see cref="Visit"/> has returned for it, which is after every ship in it has
        /// been handed to the caller and recorded, so a path present here is a path with nothing
        /// left to do.
        /// </summary>
        private static string DonePath(string label)
        {
            string directory = CorpusRecord.Directory();
            if (directory == null || string.IsNullOrEmpty(label)) return null;

            // One record per walk. Two walks share a corpus and not a state: a determinism pass
            // that inherited the survey's record would skip the whole corpus and report success.
            return System.IO.Path.Combine(directory, "done-" + label + ".txt");
        }

        /// <summary>
        /// Blueprints an earlier run of this sweep finished, read once.
        ///
        /// **This replaces a skip counted in files.** The count was supplied by hand off a progress
        /// line, and the two did not mean the same thing: files were counted as they were handed to
        /// a batch while rows were written as each ship finished, so a resume both re-emitted the
        /// ships of the interrupted batch and lost the ones it had not reached. The shipped
        /// 2026-08-21 dataset carries fifty duplicate rows from exactly that. Nothing is counted
        /// here — a path is either finished or it is not.
        /// </summary>
        private static HashSet<string> Done(string label)
        {
            HashSet<string> done = new HashSet<string>(StringComparer.Ordinal);

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
                // A resume that cannot read its own record starts over, which is slow and correct.
            }

            return done;
        }

        /// <summary>
        /// What is left of a corpus once the finished blueprints are taken out. Order is kept, so a
        /// resumed run still reads largest first and its batches are still alike.
        /// </summary>
        public static List<string> Remaining(IList<string> corpus, ICollection<string> done)
        {
            List<string> remaining = new List<string>(corpus.Count);

            for (int i = 0; i < corpus.Count; i++)
            {
                if (done == null || !done.Contains(corpus[i])) remaining.Add(corpus[i]);
            }

            return remaining;
        }

        /// <summary>Records that a blueprint is finished. Called with the collect lock held.</summary>
        private static void MarkDone(string label, string blueprint)
        {
            string path = DonePath(label);
            if (path == null) return;

            try
            {
                System.IO.File.AppendAllText(path, blueprint + System.Environment.NewLine);
            }
            catch (System.IO.IOException)
            {
                // Losing a line costs one blueprint of rework on the next resume, which is not a
                // reason to lose the run.
            }
        }

        /// <summary>
        /// The blueprints a walk should read, with anything an earlier run of that same walk
        /// already finished taken out. A walk with no name resumes nothing, because there is
        /// nothing to tell its record apart from another walk's.
        /// </summary>
        public static List<string> Sample(string label = null)
        {
            List<string> corpus = Files();

            HashSet<string> done = Done(label);
            if (done.Count > 0)
            {
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

            List<string> sample = new List<string>();
            int stride = corpus.Count / cap;
            if (stride < 1) stride = 1;

            for (int i = 0; i < corpus.Count && sample.Count < cap; i += stride)
            {
                sample.Add(corpus[i]);
            }

            return sample;
        }

        /// <summary>
        /// One batch of ships spread across the corpus, for the tests that must not walk all of it.
        ///
        /// <para>
        /// Almost every corpus test should see every ship. The exception is a test whose reference
        /// side is serial by construction — comparing the parallel lab against the linear one means
        /// running the linear one, and that is one core no matter how many the machine has. A
        /// population makes such a test slower without making it stronger: what it needs is enough
        /// ships in flight at once to provoke a collision, and that is a batch, not a corpus.
        /// </para>
        /// </summary>
        public static List<Blueprints.Ship> Spread(int count)
        {
            List<string> corpus = Files();
            List<Blueprints.Ship> ships = new List<Blueprints.Ship>();
            if (corpus.Count == 0 || count <= 0) return ships;

            GameBlocks.BySubtype();

            int stride = corpus.Count / count;
            if (stride < 1) stride = 1;

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
        /// <summary>
        /// Where a walk of the corpus reports how far it has got, when
        /// <c>THERMAL_CORPUS_PROGRESS</c> names a file.
        ///
        /// <para>
        /// A pass over ten thousand ships runs for hours and the test runner says nothing until a
        /// test ends, so from the outside a working run and a wedged one look identical. Each batch
        /// appends a line as it is handed over: which walk, how far through, and how many ships it
        /// has seen. Off unless the variable is set, so the ordinary suite writes nothing.
        /// </para>
        /// </summary>
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
                // Progress reporting must never be the reason a run fails.
            }
        }

        private static List<string> excluded = new List<string>();

        /// <summary>
        /// Blueprints left out for being larger than the harness can hold, largest first. Empty
        /// until the corpus has been listed.
        /// </summary>
        public static List<string> Excluded()
        {
            Files();
            return excluded;
        }

        /// <summary>
        /// The largest blueprint the walk will read, in bytes. <c>THERMAL_CORPUS_MAX_MB</c>
        /// overrides it; zero or less means no ceiling at all.
        /// </summary>
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

        /// <summary>Writes one line to the progress file, when there is one.</summary>
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

        /// <summary>
        /// A file above this competes for a parse slot instead of being read immediately.
        ///
        /// **This is the whole of what bounds a sweep's memory**, and it is a bound on bytes rather
        /// than on a count because a count is not a bound on anything that matters: the corpus
        /// holds one blueprint of 1.85 GB against a median of 1 MB, and an XML document costs
        /// several times its file on the heap. What is alive at once is four heavy files and the
        /// light ones the workers hold, which is a few gigabytes rather than the sixty-eight the
        /// corpus weighs.
        /// </summary>
        private const long HeavyFileBytes = 48L * 1024L * 1024L;

        /// <summary>
        /// Files between progress lines.
        ///
        /// Small, because the corpus is walked largest first: a hundred would mean silence through
        /// the most expensive stretch of the run, which is exactly the stretch someone watching it
        /// needs to see moving.
        /// </summary>
        private const int ReportEvery = 10;

        /// <summary>
        /// Every ship in the corpus, one at a time, across every worker — **no batches and no
        /// barrier**, since ships differ in cost by three orders of magnitude and a barrier leaves the
        /// machine waiting on one capital hull. The ship is the unit of work, so one worker owns it and
        /// nothing has to be re-parsed per scenario (`M3`). The heaviest files still queue for a parse
        /// slot. See balance-lab.md, Running the lab.
        /// </summary>
        public static List<T> Sweep<T>(string label, Func<Blueprints.Ship, T> work) where T : class
        {
            List<string> paths = Sample(label);
            List<T> results = new List<T>();
            if (paths.Count == 0) return results;

            GameBlocks.BySubtype();

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

        /// <summary>Reads one file and runs the caller's work over the usable ships in it.</summary>
        private static List<T> Visit<T>(string path, Func<Blueprints.Ship, T> work) where T : class
        {
            List<T> results = new List<T>();

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
                // The same two filters the corpus scan applies: a ship with one unresolved subtype
                // is not a measurement of vanilla balance, and anything under the floor is a
                // cockpit or a door rather than a design.
                if (!ship.IsVanilla) continue;
                if (ship.Blocks < CorpusLab.MinimumBlocks) continue;

                try
                {
                    T result = work(ship);
                    if (result != null) results.Add(result);
                }
                catch
                {
                    // A ship this model cannot run must not lose the pass, exactly as LabRun.Map
                    // has always treated one.
                }
            }

            return results;
        }

        /// <summary>Bytes on disk, or zero for a file that cannot be measured.</summary>
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

        /// <summary>
        /// How many heavy blueprints may be parsed at once **across every walk in the run**, which
        /// is the quantity that has to fit: the walks start together and read the same corpus
        /// largest-first, so all of them reach for the same 1.85 GB blueprint at once. Only a heavy
        /// file queues, and a light one is read the moment a worker reaches it (`O1`, `O5`).
        /// </summary>
        private static readonly System.Threading.SemaphoreSlim ParseSlots =
            new System.Threading.SemaphoreSlim(4, 4);
    }
}
