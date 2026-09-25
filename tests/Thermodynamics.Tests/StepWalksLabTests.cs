using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    public class StepWalksLabTests
    {
        [Fact]

        public void BothSchedulesAgreeGetTimedAndTheMirrorIsMeasuredAgainstAStep()
        {
            StepWalksLab.Result result = StepWalksLab.Run("ship", 4000, 3);

            Assert.Equal(2, result.Fusion.Count);
            foreach (StepWalksLab.Row row in result.Fusion)
            {
                Assert.True(row.Nodes > 1000, row.Walk + " judged only " + row.Nodes + " nodes (`E8`)");
                Assert.True(row.BestMs > 0, row.Walk + " was never timed");
            }

            Assert.True(result.MirrorMs > 0, "the mirror was never timed");
            Assert.True(result.SettledStepMs > result.MirrorMs,

                "a settled step (" + result.SettledStepMs + " ms) reads cheaper than the mirror it contains ("
                + result.MirrorMs + " ms), so one of the two clocks is wrong");

        }
    }
}
