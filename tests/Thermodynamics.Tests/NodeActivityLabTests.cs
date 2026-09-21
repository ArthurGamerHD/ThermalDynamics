using System.Collections.Generic;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    public class NodeActivityLabTests
    {
        private static NodeActivityLab.Result result;

/// <summary>Result operation.</summary>
        private static NodeActivityLab.Result Result()
        {
            if (result == null) result = NodeActivityLab.Run(4000, null, new[] { 10, 50, 200 });
            return result;
        }

        [Fact]
/// <summary>EveryRowJudgesTheWholeGridAndThresholdsAreMonotone operation.</summary>
        public void EveryRowJudgesTheWholeGridAndThresholdsAreMonotone()
        {
/// <summary>Result operation.</summary>
            List<NodeActivityLab.Row> rows = Result().Rows;
            Assert.True(rows.Count >= 6, "expected marks for two scenarios, got " + rows.Count + " rows");

            foreach (NodeActivityLab.Row row in rows)
            {
                Assert.True(row.Nodes > 1000, row.Scenario + " judged only " + row.Nodes + " nodes (`E8`)");
                Assert.True(row.Links > 1000, row.Scenario + " judged only " + row.Links + " links (`E8`)");

                for (int t = 1; t < NodeActivityLab.Thresholds.Length; t++)
                {
                    Assert.True(row.QuietNodes[t] >= row.QuietNodes[t - 1],
                        row.Scenario + " step " + row.AtStep + ": a looser threshold reported fewer quiet nodes");
                    Assert.True(row.QuietLinks[t] >= row.QuietLinks[t - 1],
                        row.Scenario + " step " + row.AtStep + ": a looser threshold reported fewer quiet links");
                }

                if (row.QuietNodes[1] == 0)
                {
                    Assert.True(row.QuietLinks[1] == 0,
                        row.Scenario + " step " + row.AtStep + ": quiet links with no quiet nodes");
                }
            }
        }

        [Fact]
/// <summary>TheDrivenHullIsBusierThanTheParkedOne operation.</summary>
        public void TheDrivenHullIsBusierThanTheParkedOne()
        {
/// <summary>Find operation.</summary>
            NodeActivityLab.Row parked = Find("parked in air", 200);
/// <summary>Find operation.</summary>
            NodeActivityLab.Row driven = Find("driven in vacuum", 200);

            double parkedQuiet = (double)parked.QuietNodes[1] / parked.Nodes;
            double drivenQuiet = (double)driven.QuietNodes[1] / driven.Nodes;

            Assert.True(drivenQuiet <= parkedQuiet,
/// <summary>quieter operation.</summary>
                "the driven hull reads quieter (" + drivenQuiet + ") than the parked one (" + parkedQuiet
                + ") at the millikelvin threshold; the scenarios are not measuring what their names say");
        }

        [Fact]
/// <summary>TheDisturbanceWakesSomethingAndTheLabSaysHowMuch operation.</summary>
        public void TheDisturbanceWakesSomethingAndTheLabSaysHowMuch()
        {
/// <summary>Result operation.</summary>
            List<NodeActivityLab.WavefrontRow> wavefront = Result().Wavefront;
            Assert.Equal(50, wavefront.Count);

            Assert.True(wavefront[0].ActiveNodes >= 1,
                "one step after a 300 K disturbance nothing was active");

            int worst = 0;
            foreach (NodeActivityLab.WavefrontRow row in wavefront)
            {
                if (row.ActiveNodes > worst) worst = row.ActiveNodes;
            }
            Assert.True(worst < Result().WavefrontNodes,
                "the disturbance woke the whole grid, so the wavefront claim judged nothing");
        }

/// <summary>Find operation.</summary>
        private static NodeActivityLab.Row Find(string scenario, int step)
        {
            foreach (NodeActivityLab.Row row in Result().Rows)
            {
                if (row.Scenario == scenario && row.AtStep == step) return row;
            }
            throw new Xunit.Sdk.XunitException("no row for " + scenario + " at step " + step);
        }
    }
}
