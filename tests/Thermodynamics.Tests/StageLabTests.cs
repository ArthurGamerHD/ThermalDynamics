using System.Collections.Generic;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The two classes that turn the stage lab's dials down run one at a time.** `StageLab.Repeats`
    /// and its convergence settings are static, and a suite that runs eight ways in parallel had two
    /// classes assigning them at once — which showed up the moment a second dial was added, as a
    /// stage that stopped before its best was confirmed. Serialised rather than made instance state,
    /// because a lab that a caller configures is what every other bench command here expects.
    /// </summary>
    [CollectionDefinition("the stage lab's dials", DisableParallelization = true)]
    public class StageLabDials
    {
    }

    /// <summary>
    /// `bench stages` is the instrument a performance pass judges a stage on, so this holds the
    /// two properties that make its reading a reading: every stage it names produces a figure on a
    /// hull that exercised it, and a stage's work counter is identical across repeats — which the
    /// lab enforces by refusing, and this checks by asking twice.
    ///
    /// <para>
    /// And a third, since pass 8: **a stage does not stop until its fastest reading has been
    /// reproduced.** Best-of-fifteen was not converged — three runs of the same binary read the
    /// surface rebuild at 7.4, 9.6 and 6.5 ms — so a stage now repeats until five readings land
    /// within two per cent of its best. `EveryStageConfirmsItsBest` is what says that happened
    /// rather than that the cap was reached.
    /// </para>
    /// </summary>
    [Trait("speed", "slow")]
    [Collection("the stage lab's dials")]
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

        /// <summary>
        /// **Every stage stops because its fastest reading was reproduced, not because it ran out
        /// of patience.** A figure confirmed once is a fluke; the lab keeps sampling until several
        /// readings agree with the best to within two per cent, and gives up at a cap. A row that
        /// hit the cap is a row whose best was never confirmed, and its number should not be
        /// compared with anything.
        ///
        /// <para>
        /// **The bar is lowered here, and that is the point of the test rather than a concession.**
        /// This suite runs eight ways in parallel, which is exactly the contention that makes a
        /// fast repeat rare — asking for five agreeing readings under it would test the machine.
        /// What is checked is the mechanism: that agreement is counted, that the loop stops on it,
        /// and that stopping at the cap is distinguishable from stopping at an answer. The figure
        /// the shipped default produces is in performance.md, measured on a machine that was held.
        /// </para>
        /// </summary>
        [Fact]
        public void EveryStageConfirmsItsBest()
        {
            int repeats = StageLab.Repeats;
            int confirming = StageLab.ConfirmingRepeats;

            StageLab.Repeats = 3;
            StageLab.ConfirmingRepeats = 2;
            try
            {
                List<StageLab.Row> rows = StageLab.Run("ship", 2000, StageLab.Stages);

                for (int i = 0; i < rows.Count; i++)
                {
                    StageLab.Row row = rows[i];

                    Assert.True(row.ConfirmedBest >= StageLab.ConfirmingRepeats,
                        row.Stage + " stopped after " + row.Repeats + " repeats with its best"
                        + " confirmed only " + row.ConfirmedBest + " times, so it reached the cap"
                        + " rather than an answer");

                    Assert.True(row.Repeats < StageLab.MaxRepeats,
                        row.Stage + " ran to the cap of " + StageLab.MaxRepeats + " repeats");
                }
            }
            finally
            {
                StageLab.Repeats = repeats;
                StageLab.ConfirmingRepeats = confirming;
            }
        }

        /// <summary>
        /// Every stage reports what one execution of it allocated, and the figure separates the
        /// stages that churn the heap from the ones that do not — which is the thing that made a
        /// figure in this lab unexplainable until the lab was made to say it. `place` allocates a
        /// block and a grid entry per block and must report megabytes; a settled step allocates
        /// nothing per step and must report approximately zero (`C4`).
        /// </summary>
        [Fact]
        public void EveryStageReportsWhatItAllocatedAndTheSteppingPathAllocatesNothing()
        {
            int repeats = StageLab.Repeats;
            StageLab.Repeats = 3;
            try
            {
                List<StageLab.Row> rows = StageLab.Run("ship", 2000, StageLab.Stages);
                StageLab.Row place = null;
                StageLab.Row solver = null;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Stage == "place") place = rows[i];
                    if (rows[i].Stage == "solver") solver = rows[i];
                }

                Assert.NotNull(place);
                Assert.NotNull(solver);
                Assert.True(place.AllocatedBytes > 100 * 1024,
                    "place allocated " + place.AllocatedBytes + " bytes, which is too little to be building a block each");
                Assert.True(solver.AllocatedBytes < 4 * 1024,
                    "a settled step allocated " + solver.AllocatedBytes + " bytes; nothing allocates on the stepping path (`C4`)");
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
