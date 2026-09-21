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
    public class ThermalBlock
    {
        public readonly ThermalGrid Grid;
        public readonly IMySlimBlock Block;

        public readonly BlockInstance Instance;

        public ThermalNode Node;

        public BlockTypeTelemetry Stats;

        public IMyAirVent Vent;

        public ThermalHeatPumpBlock HeatPump;

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

/// <summary>ThermalBlock operation.</summary>
        public ThermalBlock(ThermalGrid grid, IMySlimBlock block, BlockModel model)
        {
            Grid = grid;
            Block = block;

            MyBlockOrientation orientation = block.Orientation;
/// <summary>BlockInstance operation.</summary>
            Instance = new BlockInstance(
                model,
                block.Min,
/// <summary>BlockOrientation operation.</summary>
                new BlockOrientation(orientation.Forward, orientation.Up));

            Instance.Mass = Math.Max(0f, block.Mass);

            Stats = Telemetry.GetBlockType(block.BlockDefinition.Id);
        }

/// <summary>RefreshStats operation.</summary>
        public void RefreshStats()
        {
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


/// <summary>Attach operation.</summary>
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

/// <summary>Detach operation.</summary>
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

/// <summary>AttachSource operation.</summary>
        private void AttachSource(MyResourceSourceComponent component)
        {
            if (component == null) return;

            source = component;
            outputChanged = OnPowerProduced;
            source.OutputChanged += outputChanged;

            if (Carries(source.ResourceTypes))
            {
                Instance.PowerProducedWatts =
                    source.CurrentOutputByType(MyResourceDistributorComponent.ElectricityId) * ThermalConstants.MegawattsToWatts;
            }

            RefreshHeat();
        }

/// <summary>AttachSink operation.</summary>
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

/// <summary>Carries operation.</summary>
        private static bool Carries(ListReader<MyDefinitionId> resources)
        {
            for (int i = 0; i < resources.Count; i++)
            {
                if (resources[i] == MyResourceDistributorComponent.ElectricityId) return true;
            }
            return false;
        }

/// <summary>OnComponentAdded operation.</summary>
        private void OnComponentAdded(Type type, IMyEntityComponentBase component)
        {
            if (type == typeof(MyResourceSourceComponent)) AttachSource(component as MyResourceSourceComponent);
            if (type == typeof(MyResourceSinkComponent)) AttachSink(component as MyResourceSinkComponent);
        }

/// <summary>OnComponentRemoved operation.</summary>
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


/// <summary>OnPowerProduced operation.</summary>
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

/// <summary>OnPowerConsumed operation.</summary>
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

/// <summary>OnThrustChanged operation.</summary>
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

/// <summary>OnDoorStateChanged operation.</summary>
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

/// <summary>OnAttachmentChanged operation.</summary>
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

        public int SweepSlot = -1;

/// <summary>RefreshMass operation.</summary>
        public void RefreshMass()
        {
            float mass = Math.Max(0f, Block.Mass);
            if (Instance.Mass == mass) return;

            Instance.Mass = mass;
            if (Node != null) Node.RefreshThermalMass();
        }

/// <summary>RefreshHeat operation.</summary>
        private void RefreshHeat()
        {
            if (Node != null) Node.RefreshHeatGeneration();
        }

/// <summary>RefreshProperties operation.</summary>
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
