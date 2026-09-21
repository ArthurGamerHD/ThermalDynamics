using System.Collections.Generic;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    public class EnvironmentWalkLabTests
    {
        [Fact]
/// <summary>TheFixtureHoldsBothClassesAndBothShapesGetTimed operation.</summary>
        public void TheFixtureHoldsBothClassesAndBothShapesGetTimed()
        {
            List<EnvironmentWalkLab.Row> rows = EnvironmentWalkLab.Run("ship", 4000, 3);

            Assert.Equal(2, rows.Count);
            foreach (EnvironmentWalkLab.Row row in rows)
            {
                Assert.True(row.Nodes > 1000, row.Shape + " judged only " + row.Nodes + " nodes");
                Assert.True(row.ExposedNodes > 0 && row.ExposedNodes < row.Nodes,
                    row.Shape + ": " + row.ExposedNodes + " exposed of " + row.Nodes
/// <summary>about operation.</summary>
                    + " — the fixture is missing one of the two classes the comparison is about (`E8`)");
                Assert.True(row.BestNs > 0, row.Shape + " was never timed");
            }

        }
    }
}
