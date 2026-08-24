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
    public class StaggerTests
    {
        private readonly ITestOutputHelper output;

        public StaggerTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const int NodesEach = 600;

        /// <summary>
        /// **Spreading costs nothing on a small fleet and something on a large one.** Two grids
        /// interleaved still fit in cache; two hundred do not, and every resume is then a cold
        /// start. The threshold is the machine's, so what is asserted is the *shape* — a large
        /// fleet pays more than a small one — rather than a percentage this repository cannot pin
        /// across machines (`M4`, `M5`).
        /// </summary>
        [Fact]
        public void SpreadingCostsMoreAsMoreGridsCompeteForCache()
        {
            List<StaggerLab.Row> rows = StaggerLab.Run(new[] { 4, 64 }, NodesEach, 8);
            Assert.Equal(2, rows.Count);

            foreach (StaggerLab.Row row in rows)
            {
                output.WriteLine("{0,4} grids: staggered {1:n3} ms, spread {2:n3} ms, {3:n1} % more",
                    row.Grids, row.StaggeredMs, row.SpreadMs, (row.Penalty - 1d) * 100d);

                // Both sides did the same work, or the ratio is a comparison of two run lengths.
                Assert.True(row.StaggeredMs > 0d && row.SpreadMs > 0d);

                // And the noise floor is small enough for the ratio to mean anything (`M4`).
                Assert.True(row.StaggeredSpread < 1.3d && row.SpreadSpread < 1.3d,
                    "the repeats spread " + row.StaggeredSpread + " / " + row.SpreadSpread
                    + "x, which is too noisy to read a few per cent through");
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

            output.WriteLine("lump {0:n3} ms at 4 grids, {1:n3} ms at 64", small, large);

            Assert.True(small > 0d, "a grid's step measured as nothing");
            Assert.True(large < small * 1.5d && small < large * 1.5d,
                "the lump moved from " + small + " ms to " + large + " ms with the fleet size,"
                + " so it is not one grid's step");
        }
    }
}
