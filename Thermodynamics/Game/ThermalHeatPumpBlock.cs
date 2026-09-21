using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SENetworkAPI;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Thermodynamics
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false,
        "Gauge_LG_HeatPump", "Gauge_SG_HeatPump")]
    public class ThermalHeatPumpBlock : MyGameLogicComponent
    {
        private static readonly MyStringHash SinkGroup = MyStringHash.GetOrCompute("Utility");

        private IMyCubeBlock block;
        private IMyFunctionalBlock functional;
        private MyResourceSinkComponent sink;

        private NetSync<float> powerSetting;

        private float demandMegawatts;

/// <summary>Sets the demandwatts.</summary>
        public void SetDemandWatts(float watts)
        {
            float megawatts = watts > 0f ? watts * ThermalConstants.WattsToMegawatts : 0f;

            if (Math.Abs(megawatts - demandMegawatts) < 0.000001f) return;

            demandMegawatts = megawatts;
            if (sink != null) sink.Update();
        }

        public bool IsRunning
        {
            get
            {
                if (functional == null) return false;
                if (!functional.Enabled || !functional.IsFunctional) return false;
                return true;
            }
        }

        public float PowerAvailable
        {
            get
            {
                if (sink == null) return 0f;
                if (demandMegawatts <= 0f) return 1f;

                float supplied = sink.CurrentInputByType(MyResourceDistributorComponent.ElectricityId);
                if (supplied <= 0f) return 0f;

                float ratio = supplied / demandMegawatts;
                if (ratio > 1f) return 1f;
                return ratio;
            }
        }

        public float PowerSetting
        {
            get { return powerSetting == null ? 1f : ThermalMath.Clamp01(powerSetting.Value); }
        }

/// <summary>Sets the powersetting.</summary>
        public void SetPowerSetting(float value)
        {
            if (powerSetting == null) return;
            powerSetting.Value = ThermalMath.Clamp01(value);
        }

/// <summary>Init operation.</summary>
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            block = Entity as IMyCubeBlock;
            functional = Entity as IMyFunctionalBlock;
            if (block == null) return;

            try
            {
                if (!NetworkAPI.IsInitialized)
                {
                    NetworkAPI.Init(Session.ModID, Settings.Name);
                }

/// <summary>NetSync operation.</summary>
                powerSetting = new NetSync<float>(this, TransferType.Both, 1f);

                AttachSink();
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalHeatPumpBlock.Init", e);
            }
        }

/// <summary>AttachSink operation.</summary>
        private void AttachSink()
        {
            if (Entity.Components.Contains(typeof(MyResourceSinkComponent))) return;

            MyResourceSinkInfo info = new MyResourceSinkInfo
            {
                ResourceTypeId = MyResourceDistributorComponent.ElectricityId,
/// <summary>MaxDrawMegawatts operation.</summary>
                MaxRequiredInput = MaxDrawMegawatts(),
                RequiredInputFunc = RequiredInput,
            };

/// <summary>MyResourceSinkComponent operation.</summary>
            sink = new MyResourceSinkComponent();
            sink.Init(SinkGroup, info);

            Entity.Components.Add<MyResourceSinkComponent>(sink);
        }

/// <summary>MaxDrawMegawatts operation.</summary>
        private float MaxDrawMegawatts()
        {
            return ThermalHeatPumpShapes.MaxPowerWatts(BlockSubtype()) * ThermalConstants.WattsToMegawatts;
        }

/// <summary>BlockSubtype operation.</summary>
        private string BlockSubtype()
        {
            return block == null || block.BlockDefinition.SubtypeName == null
                ? ""
                : block.BlockDefinition.SubtypeName;
        }

/// <summary>RequiredInput operation.</summary>
        private float RequiredInput()
        {
            return IsRunning ? demandMegawatts : 0f;
        }
    }
}
