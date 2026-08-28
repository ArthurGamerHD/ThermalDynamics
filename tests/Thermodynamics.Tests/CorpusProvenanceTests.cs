using System;
using System.IO;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **A dataset records the build it was collected on**, because the alternative was found out
    /// the expensive way: the 2026-08-24 air walk finished one minute after a commit that moved
    /// twenty-seven waste fractions, and establishing that took comparing its rows against a later
    /// walk and then reading the git log for the window between them.
    /// </summary>
    public class CorpusProvenanceTests
    {
        /// <summary>
        /// The record is written, names the walk, and carries a commit and both definition hashes.
        ///
        /// It runs against a temporary directory rather than a real dataset, so it exercises the
        /// writer without the corpus opt-in and without touching anything a walk owns.
        /// </summary>
        [Fact]
        public void AWalkWritesWhatBuildItRanOn()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "thermal-provenance-" + Guid.NewGuid().ToString("n"));

            Directory.CreateDirectory(directory);
            string previous = Environment.GetEnvironmentVariable("THERMAL_CORPUS_DATA");

            try
            {
                Environment.SetEnvironmentVariable("THERMAL_CORPUS_DATA", directory);
                CorpusRecord.Provenance("a-walk-that-does-not-exist");

                string path = Path.Combine(directory, "provenance.txt");
                Assert.True(File.Exists(path), "no provenance written to " + path);

                string text = File.ReadAllText(path);
                Assert.Contains("walk a-walk-that-does-not-exist", text);
                Assert.Contains("commit ", text);
                Assert.Contains("Cubes.xml ", text);

                // **The commit has to be a commit.** `unknown` is the honest answer when `.git` is
                // unreadable and a silent one when the reader is wrong, so this repository — which
                // has a `.git` — is where the difference shows.
                foreach (string line in text.Split('\n'))
                {
                    if (!line.StartsWith("commit ", StringComparison.Ordinal)) continue;

                    string hash = line.Substring("commit ".Length).Trim();
                    Assert.True(hash.Length == 40,
                        "the commit reads '" + hash + "', which is not a hash");
                }

                // And the definition hash is a hash of something that is there.
                Assert.DoesNotContain("Cubes.xml missing", text);
                Assert.DoesNotContain("Cubes.xml unreadable", text);

                // **Once per walk, not once per batch.** A second call is a no-op, or a dataset
                // gains a block of provenance for every batch it writes.
                CorpusRecord.Provenance("a-walk-that-does-not-exist");
                Assert.Equal(text, File.ReadAllText(path));

                // **And a resumed walk appends rather than overwrites**, which is a different
                // claim: the guard above is per process, and a resume is a new one. The record is
                // the only thing that can say a dataset was assembled across two builds — the
                // 2026-08-25 survey ran in five slices and the radiator's emissivity moved between
                // the fourth and the fifth — and a writer that overwrote would leave it claiming
                // the last build for rows taken under the first.
                //
                // Nothing asserted this until 2026-08-28, and the reader on the other side of the
                // format had drifted to match: `tools/corpus/provenance.py` read only the last
                // `Cubes.xml` line and reported a split dataset as one (`D3`).
                CorpusRecord.Started.Clear();
                CorpusRecord.Provenance("a-walk-that-does-not-exist");

                string appended = File.ReadAllText(path);
                Assert.StartsWith(text, appended);
                Assert.True(appended.Length > text.Length,
                    "a resumed walk overwrote its provenance instead of appending, so a dataset"
                    + " assembled across two builds would claim only the second");
                Assert.Equal(2, Occurrences(appended, "walk a-walk-that-does-not-exist"));
                Assert.Equal(2, Occurrences(appended, "Cubes.xml "));
            }
            finally
            {
                Environment.SetEnvironmentVariable("THERMAL_CORPUS_DATA", previous);
                try
                {
                    Directory.Delete(directory, true);
                }
                catch (IOException)
                {
                }
            }
        }

        /// <summary>How many times one string occurs, which is what says a block was added rather than replaced.</summary>
        private static int Occurrences(string text, string needle)
        {
            int count = 0;
            for (int i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
                i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            {
                count++;
            }
            return count;
        }
    }
}
