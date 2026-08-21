using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The cost table's total, and the nesting rule behind it.
    ///
    /// The report measures the same milliseconds at several depths: the session frame wraps the
    /// call that drives every grid, grid simulation sits inside it, and the solver and the mapping
    /// passes sit inside that. Adding an inner row to the outer one it lives in charges the same
    /// work twice — which is what the table did for grid simulation, reporting a field dump that
    /// cost 36.8 % of real time as costing 73.1 %.
    ///
    /// These tests exist because the arithmetic has no other guard: a report is written from a live
    /// session and read by eye, and a total that is twice the truth still looks like a plausible
    /// total.
    /// </summary>
    public class CostRollupTests
    {
        [Fact]
        public void TheTotalIsTheRootsOnly()
        {
            // Save, load and a grid's one-off build are all raised by the engine outside the
            // session frame, so all three are roots too.
            Assert.Equal(1000.0, Thermodynamics.CostRollup.MeasuredMilliseconds(880.0, 40.0, 60.0, 20.0));
        }

        [Fact]
        public void WorkNestedInsideTheFrameIsNotChargedTwice()
        {
            // A session where the frame is almost entirely grid simulation — the shape of every
            // field dump. The total must be the frame, not the frame plus what it contains.
            const double sessionFrame = 221719.10;
            const double gridSimulation = 218419.39;

            double total = Thermodynamics.CostRollup.MeasuredMilliseconds(sessionFrame, 19.81, 47.33, 0.0);

            Assert.True(total < sessionFrame + gridSimulation);
            Assert.Equal(221786.24, total, 2);
        }

        [Fact]
        public void AModCannotCostMoreTimeThanThereWas()
        {
            // The failing case in miniature: a mod using 60 % of real time whose frame is nearly
            // all simulation reported 120 %, a figure that cannot be true of a single thread.
            double total = Thermodynamics.CostRollup.MeasuredMilliseconds(600.0, 0.0, 0.0, 0.0);

            Assert.Equal(60.0, Thermodynamics.CostRollup.ShareOfRealTime(total, 1.0), 6);
        }

        /// <summary>
        /// The other half of the nesting rule. Not charging an inner row twice is what keeps the
        /// total honest; knowing what the inner rows leave over is what keeps the *breakdown*
        /// honest, and until the observation around a step was timed the breakdown left an eighth
        /// of grid simulation unexplained.
        /// </summary>
        [Fact]
        public void WhatTheChildrenDoNotClaimIsReportedRatherThanLost()
        {
            // The 2026-08-20 fleet dump: grid simulation against the stages that were timed at the
            // time. An eighth of it belonged to nothing.
            const double gridSimulation = 39540.00;
            double timed = 521.40 + 260.05 + 189.43 + 33627.24;

            double unattributed = Thermodynamics.CostRollup.Unattributed(gridSimulation, timed);

            Assert.Equal(4941.88, unattributed, 2);
            Assert.True(unattributed / gridSimulation > 0.12);
        }

        /// <summary>
        /// Two stages timing the same milliseconds is a defect in the instrumentation. Clamping it
        /// at zero would hide exactly the case the row exists to expose.
        /// </summary>
        [Fact]
        public void DoubleTimedWorkShowsAsNegativeRatherThanZero()
        {
            Assert.Equal(-10.0, Thermodynamics.CostRollup.Unattributed(20.0, 30.0), 6);
        }

        /// <summary>
        /// A grid's one-off build runs from the entity's own callback, so nothing else in the table
        /// contains it. It was left out of the total entirely — the mod under-reporting itself by
        /// the largest single call any grid ever makes.
        /// </summary>
        [Fact]
        public void TheOneOffBuildIsARootAndReachesTheTotal()
        {
            double without = Thermodynamics.CostRollup.MeasuredMilliseconds(900.0, 0.0, 0.0, 0.0);
            double with = Thermodynamics.CostRollup.MeasuredMilliseconds(900.0, 0.0, 0.0, 179.1);

            Assert.Equal(179.1, with - without, 6);
        }

        [Fact]
        public void TheShareIsUndefinedBeforeTheClockHasRun()
        {
            Assert.True(Thermodynamics.CostRollup.ShareOfRealTime(100.0, 0.0) < 0.0);
        }
    }
}
