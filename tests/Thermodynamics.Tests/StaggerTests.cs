using System.Collections.Generic;
using Thermodynamics.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **What spreading a step across frames costs, and what staggering whole steps would cost
    /// instead** — the trade [backlog.md](../../docs/backlog.md) `D14` asks to re-decide.
    ///
    /// <para>
    /// Both schedules do identical arithmetic over the same nodes in the same order; only the
    /// interleaving differs, so what separates them is locality. These hold the two halves of the
    /// answer: that the penalty is set by how many grids are competing for cache rather than by how
    /// big any of them is, and that a whole grid's step — the lump staggering would put on one
    /// frame — is small enough to say where the schedule could change.
    /// </para>
    /// </summary>
    [Trait("speed", "slow")]
    [Collection("alone")]
    public class StaggerTests
    {
        private readonly ITestOutputHelper output;

        public StaggerTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const int NodesEach = 600;

        /// <summary>
        /// A run-to-run spread this wide means the machine was doing something else, and no claim
        /// below is readable through it.
        ///
        /// <para>
        /// **Thirty per cent is not a new number.** It is the `1.3x` this file already asserted
        /// on, unchanged — what moved is what happens when it is exceeded, from a failure that
        /// belongs to the machine to a reading refused out loud. It was the right size to begin
        /// with: both claims here are about locality, which is worth a few per cent to about a
        /// fifth, so a floor of a third is already several times the largest effect either could
        /// find and past it there is nothing to see rather than something small. Refused in
        /// practice at load average 37 with a corpus walk on every core, where the spread reaches
        /// 500 % ([backlog.md](../../docs/backlog.md) `A11`).
        /// </para>
        /// </summary>
        private const double UnreadableFloor = 0.30d;

        /// <summary>
        /// **Spreading costs nothing until the fleet stops fitting in cache**, and where that falls
        /// is the machine's business rather than this repository's.
        ///
        /// <para>
        /// So the claim is conditional on the measurement, which is `M5` taken seriously: a
        /// difference smaller than the run-to-run spread has not been observed, and asserting an
        /// ordering between two such differences is asserting noise. The first version of this test
        /// did exactly that — it compared 1.0056 against 0.9967 on a fleet that fits in cache and
        /// passed by luck. What is held now is that *if* the large fleet's penalty is real, it is
        /// larger than the small fleet's; and that if neither is, the two schedules agree.
        /// </para>
        /// </summary>
        [Fact]
        public void SpreadingCostsMoreOnlyOnceTheFleetStopsFittingInCache()
        {
            List<StaggerLab.Row> rows = StaggerLab.Run(new[] { 4, 64 }, NodesEach, 8);
            Assert.Equal(2, rows.Count);

            foreach (StaggerLab.Row row in rows)
            {
                output.WriteLine(
                    "{0,4} grids x {1} rounds: staggered {2:n3} ms, spread {3:n3} ms, {4:n1} % more"
                    + " (noise {5:n2}/{6:n2}x)",
                    row.Grids, row.Rounds, row.StaggeredMs, row.SpreadMs,
                    (row.Penalty - 1d) * 100d, row.StaggeredSpread, row.SpreadSpread);

                // Both sides did the same work, or the ratio is a comparison of two run lengths.
                Assert.True(row.StaggeredMs > 0d && row.SpreadMs > 0d);

                // And both rungs were read through the same instrument, which is the correction
                // `A11` came to: a timed window is the same number of grid-steps at every fleet
                // size, so a minimum over it means the same thing at each (`P6`).
                Assert.Equal(StaggerLab.GridStepsPerRepeat, row.Grids * row.Rounds);
            }

            // The noise floor either side, from the repeats themselves rather than assumed.
            double floor = System.Math.Max(
                System.Math.Max(rows[0].StaggeredSpread, rows[0].SpreadSpread),
                System.Math.Max(rows[1].StaggeredSpread, rows[1].SpreadSpread)) - 1d;

            output.WriteLine("noise floor {0:P1}; penalties {1:P1} and {2:P1}",
                floor, rows[0].Penalty - 1d, rows[1].Penalty - 1d);

            // **The instrument reports whether it could see, before the result is read** (`M5`).
            // A machine busy with something else can spread these repeats past anything a few per
            // cent of locality could show through, and the honest outcome then is neither a pass
            // that means nothing nor a failure that belongs to the box: it is a reading refused,
            // said out loud. A regression larger than the floor still fails, which is the half of
            // the claim that survives a loaded machine.
            if (floor >= UnreadableFloor)
            {
                output.WriteLine("REFUSED: the repeats spread {0:P0}, which is wider than any"
                    + " locality effect this compares. Nothing is asserted about the ordering.",
                    floor);
                return;
            }

            if (rows[1].Penalty - 1d <= floor)
            {
                // Nothing to order. What that says is the finding for a fleet this size: the two
                // schedules are the same speed while the working set fits.
                Assert.True(System.Math.Abs(rows[0].Penalty - 1d) <= floor + 0.05d,
                    "a small fleet paid " + rows[0].Penalty + "x for spreading, which is outside a"
                    + " noise floor of " + floor + " and should have been free");
                return;
            }

            Assert.True(rows[1].Penalty >= rows[0].Penalty,
                "a 64-grid fleet paid " + rows[1].Penalty + "x for spreading against a 4-grid"
                + " fleet's " + rows[0].Penalty + "x, so the penalty is not the fleet size");
        }

        /// <summary>
        /// **And the lump is flat in fleet size**, which is what says it is a property of one
        /// grid's step rather than of the schedule: staggering does not make a frame carry more
        /// work, it makes the work a frame carries belong to fewer grids.
        /// </summary>
        [Fact]
        public void TheLumpIsOneGridsStepAndDoesNotGrowWithTheFleet()
        {
            List<StaggerLab.Row> rows = StaggerLab.Run(new[] { 4, 64 }, NodesEach, 8);

            double small = rows[0].LumpMs;
            double large = rows[1].LumpMs;

            double floor = System.Math.Max(rows[0].StaggeredSpread, rows[1].StaggeredSpread) - 1d;

            output.WriteLine("lump {0:n3} ms at 4 grids x {1} rounds, {2:n3} ms at 64 x {3};"
                + " noise floor {4:P1}",
                small, rows[0].Rounds, large, rows[1].Rounds, floor);

            Assert.True(small > 0d, "a grid's step measured as nothing");

            // The same instrument at both rungs, which is what this claim rests on: it divides one
            // rung's reading by the other's, so a window of four milliseconds against one of
            // seventy is the whole difference before any grid is stepped (`P6`, `A11`).
            Assert.Equal(StaggerLab.GridStepsPerRepeat, rows[0].Grids * rows[0].Rounds);
            Assert.Equal(StaggerLab.GridStepsPerRepeat, rows[1].Grids * rows[1].Rounds);

            if (floor >= UnreadableFloor)
            {
                output.WriteLine("REFUSED: the repeats spread {0:P0}, so a 1.5x bound on the ratio"
                    + " of two readings taken through it says nothing.", floor);
                return;
            }

            Assert.True(large < small * 1.5d && small < large * 1.5d,
                "the lump moved from " + small + " ms to " + large + " ms with the fleet size,"
                + " so it is not one grid's step");
        }
    }
}
