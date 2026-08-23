using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// A coolant loop costs power to run, and it is very much cheaper than a heat pump.
    ///
    /// <para>
    /// `CoolantPump.MaxPowerWatts` was declared and assigned nowhere, so a loop was free once built
    /// while a heat pump paid for every watt it lifted. The asymmetry is right — a circulator is
    /// not a refrigerator — but nothing had ever said so, and the mod's own intent is that *a
    /// cooling system costs power and makes heat doing it*. [backlog](../../docs/backlog.md) `C13`.
    /// </para>
    ///
    /// <para>
    /// The rating is derived rather than chosen: a large-grid ring moves 200 kg/s of coolant, and
    /// pushing that against two bar of head at seventy per cent efficiency is `ṁ ΔP / (ρ η)` =
    /// 57 kW. What these check is the *shape* — that it is charged at all, linear in speed, off
    /// when the block is, and small against what the ring carries.
    /// </para>
    /// </summary>
    public class PumpPowerTests
    {
        [Fact]
        public void APumpAsksForPowerInProportionToItsSpeed()
        {
            CoolantPump pump = new CoolantPump { MaxPowerWatts = 50000f };

            Assert.Equal(50000f, pump.DemandWatts, 1);

            pump.Speed = 0.5f;
            Assert.Equal(25000f, pump.DemandWatts, 1);

            pump.Speed = 0f;
            Assert.Equal(0f, pump.DemandWatts, 1);
        }

        /// <summary>
        /// A pump that is switched off or broken asks for nothing. It is the same test the flow
        /// contribution passes, and it has to be the same answer or a stopped pump would be billed.
        /// </summary>
        [Fact]
        public void AStoppedPumpAsksForNothing()
        {
            CoolantPump pump = new CoolantPump { MaxPowerWatts = 50000f, Enabled = false };

            Assert.Equal(0f, pump.DemandWatts, 1);
            Assert.Equal(0f, pump.Contribution, 3);
        }

        /// <summary>
        /// An under-supplied pump circulates proportionally slower rather than stopping, so a ship
        /// whose reactors are failing loses its cooling gradually.
        /// </summary>
        [Fact]
        public void AnUnderSuppliedPumpCirculatesSlowerRatherThanStopping()
        {
            CoolantPump pump = new CoolantPump { MaxPowerWatts = 50000f };

            Assert.Equal(1f, pump.Contribution, 3);

            pump.PowerAvailable = 0.4f;
            Assert.Equal(0.4f, pump.Contribution, 3);

            pump.PowerAvailable = 0f;
            Assert.Equal(0f, pump.Contribution, 3);
        }

        /// <summary>
        /// **The contrast the two blocks exist for.** A circulator costs a small fraction of what
        /// it moves; a heat pump costs a third. Both figures come from the shipped ratings rather
        /// than from prose, so the claim moves if either does.
        /// </summary>
        [Fact]
        public void ACirculatorIsFarCheaperPerWattMovedThanAHeatPump()
        {
            LoopThermalProperties loop = new LoopThermalProperties();

            // What a large-grid ring carries per kelvin of difference around it: parcels a second,
            // times the coolant in one, times its specific heat.
            const float ParcelMetres = 2.5f;
            float parcelsPerSecond = loop.FlowRateFor(ParcelMetres) / ParcelMetres;
            float wattsPerKelvin = parcelsPerSecond * loop.CoolantMassPerPipe * loop.SpecificHeat;

            Assert.True(wattsPerKelvin > 100000f,
                "a ring should carry hundreds of kilowatts per kelvin: " + wattsPerKelvin);

            // Even at one kelvin of difference the pump is a small fraction of what it moves.
            float pumpShare = 50000f / wattsPerKelvin;
            Assert.True(pumpShare < 0.2f,
                "a circulator costing " + (pumpShare * 100f).ToString("n1")
                + "% of one kelvin's worth of transport is not a circulator");

            // Against a heat pump, which pays 20 kW to lift 60 kW.
            HeatPumpShape shape = HeatPumpShape.Centred(Vector3I.Forward, Vector3I.One, 60000f, 20000f);
            float lift = shape.RatedWatts / shape.MaxPowerWatts;

            Assert.True(lift < 1f / pumpShare,
                "the heat pump should be the expensive way to move heat: it lifts " + lift
                + " W per watt against the loop's " + (1f / pumpShare));
        }
    }
}
