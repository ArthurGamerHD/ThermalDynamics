using System;
using System.Collections.Generic;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The blueprint corpus, read once and shared by every test that wants it.
    ///
    /// <para>
    /// **Opt-in.** These tests read thousands of other people's ships off a corpus that is
    /// gigabytes, lives outside the repository and is fetched rather than authored, and they take
    /// minutes. A suite that everyone runs on every change cannot depend on any of that, so they
    /// stand down unless <c>THERMAL_CORPUS_TESTS</c> is set — the same shape as the guards that
    /// stand down without a game install.
    /// </para>
    ///
    /// <code>
    ///     THERMAL_CORPUS_TESTS=1 dotnet test --filter LabInvariantTests
    /// </code>
    ///
    /// <para>
    /// **Once.** Three classes wanted the corpus and each scanned it for itself, one of them once
    /// per test method: seven full recursive walks and re-parses of a corpus that runs to tens of
    /// gigabytes, to run a handful of ships. That cost is why the samples downstream are as small
    /// as they are, so it is paid here once and the ships handed out afterwards.
    /// </para>
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

        private static List<string> Find()
        {
            if (Environment.GetEnvironmentVariable("THERMAL_CORPUS_TESTS") == null)
            {
                return new List<string>();
            }

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
        /// The ships a corpus test should run: **every one of them**, unless told otherwise.
        ///
        /// <para>
        /// A corpus exists to replace "one ship is one ship" with a population, and a test that
        /// takes a dozen off the front of it has quietly gone back to the hypothesis. Worse, the
        /// list is sorted largest first, so any fixed slice is an extreme of the distribution — the
        /// twelve biggest hulls, or the two smallest — and which ships those are moves every time
        /// the corpus grows. So the default is the whole thing.
        /// </para>
        ///
        /// <para>
        /// <c>THERMAL_CORPUS_SHIPS</c> caps it for a quick pass. The cap is a stride rather than a
        /// prefix, so a capped run is still spread from the largest ship to the smallest instead of
        /// being all capital hulls.
        /// </para>
        /// </summary>
        /// <summary>
        /// Files to skip from the front, for resuming a sweep that was interrupted.
        ///
        /// <para>
        /// A pass over ten thousand ships runs for hours, and an interruption at hour eight — a
        /// hang detector firing on a test that is merely slow, a machine rebooted, a decision to
        /// change something — should not mean re-simulating everything already measured. The
        /// dataset is written per ship, so the rows survive; this is what lets the next run pick up
        /// where the last one stopped. The corpus order is deterministic (sorted largest first), so
        /// a skip of N means exactly the N files already done.
        /// </para>
        /// </summary>
        private static int Skip()
        {
            int skip;
            string configured = Environment.GetEnvironmentVariable("THERMAL_CORPUS_SKIP");
            return !string.IsNullOrEmpty(configured)
                && int.TryParse(configured, out skip) && skip > 0 ? skip : 0;
        }

        public static List<string> Sample()
        {
            List<string> corpus = Files();

            int skip = Skip();
            if (skip > 0)
            {
                if (skip >= corpus.Count) return new List<string>();
                corpus = corpus.GetRange(skip, corpus.Count - skip);
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
        /// Ships per batch when a test walks the whole corpus.
        ///
        /// Every batch is a barrier — the next one cannot start until the slowest ship in this one
        /// has settled — so a small batch pays that cost often and leaves workers idle waiting for
        /// one capital hull. Large enough to keep thirty-odd workers fed, small enough that what is
        /// alive at once is a few gigabytes rather than the sixty-eight the corpus weighs.
        /// </summary>
        public const int BatchSize = 120;

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

        /// <summary>A file above this competes for a parse slot instead of being read immediately.</summary>
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
        /// barrier**.
        ///
        /// <para>
        /// The batched shape read a hundred and twenty ships, ran them all, and waited for the
        /// slowest before reading the next hundred and twenty. Ships differ in cost by three orders
        /// of magnitude, so most of the machine spent its time at that barrier waiting for one
        /// capital hull: measured mid-run, ten threads of sixty-three were busy and the sweep was
        /// using thirty per cent of the box. Here a worker that finishes a ship takes the next one
        /// immediately, so nothing waits for anything.
        /// </para>
        ///
        /// <para>
        /// **It also removes the re-parsing.** <see cref="BatteryLab"/> hands each ship-and-scenario
        /// pair to a worker and calls <c>Reload()</c> — a full re-read of the blueprint from disk —
        /// because those workers would otherwise share one ship's block instances and write their
        /// loads onto each other. Here the ship *is* the unit of work: one worker owns it, runs
        /// whatever scenarios the caller asks for in sequence, and lets it go. Nothing is shared, so
        /// nothing has to be re-read. That is one parse per ship in place of one per ship per
        /// scenario.
        /// </para>
        ///
        /// <para>
        /// Memory is bounded by the ships in flight rather than by a batch, which is strictly less
        /// than before. The heaviest files still queue for a slot, because the corpus is walked
        /// largest first and thirty workers each opening a quarter-gigabyte blueprint at the same
        /// moment is how the earlier runs died.
        /// </para>
        /// </summary>
        public static List<T> Sweep<T>(string label, Func<Blueprints.Ship, T> work) where T : class
        {
            List<string> paths = Sample();
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
        /// Source XML a batch may hold before it is closed early, whatever the ship count.
        ///
        /// <para>
        /// A count alone is not a bound on anything that matters. The corpus holds one blueprint of
        /// 1.85 GB and a median of 1 MB, and an XML document costs several times its file on the
        /// heap — so a hundred and twenty of the largest ships is not a large batch, it is an
        /// impossible one, and reading them across thirty-one workers at once is how a run finds
        /// the memory ceiling and dies there. Sorted largest-first, that batch is the *first* one.
        /// A byte budget is the bound the count was pretending to be.
        /// </para>
        /// </summary>
        public const long BatchBytes = 768L * 1024L * 1024L;

        /// <summary>A batch above this competes for a parse slot instead of reading immediately.</summary>
        private const long HeavyBatchBytes = 256L * 1024L * 1024L;

        /// <summary>
        /// How many heavy batches may be parsed at once, across every walk in the run.
        ///
        /// <para>
        /// **The walks are in lockstep and that is the danger.** Each one is its own test class so
        /// they start together, and each reads the same corpus in the same largest-first order — so
        /// at the moment the run begins, all of them reach for the same 1.85 GB blueprint at the
        /// same time. An XML document costs several times its file on the heap, so eight concurrent
        /// reads of that one file want more memory than the machine has. Measured: 27 GB inside the
        /// first minute, still climbing, before anything had been simulated.
        /// </para>
        ///
        /// <para>
        /// The batch byte budget bounds one walk. This bounds their sum, which is the quantity that
        /// actually has to fit. Only heavy batches queue — the median blueprint is a megabyte, so
        /// once the walks are past the giants nothing waits here at all.
        /// </para>
        /// </summary>
        private static readonly System.Threading.SemaphoreSlim ParseSlots =
            new System.Threading.SemaphoreSlim(4, 4);

        /// <summary>
        /// The sample cut into batches, read lazily.
        ///
        /// <para>
        /// Handing five thousand ships to the lab in one call would build five thousand assemblies
        /// across every worker at once, and the corpus runs to tens of gigabytes of blueprints. An
        /// uncapped sweep of it has already taken a machine down. Batching bounds what is alive at
        /// any moment to the batch rather than to the corpus, at no cost to coverage — every ship
        /// is still run, and the lab still fans each batch out across its workers.
        /// </para>
        ///
        /// <param name="label">Names this walk in the progress file, so concurrent tests can be
        /// told apart.</param>
        /// </summary>
        public static IEnumerable<List<Blueprints.Ship>> Batches(string label)
        {
            List<string> sample = Sample();
            // An upper bound rather than a count: a batch also closes when it reaches its byte
            // budget, so the real number is this or more. Progress reads as conservative.
            int batches = (sample.Count + BatchSize - 1) / BatchSize;
            int batchNumber = 0;
            int seen = 0;

            // The definition table is built under a lock on first use. Warming it here means the
            // workers below find it built rather than all queueing on the same lock.
            GameBlocks.BySubtype();

            int i = 0;
            while (i < sample.Count)
            {
                // Whichever bound is reached first: the ship count, or the bytes. A batch always
                // takes at least one file, so a single blueprint larger than the whole budget is
                // read on its own rather than not at all.
                List<string> paths = new List<string>();
                long bytes = 0L;

                while (i < sample.Count && paths.Count < BatchSize)
                {
                    long length = Length(sample[i]);
                    if (paths.Count > 0 && bytes + length > BatchBytes) break;

                    paths.Add(sample[i]);
                    bytes += length;
                    i++;
                }

                // **Read the batch across every worker.** Parsing was the serial half of this
                // loop: one thread reading twenty-five blueprints while thirty-one cores waited,
                // then thirty-one cores stepping them while the reader waited. Measured mid-run,
                // the whole sweep was drawing nine cores of thirty-two. Reading is per-file and
                // shares nothing, so it fans out exactly as the stepping does.
                bool heavy = bytes > HeavyBatchBytes;
                if (heavy) ParseSlots.Wait();

                List<List<Blueprints.Ship>> read;
                try
                {
                    read = LabRun.Map(paths, Blueprints.Read, LabMode.Parallel);
                }
                finally
                {
                    if (heavy) ParseSlots.Release();
                }

                List<Blueprints.Ship> batch = new List<Blueprints.Ship>();
                foreach (List<Blueprints.Ship> ships in read)
                {
                    foreach (Blueprints.Ship ship in ships)
                    {
                        // The same two filters the corpus scan applies: a ship with one unresolved
                        // subtype is not a measurement of vanilla balance, and anything under the
                        // floor is a cockpit or a door rather than a design.
                        if (!ship.IsVanilla) continue;
                        if (ship.Blocks < CorpusLab.MinimumBlocks) continue;

                        batch.Add(ship);
                    }
                }

                seen += batch.Count;
                Report(label, ++batchNumber, batches, seen, i);

                if (batch.Count > 0) yield return batch;

                // Lazily, so the caller's batch is collectable before the next one is read. A
                // method that built every batch up front would hold the whole corpus again by a
                // longer route.
            }
        }
    }
}
