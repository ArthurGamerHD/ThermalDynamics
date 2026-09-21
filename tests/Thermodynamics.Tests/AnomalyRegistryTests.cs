using System.Collections.Generic;
using Xunit;

namespace Thermodynamics.Tests
{
    public class AnomalyRegistryTests
    {
        private const bool Collecting = true;
        private const bool NotCollecting = false;
        private const bool Fault = true;
        private const bool Observation = false;

/// <summary>Registry operation.</summary>
        private static AnomalyRegistry Registry(int maxKinds = 64)
        {
            return new AnomalyRegistry(maxKinds);
        }

        [Fact]
/// <summary>AFaultIsRecordedWhileCollectionIsOff operation.</summary>
        public void AFaultIsRecordedWhileCollectionIsOff()
        {
/// <summary>Registry operation.</summary>
            AnomalyRegistry registry = Registry();

            registry.Record("exception in ThermalGrid.Tick", "NullReferenceException", Fault, NotCollecting, 12.5d);

            Assert.Equal(1, registry.Count);
            Assert.Single(registry.Faults());
        }

        [Fact]
/// <summary>AnObservationIsDroppedWhileCollectionIsOff operation.</summary>
        public void AnObservationIsDroppedWhileCollectionIsOff()
        {
/// <summary>Registry operation.</summary>
            AnomalyRegistry registry = Registry();

            registry.Record("temperature is NaN", "grid / block", Observation, NotCollecting, 12.5d);

            Assert.Equal(0, registry.Count);
        }

        [Fact]
/// <summary>AnObservationIsRecordedWhileCollectionIsOn operation.</summary>
        public void AnObservationIsRecordedWhileCollectionIsOn()
        {
/// <summary>Registry operation.</summary>
            AnomalyRegistry registry = Registry();

            registry.Record("temperature is NaN", "grid / block", Observation, Collecting, 12.5d);

            Assert.Equal(1, registry.Count);
            Assert.Empty(registry.Faults());
        }

        [Fact]
/// <summary>OnlyTheFirstOccurrenceOfAFaultAsksToBeLogged operation.</summary>
        public void OnlyTheFirstOccurrenceOfAFaultAsksToBeLogged()
        {
/// <summary>Registry operation.</summary>
            AnomalyRegistry registry = Registry();

            Assert.True(registry.Record("exception in Tick", "first", Fault, NotCollecting, 1d));
            Assert.False(registry.Record("exception in Tick", "second", Fault, NotCollecting, 2d));
            Assert.False(registry.Record("exception in Tick", "third", Fault, NotCollecting, 3d));

            AnomalyRecord record = registry.Faults()[0];
            Assert.Equal(3L, record.Count);
        }

        [Fact]
/// <summary>AnObservationNeverAsksToBeLogged operation.</summary>
        public void AnObservationNeverAsksToBeLogged()
        {
/// <summary>Registry operation.</summary>
            AnomalyRegistry registry = Registry();

            Assert.False(registry.Record("temperature is NaN", "first", Observation, Collecting, 1d));
        }

        [Fact]
/// <summary>TheFirstAndLastExampleOfAKindAreBothKept operation.</summary>
        public void TheFirstAndLastExampleOfAKindAreBothKept()
        {
/// <summary>Registry operation.</summary>
            AnomalyRegistry registry = Registry();

            registry.Record("exception in Tick", "first", Fault, NotCollecting, 1d);
            registry.Record("exception in Tick", "middle", Fault, NotCollecting, 2d);
            registry.Record("exception in Tick", "last", Fault, NotCollecting, 9d);

            AnomalyRecord record = registry.Faults()[0];

            Assert.Equal("first", record.FirstExample);
            Assert.Equal("last", record.LastExample);
            Assert.Equal(1d, record.FirstSeconds);
            Assert.Equal(9d, record.LastSeconds);
        }

        [Fact]
/// <summary>TheRegistryIsBoundedAndSaysWhatItDropped operation.</summary>
        public void TheRegistryIsBoundedAndSaysWhatItDropped()
        {
/// <summary>Registry operation.</summary>
            AnomalyRegistry registry = Registry(2);

            registry.Record("a", "x", Fault, NotCollecting, 1d);
            registry.Record("b", "x", Fault, NotCollecting, 1d);
            registry.Record("c", "x", Fault, NotCollecting, 1d);
            registry.Record("d", "x", Observation, Collecting, 1d);

            Assert.Equal(2, registry.Count);
            Assert.Equal(2L, registry.KindsDropped);
        }

        [Fact]
/// <summary>AKindSeenAsBothIsTreatedAsAFault operation.</summary>
        public void AKindSeenAsBothIsTreatedAsAFault()
        {
/// <summary>Registry operation.</summary>
            AnomalyRegistry registry = Registry();

            registry.Record("mixed", "observation", Observation, Collecting, 1d);
            registry.Record("mixed", "fault", Fault, Collecting, 2d);

            Assert.Single(registry.Faults());
        }

        [Fact]
/// <summary>ThereIsNoSummaryWhenNothingFailed operation.</summary>
        public void ThereIsNoSummaryWhenNothingFailed()
        {
/// <summary>Registry operation.</summary>
            AnomalyRegistry registry = Registry();

            registry.Record("temperature is NaN", "x", Observation, Collecting, 1d);

            Assert.Null(registry.FaultSummary(Collecting));
        }

        [Fact]
/// <summary>TheSummaryCarriesEveryFaultItsCountAndHowToGetMore operation.</summary>
        public void TheSummaryCarriesEveryFaultItsCountAndHowToGetMore()
        {
/// <summary>Registry operation.</summary>
            AnomalyRegistry registry = Registry();

            for (int i = 0; i < 7; i++)
            {
                registry.Record("exception in ThermalGrid.Tick", "boom", Fault, NotCollecting, i);
            }
            registry.Record("exception in ThermalGrid.Save", "bang", Fault, NotCollecting, 20d);

            string summary = registry.FaultSummary(NotCollecting);

            Assert.Contains("2 fault kind(s)", summary);
            Assert.Contains("7x exception in ThermalGrid.Tick", summary);
            Assert.Contains("1x exception in ThermalGrid.Save", summary);
            Assert.Contains("EnableTelemetry", summary);
        }

        [Fact]
/// <summary>TheSummaryDoesNotAskForTelemetryThatIsAlreadyOn operation.</summary>
        public void TheSummaryDoesNotAskForTelemetryThatIsAlreadyOn()
        {
/// <summary>Registry operation.</summary>
            AnomalyRegistry registry = Registry();
            registry.Record("exception in Tick", "boom", Fault, Collecting, 1d);

            Assert.DoesNotContain("EnableTelemetry", registry.FaultSummary(Collecting));
        }

        [Fact]
/// <summary>FaultsAreSummarisedInTheOrderTheyFirstAppeared operation.</summary>
        public void FaultsAreSummarisedInTheOrderTheyFirstAppeared()
        {
/// <summary>Registry operation.</summary>
            AnomalyRegistry registry = Registry();

            registry.Record("second", "x", Fault, NotCollecting, 50d);
            registry.Record("first", "x", Fault, NotCollecting, 10d);

            List<AnomalyRecord> faults = registry.Faults();

            Assert.Equal("first", faults[0].Kind);
            Assert.Equal("second", faults[1].Kind);
        }

        [Fact]
/// <summary>ClearingDropsEverythingIncludingTheDropCount operation.</summary>
        public void ClearingDropsEverythingIncludingTheDropCount()
        {
/// <summary>Registry operation.</summary>
            AnomalyRegistry registry = Registry(1);

            registry.Record("a", "x", Fault, NotCollecting, 1d);
            registry.Record("b", "x", Fault, NotCollecting, 1d);
            registry.Clear();

            Assert.Equal(0, registry.Count);
            Assert.Equal(0L, registry.KindsDropped);
            Assert.Null(registry.FaultSummary(NotCollecting));
        }
    }
}
