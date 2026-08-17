using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Thermodynamics
{
    /// <summary>
    /// The electrical half of a heat pump.
    ///
    /// The simulation works out how much power the pump wants — that figure follows from Carnot
    /// and the two temperatures, and only the solver knows those. This component is what turns the
    /// answer into an actual load on the ship: it gives the block a resource sink, tells the sink
    /// what the pump asked for, and reads back how much of it the grid could supply.
    ///
    /// The sink is created here rather than declared in the block definition because Space
    /// Engineers has no definition field for one on an upgrade module. It is added during
    /// <see cref="Init"/>, before the block joins the grid's resource system, which is what gets
    /// it registered with the distributor.
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
        /// What the pump asked for on its last step, MW — the unit the resource system uses.
        /// Read by the sink through a callback, so it has to be a field the callback can see.
        /// </summary>
        private float demandMegawatts;

        /// <summary>Watts the pump wants. Set by <see cref="ThermalGrid"/> after each step.</summary>
        public void SetDemandWatts(float watts)
        {
            float megawatts = watts > 0f ? watts * Tools.WattToMW : 0f;

            // Only disturb the resource system when the answer actually moved. A pump holding
            // steady is the common case and re-registering its draw every step is pure cost.
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
        /// Fraction of the electricity asked for that the grid supplied, 0..1. A pump on a
        /// browned-out ship runs slower rather than stopping.
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

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            block = Entity as IMyCubeBlock;
            functional = Entity as IMyFunctionalBlock;
            if (block == null) return;

            try
            {
                AttachSink();
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalHeatPumpBlock.Init", e);
            }
        }

        private void AttachSink()
        {
            // A block that already carries one keeps it: adding a second sink for the same
            // resource is how a block ends up billed twice.
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
        /// The block's own rating, in the resource system's units. The sink needs a ceiling up
        /// front, and it has to be the hardware's, not whatever the pump happens to want now.
        /// </summary>
        private float MaxDrawMegawatts()
        {
            return ThermalHeatPumpShapes.MaxPowerWatts(BlockSubtype()) * Tools.WattToMW;
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
