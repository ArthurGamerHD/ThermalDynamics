using System;
using System.IO;
using Xunit;

namespace Thermodynamics.Tests
{
    [Collection("alone")]
    public class CorpusRecordTests : IDisposable
    {
        private readonly string previous =
            Environment.GetEnvironmentVariable("THERMAL_CORPUS_DATA");

        private readonly string scratch =
            Path.Combine(Path.GetTempPath(), "thermal-record-" + Guid.NewGuid().ToString("n"));

/// <summary>Dispose operation.</summary>
        public void Dispose()
        {
            Environment.SetEnvironmentVariable("THERMAL_CORPUS_DATA", previous);
/// <summary>if operation.</summary>
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            catch (IOException) { }
        }

        [Fact]
/// <summary>ARelativeDataDirectoryIsResolvedAgainstTheRepositoryRoot operation.</summary>
        public void ARelativeDataDirectoryIsResolvedAgainstTheRepositoryRoot()
        {
            Environment.SetEnvironmentVariable("THERMAL_CORPUS_DATA", "out/a-relative-name");

            string resolved = CorpusRecord.Directory();

            Assert.True(Path.IsPathRooted(resolved),
                "a relative THERMAL_CORPUS_DATA came back relative: " + resolved);
            Assert.StartsWith(Harness.ShippedBlocks.RepoRoot(), resolved, StringComparison.Ordinal);
            Assert.EndsWith(Path.Combine("out", "a-relative-name"), resolved, StringComparison.Ordinal);
        }

        [Fact]
/// <summary>AnAbsoluteDataDirectoryIsUsedAsGiven operation.</summary>
        public void AnAbsoluteDataDirectoryIsUsedAsGiven()
        {
            Environment.SetEnvironmentVariable("THERMAL_CORPUS_DATA", scratch);

            Assert.Equal(scratch, CorpusRecord.Directory());
        }

        [Fact]
/// <summary>ProvenanceIsWrittenIntoADirectoryThatDoesNotExistYet operation.</summary>
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

        [Fact]
/// <summary>NothingIsWrittenWhenRecordingIsOff operation.</summary>
        public void NothingIsWrittenWhenRecordingIsOff()
        {
            Environment.SetEnvironmentVariable("THERMAL_CORPUS_DATA", null);

            Assert.Null(CorpusRecord.Directory());
            Assert.False(CorpusRecord.On);

            CorpusRecord.Provenance("a-walk");
            Assert.False(Directory.Exists(scratch));
        }

        [Fact]
/// <summary>ANameWithAQuoteAndACommaRoundTripsThroughTheOneSplit operation.</summary>
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
