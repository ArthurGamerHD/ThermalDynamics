using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class PumpPowerTests
    {
        [Fact]
/// <summary>APumpAsksForPowerInProportionToItsSpeed operation.</summary>
        public void APumpAsksForPowerInProportionToItsSpeed()
        {
            CoolantPump pump = new CoolantPump { MaxPowerWatts = 50000f };

            Assert.Equal(50000f, pump.DemandWatts, 1);

            pump.Speed = 0.5f;
            Assert.Equal(25000f, pump.DemandWatts, 1);

            pump.Speed = 0f;
            Assert.Equal(0f, pump.DemandWatts, 1);
        }

        [Fact]
/// <summary>AStoppedPumpAsksForNothing operation.</summary>
        public void AStoppedPumpAsksForNothing()
        {
            CoolantPump pump = new CoolantPump { MaxPowerWatts = 50000f, Enabled = false };

            Assert.Equal(0f, pump.DemandWatts, 1);
            Assert.Equal(0f, pump.Contribution, 3);
        }

        [Fact]
/// <summary>AnUnderSuppliedPumpCirculatesSlowerRatherThanStopping operation.</summary>
        public void AnUnderSuppliedPumpCirculatesSlowerRatherThanStopping()
        {
            CoolantPump pump = new CoolantPump { MaxPowerWatts = 50000f };

            Assert.Equal(1f, pump.Contribution, 3);

            pump.PowerAvailable = 0.4f;
            Assert.Equal(0.4f, pump.Contribution, 3);

            pump.PowerAvailable = 0f;
            Assert.Equal(0f, pump.Contribution, 3);
        }

        [Fact]
/// <summary>ACirculatorIsFarCheaperPerWattMovedThanAHeatPump operation.</summary>
        public void ACirculatorIsFarCheaperPerWattMovedThanAHeatPump()
        {
/// <summary>LoopThermalProperties operation.</summary>
            LoopThermalProperties loop = new LoopThermalProperties();

            const float ParcelMetres = 2.5f;
            float parcelsPerSecond = loop.FlowRateFor(ParcelMetres) / ParcelMetres;
            float wattsPerKelvin =
                parcelsPerSecond * loop.MassPerPipe(ParcelMetres) * loop.SpecificHeat;

            Assert.True(wattsPerKelvin > 100000f,
                "a ring should carry hundreds of kilowatts per kelvin: " + wattsPerKelvin);

            float pumpShare = 50000f / wattsPerKelvin;
            Assert.True(pumpShare < 0.2f,
                "a circulator costing " + (pumpShare * 100f).ToString("n1")
                + "% of one kelvin's worth of transport is not a circulator");

            HeatPumpShape shape = HeatPumpShape.Centred(Vector3I.Forward, Vector3I.One, 60000f, 20000f);
            float lift = shape.RatedWatts / shape.MaxPowerWatts;

            Assert.True(lift < 1f / pumpShare,
                "the heat pump should be the expensive way to move heat: it lifts " + lift
                + " W per watt against the loop's " + (1f / pumpShare));
        }
    }
}
