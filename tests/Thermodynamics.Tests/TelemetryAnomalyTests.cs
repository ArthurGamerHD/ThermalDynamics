using System.Collections.Generic;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The telemetry module's judgement about what counts as a problem worth reporting.
    ///
    /// Both directions matter. A missed anomaly loses the only evidence of a solver fault that
    /// a player's session will ever produce; a false one buries the real findings under noise
    /// from ordinary gameplay.
    /// </summary>
    public class TelemetryAnomalyTests
    {
        private const float Implausible = 20000f;

        private static TelemetryAnomalyKind Classify(float temperature, float last)
        {
            return TelemetryAnomalies.Classify(temperature, last, Implausible);
        }

        [Theory]
        [InlineData(2.7f, 2.7f)]        // a block soaking in vacuum
        [InlineData(293.15f, 293.1f)]   // room temperature, warming slightly
        [InlineData(1200f, 1150f)]      // a hot reactor, well short of implausible
        [InlineData(0f, 0f)]            // never simulated, and still not simulated
        [InlineData(19999.9f, 100f)]    // just under the threshold
        public void OrdinaryTemperaturesAreNotAnomalies(float temperature, float last)
        {
            Assert.Equal(TelemetryAnomalyKind.None, Classify(temperature, last));
        }

        [Fact]
        public void NotANumberIsCaught()
        {
            Assert.Equal(TelemetryAnomalyKind.NotANumber, Classify(float.NaN, 300f));
        }

        [Fact]
        public void BothInfinitiesAreCaught()
        {
            Assert.Equal(TelemetryAnomalyKind.Infinite, Classify(float.PositiveInfinity, 300f));
            Assert.Equal(TelemetryAnomalyKind.Infinite, Classify(float.NegativeInfinity, 300f));
        }

        [Fact]
        public void AFiniteButAbsurdTemperatureIsCaught()
        {
            Assert.Equal(TelemetryAnomalyKind.Implausible, Classify(20000.1f, 300f));
            Assert.Equal(TelemetryAnomalyKind.Implausible, Classify(1e30f, 300f));
        }

        [Fact]
        public void TheThresholdItselfIsNotAnAnomaly()
        {
            Assert.Equal(TelemetryAnomalyKind.None, Classify(Implausible, 300f));
        }

        [Fact]
        public void FallingToZeroFromHeatIsCaughtAsAClamp()
        {
            // The solver's Math.Max(0, T) hides a negative excursion. That excursion is the
            // signature of an unstable step, so the clamp is worth reporting.
            Assert.Equal(TelemetryAnomalyKind.ClampedToZero, Classify(0f, 500f));
            Assert.Equal(TelemetryAnomalyKind.ClampedToZero, Classify(0f, 0.001f));
        }

        [Fact]
        public void AColdBlockThatStaysColdIsNotAClamp()
        {
            // Otherwise every unsimulated block on every grid would report on every update.
            Assert.Equal(TelemetryAnomalyKind.None, Classify(0f, 0f));
        }

        [Fact]
        public void NotANumberWinsOverEveryOtherReading()
        {
            // NaN compares false against every threshold, so the order of the checks is what
            // stops it being silently classified as ordinary.
            Assert.Equal(TelemetryAnomalyKind.NotANumber, Classify(float.NaN, float.NaN));
        }

        [Fact]
        public void EveryKindHasItsOwnStableName()
        {
            TelemetryAnomalyKind[] kinds =
            {
                TelemetryAnomalyKind.NotANumber,
                TelemetryAnomalyKind.Infinite,
                TelemetryAnomalyKind.Implausible,
                TelemetryAnomalyKind.ClampedToZero
            };

            HashSet<string> names = new HashSet<string>();
            foreach (TelemetryAnomalyKind kind in kinds)
            {
                string name = TelemetryAnomalies.Name(kind, Implausible);

                Assert.False(string.IsNullOrEmpty(name));
                Assert.True(names.Add(name), "two kinds share the name " + name);
            }
        }

        [Fact]
        public void TheImplausibleNameCarriesTheThresholdInInvariantForm()
        {
            string name = TelemetryAnomalies.Name(TelemetryAnomalyKind.Implausible, 20000f);

            // The name is a dictionary key that groups occurrences, so it must not vary with the
            // client's locale.
            Assert.Equal("temperature above 20000K", name);
        }
    }

    /// <summary>
    /// The stride that keeps the wide per-block-type statistics off the hot path.
    /// </summary>
    public class SampleGateTests
    {
        private static int AdmittedOutOf(int stride, int calls)
        {
            SampleGate gate = new SampleGate { Stride = stride };

            int admitted = 0;
            for (int i = 0; i < calls; i++)
            {
                if (gate.Admit()) admitted++;
            }

            return admitted;
        }

        [Fact]
        public void AStrideOfOneAdmitsEveryCall()
        {
            Assert.Equal(1000, AdmittedOutOf(1, 1000));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(10)]
        [InlineData(64)]
        public void AStrideOfNAdmitsExactlyOneCallInN(int stride)
        {
            const int calls = 1000;

            // The first call is admitted, so the count rounds up rather than down.
            Assert.Equal((calls + stride - 1) / stride, AdmittedOutOf(stride, calls));
        }

        [Fact]
        public void TheFirstCallIsAlwaysAdmitted()
        {
            // A grid that is destroyed after a handful of updates should still contribute a
            // sample rather than nothing at all.
            SampleGate gate = new SampleGate { Stride = 1000 };

            Assert.True(gate.Admit());
        }

        [Fact]
        public void AdmittedCallsAreEvenlySpaced()
        {
            SampleGate gate = new SampleGate { Stride = 4 };

            List<int> admitted = new List<int>();
            for (int i = 0; i < 20; i++)
            {
                if (gate.Admit()) admitted.Add(i);
            }

            Assert.Equal(new List<int> { 0, 4, 8, 12, 16 }, admitted);
        }

        [Fact]
        public void AnInvalidStrideFallsBackToSamplingEverything()
        {
            // Silently sampling nothing would be the worst outcome of a bad config value.
            Assert.Equal(1, new SampleGate { Stride = 0 }.Stride);
            Assert.Equal(1, new SampleGate { Stride = -5 }.Stride);
            Assert.Equal(100, AdmittedOutOf(0, 100));
        }

        [Fact]
        public void ADefaultGateSamplesEverythingUntilAStrideIsSet()
        {
            SampleGate gate = new SampleGate();

            Assert.Equal(1, gate.Stride);
            Assert.True(gate.Admit());
            Assert.True(gate.Admit());
        }

        [Fact]
        public void ResettingMakesTheNextCallAdmit()
        {
            SampleGate gate = new SampleGate { Stride = 10 };
            gate.Admit();

            Assert.False(gate.Admit());

            gate.Reset();

            Assert.True(gate.Admit());
        }
    }

    /// <summary>
    /// The always-on health check: whether a whole grid has gone numerically bad, decided from
    /// three figures the solver already publishes rather than by walking it.
    ///
    /// This exists because per-node classification runs only on the sampling walk, and the walk
    /// runs only while collection is on — which is never, by default. The one class of bug worst
    /// worth catching, a grid that goes NaN or runs away, was the one nothing in an ordinary
    /// world was looking for. A grid welded past its buffer capacity did exactly that, whole.
    /// </summary>
    public class GridHealthTests
    {
        private const float Implausible = 20000f;

        private static TelemetryAnomalyKind Check(float environmentWatts, float gainWatts, float hottest)
        {
            return TelemetryAnomalies.ClassifyGrid(environmentWatts, gainWatts, hottest, Implausible);
        }

        [Theory]
        [InlineData(-50000f, 50000f, 400f)]     // a ship in balance
        [InlineData(0f, 0f, 2.7f)]              // cold and idle in vacuum
        [InlineData(120000f, 0f, 330f)]         // absorbing more than it sheds, in sunlight
        [InlineData(-1e9f, 1e9f, 19999f)]       // enormous, and still finite and below the threshold
        public void AWorkingGridIsNotAFault(float environmentWatts, float gainWatts, float hottest)
        {
            Assert.Equal(TelemetryAnomalyKind.None, Check(environmentWatts, gainWatts, hottest));
        }

        /// <summary>
        /// The watt sums are accumulated over every node on the stepping path, so a NaN anywhere on
        /// the grid reaches them. That is what makes a three-float test a whole-grid check.
        /// </summary>
        [Fact]
        public void ANanInEitherWattSumFailsTheGrid()
        {
            Assert.Equal(TelemetryAnomalyKind.NotANumber, Check(float.NaN, 1000f, 400f));
            Assert.Equal(TelemetryAnomalyKind.NotANumber, Check(-1000f, float.NaN, 400f));
        }

        [Fact]
        public void ANanTemperatureFailsTheGrid()
        {
            Assert.Equal(TelemetryAnomalyKind.NotANumber, Check(-1000f, 1000f, float.NaN));
        }

        [Theory]
        [InlineData(float.PositiveInfinity, 1000f, 400f)]
        [InlineData(float.NegativeInfinity, 1000f, 400f)]
        [InlineData(-1000f, float.PositiveInfinity, 400f)]
        [InlineData(-1000f, 1000f, float.PositiveInfinity)]
        public void AnInfinityInAnyOfTheThreeFailsTheGrid(float environmentWatts, float gainWatts, float hottest)
        {
            Assert.Equal(TelemetryAnomalyKind.Infinite, Check(environmentWatts, gainWatts, hottest));
        }

        /// <summary>
        /// NaN is reported ahead of infinity when both are present, because NaN is the one that
        /// spreads: an infinity minus an infinity is a NaN, so a grid showing both went NaN first.
        /// </summary>
        [Fact]
        public void NotANumberIsReportedAheadOfInfinity()
        {
            Assert.Equal(TelemetryAnomalyKind.NotANumber, Check(float.PositiveInfinity, float.NaN, 400f));
        }

        [Fact]
        public void ARunawayTemperatureFailsTheGrid()
        {
            Assert.Equal(TelemetryAnomalyKind.Implausible, Check(-1000f, 1000f, Implausible + 1f));
        }

        /// <summary>
        /// A grid with no nodes reports a hottest temperature of zero, which must not read as a
        /// fault — every grid is in that state for its first step, and a paste preview never
        /// leaves it.
        /// </summary>
        [Fact]
        public void AGridWithNoNodesIsNotAFault()
        {
            Assert.Equal(TelemetryAnomalyKind.None, Check(0f, 0f, 0f));
        }

        /// <summary>
        /// The names are dictionary keys grouping every occurrence of the same problem, and they
        /// must not collide with the per-node names or a grid fault and a node reading would merge
        /// into one record.
        /// </summary>
        [Fact]
        public void GridKindsAreNamedApartFromNodeKinds()
        {
            foreach (TelemetryAnomalyKind kind in new TelemetryAnomalyKind[]
            {
                TelemetryAnomalyKind.NotANumber,
                TelemetryAnomalyKind.Infinite,
                TelemetryAnomalyKind.Implausible,
            })
            {
                string grid = TelemetryAnomalies.GridName(kind, Implausible);
                string node = TelemetryAnomalies.Name(kind, Implausible);

                Assert.False(string.IsNullOrEmpty(grid));
                Assert.NotEqual(node, grid);
            }
        }
    }

}
