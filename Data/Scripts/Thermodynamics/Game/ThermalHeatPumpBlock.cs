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
    /// <summary>
    /// The electrical half of a heat pump: it gives the block a resource sink, reports the demand the
    /// solver derived to it, and reads back what the grid supplied. The sink is created in
    /// <see cref="Init"/> rather than declared, because an upgrade module has no definition field for
    /// one. See known-issues.md, Open defects.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false,
        "Gauge_LG_HeatPump", "Gauge_SG_HeatPump")]
    public class ThermalHeatPumpBlock : MyGameLogicComponent
    {
        private static readonly MyStringHash SinkGroup = MyStringHash.GetOrCompute("Utility");

        private IMyCubeBlock block;
        private IMyFunctionalBlock functional;
        private MyResourceSinkComponent sink;

        /// <summary>
        /// How much of its rating the pump is allowed to draw, 0..1, replicated both ways. Carried here
        /// because the game has no model for a custom slider's value.
        /// </summary>
        private NetSync<float> powerSetting;

        /// <summary>
        /// What the pump requested on its last step, MW, which is the unit the resource system uses.
        /// Read by the sink through a callback, so it must be a field that callback can capture.
        /// </summary>
        private float demandMegawatts;

        /// <summary>Watts the pump wants. Set by <see cref="ThermalGrid"/> after each step.</summary>
        public void SetDemandWatts(float watts)
        {
            float megawatts = watts > 0f ? watts * ThermalConstants.WattsToMegawatts : 0f;

            // Update the resource system only when the figure changed. A pump holding steady is the
            // common case, and re-registering its draw every step is wasted work.
            if (Math.Abs(megawatts - demandMegawatts) < 0.000001f) return;

            demandMegawatts = megawatts;
            if (sink != null) sink.Update();
        }

        /// <summary>
        /// Whether the block is switched on and working. A pump that is off, damaged past
        /// functional or unpowered moves nothing.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                if (functional == null) return false;
                if (!functional.Enabled || !functional.IsFunctional) return false;
                return true;
            }
        }

        /// <summary>
        /// Fraction of the requested electricity the grid supplied, 0..1. An under-supplied pump runs
        /// proportionally slower rather than stopping.
        /// </summary>
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

        /// <summary>Throttle setting, 0..1. Defaults to full rating.</summary>
        public float PowerSetting
        {
            get { return powerSetting == null ? 1f : Clamp01(powerSetting.Value); }
        }

        /// <summary>Sets the throttle and replicates it. Called by the terminal slider.</summary>
        public void SetPowerSetting(float value)
        {
            if (powerSetting == null) return;
            powerSetting.Value = Clamp01(value);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

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

                // One property, so its index on this entity is zero on every side. Anything added
                // here later must go after it.
                powerSetting = new NetSync<float>(this, TransferType.Both, 1f);

                AttachSink();
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalHeatPumpBlock.Init", e);
            }
        }

        private void AttachSink()
        {
            // A block that already carries a sink keeps it: a second sink for the same resource
            // would bill the block twice.
            if (Entity.Components.Contains(typeof(MyResourceSinkComponent))) return;

            MyResourceSinkInfo info = new MyResourceSinkInfo
            {
                ResourceTypeId = MyResourceDistributorComponent.ElectricityId,
                MaxRequiredInput = MaxDrawMegawatts(),
                RequiredInputFunc = RequiredInput,
            };

            sink = new MyResourceSinkComponent();
            sink.Init(SinkGroup, info);

            Entity.Components.Add<MyResourceSinkComponent>(sink);
        }

        /// <summary>
        /// The block's rated draw, in the resource system's units. The sink needs a ceiling at
        /// construction, and it must be the hardware's rating rather than the current request.
        /// </summary>
        private float MaxDrawMegawatts()
        {
            return ThermalHeatPumpShapes.MaxPowerWatts(BlockSubtype()) * ThermalConstants.WattsToMegawatts;
        }

        private string BlockSubtype()
        {
            return block == null || block.BlockDefinition.SubtypeName == null
                ? ""
                : block.BlockDefinition.SubtypeName;
        }

        private float RequiredInput()
        {
            return IsRunning ? demandMegawatts : 0f;
        }
    }
}
