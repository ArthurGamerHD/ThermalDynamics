using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    public class CorpusArchiveTests
    {
        [Theory]
        [InlineData("bp.sbc")]
        [InlineData("p.sbc")]          // the truncation actually found in the corpus
        [InlineData(".sbc")]           // and its worst case: nothing left but the extension
        [InlineData("BP.SBC")]
/// <summary>EveryShapeOfBlueprintEntryIsFound operation.</summary>
        public void EveryShapeOfBlueprintEntryIsFound(string name)
        {
            Assert.True(Blueprints.IsLegacyBlueprintEntry(name));
        }

        [Theory]
        [InlineData("thumb.png")]
        [InlineData("humb.png")]
        [InlineData("umb.png")]
        [InlineData("")]
        [InlineData(null)]
/// <summary>NothingElseIsMistakenForOne operation.</summary>
        public void NothingElseIsMistakenForOne(string name)
        {
            Assert.False(Blueprints.IsLegacyBlueprintEntry(name));
        }
    }
}
