using System.IO;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The auditor against a real field dump rather than invented rows.
    ///
    /// <para>Separate from <see cref="DumpAuditTests"/> on purpose. Those tests build the smallest
    /// CSV that could break one check and assert that it breaks; nothing about them changes when a
    /// new dump arrives. These read a dump a world produced, and every number in them is a fact
    /// about that dump — so refreshing the fixture changes this file and only this file.</para>
    ///
    /// <para>Synthetic rows prove a check can fire. They cannot prove it holds against numbers a
    /// planet produced, which is the only place the model and the world meet — the same reason
    /// `Census` keeps a real ship's block population rather than a plausible one.</para>
    ///
    /// <para>The fixture is the 2026-08-21 ToastyBugs dump — five planets, a grid buried 99 m
    /// into Mars, a Hailstorm, and the first session with per-stage after-step timing: 597 climate
    /// rows thinned one in twenty and then completed with the minority rows a uniform sample would
    /// drop (every row under weather or over the engine's ceiling; one in ten of the buried,
    /// airless and slope-wind rows), beside all 304 of its grids and 720 block types. Refresh it
    /// from `-- dump`.</para>
    /// </summary>
    public class FieldDumpTests
    {
        private static string Fixture()
        {
            // The build output no longer sits inside the repository, so the fixture is located the
            // way the benchmark baseline is: from the compiled-in source path. The folder holds the
            // three CSVs of one dump under their own names, so the auditor finds the siblings the
            // way it finds them in a world's storage.
            string folder = Path.Combine(
                ShippedBlocks.RepoRoot(), "tests", "benchmarks", "field-dump");

            string path = DumpAudit.Newest(folder);
            Assert.True(path != null, "field dump fixture missing under " + folder);
            return path;
        }

        private static DumpAudit.CheckResult Check(DumpAudit.Result result, string name)
        {
            foreach (DumpAudit.CheckResult check in result.Checks)
            {
                if (check.Name.StartsWith(name, System.StringComparison.Ordinal)) return check;
            }

            throw new Xunit.Sdk.XunitException("no check named " + name);
        }

        /// <summary>Every check reaches the fixture: a lost column must fail, not skip.</summary>
        [Fact]
        public void EveryCheckReachesTheFixture()
        {
            DumpAudit.Result result = DumpAudit.Run(Fixture());

            foreach (DumpAudit.CheckResult check in result.Checks)
            {
                Assert.False(check.Skipped, check.Name + " skipped: the fixture lost a column");
            }

            Assert.Equal(597, result.Rows);
            Assert.Equal(304, result.GridRows);
            Assert.Equal(720, result.BlockTypeRows);
        }

        /// <summary>
        /// The fault this fixture caught, held as the diagnosis rather than as a passing test.
        ///
        /// <para>A dump records what the mod was when it was taken. This one carries the census
        /// race: the game places blocks from worker threads, the per-type counters were plain
        /// increments, and one placement in fourteen thousand lost its ++ — placed 14,273, removed
        /// 113, live 14,159. The counters are interlocked now; the fixture still shows the tear,
        /// because that is what proves the check can see one.</para>
        /// </summary>
        [Fact]
        public void TheFixtureStillCarriesTheCensusTearItCaught()
        {
            DumpAudit.Result result = DumpAudit.Run(Fixture());

            DumpAudit.CheckResult census = Check(result, "live blocks");
            Assert.Equal(1, census.Hits);
            Assert.Contains("SmallBlockArmorBlock", census.Worst);

            foreach (DumpAudit.CheckResult check in result.Checks)
            {
                if (check == census) continue;
                Assert.False(check.Failed, check.Name + " failed: " + check.Worst);
            }
        }

        /// <summary>
        /// The open questions the fixture is evidence for. Counted rather than failed, and here so
        /// a change that quietly resolves — or worsens — one of them cannot pass unnoticed.
        /// </summary>
        [Fact]
        public void TheFixtureCarriesTheOpenObservations()
        {
            DumpAudit.Result result = DumpAudit.Run(Fixture());

            // B18: the engine's figure scales the wind rather than bounding it. 23 rows exceed it,
            // the worst at three times the local ceiling, 8.4 km up a mountain on Alien.
            DumpAudit.CheckResult ceiling = Check(result, "wind stays under");
            Assert.Equal(23, ceiling.Hits);
            Assert.Contains("103.94", ceiling.Worst);

            // B15: slope wind is what carries a reading above the composed factors.
            DumpAudit.CheckResult slope = Check(result, "slope wind");
            Assert.Equal(112, slope.Hits);

            // The dump predates the grid_speed column, so rows below the composed product — a ship
            // flying downwind — are observations rather than verdicts here.
            DumpAudit.CheckResult decomposes = Check(result, "wind decomposes");
            Assert.True(decomposes.Observation);
            Assert.Equal(20, decomposes.Hits);
        }
    }
}
