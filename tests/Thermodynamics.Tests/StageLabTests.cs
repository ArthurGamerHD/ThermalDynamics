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
        /// **Both statistics reach the reader, and the shape that says which to believe.**
        ///
        /// <para>
        /// The fastest repeat reproduces between runs for some stages and not others, and which is
        /// which cannot be guessed — measured over four runs of one binary at 126,731 blocks, the
        /// minimum spreads 4.5 % on the room pass and 97 % on `register`, while the median spreads
        /// 0.9 % on exposure where the minimum spreads 30 % (performance.md, Pass 9, Iteration 5).
        /// So the lab reports both and privileges neither, and `best/med` is the ratio that says
        /// whether a row's minimum sits in its own bulk or in a fast mode it reached a few times.
        /// </para>
        ///
        /// <para>
        /// This asserts the arithmetic and the artefacts, not the reproducibility: whether two runs
        /// agree is a property of the machine, and a test that demanded it under an eight-way
        /// parallel suite would be testing the hardware — the mistake pass 8 made and undid.
        /// </para>
        /// </summary>
        [Fact]
        public void EveryRowCarriesItsMedianAndItsShape()
        {
            int repeats = StageLab.Repeats;
            int confirming = StageLab.ConfirmingRepeats;

            StageLab.Repeats = 5;
            StageLab.ConfirmingRepeats = 2;
            try
            {
                List<StageLab.Row> rows = StageLab.Run("ship", 2000, StageLab.Stages);

                foreach (StageLab.Row row in rows)
                {
                    // The median is one of the readings taken, and it is between the two extremes.
                    Assert.Contains(row.MedianMs, row.Samples);
                    Assert.True(row.MedianMs >= row.BestMs,
                        row.Stage + " has a median of " + row.MedianMs + " below its best of "
                        + row.BestMs + ", so one of the two is not of these repeats");
                    Assert.True(row.MedianMs <= row.WorstMs,
                        row.Stage + " has a median of " + row.MedianMs + " above its worst of "
                        + row.WorstMs);

                    // The shape is the ratio, and it cannot exceed one by construction.
                    Assert.True(row.FastModeShare > 0d && row.FastModeShare <= 1d,
                        row.Stage + " reports a best/median of " + row.FastModeShare);
                }

                string table = StageLab.Table(rows);
                string header = StageLab.Csv(rows).Split('\n')[0];

                Assert.Contains("median ms", table);
                Assert.Contains("best/med", table);
                Assert.Contains("median_ms", header);
                Assert.Contains("fast_mode_share", header);
            }
            finally
            {
                StageLab.Repeats = repeats;
                StageLab.ConfirmingRepeats = confirming;
            }
        }

        /// <summary>
        /// The step-phase path reports a median too, rather than a zero that would read as a stage
        /// with an infinitely rare fast mode. It takes a fixed count of repeats and says `fixed`,
        /// and it keeps them all like every other row.
        /// </summary>
        [Fact]
        public void AStepPhaseRowCarriesItsRepeatsAndItsMedian()
        {
            int repeats = StageLab.Repeats;

            StageLab.Repeats = 3;
            try
            {
                List<StageLab.Row> rows = StageLab.StepPhases("ship", 2000);

                Assert.NotEmpty(rows);
                foreach (StageLab.Row row in rows)
                {
                    Assert.Equal(StageLab.Row.Fixed, row.Stop);
                    Assert.Equal(3, row.Repeats);
                    Assert.Equal(3, row.Samples.Count);
                    Assert.True(row.MedianMs > 0d,
                        row.Stage + " reports no median, which would read as a best that is"
                        + " infinitely far below its own bulk");
                }
            }
            finally
            {
                StageLab.Repeats = repeats;
            }
        }

        /// <summary>
        /// **And the reason reaches the reader.** The lab has counted confirmations since pass 8
        /// and the test below has asserted that every row is either confirmed or capped — but for a
        /// pass neither the table a person reads nor the CSV a comparison is built from carried the
        /// answer, so a capped row and a settled one printed identically. An instrument that knows
        /// and does not say is the failure this whole page is about (`P2`, `E9`).
        ///
        /// <para>
        /// This asserts the two artefacts, not the counting: that the header names the columns, that
        /// every row's word is one of the three the lab defines, and that the word printed is the
        /// one the row holds. It runs the lab at a floor of three so it costs a second.
        /// </para>
        /// </summary>
        [Fact]
        public void TheTableAndTheCsvSayWhyEachStageStopped()
        {
            int repeats = StageLab.Repeats;
            int confirming = StageLab.ConfirmingRepeats;

            StageLab.Repeats = 3;
            StageLab.ConfirmingRepeats = 2;
            try
            {
                List<StageLab.Row> rows = StageLab.Run("ship", 2000, StageLab.Stages);

                string table = StageLab.Table(rows);
                string csv = StageLab.Csv(rows);

                Assert.Contains("stopped", table);
                Assert.Contains("repeats", table);
                Assert.Contains("stopped", csv.Split('\n')[0]);
                Assert.Contains("repeats", csv.Split('\n')[0]);
                Assert.Contains("confirmed_best", csv.Split('\n')[0]);

                for (int i = 0; i < rows.Count; i++)
                {
                    StageLab.Row row = rows[i];

                    Assert.True(row.Stop == StageLab.Row.Confirmed || row.Stop == StageLab.Row.Capped,
                        row.Stage + " stopped for \"" + row.Stop + "\", which is not one of the words"
                        + " the lab defines, so a reader cannot tell what ended it");

                    // The word has to be *in* the artefacts, not merely on the row.
                    Assert.Contains(row.Stage, table);
                    Assert.Contains(row.Stage + ",", csv);
                }

                // A capped row and a confirmed one must not print the same, which is the whole
                // point; at a floor of three on a 2,000-block hull every row confirms, so the
                // discrimination is checked on a row made to hit the cap instead.
                StageLab.Row capped = new StageLab.Row();
                capped.Stage = "contrived";
                capped.Stop = StageLab.Row.Capped;
                capped.BestMs = 1d;
                capped.WorstMs = 2d;
                capped.Work = 1;
                capped.WorkUnit = "things";

                List<StageLab.Row> mixed = new List<StageLab.Row> { rows[0], capped };
                string mixedTable = StageLab.Table(mixed);
                string mixedCsv = StageLab.Csv(mixed);

                Assert.Contains(StageLab.Row.Capped, mixedTable);
                Assert.Contains(StageLab.Row.Confirmed, mixedTable);
                Assert.Contains("," + StageLab.Row.Capped + ",", mixedCsv);
                Assert.Contains("," + StageLab.Row.Confirmed + ",", mixedCsv);
            }
            finally
            {
                StageLab.Repeats = repeats;
                StageLab.ConfirmingRepeats = confirming;
            }
        }

        /// <summary>
        /// **Every stage stops for a reason it can name.** A figure confirmed once is a fluke, so
        /// the lab keeps sampling until several readings agree with the best to within two per
        /// cent, and gives up at a cap. A row that hit the cap is a row whose best was never
        /// confirmed, and its number should not be compared with anything — so the two must be
        /// distinguishable, and every row must be one or the other.
        ///
        /// <para>
        /// **What this does not assert is that the readings are stable**, and that is deliberate.
        /// This suite runs eight ways in parallel, which is exactly the contention that makes a
        /// fast repeat rare; a test demanding convergence under it failed and passed on
        /// consecutive runs of unchanged code, which is worse than no test. The stability the rule
        /// buys — three runs within 2 %, 3 % and 7 % where fifteen repeats gave 48 %, 67 % and
        /// 28 % — is measured on a held machine and recorded in performance.md, Pass 8.
        /// </para>
        /// </summary>
        [Fact]
        public void EveryStageStopsForAReasonItCanName()
        {
            int repeats = StageLab.Repeats;
            int confirming = StageLab.ConfirmingRepeats;

            StageLab.Repeats = 3;
            StageLab.ConfirmingRepeats = 2;
            try
            {
                List<StageLab.Row> rows = StageLab.Run("ship", 2000, StageLab.Stages);

                int confirmed = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    StageLab.Row row = rows[i];

                    Assert.True(row.Repeats >= StageLab.Repeats,
                        row.Stage + " stopped after " + row.Repeats + " repeats, below the floor of "
                        + StageLab.Repeats);

                    Assert.True(row.ConfirmedBest >= StageLab.ConfirmingRepeats
                        || row.Repeats >= StageLab.MaxRepeats,
                        row.Stage + " stopped after " + row.Repeats + " repeats with its best"
                        + " confirmed " + row.ConfirmedBest + " times — neither a confirmation nor"
                        + " the cap, so the rule that ended it is not the rule as written");

                    Assert.True(row.ConfirmedBest >= 1,
                        row.Stage + " never counted its own best as confirming itself");

                    if (row.ConfirmedBest >= StageLab.ConfirmingRepeats) confirmed++;
                }

                Assert.True(confirmed > 0,
                    "not one stage of " + rows.Count + " confirmed its best, so either the counting"
                    + " is broken or the cap is being reached every time");
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
