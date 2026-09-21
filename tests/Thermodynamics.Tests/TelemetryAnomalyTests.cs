using System.Collections.Generic;
using Xunit;

namespace Thermodynamics.Tests
{
    public class TelemetryAnomalyTests
    {
        private const float Implausible = 20000f;

/// <summary>Classify operation.</summary>
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
/// <summary>OrdinaryTemperaturesAreNotAnomalies operation.</summary>
        public void OrdinaryTemperaturesAreNotAnomalies(float temperature, float last)
        {
            Assert.Equal(TelemetryAnomalyKind.None, Classify(temperature, last));
        }

        [Fact]
/// <summary>NotANumberIsCaught operation.</summary>
        public void NotANumberIsCaught()
        {
            Assert.Equal(TelemetryAnomalyKind.NotANumber, Classify(float.NaN, 300f));
        }

        [Fact]
/// <summary>BothInfinitiesAreCaught operation.</summary>
        public void BothInfinitiesAreCaught()
        {
            Assert.Equal(TelemetryAnomalyKind.Infinite, Classify(float.PositiveInfinity, 300f));
            Assert.Equal(TelemetryAnomalyKind.Infinite, Classify(float.NegativeInfinity, 300f));
        }

        [Fact]
/// <summary>AFiniteButAbsurdTemperatureIsCaught operation.</summary>
        public void AFiniteButAbsurdTemperatureIsCaught()
        {
            Assert.Equal(TelemetryAnomalyKind.Implausible, Classify(20000.1f, 300f));
            Assert.Equal(TelemetryAnomalyKind.Implausible, Classify(1e30f, 300f));
        }

        [Fact]
/// <summary>TheThresholdItselfIsNotAnAnomaly operation.</summary>
        public void TheThresholdItselfIsNotAnAnomaly()
        {
            Assert.Equal(TelemetryAnomalyKind.None, Classify(Implausible, 300f));
        }

        [Fact]
/// <summary>FallingToZeroFromHeatIsCaughtAsAClamp operation.</summary>
        public void FallingToZeroFromHeatIsCaughtAsAClamp()
        {
            Assert.Equal(TelemetryAnomalyKind.ClampedToZero, Classify(0f, 500f));
            Assert.Equal(TelemetryAnomalyKind.ClampedToZero, Classify(0f, 0.001f));
        }

        [Fact]
/// <summary>AColdBlockThatStaysColdIsNotAClamp operation.</summary>
        public void AColdBlockThatStaysColdIsNotAClamp()
        {
            Assert.Equal(TelemetryAnomalyKind.None, Classify(0f, 0f));
        }

        [Fact]
/// <summary>NotANumberWinsOverEveryOtherReading operation.</summary>
        public void NotANumberWinsOverEveryOtherReading()
        {
            Assert.Equal(TelemetryAnomalyKind.NotANumber, Classify(float.NaN, float.NaN));
        }

        [Fact]
/// <summary>EveryKindHasItsOwnStableName operation.</summary>
        public void EveryKindHasItsOwnStableName()
        {
            TelemetryAnomalyKind[] kinds =
            {
                TelemetryAnomalyKind.NotANumber,
                TelemetryAnomalyKind.Infinite,
                TelemetryAnomalyKind.Implausible,
                TelemetryAnomalyKind.ClampedToZero
            };

/// <summary>HashSet operation.</summary>
            HashSet<string> names = new HashSet<string>();
            foreach (TelemetryAnomalyKind kind in kinds)
            {
                string name = TelemetryAnomalies.Name(kind, Implausible);

                Assert.False(string.IsNullOrEmpty(name));
                Assert.True(names.Add(name), "two kinds share the name " + name);
            }
        }

        [Fact]
/// <summary>TheImplausibleNameCarriesTheThresholdInInvariantForm operation.</summary>
        public void TheImplausibleNameCarriesTheThresholdInInvariantForm()
        {
            string name = TelemetryAnomalies.Name(TelemetryAnomalyKind.Implausible, 20000f);

            Assert.Equal("temperature above 20000K", name);
        }
    }

    public class SampleGateTests
    {
/// <summary>AdmittedOutOf operation.</summary>
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
/// <summary>AStrideOfOneAdmitsEveryCall operation.</summary>
        public void AStrideOfOneAdmitsEveryCall()
        {
            Assert.Equal(1000, AdmittedOutOf(1, 1000));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(10)]
        [InlineData(64)]
/// <summary>AStrideOfNAdmitsExactlyOneCallInN operation.</summary>
        public void AStrideOfNAdmitsExactlyOneCallInN(int stride)
        {
            const int calls = 1000;

            Assert.Equal((calls + stride - 1) / stride, AdmittedOutOf(stride, calls));
        }

        [Fact]
/// <summary>TheFirstCallIsAlwaysAdmitted operation.</summary>
        public void TheFirstCallIsAlwaysAdmitted()
        {
            SampleGate gate = new SampleGate { Stride = 1000 };

            Assert.True(gate.Admit());
        }

        [Fact]
/// <summary>AdmittedCallsAreEvenlySpaced operation.</summary>
        public void AdmittedCallsAreEvenlySpaced()
        {
            SampleGate gate = new SampleGate { Stride = 4 };

/// <summary>List operation.</summary>
            List<int> admitted = new List<int>();
            for (int i = 0; i < 20; i++)
            {
                if (gate.Admit()) admitted.Add(i);
            }

            Assert.Equal(new List<int> { 0, 4, 8, 12, 16 }, admitted);
        }

        [Fact]
/// <summary>AnInvalidStrideFallsBackToSamplingEverything operation.</summary>
        public void AnInvalidStrideFallsBackToSamplingEverything()
        {
            Assert.Equal(1, new SampleGate { Stride = 0 }.Stride);
            Assert.Equal(1, new SampleGate { Stride = -5 }.Stride);
            Assert.Equal(100, AdmittedOutOf(0, 100));
        }

        [Fact]
/// <summary>ADefaultGateSamplesEverythingUntilAStrideIsSet operation.</summary>
        public void ADefaultGateSamplesEverythingUntilAStrideIsSet()
        {
/// <summary>SampleGate operation.</summary>
            SampleGate gate = new SampleGate();

            Assert.Equal(1, gate.Stride);
            Assert.True(gate.Admit());
            Assert.True(gate.Admit());
        }

        [Fact]
/// <summary>ResettingMakesTheNextCallAdmit operation.</summary>
        public void ResettingMakesTheNextCallAdmit()
        {
            SampleGate gate = new SampleGate { Stride = 10 };
            gate.Admit();

            Assert.False(gate.Admit());

            gate.Reset();

            Assert.True(gate.Admit());
        }
    }

    public class GridHealthTests
    {
        private const float Implausible = 20000f;

/// <summary>Check operation.</summary>
        private static TelemetryAnomalyKind Check(float environmentWatts, float gainWatts, float hottest)
        {
            return TelemetryAnomalies.ClassifyGrid(environmentWatts, gainWatts, hottest, Implausible);
        }

        [Theory]
        [InlineData(-50000f, 50000f, 400f)]     // a ship in balance
        [InlineData(0f, 0f, 2.7f)]              // cold and idle in vacuum
        [InlineData(120000f, 0f, 330f)]         // absorbing more than it sheds, in sunlight
        [InlineData(-1e9f, 1e9f, 19999f)]       // enormous, and still finite and below the threshold
/// <summary>AWorkingGridIsNotAFault operation.</summary>
        public void AWorkingGridIsNotAFault(float environmentWatts, float gainWatts, float hottest)
        {
            Assert.Equal(TelemetryAnomalyKind.None, Check(environmentWatts, gainWatts, hottest));
        }

        [Fact]
/// <summary>ANanInEitherWattSumFailsTheGrid operation.</summary>
        public void ANanInEitherWattSumFailsTheGrid()
        {
            Assert.Equal(TelemetryAnomalyKind.NotANumber, Check(float.NaN, 1000f, 400f));
            Assert.Equal(TelemetryAnomalyKind.NotANumber, Check(-1000f, float.NaN, 400f));
        }

        [Fact]
/// <summary>ANanTemperatureFailsTheGrid operation.</summary>
        public void ANanTemperatureFailsTheGrid()
        {
            Assert.Equal(TelemetryAnomalyKind.NotANumber, Check(-1000f, 1000f, float.NaN));
        }

        [Theory]
        [InlineData(float.PositiveInfinity, 1000f, 400f)]
        [InlineData(float.NegativeInfinity, 1000f, 400f)]
        [InlineData(-1000f, float.PositiveInfinity, 400f)]
        [InlineData(-1000f, 1000f, float.PositiveInfinity)]
/// <summary>AnInfinityInAnyOfTheThreeFailsTheGrid operation.</summary>
        public void AnInfinityInAnyOfTheThreeFailsTheGrid(float environmentWatts, float gainWatts, float hottest)
        {
            Assert.Equal(TelemetryAnomalyKind.Infinite, Check(environmentWatts, gainWatts, hottest));
        }

        [Fact]
/// <summary>NotANumberIsReportedAheadOfInfinity operation.</summary>
        public void NotANumberIsReportedAheadOfInfinity()
        {
            Assert.Equal(TelemetryAnomalyKind.NotANumber, Check(float.PositiveInfinity, float.NaN, 400f));
        }

        [Fact]
/// <summary>ARunawayTemperatureFailsTheGrid operation.</summary>
        public void ARunawayTemperatureFailsTheGrid()
        {
            Assert.Equal(TelemetryAnomalyKind.Implausible, Check(-1000f, 1000f, Implausible + 1f));
        }

        [Fact]
/// <summary>AGridWithNoNodesIsNotAFault operation.</summary>
        public void AGridWithNoNodesIsNotAFault()
        {
            Assert.Equal(TelemetryAnomalyKind.None, Check(0f, 0f, 0f));
        }

        [Fact]
/// <summary>GridKindsAreNamedApartFromNodeKinds operation.</summary>
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
