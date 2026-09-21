using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    [Collection("alone")]
    public class SelfShadowScenarioTests
    {
/// <summary>Vector3 operation.</summary>
        private static readonly Vector3 Sun = new Vector3(0.9004f, 0.1619f, -0.4038f);

        [Fact]
/// <summary>EveryDirectionMatchesGeometryWorkedOutIndependently operation.</summary>
        public void EveryDirectionMatchesGeometryWorkedOutIndependently()
        {
            ScenarioResult result = Scenarios.Run("self-shadow");
            ThermalSimulation simulation = result.Runner.Simulation;
            SunShadowMap shadow = simulation.Solver.SunShadow;

            Assert.True(shadow.IsBuilt, "the pass should have completed inside the run");

            GridModel grid = simulation.Grid;

            for (int face = 0; face < Face.Count; face++)
            {
                int exposed = 0;
                int lit = 0;
                int expected = 0;

                IList<BlockInstance> blocks = grid.Blocks;
                for (int i = 0; i < blocks.Count; i++)
                {
                    Vector3I cell = blocks[i].Min;
                    Vector3I outside = cell + Face.Offsets[face];
                    if (grid.IsOccupied(outside)) continue;

                    exposed++;
                    if (shadow.IsFaceLit(cell, face)) lit++;
                    if (Reference.Lit(grid, outside, Sun)) expected++;
                }

                Assert.True(exposed > 0, Face.Name(face) + " should have exposed faces");
                Assert.Equal(expected, lit);
            }
        }

        [Fact]
/// <summary>TheFaceTurnedToTheSunIsLitWholeAndTheRecessIsDark operation.</summary>
        public void TheFaceTurnedToTheSunIsLitWholeAndTheRecessIsDark()
        {
            ScenarioResult result = Scenarios.Run("self-shadow");
            ThermalSimulation simulation = result.Runner.Simulation;
            SunShadowMap shadow = simulation.Solver.SunShadow;

            for (int y = 0; y < 7; y++)
            {
                for (int z = 0; z < 4; z++)
                {
                    ThermalNode node = simulation.Solver.GetNodeAt(new Vector3I(3, y, z));
                    Assert.NotNull(node);
                    Assert.Equal(1f, shadow.FaceLitFraction(node.Block, Face.Right), 5);
                }
            }

            for (int y = 2; y < 5; y++)
            {
                for (int z = 1; z < 3; z++)
                {
                    ThermalNode node = simulation.Solver.GetNodeAt(new Vector3I(2, y, z));
                    Assert.NotNull(node);
                    Assert.Equal(0f, shadow.FaceLitFraction(node.Block, Face.Left), 5);
                }
            }
        }

        [Fact]
/// <summary>TheHeadlineSharesAreTheOnesTheGeometryImplies operation.</summary>
        public void TheHeadlineSharesAreTheOnesTheGeometryImplies()
        {
            string summary = Scenarios.Run("self-shadow").Summary;

            Assert.Contains("sunward 100%", summary);
            Assert.Contains("recess floor 0%", summary);

            Assert.True(Share(summary, "top ") >= 70f, summary);
            Assert.True(Share(summary, "flank ") >= 70f, summary);
        }

/// <summary>Share operation.</summary>
        private static float Share(string summary, string after)
        {
            int start = summary.IndexOf(after) + after.Length;
            int end = summary.IndexOf('%', start);
            return float.Parse(summary.Substring(start, end - start),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        [Fact]
/// <summary>SelfShadowingTakesSolarGainAwayAndNeverAddsIt operation.</summary>
        public void SelfShadowingTakesSolarGainAwayAndNeverAddsIt()
        {
            ScenarioResult result = Scenarios.Run("self-shadow");

/// <summary>Extract operation.</summary>
            float shadowed = Extract(result.Summary, "self-shadowing ");
/// <summary>Extract operation.</summary>
            float cheap = Extract(result.Summary, "against ");

            Assert.True(shadowed < cheap,
                "self-shadowing should remove solar gain, not add it: " + result.Summary);
        }

        [Fact]
/// <summary>SelfShadowingCostsNothingPerStepBetweenPasses operation.</summary>
        public void SelfShadowingCostsNothingPerStepBetweenPasses()
        {
            string summary = Scenarios.Run("shadow-cost").Summary;

/// <summary>Milliseconds operation.</summary>
            float off = Milliseconds(summary, "self-shadowing off ");
/// <summary>Milliseconds operation.</summary>
            float on = Milliseconds(summary, ", on ");

            Assert.True(on < (off * 1.5f) + 0.005f,
                "per-step cost should be near identical between passes: " + summary);
        }

/// <summary>Milliseconds operation.</summary>
        private static float Milliseconds(string summary, string after)
        {
            int start = summary.IndexOf(after) + after.Length;
            int end = summary.IndexOf(" ms", start);
            return float.Parse(summary.Substring(start, end - start),
                System.Globalization.CultureInfo.InvariantCulture);
        }

/// <summary>Extract operation.</summary>
        private static float Extract(string summary, string after)
        {
            int start = summary.IndexOf(after) + after.Length;
            int end = summary.IndexOf(" kW", start);
            return float.Parse(summary.Substring(start, end - start).Replace(",", ""),
                System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
