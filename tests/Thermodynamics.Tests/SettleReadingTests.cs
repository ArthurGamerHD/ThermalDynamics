using System.Collections.Generic;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    public class SettleReadingTests
    {
        private const float Floor = 2f * Battery.Chunk;

        [Fact]
/// <summary>ATraceThatNeverMovedReportsTheFloor operation.</summary>
        public void ATraceThatNeverMovedReportsTheFloor()
        {
/// <summary>List operation.</summary>
            List<float> flat = new List<float>();
            for (int i = 0; i < 30; i++) flat.Add(293.15f);

            Assert.Equal(Floor, Battery.SettleSeconds(flat, 293.15f), 1);
        }

        [Fact]
/// <summary>ATraceThatDriftedLessThanTheToleranceAlsoReportsTheFloor operation.</summary>
        public void ATraceThatDriftedLessThanTheToleranceAlsoReportsTheFloor()
        {
/// <summary>List operation.</summary>
            List<float> creeping = new List<float>();
            for (int i = 0; i < 120; i++) creeping.Add(297.0f - i * (4f / 120f));

            float final = creeping[creeping.Count - 1];
            Assert.True(creeping[0] - final < Battery.SettledWithinOfFinal,
                "the rig is supposed to move less than the tolerance");
            Assert.Equal(Floor, Battery.SettleSeconds(creeping, final), 1);
        }

        [Fact]
/// <summary>ATraceThatSettledReportsWhereItSettled operation.</summary>
        public void ATraceThatSettledReportsWhereItSettled()
        {
/// <summary>List operation.</summary>
            List<float> cooling = new List<float>();
            for (int i = 0; i < 30; i++)
            {
                cooling.Add(200f + 100f * (float)System.Math.Exp(-i / 5.0));
            }

            float settled = Battery.SettleSeconds(cooling, cooling[cooling.Count - 1]);

            Assert.True(settled > Floor,
                "a trace with a real excursion should settle later than the first chunk; it "
                + "reported " + settled);
            Assert.True(settled < 30 * Battery.Chunk, "and it should settle before the run ends");
        }

        [Fact]
/// <summary>ATraceStillClimbingAtTheEndReportsNothing operation.</summary>
        public void ATraceStillClimbingAtTheEndReportsNothing()
        {
/// <summary>List operation.</summary>
            List<float> climbing = new List<float>();
            for (int i = 0; i < 30; i++) climbing.Add(293.15f + i * 20f);

            Assert.Equal(-1f, Battery.SettleSeconds(climbing, climbing[climbing.Count - 1] + 40f), 1);
        }
    }
}
