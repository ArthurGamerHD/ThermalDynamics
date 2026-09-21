using Thermodynamics.Core;
using Xunit;

namespace Thermodynamics.Tests
{
    public class RescanGateTests
    {
        private const int Interval = 240;

/// <summary>Run operation.</summary>
        private static int Run(RescanGate gate, int version, int steps)
        {
            int scans = 0;
            for (int i = 0; i < steps; i++)
            {
                if (gate.Due(version, 1)) scans++;
            }

            return scans;
        }

        [Fact]
/// <summary>TheFirstCheckScans operation.</summary>
        public void TheFirstCheckScans()
        {
/// <summary>RescanGate operation.</summary>
            RescanGate gate = new RescanGate(Interval);

            Assert.True(gate.Due(0, 1));
            Assert.True(gate.HasScanned);
        }

        [Fact]
/// <summary>NothingScansAgainUntilTheIntervalHasPassed operation.</summary>
        public void NothingScansAgainUntilTheIntervalHasPassed()
        {
/// <summary>RescanGate operation.</summary>
            RescanGate gate = new RescanGate(Interval);
            gate.Due(0, 1);

            Assert.Equal(0, Run(gate, 1, Interval - 1));
            Assert.True(gate.Due(1, 1));
        }

        [Fact]
/// <summary>AnUnchangedSubjectIsNeverScannedTwice operation.</summary>
        public void AnUnchangedSubjectIsNeverScannedTwice()
        {
/// <summary>RescanGate operation.</summary>
            RescanGate gate = new RescanGate(Interval);

            Assert.Equal(1, Run(gate, 7, Interval * 20));
            Assert.True(gate.Skipped > 0);
        }

        [Fact]
/// <summary>AChangedSubjectIsScannedAtTheNextInterval operation.</summary>
        public void AChangedSubjectIsScannedAtTheNextInterval()
        {
/// <summary>RescanGate operation.</summary>
            RescanGate gate = new RescanGate(Interval);
            Run(gate, 7, Interval * 4);

            Assert.Equal(1, Run(gate, 8, Interval + 1));
        }

        [Fact]
/// <summary>ASubjectChangingConstantlyIsStillHeldToTheCadence operation.</summary>
        public void ASubjectChangingConstantlyIsStillHeldToTheCadence()
        {
/// <summary>RescanGate operation.</summary>
            RescanGate gate = new RescanGate(Interval);

            int scans = 0;
            for (int i = 0; i < Interval * 10; i++)
            {
                if (gate.Due(i, 1)) scans++;
            }

            Assert.InRange(scans, 9, 11);
        }

        [Fact]
/// <summary>GoingIdleMakesTheNextReaderWaitForNothing operation.</summary>
        public void GoingIdleMakesTheNextReaderWaitForNothing()
        {
/// <summary>RescanGate operation.</summary>
            RescanGate gate = new RescanGate(Interval);
            Run(gate, 3, Interval * 2);

            gate.Idle();

            Assert.False(gate.HasScanned);
            Assert.True(gate.Due(3, 1));
        }

        [Fact]
/// <summary>AForcedScanSatisfiesTheCadence operation.</summary>
        public void AForcedScanSatisfiesTheCadence()
        {
/// <summary>RescanGate operation.</summary>
            RescanGate gate = new RescanGate(Interval);
            gate.Mark(12);

            Assert.Equal(0, Run(gate, 12, Interval * 3));
            Assert.True(gate.HasScanned);
        }

        [Fact]
/// <summary>AMultiStepTickAdvancesByAllOfIt operation.</summary>
        public void AMultiStepTickAdvancesByAllOfIt()
        {
/// <summary>RescanGate operation.</summary>
            RescanGate gate = new RescanGate(Interval);
            gate.Due(0, 1);

            Assert.Equal(0, gate.Due(1, Interval - 4) ? 1 : 0);
            Assert.True(gate.Due(1, 8));
        }

        [Fact]
/// <summary>AnIntervalOfZeroScansEveryChange operation.</summary>
        public void AnIntervalOfZeroScansEveryChange()
        {
/// <summary>RescanGate operation.</summary>
            RescanGate gate = new RescanGate(0);

            Assert.True(gate.Due(1, 1));
            Assert.False(gate.Due(1, 1));
            Assert.True(gate.Due(2, 1));
        }
    }
}
