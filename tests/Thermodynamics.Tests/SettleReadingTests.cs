using System.Collections.Generic;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **What `SecondsToSettle` cannot see**, pinned so that fixing it fails a test rather than
    /// moving a number nobody is watching (`D5`).
    ///
    /// <para>
    /// The figure is *seconds until the hottest block came within 5 K of where it ended*. That is a
    /// settling time only when the block moved: a hull that barely changed over the whole run is
    /// inside 5 K of its final value at the first sample, so it reports the floor — one chunk — and
    /// **a hull that has not begun reads exactly like one that has finished**.
    /// </para>
    ///
    /// <para>
    /// This is not the limitation <see cref="ScenarioOutcome.BulkDriftKelvinPerSecond"/> already
    /// records. That one is *one block settled while the hull behind it is still cooling*, and the
    /// drift is the guard against it. This one is *nothing moved at all*, which the drift does not
    /// catch either: a hull that has not started moving has a small drift for the same reason it
    /// has a small excursion.
    /// </para>
    ///
    /// <para>
    /// Found by running the paired grid at low `HeatTimeScale`, where a hull in shadow cools so
    /// slowly that 26 to 36 of 40 report the floor, and the median settling time falls from 3,450 s
    /// at clock 56 to 120 s at clock 25 — a discontinuity in the direction that says *faster*, on
    /// the dial that makes everything slower. It is why `G8`'s settling half is scored where a hull
    /// is driven somewhere rather than at idle.
    /// See balance-lab.md, G8.
    /// </para>
    /// </summary>
    public class SettleReadingTests
    {
        /// <summary>
        /// The smallest answer the rule can give. The scan starts at the second sample, so it is
        /// two chunks rather than one — and 120 s is what a hull that has not begun reports.
        /// </summary>
        private const float Floor = 2f * Battery.Chunk;

        /// <summary>
        /// A trace that never moved reports the first chunk rather than "no settling event here".
        /// </summary>
        [Fact]
        public void ATraceThatNeverMovedReportsTheFloor()
        {
            List<float> flat = new List<float>();
            for (int i = 0; i < 30; i++) flat.Add(293.15f);

            // Two chunks, not one: the scan starts at the second sample, so the earliest answer
            // the rule can give is 120 s — which is exactly the value 26 to 36 of 40 hulls a cell
            // reported in the grid, and the reason that column was recognisable as a floor.
            Assert.Equal(Floor, Battery.SettleSeconds(flat, 293.15f), 1);
        }

        /// <summary>
        /// A trace that drifted by less than the tolerance over the whole run reports the floor
        /// too, which is the case the grid actually hit — a hull at clock 11 in shadow moves about
        /// four kelvin in two hours.
        /// </summary>
        [Fact]
        public void ATraceThatDriftedLessThanTheToleranceAlsoReportsTheFloor()
        {
            List<float> creeping = new List<float>();
            for (int i = 0; i < 120; i++) creeping.Add(297.0f - i * (4f / 120f));

            float final = creeping[creeping.Count - 1];
            Assert.True(creeping[0] - final < Battery.SettledWithinOfFinal,
                "the rig is supposed to move less than the tolerance");
            Assert.Equal(Floor, Battery.SettleSeconds(creeping, final), 1);
        }

        /// <summary>
        /// A trace that actually settled reports where it settled, which is what makes the two
        /// cases above a blind spot rather than the metric working as intended.
        /// </summary>
        [Fact]
        public void ATraceThatSettledReportsWhereItSettled()
        {
            List<float> cooling = new List<float>();
            for (int i = 0; i < 30; i++)
            {
                // 100 K of excursion, decaying: inside 5 K of the end somewhere in the middle.
                cooling.Add(200f + 100f * (float)System.Math.Exp(-i / 5.0));
            }

            float settled = Battery.SettleSeconds(cooling, cooling[cooling.Count - 1]);

            Assert.True(settled > Floor,
                "a trace with a real excursion should settle later than the first chunk; it "
                + "reported " + settled);
            Assert.True(settled < 30 * Battery.Chunk, "and it should settle before the run ends");
        }

        /// <summary>
        /// A trace still moving when the run ended reports no settling time at all. This is the
        /// reading a walk has to treat as *did not settle* rather than as a missing value.
        /// </summary>
        [Fact]
        public void ATraceStillClimbingAtTheEndReportsNothing()
        {
            List<float> climbing = new List<float>();
            for (int i = 0; i < 30; i++) climbing.Add(293.15f + i * 20f);

            Assert.Equal(-1f, Battery.SettleSeconds(climbing, climbing[climbing.Count - 1] + 40f), 1);
        }
    }
}
