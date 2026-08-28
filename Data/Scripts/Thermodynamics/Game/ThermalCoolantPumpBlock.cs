using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SENetworkAPI;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Thermodynamics
{
    /// <summary>
    /// The game-side half of a coolant pump: its switch and its speed setting, carried into
    /// <see cref="Core.CoolantPump"/>. <c>Enabled</c> is the block's own functional state, which the
    /// game replicates; the speed does not exist in the game's model at all, so it is one
    /// <see cref="NetSync{T}"/> declared here, where its address is the same on every side.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false,
        "Gauge_LG_CoolantPump", "Gauge_SG_CoolantPump")]
    public class ThermalCoolantPumpBlock : MyGameLogicComponent
    {
        private static readonly MyStringHash SinkGroup = MyStringHash.GetOrCompute("Utility");

        private IMyFunctionalBlock functional;
        private IMyCubeBlock block;
        private MyResourceSinkComponent sink;

        /// <summary>
        /// Speed the terminal is set to, 0..1, replicated both ways: a client turns the dial and
        /// the server is the one whose value the simulation reads.
        /// </summary>
        private NetSync<float> speed;

        /// <summary>
        /// Whether the block is switched on and working. A pump that is off or damaged past
        /// functional drives nothing — the ring keeps its coolant and stops carrying it anywhere.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                if (functional == null) return false;
                return functional.Enabled && functional.IsFunctional;
            }
        }

        /// <summary>Speed setting, 0..1. Defaults to full, so a pump built and left alone works.</summary>
        public float Speed
        {
            get { return speed == null ? 1f : Clamp01(speed.Value); }
        }

        /// <summary>Sets the speed and replicates it. Called by the terminal slider.</summary>
        public void SetSpeed(float value)
        {
            if (speed == null) return;
            speed.Value = Clamp01(value);
        }

        /// <summary>
        /// Fraction of the electricity it asked for that the grid supplied, 0..1.
        ///
        /// An under-supplied pump circulates proportionally slower rather than stopping, which is
        /// what <c>CoolantPump.Contribution</c> does with it: a ship whose reactors are failing
        /// loses its cooling gradually.
        /// </summary>
        public float PowerAvailable
        {
            get
            {
                if (sink == null) return 1f;

                float demand = DemandMegawatts();
                if (demand <= 0f) return 1f;

                float supplied = sink.CurrentInputByType(MyResourceDistributorComponent.ElectricityId);
                if (supplied <= 0f) return 0f;

                float ratio = supplied / demand;
                return ratio > 1f ? 1f : ratio;
            }
        }

        /// <summary>Most electricity this block draws at full speed, W.</summary>
        public float MaxPowerWatts
        {
            get
            {
                return block != null && block.BlockDefinition.SubtypeName != null
                    && block.BlockDefinition.SubtypeName.StartsWith("Gauge_SG_")
                    ? ThermalCoolantShapes.SmallGridPumpWatts
                    : ThermalCoolantShapes.LargeGridPumpWatts;
            }
        }

        /// <summary>
        /// What refilling this pump's ring is asking for, W, published every step by
        /// <c>ThermalGridSimulation</c> from <c>CoolantLoop.RefillDemandWatts</c>. Zero on a full
        /// ring, and zero on every pump but the one the refill is charged to.
        ///
        /// <para>
        /// **It has to be inside the request, not only inside the bill.** The refill's watts were
        /// added to the block's *drawn* power — which is what turns them into heat through the
        /// pump's own `ConsumerWasteEnergy` — and never asked of the distributor, so a ship with no
        /// power to spare refilled its rings anyway and paid only in heat. See backlog.md `B44`.
        /// </para>
        /// </summary>
        private float refillDemandWatts;

        /// <summary>Sets what the ring's refill is asking for, W. The host's to publish.</summary>
        public void SetRefillDemandWatts(float watts)
        {
            refillDemandWatts = watts > 0f ? watts : 0f;
        }

        /// <summary>What the refill is asking for, W, as the terminal and the tests read it.</summary>
        public float RefillDemandWatts
        {
            get { return IsRunning ? refillDemandWatts : 0f; }
        }

        /// <summary>
        /// What it is asking for now, in the resource system's units: circulation plus whatever the
        /// ring's refill wants. Circulation is linear in speed for the reason
        /// `CoolantPump.DemandWatts` gives — a cubed affinity law against a square-root flow makes
        /// ten idling pumps a hundredth the price of one working.
        ///
        /// **A pump that is off asks for nothing and therefore refills nothing**, which is the
        /// answer `CoolantLoop.HasDrivingPump` gives on the other side of the same rule.
        /// </summary>
        private float DemandMegawatts()
        {
            if (!IsRunning) return 0f;

            float watts = (MaxPowerWatts * Clamp01(Speed)) + refillDemandWatts;
            return watts * ThermalConstants.WattsToMegawatts;
        }

        /// <summary>
        /// The ceiling the sink is constructed with: full circulation plus the most a refill can
        /// ask for. **A ceiling under the real request is a request the distributor quietly
        /// trims**, and at the shipped fluid a refill is 18.9 kW — 38 % of a large-grid pump's
        /// rating and nearly twice a small one's — far too much to leave outside it.
        ///
        /// Read from the world's own settings where they exist, because both halves are dials.
        /// </summary>
        private float MaxDrawMegawatts()
        {
            float clock = Settings.Instance == null
                ? new ThermalSettings().HeatTimeScale
                : Settings.Instance.HeatTimeScale;

            float refill = LoopThermalProperties.Default().RefillWattsAt(clock);

            return (MaxPowerWatts + refill) * ThermalConstants.WattsToMegawatts;
        }

        /// <summary>
        /// Attaches the sink that makes a loop cost something.
        ///
        /// **The heat comes free with it.** `ThermalBlock` already subscribes to whatever resource
        /// sink an entity carries and turns its draw into `PowerConsumedWatts`, which the waste-heat
        /// model converts through the block's own `ConsumerWasteEnergy` — so wiring the cost wires
        /// *a cooling system costs power and makes heat doing it* with no second path.
        /// See document-of-intent.md, A cooling system costs power, and makes heat doing it.
        /// </summary>
        private void AttachSink()
        {
            // A block that already carries a sink keeps it: a second sink for the same resource
            // would bill the block twice.
            if (Entity.Components.Contains(typeof(MyResourceSinkComponent))) return;

            MyResourceSinkInfo info = new MyResourceSinkInfo
            {
                ResourceTypeId = MyResourceDistributorComponent.ElectricityId,
                MaxRequiredInput = MaxDrawMegawatts(),
                RequiredInputFunc = DemandMegawatts,
            };

            sink = new MyResourceSinkComponent();
            sink.Init(SinkGroup, info);

            Entity.Components.Add<MyResourceSinkComponent>(sink);
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            functional = Entity as IMyFunctionalBlock;
            block = Entity as IMyCubeBlock;

            try
            {
                if (!NetworkAPI.IsInitialized)
                {
                    NetworkAPI.Init(Session.ModID, Settings.Name);
                }

                // One property, so its index on this entity is zero on every side. Anything added
                // here later must go after it.
                speed = new NetSync<float>(this, TransferType.Both, 1f);

                AttachSink();
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalCoolantPumpBlock.Init", e);
            }
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
