using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    public class CorpusArchiveTests
    {
        [Theory]
        [InlineData("bp.sbc")]
        [InlineData("p.sbc")]
        [InlineData(".sbc")]
        [InlineData("BP.SBC")]

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

        public void NothingElseIsMistakenForOne(string name)
        {
            Assert.False(Blueprints.IsLegacyBlueprintEntry(name));
        }
    }
}
