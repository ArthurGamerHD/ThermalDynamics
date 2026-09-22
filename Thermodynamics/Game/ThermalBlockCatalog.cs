using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    public static class ThermalBlockCatalog
    {
        private static readonly Dictionary<MyDefinitionId, BlockModel> Models =
            new Dictionary<MyDefinitionId, BlockModel>(MyDefinitionId.Comparer);

        private static readonly object ModelLock = new object();

        public static int ModelCount
        {
            get { lock (ModelLock) { return Models.Count; } }
        }

        public static int ModelsBuilt;

        public static BlockModel Get(IMySlimBlock block)
        {
            if (block == null || block.BlockDefinition == null) return null;

            MyDefinitionId id = block.BlockDefinition.Id;

            lock (ModelLock)
            {
                BlockModel model;
                if (Models.TryGetValue(id, out model)) return model;
            }

            BlockModel built = Build(block.BlockDefinition as MyCubeBlockDefinition, id, DoorKindOf(block));

            lock (ModelLock)
            {
                BlockModel existing;
                if (Models.TryGetValue(id, out existing)) return existing;

                Models[id] = built;
                ModelsBuilt++;
                return built;
            }
        }

        public static void Clear()
        {
            lock (ModelLock)
            {
                Models.Clear();
                ModelsBuilt = 0;
            }
        }

        public enum DoorKind
        {
            None = 0,

            AirtightSlide,

            AirtightGeneric,

            Plain
        }

        public static DoorKind DoorKindOf(IMySlimBlock block)
        {
            if (block == null) return DoorKind.None;

            MyCubeBlockDefinition definition = block.BlockDefinition as MyCubeBlockDefinition;
            if (definition is MyAirtightSlideDoorDefinition) return DoorKind.AirtightSlide;
            if (definition is MyAirtightDoorGenericDefinition) return DoorKind.AirtightGeneric;
            if (definition is MyDoorDefinition) return DoorKind.Plain;

            return block.FatBlock is IMyDoor ? DoorKind.Plain : DoorKind.None;
        }

        private static BlockModel Build(MyCubeBlockDefinition definition, MyDefinitionId id, DoorKind door)
        {
            BlockModel model = new BlockModel();
            model.Name = id.SubtypeName;
            if (string.IsNullOrEmpty(model.Name)) model.Name = id.TypeId.ToString();

            model.Thermal = ToThermalProperties(ThermalCellDefinition.GetDefinition(id), definition);

            if (definition == null)
            {
                model.Size = Vector3I.One;
                model.Mass = 100f;
                model.LocalSurfaces = BlockSurfaceBuilder.BuildFallbackSurfaces(model.Size, true);
                return model;
            }

            model.Size = definition.Size;
            model.Mass = definition.Mass > 0f ? definition.Mass : 100f;
            model.LocalSurfaces = BuildSurfaces(definition, door);

            if (door != DoorKind.None)
            {
                model.LocalSurfacesWhenOpen = BuildSurfaces(definition, DoorKind.None);
            }

            model.Coolant = ThermalCoolantShapes.Get(id.SubtypeName, definition.Size);
            model.HeatPump = ThermalHeatPumpShapes.Get(id.SubtypeName, definition.Size);

            return model;
        }

        private static int[] BuildSurfaces(MyCubeBlockDefinition definition, DoorKind door)
        {
            List<MountRect> mounts = new List<MountRect>();
            if (definition.MountPoints != null)
            {
                for (int i = 0; i < definition.MountPoints.Length; i++)
                {
                    MyCubeBlockDefinition.MountPoint mount = definition.MountPoints[i];
                    MountRect rect = new MountRect(mount.Normal, mount.Start, mount.End);
                    rect.Enabled = mount.Enabled;
                    mounts.Add(rect);
                }
            }

            bool airtight = definition.IsAirTight == true;
            bool decided = definition.IsAirTight.HasValue;

            if (decided || definition.IsCubePressurized == null)
            {
                return BlockSurfaceBuilder.BuildFallbackSurfaces(definition.Size, airtight);
            }

            MyCubeBlockDefinition captured = definition;
            DoorKind capturedDoor = door;
            SealTest seals = delegate (Vector3I localCell, int face)
            {
                return Seals(captured, localCell, face, capturedDoor);
            };

            int[] surfaces = BlockSurfaceBuilder.BuildSurfaces(definition.Size, airtight, seals, mounts);

            if (!HasAnyMount(surfaces))
            {
                System.Threading.Interlocked.Increment(ref MountFallbacks);
                return BlockSurfaceBuilder.BuildFallbackSurfaces(definition.Size, airtight);
            }

            return surfaces;
        }

        public static int MountFallbacks;

        private static bool HasAnyMount(int[] surfaces)
        {
            for (int i = 0; i < surfaces.Length; i++)
            {
                if ((surfaces[i] & CellSurface.SelfMountMask) != 0) return true;
            }
            return false;
        }

        private static bool Seals(MyCubeBlockDefinition definition, Vector3I localCell, int face, DoorKind door)
        {
            try
            {
                Dictionary<Vector3I, MyCubeBlockDefinition.MyCubePressurizationMark> byDirection;
                if (definition.IsCubePressurized.TryGetValue(localCell, out byDirection))
                {
                    MyCubeBlockDefinition.MyCubePressurizationMark mark;
                    if (byDirection.TryGetValue(Face.Offsets[face], out mark))
                    {
                        if (mark == MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedAlways) return true;
                        if (mark == MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedClosed) return true;
                    }
                }

                return SealsAsDoor(definition, face, door);
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalBlockCatalog.Seals", e);
                return false;
            }
        }

        private static bool SealsAsDoor(MyCubeBlockDefinition definition, int face, DoorKind door)
        {
            switch (door)
            {
                case DoorKind.AirtightSlide:
                    return face == Face.Forward;

                case DoorKind.AirtightGeneric:
                    return face == Face.Forward || face == Face.Backward;

                case DoorKind.Plain:
                    return !HasMountOnFace(definition, face);

                default:
                    return false;
            }
        }

        private static bool HasMountOnFace(MyCubeBlockDefinition definition, int face)
        {
            if (definition.MountPoints == null) return false;

            Vector3I normal = Face.Offsets[face];
            for (int i = 0; i < definition.MountPoints.Length; i++)
            {
                if (definition.MountPoints[i].Normal == normal) return true;
            }
            return false;
        }

        private static float StatedEfficiency(MyCubeBlockDefinition block)
        {
            MyJumpDriveDefinition drive = block as MyJumpDriveDefinition;
            return drive == null ? 0f : drive.PowerEfficiency;
        }

        public static BlockThermalProperties ToThermalProperties(
            ThermalCellDefinition definition, MyCubeBlockDefinition block = null)
        {
            BlockThermalProperties properties = BlockThermalDerivation.Derive(ComponentsOf(block));

            if (definition == null
                || definition.ResolvedAt == ThermalCellDefinition.Resolution.Fallback)
            {
                return properties.Clamp();
            }

            if (definition.WasDeclared(ThermalCellDefinition.DeclaredProperties.ExcludeFromSimulation))
                properties.ExcludeFromSimulation = definition.ExcludeFromSimulation;
            if (definition.WasDeclared(ThermalCellDefinition.DeclaredProperties.Conductivity))
                properties.Conductivity = definition.Conductivity;
            if (definition.WasDeclared(ThermalCellDefinition.DeclaredProperties.SpecificHeat))
                properties.SpecificHeat = definition.SpecificHeat;
            if (definition.WasDeclared(ThermalCellDefinition.DeclaredProperties.Emissivity))
                properties.Emissivity = definition.Emissivity;

            if (definition.WasDeclared(ThermalCellDefinition.DeclaredProperties.SolarAbsorptivity))
                properties.SolarAbsorptivity = definition.SolarAbsorptivity;
            if (definition.WasDeclared(ThermalCellDefinition.DeclaredProperties.ExposedSurfaceMultiplier))
                properties.ExposedSurfaceMultiplier = definition.ExposedSurfaceMultiplier;
            if (definition.WasDeclared(ThermalCellDefinition.DeclaredProperties.ProducerWasteEnergy))
                properties.ProducerWasteEnergy = definition.ProducerWasteEnergy;
            if (definition.WasDeclared(ThermalCellDefinition.DeclaredProperties.ConsumerWasteEnergy))
                properties.ConsumerWasteEnergy = definition.ConsumerWasteEnergy;
            if (definition.WasDeclared(ThermalCellDefinition.DeclaredProperties.CriticalTemperature))
                properties.CriticalTemperature = definition.CriticalTemperature;
            if (definition.WasDeclared(ThermalCellDefinition.DeclaredProperties.OverheatDamagePerKelvin))
                properties.OverheatDamagePerKelvin = definition.OverheatDamagePerKelvin;
            if (definition.WasDeclared(ThermalCellDefinition.DeclaredProperties.HeatSourceWatts))
                properties.HeatSourceWatts = definition.HeatSourceWatts;

            if (definition.ResolvedAt != ThermalCellDefinition.Resolution.Subtype)
            {
                float stated = BlockThermalDerivation.WasteFromEfficiency(StatedEfficiency(block));
                if (stated >= 0f) properties.ConsumerWasteEnergy = stated;
            }

            Core.ThermalValidation.Check(
                block == null ? "a block definition" : block.Id.SubtypeName, properties);

            return properties.Clamp();
        }

        private static List<BlockComponent> ComponentsOf(MyCubeBlockDefinition block)
        {
            List<BlockComponent> components = new List<BlockComponent>();
            if (block == null || block.Components == null) return components;

            for (int i = 0; i < block.Components.Length; i++)
            {
                MyCubeBlockDefinition.Component component = block.Components[i];
                if (component == null || component.Definition == null) continue;

                components.Add(new BlockComponent(
                    component.Definition.Id.SubtypeName, component.Count, component.Definition.Mass));
            }

            return components;
        }

        public static LoopThermalProperties ToLoopProperties(ThermalLoopDefinition definition)
        {
            LoopThermalProperties properties = new LoopThermalProperties();
            if (definition == null) return properties.Clamp();

            properties.CoolantMassPerPipe = definition.CoolantMassPerPipe;
            properties.CoolantKilogramsPerCubicMetre = definition.CoolantKilogramsPerCubicMetre;
            properties.HeatTransferCoefficient = definition.HeatTransferCoefficient;
            properties.SpecificHeat = definition.SpecificHeat;
            properties.PipeContactMultiplier = definition.PipeContactMultiplier;
            properties.SinkContactMultiplier = definition.SinkContactMultiplier;
            properties.LargeGridFlowRate = definition.LargeGridFlowRate;
            properties.SmallGridFlowRate = definition.SmallGridFlowRate;
            properties.StagnantTransferFraction = definition.StagnantTransferFraction;

            ApplyWorldLoopValues(properties);

            return properties.Clamp();
        }

        public static PlanetThermalProperties ToPlanetProperties(PlanetDefinition definition)
        {
            PlanetThermalProperties properties = new PlanetThermalProperties();
            if (definition == null)
            {
                ApplyWorldPlanetValues(properties);
                return properties.Clamp();
            }

            PlanetThermalProperties read = new PlanetThermalProperties();
            read.NightTemperature = definition.NightTemperature;
            read.DayTemperature = definition.DayTemperature;
            read.UndergroundTemperature = definition.UndergroundTemperature;
            read.CoreTemperature = definition.CoreTemperature;
            read.SealevelDeadzone = definition.SealevelDeadzone;
            read.PoleTemperatureDrop = definition.PoleTemperatureDrop;
            read.AmbientLagSeconds = definition.AmbientLagSeconds;
            read.AmbientLapseRate = definition.AmbientLapseRate;
            read.UndergroundDampingDepth = definition.UndergroundDampingDepth;
            read.SolarDecay = definition.SolarDecay;
            read.ConvectionCoefficient = definition.ConvectionCoefficient;
            read.UndergroundConvectionCoefficient = definition.UndergroundConvectionCoefficient;
            read.AmbientLagShareOfDay = definition.AmbientLagShareOfDay;

            properties = PlanetProperties.Merge(properties, read, definition.Supplied);

            ApplyWorldPlanetValues(properties);

            return properties.Clamp();
        }

        private static void ApplyWorldLoopValues(LoopThermalProperties properties)
        {
            Settings world = Settings.Instance;
            Settings shipped = Defaults;
            if (world == null || shipped == null) return;

            if (Moved(world.LoopCoolantKilogramsPerCubicMetre,
                shipped.LoopCoolantKilogramsPerCubicMetre))
                properties.CoolantKilogramsPerCubicMetre = world.LoopCoolantKilogramsPerCubicMetre;

            if (Moved(world.LoopRefillEquivalentKelvin, shipped.LoopRefillEquivalentKelvin))
                properties.RefillEquivalentKelvin = world.LoopRefillEquivalentKelvin;

            if (Moved(world.LoopRefillKilogramsPerSecond, shipped.LoopRefillKilogramsPerSecond))
                properties.RefillKilogramsPerSecond = world.LoopRefillKilogramsPerSecond;

            if (Moved(world.LoopHeatTransferCoefficient, shipped.LoopHeatTransferCoefficient))
                properties.HeatTransferCoefficient = world.LoopHeatTransferCoefficient;

            if (Moved(world.LoopSpecificHeat, shipped.LoopSpecificHeat))
                properties.SpecificHeat = world.LoopSpecificHeat;

            if (Moved(world.LoopContactMultiplier, shipped.LoopContactMultiplier))
            {
                properties.PipeContactMultiplier = world.LoopContactMultiplier;
                properties.SinkContactMultiplier = world.LoopContactMultiplier;
            }

            if (Moved(world.LoopFlowRate, shipped.LoopFlowRate))
            {
                properties.LargeGridFlowRate = world.LoopFlowRate;
                properties.SmallGridFlowRate = world.LoopFlowRate;
            }

            if (Moved(world.LoopStagnantTransferFraction, shipped.LoopStagnantTransferFraction))
                properties.StagnantTransferFraction = world.LoopStagnantTransferFraction;
        }

        private static void ApplyWorldPlanetValues(PlanetThermalProperties properties)
        {
            Settings world = Settings.Instance;
            Settings shipped = Defaults;
            if (world == null || shipped == null) return;

            if (Moved(world.PlanetDayTemperature, shipped.PlanetDayTemperature))
                properties.DayTemperature = world.PlanetDayTemperature;

            if (Moved(world.PlanetNightTemperature, shipped.PlanetNightTemperature))
                properties.NightTemperature = world.PlanetNightTemperature;

            if (Moved(world.PlanetPoleTemperatureDrop, shipped.PlanetPoleTemperatureDrop))
                properties.PoleTemperatureDrop = world.PlanetPoleTemperatureDrop;

            if (Moved(world.PlanetAmbientLapseRate, shipped.PlanetAmbientLapseRate))
                properties.AmbientLapseRate = world.PlanetAmbientLapseRate;

            if (Moved(world.PlanetAmbientLagSeconds, shipped.PlanetAmbientLagSeconds))
                properties.AmbientLagSeconds = world.PlanetAmbientLagSeconds;

            if (Moved(world.PlanetConvectionCoefficient, shipped.PlanetConvectionCoefficient))
                properties.ConvectionCoefficient = world.PlanetConvectionCoefficient;

            if (Moved(world.PlanetUndergroundConvectionCoefficient,
                    shipped.PlanetUndergroundConvectionCoefficient))
            {
                properties.UndergroundConvectionCoefficient =
                    world.PlanetUndergroundConvectionCoefficient;
            }

            if (Moved(world.PlanetSolarDecay, shipped.PlanetSolarDecay))
                properties.SolarDecay = world.PlanetSolarDecay;

            if (Moved(world.PlanetUndergroundTemperature, shipped.PlanetUndergroundTemperature))
                properties.UndergroundTemperature = world.PlanetUndergroundTemperature;

            if (Moved(world.PlanetUndergroundDampingDepth, shipped.PlanetUndergroundDampingDepth))
                properties.UndergroundDampingDepth = world.PlanetUndergroundDampingDepth;

            if (Moved(world.PlanetCoreTemperature, shipped.PlanetCoreTemperature))
                properties.CoreTemperature = world.PlanetCoreTemperature;

            if (Moved(world.PlanetSealevelDeadzone, shipped.PlanetSealevelDeadzone))
                properties.SealevelDeadzone = world.PlanetSealevelDeadzone;
        }

        private static Settings Defaults
        {
            get { return defaults ?? (defaults = Settings.GetDefaults()); }
        }

        private static Settings defaults;

        private static bool Moved(float world, float shipped)
        {
            float difference = world - shipped;
            if (difference < 0f) difference = -difference;

            float scale = shipped < 0f ? -shipped : shipped;
            return difference > 0.0001f * (scale < 1f ? 1f : scale);
        }
    }
}
