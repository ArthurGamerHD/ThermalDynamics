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
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false,
        "Gauge_LG_CoolantPump", "Gauge_SG_CoolantPump")]
    public class ThermalCoolantPumpBlock : MyGameLogicComponent
    {
        private static readonly MyStringHash SinkGroup = MyStringHash.GetOrCompute("Utility");

        private IMyFunctionalBlock functional;
        private IMyCubeBlock block;
        private MyResourceSinkComponent sink;

        private NetSync<float> speed;

        public bool IsRunning
        {
            get
            {
                if (functional == null) return false;
                return functional.Enabled && functional.IsFunctional;
            }
        }

        public float Speed
        {
            get { return speed == null ? 1f : ThermalMath.Clamp01(speed.Value); }
        }

        public void SetSpeed(float value)
        {
            if (speed == null) return;
            speed.Value = ThermalMath.Clamp01(value);
        }

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

        private float refillDemandWatts;

        public void SetRefillDemandWatts(float watts)
        {
            refillDemandWatts = watts > 0f ? watts : 0f;
        }

        public float RefillDemandWatts
        {
            get { return IsRunning ? refillDemandWatts : 0f; }
        }

        private float DemandMegawatts()
        {
            if (!IsRunning) return 0f;

            float watts = (MaxPowerWatts * ThermalMath.Clamp01(Speed)) + refillDemandWatts;
            return watts * ThermalConstants.WattsToMegawatts;
        }

        private float MaxDrawMegawatts()
        {
            float clock = Settings.Instance == null
                ? new ThermalSettings().HeatTimeScale
                : Settings.Instance.HeatTimeScale;

            float refill = LoopThermalProperties.Default().RefillWattsAt(clock);

            return (MaxPowerWatts + refill) * ThermalConstants.WattsToMegawatts;
        }

        private void AttachSink()
        {
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

                speed = new NetSync<float>(this, TransferType.Both, 1f);

                AttachSink();
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalCoolantPumpBlock.Init", e);
            }
        }
    }
}
