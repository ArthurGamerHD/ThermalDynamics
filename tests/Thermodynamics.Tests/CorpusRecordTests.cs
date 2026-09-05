using System;
using System.IO;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Where a dataset goes and what it says about itself — the two things a corpus walk gets
    /// wrong without anyone finding out.
    ///
    /// <para>
    /// **Both cases here are failures that happened**, on 2026-08-25, on the same run. The `A13`
    /// census was asked for in one directory and written to another, and it recorded nothing about
    /// the build it was taken on. Neither showed up as a failure: the walk passed, and the
    /// question it answers — *which waste fractions was this measured at* — had to go back to the
    /// git log, which is precisely what `CorpusRecord.Provenance` exists to replace.
    /// </para>
    ///
    /// <para>
    /// These run without the corpus, because neither is about the corpus. They set the environment
    /// variable, and restore it, so an opted-in run is not disturbed.
    /// </para>
    /// </summary>
    [Collection("alone")]
    public class CorpusRecordTests : IDisposable
    {
        private readonly string previous =
            Environment.GetEnvironmentVariable("THERMAL_CORPUS_DATA");

        private readonly string scratch =
            Path.Combine(Path.GetTempPath(), "thermal-record-" + Guid.NewGuid().ToString("n"));

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("THERMAL_CORPUS_DATA", previous);
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            catch (IOException) { }
        }

        /// <summary>
        /// **A relative `THERMAL_CORPUS_DATA` is relative to the repository, not to whatever
        /// directory the test host happens to run from.**
        ///
        /// The host runs from its own `bin/Debug`, so `../out/somewhere` used to land three
        /// directories from where it was asked for. The run passes either way, and the named
        /// directory is simply empty afterwards — which reads as *the walk produced nothing*.
        /// </summary>
        [Fact]
        public void ARelativeDataDirectoryIsResolvedAgainstTheRepositoryRoot()
        {
            Environment.SetEnvironmentVariable("THERMAL_CORPUS_DATA", "out/a-relative-name");

            string resolved = CorpusRecord.Directory();

            Assert.True(Path.IsPathRooted(resolved),
                "a relative THERMAL_CORPUS_DATA came back relative: " + resolved);
            Assert.StartsWith(Harness.ShippedBlocks.RepoRoot(), resolved, StringComparison.Ordinal);
            Assert.EndsWith(Path.Combine("out", "a-relative-name"), resolved, StringComparison.Ordinal);
        }

        /// <summary>An absolute path is left exactly as it was given.</summary>
        [Fact]
        public void AnAbsoluteDataDirectoryIsUsedAsGiven()
        {
            Environment.SetEnvironmentVariable("THERMAL_CORPUS_DATA", scratch);

            Assert.Equal(scratch, CorpusRecord.Directory());
        }

        /// <summary>
        /// **A walk writes its provenance even when its output directory does not exist yet**,
        /// which is the state every new dataset starts in.
        ///
        /// `Provenance` runs before the first batch, so it ran before anything had created the
        /// directory; `AppendAllText` threw `DirectoryNotFoundException`, which is an
        /// `IOException`, and the catch that keeps provenance from failing a walk swallowed it. No
        /// dataset with a fresh directory ever recorded its build — and a dataset that records its
        /// build is the whole point, since a walk outlives the tree it came from.
        /// </summary>
        [Fact]
        public void ProvenanceIsWrittenIntoADirectoryThatDoesNotExistYet()
        {
            Environment.SetEnvironmentVariable("THERMAL_CORPUS_DATA", scratch);
            Assert.False(Directory.Exists(scratch), "the scratch directory should start absent");

            CorpusRecord.Provenance("a-walk-that-has-not-written-a-row");

            string path = Path.Combine(scratch, "provenance.txt");
            Assert.True(File.Exists(path), "no provenance.txt was written into a fresh directory");

            string text = File.ReadAllText(path);
            Assert.Contains("walk a-walk-that-has-not-written-a-row", text);
            Assert.Contains("commit ", text);
            Assert.Contains("Cubes.xml ", text);
        }

        /// <summary>
        /// Recording off means nothing is written and nothing is created, which is what makes the
        /// opt-in an opt-in rather than a default with an empty directory beside it.
        /// </summary>
        [Fact]
        public void NothingIsWrittenWhenRecordingIsOff()
        {
            Environment.SetEnvironmentVariable("THERMAL_CORPUS_DATA", null);

            Assert.Null(CorpusRecord.Directory());
            Assert.False(CorpusRecord.On);

            CorpusRecord.Provenance("a-walk");
            Assert.False(Directory.Exists(scratch));
        }

        /// <summary>
        /// What Text writes, CsvLine.Split reads back — pinned at the field the drifted reader
        /// got wrong. Three readers carried the split and one was a naive quote-toggle that
        /// silently dropped the doubled quote this writer legitimately produces, so a ship name
        /// carrying a quote parsed differently depending on which lab read it (`D3`).
        /// </summary>
        [Fact]
        public void ANameWithAQuoteAndACommaRoundTripsThroughTheOneSplit()
        {
            string name = "The \"Iron\" Maiden, Mk II";
            string line = CorpusRecord.Text(name) + "," + CorpusRecord.Text("plain");

            var fields = Thermodynamics.Harness.CsvLine.Split(line);

            Assert.Equal(2, fields.Count);
            Assert.Equal(name, fields[0]);
            Assert.Equal("plain", fields[1]);
        }
    }
}
