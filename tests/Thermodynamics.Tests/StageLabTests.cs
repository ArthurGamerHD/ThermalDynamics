using System;
using System.Collections.Generic;
using System.IO;
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
                // Both lists, because a stage nobody names in `Stages` is a stage nothing here
                // would run — which is how a lab comes to have a case in its switch and no
                // coverage at all (`D2`).
                List<string> all = new List<string>(StageLab.Stages);
                all.AddRange(StageLab.ExtraStages);

                List<StageLab.Row> rows = StageLab.Run("ship", 2000, all);
                Assert.Equal(all.Count, rows.Count);

                for (int i = 0; i < rows.Count; i++)
                {
                    StageLab.Row row = rows[i];
                    Assert.True(row.Work > 0, row.Stage + " reports no work, so it timed nothing");
                    Assert.True(row.BestMs > 0d && row.BestMs <= row.WorstMs, row.Stage + " has no usable time");
                    Assert.True(row.Blocks > 1000, row.Stage + " ran on " + row.Blocks + " blocks");
                }

                // **The two sync rows must not be the same row.** They exist to price a dirty flag
                // and they differ only in that flag, so a `syncclean` that quietly mirrored every
                // node anyway — or a `syncdirty` that marked none — would still report a time and a
                // work count and would price nothing (`E8`). At 2,000 blocks the gap is small; that
                // it exists at all is what is asserted here, and performance.md carries its size.
                StageLab.Row dirty = Find(rows, "syncdirty");
                StageLab.Row clean = Find(rows, "syncclean");
                Assert.Equal(dirty.Work, clean.Work);
                Assert.True(dirty.BestMs > clean.BestMs,
                    "mirroring every node took " + dirty.BestMs.ToString("n4")
                    + " ms and mirroring none took " + clean.BestMs.ToString("n4")
                    + ", so this pair prices nothing");
            }
            finally
            {
                StageLab.Repeats = repeats;
            }
        }

        /// <summary>
        /// **A row survives the round trip through the artefact**, which is what lets a stage be
        /// timed in a process of its own and still appear in one table.
        ///
        /// <para>
        /// `--isolate` exists because settling the heap between stages is not enough: pass 9's
        /// tenth iteration measured the room pass moving 4.5 % between two binaries when `place`
        /// and `exposure` ran before it in the same process, and 0.3 % when it ran alone. The child
        /// process writes `stages.csv` and the parent reads it back, so what is asserted here is
        /// that reading it back loses nothing a comparison uses — including the stopping reason,
        /// because a capped row that came back as a confirmed one would be compared with things it
        /// must not be.
        /// </para>
        ///
        /// <para>
        /// Columns are found by name, so this also pins that a column added in the middle cannot
        /// silently shift the rest — the failure that reads as a plausible number rather than as an
        /// error (`D3`).
        /// </para>
        /// </summary>
        [Fact]
        public void ARowSurvivesBeingWrittenAndReadBack()
        {
            int repeats = StageLab.Repeats;
            StageLab.Repeats = 3;
            try
            {
                List<StageLab.Row> written = StageLab.Run("ship", 2000,
                    new List<string> { "place", "rooms" });

                string path = Path.Combine(Path.GetTempPath(),
                    "stagelab-" + Guid.NewGuid().ToString("n") + ".csv");
                try
                {
                    File.WriteAllText(path, StageLab.Csv(written));
                    List<StageLab.Row> read = StageLab.ReadCsv(path);

                    Assert.Equal(written.Count, read.Count);
                    for (int i = 0; i < written.Count; i++)
                    {
                        Assert.Equal(written[i].Stage, read[i].Stage);
                        Assert.Equal(written[i].Blocks, read[i].Blocks);
                        Assert.Equal(written[i].BestMs, read[i].BestMs);
                        Assert.Equal(written[i].MedianMs, read[i].MedianMs);
                        Assert.Equal(written[i].WorstMs, read[i].WorstMs);
                        Assert.Equal(written[i].Repeats, read[i].Repeats);
                        Assert.Equal(written[i].ConfirmedBest, read[i].ConfirmedBest);
                        Assert.Equal(written[i].Stop, read[i].Stop);
                        Assert.Equal(written[i].Work, read[i].Work);
                        Assert.Equal(written[i].WorkUnit, read[i].WorkUnit);
                        Assert.Equal(written[i].AllocatedBytes, read[i].AllocatedBytes);
                        Assert.Equal(written[i].FastModeShare, read[i].FastModeShare);
                    }
                }
                finally
                {
                    File.Delete(path);
                }
            }
            finally
            {
                StageLab.Repeats = repeats;
            }
        }

        /// <summary>
        /// A file this lab did not write is refused by name rather than parsed into a plausible
        /// row. The case that matters is a `stages.csv` from before a column existed — the pass's
        /// own starting binary writes one — which positional parsing would read as a row of
        /// numbers in the wrong columns.
        /// </summary>
        [Fact]
        public void AnArtefactMissingAColumnIsRefusedRatherThanMisread()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "stagelab-old-" + Guid.NewGuid().ToString("n") + ".csv");
            try
            {
                // The header this lab wrote before pass 9's second iteration: no median, no
                // stopping reason, no repeats.
                File.WriteAllText(path,
                    "stage,blocks,best_ms,worst_ms,spread_percent,work,work_unit,ns_per_unit,allocated_bytes"
                    + Environment.NewLine
                    + "rooms,126731,13.6,69.4,409,1651592,cells visited,8.3,4882720"
                    + Environment.NewLine);

                InvalidOperationException failure =
                    Assert.Throws<InvalidOperationException>(() => StageLab.ReadCsv(path));
                Assert.Contains("median_ms", failure.Message);

                // And a file with a header and nothing under it is a stage that reported nothing,
                // not an empty table.
                File.WriteAllText(path, "stage,blocks,best_ms" + Environment.NewLine);
                Assert.Throws<InvalidOperationException>(() => StageLab.ReadCsv(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static StageLab.Row Find(IList<StageLab.Row> rows, string stage)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Stage == stage) return rows[i];
            }

            throw new System.InvalidOperationException("no row for " + stage);
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

                // **Which window a figure came from is part of the figure** (`M7`): the same code
                // read 2.2x apart between two sessions of this machine, so a row that does not say
                // when it was taken is a row that will be compared with one from another day. The
                // stamp is asserted in the artefact rather than on the row, because the row is not
                // what anybody reads a fortnight later.
                Assert.Contains("taken_utc", csv.Split('\n')[0]);
                Assert.Contains("host", csv.Split('\n')[0]);
                Assert.Contains(StageLab.TakenUtc, csv);
                Assert.Contains(StageLab.Host, csv);

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
