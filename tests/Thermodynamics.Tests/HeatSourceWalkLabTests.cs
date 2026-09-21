using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    public class HeatSourceWalkLabTests
    {
        [Fact]
/// <summary>MoreSourcesCostMoreAndTheStepIsAlwaysTimed operation.</summary>
        public void MoreSourcesCostMoreAndTheStepIsAlwaysTimed()
        {
            List<HeatSourceWalkLab.Row> rows = HeatSourceWalkLab.Run("ship", 4000, 4);

            Assert.Equal(HeatSourceWalkLab.SourceCounts.Length, rows.Count);
            double baseMs = 0d;
            foreach (HeatSourceWalkLab.Row row in rows)
            {
                Assert.True(row.StepMs > 0, row.Sources + " sources: the step was never timed");
                Assert.True(row.Substeps > 0, row.Sources + " sources: no substeps ran");
                if (row.Sources == 0) baseMs = row.StepMs;
            }

            HeatSourceWalkLab.Row high = null;
            foreach (HeatSourceWalkLab.Row row in rows) { if (row.Sources == 32) high = row; }
            Assert.NotNull(high);
            Assert.True(high.StepMs >= baseMs,
/// <summary>sources operation.</summary>
                "32 sources (" + high.StepMs + " ms) cost less than none (" + baseMs
                + " ms); the sources are not reaching the solver");
        }

        [Fact]
/// <summary>ARegisteredSourceActuallyHeatsANode operation.</summary>
        public void ARegisteredSourceActuallyHeatsANode()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", 2000));
            ThermalSimulation warm = builder.BuildSimulation(Hulls.Uncapped(), 293.15f);
            warm.Solver.CollectDiagnostics = true;
            warm.StepExact(1, Worlds.DarkVacuumWithSources(4));

            float mostLit = 0f;
            IList<ThermalNode> nodes = warm.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].LastHeatSourceWatts > mostLit) mostLit = nodes[i].LastHeatSourceWatts;
            }
            Assert.True(mostLit > 0f, "no node received any heat-source watts, so the fixture injects nothing");
        }
    }
}
