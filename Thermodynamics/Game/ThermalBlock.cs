using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using Thermodynamics.Core;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components.Interfaces;
using VRage.Game.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// One placed block, bound to the simulation node that represents it.
    ///
    /// Everything the game reports about a block arrives through here, and arrives by event: power
    /// output, thrust, door state and mass are pushed when they change rather than polled. This
    /// keeps a grid's per-step cost proportional to the solver rather than to its component count.
    /// </summary>
    public class ThermalBlock
    {
        public readonly ThermalGrid Grid;
        public readonly IMySlimBlock Block;

        /// <summary>The simulation's view of this block: geometry, mass, live power figures.</summary>
        public readonly BlockInstance Instance;

        /// <summary>The solver node. Null when the block was rejected by the solver.</summary>
        public ThermalNode Node;

        /// <summary>
        /// The data collection bucket for this block's definition, resolved once at construction
        /// so the per-update path never does a dictionary lookup. Null when telemetry is off.
        /// </summary>
        public BlockTypeTelemetry Stats;

        /// <summary>
        /// The air vent this block is, or null. Vents are the fallback source of pressurisation state
        /// where the game's gas system cannot be read.
        /// </summary>
        public IMyAirVent Vent;

        /// <summary>
        /// The electrical half of this block if it is a heat pump, or null. Holds the resource sink
        /// and the terminal switch; the heat moved is the simulation's concern.
        /// </summary>
        public ThermalHeatPumpBlock HeatPump;

        /// <summary>The coolant pump's switch and speed, or null when this is not a pump.</summary>
        public ThermalCoolantPumpBlock CoolantPump;

        private MyResourceSourceComponent source;
        private MyResourceSinkComponent sink;
        private IMyThrust thrust;
        private IMyDoor door;
        private IMyMechanicalConnectionBlock mechanical;
        private IMyPistonBase piston;
        private IMyMotorBase motor;

        private Action<Type, IMyEntityComponentBase> componentAdded;
        private Action<Type, IMyEntityComponentBase> componentRemoved;
        private MyResourceOutputChangedDelegate outputChanged;
        private MyCurrentResourceInputChangedDelegate inputChanged;
        private Action<IMyThrust, float, float> thrustChanged;
        private Action<bool> doorChanged;
        private Action<IMyMechanicalConnectionBlock> attachmentChanged;

        public ThermalBlock(ThermalGrid grid, IMySlimBlock block, BlockModel model)
        {
            Grid = grid;
            Block = block;

            MyBlockOrientation orientation = block.Orientation;
            Instance = new BlockInstance(
                model,
                block.Min,
                new BlockOrientation(orientation.Forward, orientation.Up));

            Instance.Mass = Math.Max(0f, block.Mass);

            Stats = Telemetry.GetBlockType(block.BlockDefinition.Id);
        }

        /// <summary>
        /// Re-resolves the per-definition telemetry record. Called when collection is switched
        /// on or off during a session; a no-op otherwise.
        /// </summary>
        public void RefreshStats()
        {
            // Records outlive a toggle, so a block that has already registered with one must not
            // register again, which would count its placement once per switch.
            if (Stats != null) return;

            Stats = Telemetry.GetBlockType(Block.BlockDefinition.Id);
            if (Stats != null) Stats.OnPlaced(this);
        }

        public float Temperature
        {
            get { return Node == null ? 0f : Node.Temperature; }
        }

        public string Name
        {
            get { return Block.BlockDefinition.Id.SubtypeName; }
        }

        // ---- wiring ------------------------------------------------------------------------

        /// <summary>Subscribes to everything that can change this block's thermal inputs.</summary>
        public void Attach()
        {
            IMyCubeBlock fat = Block.FatBlock;
            if (fat == null) return;

            thrust = fat as IMyThrust;
            if (thrust != null)
            {
                thrustChanged = OnThrustChanged;
                thrust.ThrustChanged += thrustChanged;
                OnThrustChanged(thrust, 0f, thrust.CurrentThrust);
            }
            else
            {
                componentAdded = OnComponentAdded;
                componentRemoved = OnComponentRemoved;
                fat.Components.ComponentAdded += componentAdded;
                fat.Components.ComponentRemoved += componentRemoved;

                if (fat.Components.Contains(typeof(MyResourceSourceComponent)))
                {
                    AttachSource(fat.Components.Get<MyResourceSourceComponent>());
                }

                if (fat.Components.Contains(typeof(MyResourceSinkComponent)))
                {
                    AttachSink(fat.Components.Get<MyResourceSinkComponent>());
                }
            }

            Vent = fat as IMyAirVent;
            if (Vent != null) Grid.RegisterVent(this);

            // A heat pump is the only block with a two-way exchange with the game: the simulation
            // reports the draw it wants and the power system reports how much it supplied.
            CoolantPump = fat.GameLogic == null
                ? null
                : fat.GameLogic.GetAs<ThermalCoolantPumpBlock>();

            if (ThermalHeatPumpShapes.IsHeatPump(Name))
            {
                HeatPump = fat.GameLogic == null ? null : fat.GameLogic.GetAs<ThermalHeatPumpBlock>();
                Grid.RegisterHeatPump(this);
            }

            door = fat as IMyDoor;
            if (door != null)
            {
                doorChanged = OnDoorStateChanged;
                door.DoorStateChanged += doorChanged;
                OnDoorStateChanged(door.IsFullyClosed);
            }

            // A rotor or piston conducts into the grid on the far side of the joint, which is a
            // separate simulation. Only these two block families raise the event.
            piston = fat as IMyPistonBase;
            motor = fat as IMyMotorBase;
            if (piston != null || motor != null)
            {
                mechanical = fat as IMyMechanicalConnectionBlock;
                attachmentChanged = OnAttachmentChanged;

                if (piston != null) piston.AttachedEntityChanged += attachmentChanged;
                else motor.AttachedEntityChanged += attachmentChanged;

                OnAttachmentChanged(mechanical);
            }
        }

        /// <summary>Undoes <see cref="Attach"/>. Every subscription made there is dropped here.</summary>
        public void Detach()
        {
            IMyCubeBlock fat = Block.FatBlock;

            if (thrust != null && thrustChanged != null) thrust.ThrustChanged -= thrustChanged;
            if (door != null && doorChanged != null) door.DoorStateChanged -= doorChanged;
            if (attachmentChanged != null)
            {
                if (piston != null) piston.AttachedEntityChanged -= attachmentChanged;
                if (motor != null) motor.AttachedEntityChanged -= attachmentChanged;
                ThermalBridges.RemoveAllFor(this);
            }

            if (source != null && outputChanged != null) source.OutputChanged -= outputChanged;
            if (sink != null && inputChanged != null) sink.CurrentInputChanged -= inputChanged;

            if (fat != null)
            {
                if (componentAdded != null) fat.Components.ComponentAdded -= componentAdded;
                if (componentRemoved != null) fat.Components.ComponentRemoved -= componentRemoved;
            }

            if (Vent != null)
            {
                Grid.UnregisterVent(this);
                Vent = null;
            }

            // Tested on the subtype rather than the field: a pump whose game logic could not be
            // resolved is still registered, and leaving it in the list would keep the block alive
            // after removal.
            if (ThermalHeatPumpShapes.IsHeatPump(Name))
            {
                Grid.UnregisterHeatPump(this);
                HeatPump = null;
            }

            thrust = null;
            door = null;
            mechanical = null;
            piston = null;
            motor = null;
            source = null;
            sink = null;
        }

        private void AttachSource(MyResourceSourceComponent component)
        {
            if (component == null) return;

            source = component;
            outputChanged = OnPowerProduced;
            source.OutputChanged += outputChanged;

            // Query electricity only when the component carries it. A hydrogen tank, an oxygen farm
            // and an ice-fed generator all have a source component with no electric type, and the
            // by-type accessors index a dictionary rather than probing it: a field run took a
            // KeyNotFoundException out of MyResourceSourceComponent.GetTypeIndex for this, which
            // aborted binding and left the block out of the simulation.
            if (Carries(source.ResourceTypes))
            {
                Instance.PowerProducedWatts =
                    source.CurrentOutputByType(MyResourceDistributorComponent.ElectricityId) * ThermalConstants.MegawattsToWatts;
            }

            RefreshHeat();
        }

        private void AttachSink(MyResourceSinkComponent component)
        {
            if (component == null) return;

            sink = component;
            inputChanged = OnPowerConsumed;
            sink.CurrentInputChanged += inputChanged;

            if (Carries(sink.AcceptedResources))
            {
                Instance.PowerConsumedWatts =
                    sink.CurrentInputByType(MyResourceDistributorComponent.ElectricityId) * ThermalConstants.MegawattsToWatts;
            }

            RefreshHeat();
        }

        /// <summary>
        /// Whether a resource component handles electricity.
        ///
        /// The subscription is kept either way: a sink can gain a type after construction via
        /// <c>AddType</c>, and the change event filters on the resource id, so a component that
        /// starts non-electric still reports correctly if it later becomes electric.
        /// </summary>
        private static bool Carries(ListReader<MyDefinitionId> resources)
        {
            for (int i = 0; i < resources.Count; i++)
            {
                if (resources[i] == MyResourceDistributorComponent.ElectricityId) return true;
            }
            return false;
        }

        private void OnComponentAdded(Type type, IMyEntityComponentBase component)
        {
            if (type == typeof(MyResourceSourceComponent)) AttachSource(component as MyResourceSourceComponent);
            if (type == typeof(MyResourceSinkComponent)) AttachSink(component as MyResourceSinkComponent);
        }

        private void OnComponentRemoved(Type type, IMyEntityComponentBase component)
        {
            if (type == typeof(MyResourceSourceComponent) && source != null && outputChanged != null)
            {
                source.OutputChanged -= outputChanged;
                source = null;
                Instance.PowerProducedWatts = 0f;
                RefreshHeat();
            }

            if (type == typeof(MyResourceSinkComponent) && sink != null && inputChanged != null)
            {
                sink.CurrentInputChanged -= inputChanged;
                sink = null;
                Instance.PowerConsumedWatts = 0f;
                RefreshHeat();
            }
        }

        // ---- pushed state ------------------------------------------------------------------

        private void OnPowerProduced(MyDefinitionId resource, float previous, MyResourceSourceComponent component)
        {
            try
            {
                if (resource != MyResourceDistributorComponent.ElectricityId) return;

                Instance.PowerProducedWatts = component.CurrentOutputByType(resource) * ThermalConstants.MegawattsToWatts;
                RefreshHeat();
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalBlock.OnPowerProduced", e);
            }
        }

        private void OnPowerConsumed(MyDefinitionId resource, float previous, MyResourceSinkComponent component)
        {
            try
            {
                if (resource != MyResourceDistributorComponent.ElectricityId) return;

                Instance.PowerConsumedWatts = component.CurrentInputByType(resource) * ThermalConstants.MegawattsToWatts;
                RefreshHeat();
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalBlock.OnPowerConsumed", e);
            }
        }

        private void OnThrustChanged(IMyThrust block, float previous, float current)
        {
            try
            {
                MyThrustDefinition definition = block.SlimBlock.BlockDefinition as MyThrustDefinition;
                if (definition == null || block.MaxThrust <= 0f) return;

                Instance.ThrustWatts = definition.ForceMagnitude * (block.CurrentThrust / block.MaxThrust);
                RefreshHeat();
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalBlock.OnThrustChanged", e);
            }
        }

        /// <summary>
        /// Handles a door changing state, which changes what the rooms behind it radiate to. The only
        /// per-block event costing more than a few floats, and raised only by doors.
        /// </summary>
        private void OnDoorStateChanged(bool closed)
        {
            try
            {
                bool sealed_ = door.IsFullyClosed;
                if (Instance.IsSealedByDoorState == sealed_) return;

                Instance.IsSealedByDoorState = sealed_;
                Grid.RefreshBlockSealing(this);

                if (Grid.Stats != null) Grid.Stats.DoorStateChanges++;
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalBlock.OnDoorStateChanged", e);
            }
        }

        private void OnAttachmentChanged(IMyMechanicalConnectionBlock block)
        {
            try
            {
                ThermalBridges.Rebuild(this, block);
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalBlock.OnAttachmentChanged", e);
            }
        }

        /// <summary>
        /// This block's place in its grid's mass-sweep rota, or -1 when it is not in one.
        /// Maintained by <see cref="ThermalGrid"/>; nothing else should write it.
        /// </summary>
        public int SweepSlot = -1;

        public void RefreshMass()
        {
            float mass = Math.Max(0f, Block.Mass);
            if (Instance.Mass == mass) return;

            Instance.Mass = mass;
            if (Node != null) Node.RefreshThermalMass();
        }

        private void RefreshHeat()
        {
            if (Node != null) Node.RefreshHeatGeneration();
        }

        /// <summary>
        /// Re-reads this block's thermal properties from the catalogue. The model holds them by
        /// reference, so a rebuilt entry reaches the block only by asking again, and the node's cached
        /// capacity and generation are derived from it.
        /// </summary>
        public void RefreshProperties()
        {
            BlockModel model = ThermalBlockCatalog.Get(Block);
            if (model == null) return;

            Instance.Model = model;

            if (Node == null) return;

            Node.RefreshThermalMass();
            Node.RefreshHeatGeneration();
            Node.RefreshExposure();
        }
    }
}
