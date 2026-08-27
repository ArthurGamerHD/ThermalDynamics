using System.Collections.Generic;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// `bench stages` is the instrument a performance pass judges a stage on, so this holds the
    /// two properties that make its reading a reading: every stage it names produces a figure on a
    /// hull that exercised it, and a stage's work counter is identical across repeats — which the
    /// lab enforces by refusing, and this checks by asking twice.
    /// </summary>
    [Trait("speed", "slow")]
    public class StageLabTests
    {
        [Fact]
        public void EveryStageReportsWorkAndATimeOnAHullThatExercisedIt()
        {
            int repeats = StageLab.Repeats;
            StageLab.Repeats = 3;
            try
            {
                List<StageLab.Row> rows = StageLab.Run("ship", 2000, StageLab.Stages);
                Assert.Equal(StageLab.Stages.Length, rows.Count);

                for (int i = 0; i < rows.Count; i++)
                {
                    StageLab.Row row = rows[i];
                    Assert.True(row.Work > 0, row.Stage + " reports no work, so it timed nothing");
                    Assert.True(row.BestMs > 0d && row.BestMs <= row.WorstMs, row.Stage + " has no usable time");
                    Assert.True(row.Blocks > 1000, row.Stage + " ran on " + row.Blocks + " blocks");
                }
            }
            finally
            {
                StageLab.Repeats = repeats;
            }
        }

        [Fact]
        public void AStagesWorkIsTheSameFigureEveryTimeItIsAsked()
        {
            int repeats = StageLab.Repeats;
            StageLab.Repeats = 2;
            try
            {
                List<StageLab.Row> first = StageLab.Run("ship", 2000, StageLab.Stages);
                List<StageLab.Row> second = StageLab.Run("ship", 2000, StageLab.Stages);
                for (int i = 0; i < first.Count; i++)
                {
                    Assert.Equal(first[i].Work, second[i].Work);
                    Assert.Equal(first[i].WorkUnit, second[i].WorkUnit);
                }
            }
            finally
            {
                StageLab.Repeats = repeats;
            }
        }
    }
}
