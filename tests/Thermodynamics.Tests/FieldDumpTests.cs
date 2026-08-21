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
    /// <para>The fixture is the 2026-08-20 fleet dump: 264 climate rows thinned one in twenty and
    /// then completed with every buried row, every row under weather, every airless row, and every
    /// row that exceeded the engine's wind ceiling or carried slope wind — the minorities a uniform
    /// sample would drop — beside all 242 of its grids and 560 of its 815 block types. Refresh it
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
                // The burial check reads a column added after this fixture was taken, so it skips
                // by design here. Remove the exemption when a dump from a build carrying A15
                // refreshes the fixture.
                if (check.Name.StartsWith("a wholly buried grid")) continue;

                Assert.False(check.Skipped, check.Name + " skipped: the fixture lost a column");
            }

            Assert.Equal(264, result.Rows);
            Assert.Equal(242, result.GridRows);
            Assert.Equal(560, result.BlockTypeRows);
        }

        /// <summary>
        /// The two faults this fixture caught, held as the diagnosis rather than as a passing test.
        ///
        /// <para>A dump records what the mod was when it was taken, not what it is. Both faults were
        /// fixed after this one: the one-off build was timed into the stage rows, which the cost
        /// table indents under a `grid simulation` row that does not contain it; and a block type's
        /// peak temperature was fed only by the strided sampler while its temperature range was fed
        /// by the end-of-session sweep as well, so a type the sampler never reached reported a
        /// maximum above its own peak.</para>
        ///
        /// <para>These assertions are what say the checks still detect what they were written for.
        /// When a dump is taken on a build carrying both fixes, refresh the fixture and this test
        /// becomes the ordinary "everything holds" one.</para>
        /// </summary>
        [Fact]
        public void TheFixtureStillCarriesTheTwoFaultsItCaught()
        {
            DumpAudit.Result result = DumpAudit.Run(Fixture());

            DumpAudit.CheckResult stages = Check(result, "a grid's stages fit");
            Assert.Equal(1, stages.Hits);
            Assert.Contains("Large Grid 1784", stages.Worst);

            DumpAudit.CheckResult ordered = Check(result, "a block type's temperatures");
            Assert.True(ordered.Hits > 300, "peak below max on " + ordered.Hits + " types");

            foreach (DumpAudit.CheckResult check in result.Checks)
            {
                if (check == stages || check == ordered) continue;
                Assert.False(check.Failed, check.Name + " failed: " + check.Worst);
            }
        }

        /// <summary>
        /// The two open questions the fixture is evidence for. Both are counted rather than failed,
        /// and both are here so that a change which quietly resolves — or worsens — one of them
        /// cannot pass unnoticed.
        /// </summary>
        [Fact]
        public void TheFixtureCarriesTheTwoOpenObservations()
        {
            DumpAudit.Result result = DumpAudit.Run(Fixture());

            // B18: the engine's figure scales the wind rather than bounding it, once the vertical
            // profile multiplies the band share above the reference height.
            DumpAudit.CheckResult ceiling = Check(result, "wind stays under");
            Assert.Equal(3, ceiling.Hits);
            Assert.Contains("80.29", ceiling.Worst);

            // B15: slope wind is a near-ground term and behaves like one.
            DumpAudit.CheckResult slope = Check(result, "slope wind");
            Assert.Equal(2, slope.Hits);
        }
    }
}
