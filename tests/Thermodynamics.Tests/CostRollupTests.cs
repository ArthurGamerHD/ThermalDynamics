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
            // Save and load are raised by the engine outside the frame, so they are roots too.
            Assert.Equal(1000.0, Thermodynamics.CostRollup.MeasuredMilliseconds(900.0, 40.0, 60.0));
        }

        [Fact]
        public void WorkNestedInsideTheFrameIsNotChargedTwice()
        {
            // A session where the frame is almost entirely grid simulation — the shape of every
            // field dump. The total must be the frame, not the frame plus what it contains.
            const double sessionFrame = 221719.10;
            const double gridSimulation = 218419.39;

            double total = Thermodynamics.CostRollup.MeasuredMilliseconds(sessionFrame, 19.81, 47.33);

            Assert.True(total < sessionFrame + gridSimulation);
            Assert.Equal(221786.24, total, 2);
        }

        [Fact]
        public void AModCannotCostMoreTimeThanThereWas()
        {
            // The failing case in miniature: a mod using 60 % of real time whose frame is nearly
            // all simulation reported 120 %, a figure that cannot be true of a single thread.
            double total = Thermodynamics.CostRollup.MeasuredMilliseconds(600.0, 0.0, 0.0);

            Assert.Equal(60.0, Thermodynamics.CostRollup.ShareOfRealTime(total, 1.0), 6);
        }

        [Fact]
        public void TheShareIsUndefinedBeforeTheClockHasRun()
        {
            Assert.True(Thermodynamics.CostRollup.ShareOfRealTime(100.0, 0.0) < 0.0);
        }
    }
}
