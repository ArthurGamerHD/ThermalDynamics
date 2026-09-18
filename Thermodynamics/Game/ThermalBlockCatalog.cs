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
    /// <summary>
    /// Turns Space Engineers block definitions into the simulation's <see cref="BlockModel"/>, once
    /// per definition per session, so placing a block is a dictionary lookup and a rotation.
    /// See architecture.md, Definition loading.
    /// </summary>
    public static class ThermalBlockCatalog
    {
        private static readonly Dictionary<MyDefinitionId, BlockModel> Models =
            new Dictionary<MyDefinitionId, BlockModel>(MyDefinitionId.Comparer);

        /// <summary>
        /// Guards <see cref="Models"/> and the counters beside it. Block placement is not
        /// main-thread-only — the game builds pasted and projected grids on worker threads.
        /// See known-issues.md, Block placement is not a main-thread-only path.
        /// </summary>
        private static readonly object ModelLock = new object();

        /// <summary>Definitions resolved so far. One entry per block type ever placed.</summary>
        public static int ModelCount
        {
            get { lock (ModelLock) { return Models.Count; } }
        }

        /// <summary>Definitions built, i.e. cache misses. One per block type, not per block.</summary>
        public static int ModelsBuilt;

        /// <summary>
        /// The model for a block's type. Never null; a definition the game cannot describe falls
        /// back to a solid cube of the right size.
        /// </summary>
        public static BlockModel Get(IMySlimBlock block)
        {
            if (block == null || block.BlockDefinition == null) return null;

            MyDefinitionId id = block.BlockDefinition.Id;

            lock (ModelLock)
            {
                BlockModel model;
                if (Models.TryGetValue(id, out model)) return model;
            }

            // Built outside the lock: reading a definition walks the pressurisation table and the
            // mount rectangles, and holding a session-wide lock across that would serialise every
            // worker thread the game pastes with. Two threads racing on the same new definition
            // build the same model twice and one copy is discarded.
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

        /// <summary>Drops every cached model. Called when a session ends.</summary>
        public static void Clear()
        {
            lock (ModelLock)
            {
                Models.Clear();
                ModelsBuilt = 0;
            }
        }

        /// <summary>
        /// How a door seals when closed. The game does not derive this from the pressurisation table:
        /// a closed door's way through is sealed by a rule per door family, in
        /// <c>MyGridGasSystem.IsDoorAirtight</c>.
        /// </summary>
        public enum DoorKind
        {
            /// <summary>Not a door. Sealing comes from the definition and nothing else.</summary>
            None = 0,

            /// <summary>A sliding airtight door: seals its local forward face when fully closed.</summary>
            AirtightSlide,

            /// <summary>A generic airtight door, e.g. a hangar door: forward and backward.</summary>
            AirtightGeneric,

            /// <summary>Any other door: seals every face carrying no mount point.</summary>
            Plain
        }

        /// <summary>
        /// Classifies a block's door family from its definition type, which is what the game's own
        /// check keys off. Read once per definition, with the model.
        /// </summary>
        public static DoorKind DoorKindOf(IMySlimBlock block)
        {
            if (block == null) return DoorKind.None;

            MyCubeBlockDefinition definition = block.BlockDefinition as MyCubeBlockDefinition;
            if (definition is MyAirtightSlideDoorDefinition) return DoorKind.AirtightSlide;
            if (definition is MyAirtightDoorGenericDefinition) return DoorKind.AirtightGeneric;
            if (definition is MyDoorDefinition) return DoorKind.Plain;

            // Modded doors need not use the stock definition types, but must present as one to the
            // game's own door handling.
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

            // An open door seals only what it seals in either state: the sides it is mounted in by.
            // That is the definition's own table with the door rule omitted.
            if (door != DoorKind.None)
            {
                model.LocalSurfacesWhenOpen = BuildSurfaces(definition, DoorKind.None);
            }

            model.Coolant = ThermalCoolantShapes.Get(id.SubtypeName, definition.Size);
            model.HeatPump = ThermalHeatPumpShapes.Get(id.SubtypeName, definition.Size);

            return model;
        }

        /// <summary>
        /// Reads the block's airtightness and mount points out of the definition, in the block's
        /// own local space. Orientation is applied per placed block by
        /// <see cref="BlockInstance"/>, so nothing here needs to know how the block is turned.
        /// </summary>
        private static int[] BuildSurfaces(MyCubeBlockDefinition definition, DoorKind door)
        {
            // A local rather than a pooled scratch list; see the note on ModelLock. This runs once
            // per block type per session, so the allocation is off every hot path, and a shared list
            // would be written concurrently from worker threads.
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

            // An explicit IsAirTight settles the question in either direction: the game's
            // IsAirtightFromDefinition returns it before consulting the table, so a definition that
            // says false does not seal even where its mount points cover a face.
            bool airtight = definition.IsAirTight == true;
            bool decided = definition.IsAirTight.HasValue;

            if (decided || definition.IsCubePressurized == null)
            {
                // Nothing further to read per face. Mounts everywhere so conduction still works.
                return BlockSurfaceBuilder.BuildFallbackSurfaces(definition.Size, airtight);
            }

            MyCubeBlockDefinition captured = definition;
            DoorKind capturedDoor = door;
            SealTest seals = delegate (Vector3I localCell, int face)
            {
                return Seals(captured, localCell, face, capturedDoor);
            };

            int[] surfaces = BlockSurfaceBuilder.BuildSurfaces(definition.Size, airtight, seals, mounts);

            // A block with no mount surface conducts to nothing, which is worse than over-reporting
            // contact. Where the definition's mount rectangles produced none, fall back to mounting
            // everywhere and count it, so the report names the definitions that could not be read.
            if (!HasAnyMount(surfaces))
            {
                System.Threading.Interlocked.Increment(ref MountFallbacks);
                return BlockSurfaceBuilder.BuildFallbackSurfaces(definition.Size, airtight);
            }

            return surfaces;
        }

        /// <summary>Definitions whose mount points produced no mount surface at all.</summary>
        public static int MountFallbacks;

        private static bool HasAnyMount(int[] surfaces)
        {
            for (int i = 0; i < surfaces.Length; i++)
            {
                if ((surfaces[i] & CellSurface.SelfMountMask) != 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether one face of one cell seals when the block is complete and, for a door, closed.
        /// Mirrors <c>MyGridGasSystem.IsAirtightBlock</c>: the pressurisation table, then the
        /// per-family door rule, without which every room holding a door reads unsealed. The open
        /// state belongs to the placed block, through <see cref="BlockInstance.IsSealedByDoorState"/>.
        /// </summary>
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

        /// <summary>The closed-door half of the game's rule, in the block's own local space.</summary>
        private static bool SealsAsDoor(MyCubeBlockDefinition definition, int face, DoorKind door)
        {
            switch (door)
            {
                case DoorKind.AirtightSlide:
                    return face == Face.Forward;

                case DoorKind.AirtightGeneric:
                    return face == Face.Forward || face == Face.Backward;

                case DoorKind.Plain:
                    // Everything the door does not mount through: a closed door is a wall except
                    // where it is mounted to one.
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

        /// <summary>
        /// What a block's own definition says reaches its store, or zero where it says nothing.
        ///
        /// The jump drive is the only family in the game that states one — 0.8 for the vanilla
        /// drive and its reskin, 0.9 for the two prototech ones.
        /// </summary>
        private static float StatedEfficiency(MyCubeBlockDefinition block)
        {
            MyJumpDriveDefinition drive = block as MyJumpDriveDefinition;
            return drive == null ? 0f : drive.PowerEfficiency;
        }

        /// <summary>
        /// The thermal properties of a block: the blend of the materials its build components make it,
        /// with whatever a definition actually declared laid over the top, property by property.
        /// Derivation is the floor rather than the fallback of last resort, which is what turns a
        /// partial entry into "change these, derive the rest".
        /// See definitions.md, Where a block's properties come from.
        /// </summary>
        public static BlockThermalProperties ToThermalProperties(
            ThermalCellDefinition definition, MyCubeBlockDefinition block = null)
        {
            // Materials from the build cost; the block's function comes from its type entry in
            // Cubes.xml, which GetDefinition resolves below.
            BlockThermalProperties properties = BlockThermalDerivation.Derive(ComponentsOf(block));

            // Landing on the environment-wide default means no entry anywhere named this block.
            // That entry describes mild steel, which was the best guess available before the block's
            // own build cost could be read and is a worse one now, so it is not applied over a
            // derivation that actually describes the block.
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

            // Absorptivity follows the emissivity unless it is declared in its own right, and that
            // includes a *declared* emissivity: an entry written before this property existed said
            // one number and meant both, so reading only one of them would silently change it.
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

            // **A block whose own definition states what it does with the power it draws beats the
            // type entry, and loses to a subtype entry.** A type entry describes a family and
            // cannot say both 0.8 and 0.9; a subtype entry describes this block and is a
            // deliberate override. See definitions.md, Where a block's properties come from.
            if (definition.ResolvedAt != ThermalCellDefinition.Resolution.Subtype)
            {
                float stated = BlockThermalDerivation.WasteFromEfficiency(StatedEfficiency(block));
                if (stated >= 0f) properties.ConsumerWasteEnergy = stated;
            }

            // Before the clamp, and only here: this is the one path an authored value reaches, and
            // once Clamp has run there is no problem left to describe. See ThermalValidation.
            Core.ThermalValidation.Check(
                block == null ? "a block definition" : block.Id.SubtypeName, properties);

            return properties.Clamp();
        }

        /// <summary>
        /// A block's build cost as the derivation wants it, priced with the game's own component
        /// masses. Empty for a definition the game cannot describe, which derives as plain steel.
        /// </summary>
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

        /// <summary>Copies a loop definition into the model's own type.</summary>
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

            // The world's own values last, and only where they have been moved. An untouched world
            // still gets whatever Loops.xml says; the moment someone sets a flow rate in the menu,
            // that is the flow rate.
            ApplyWorldLoopValues(properties);

            return properties.Clamp();
        }

        /// <summary>Copies a planet definition into the model's own type.</summary>
        public static PlanetThermalProperties ToPlanetProperties(PlanetDefinition definition)
        {
            // Seeded with the model's own defaults, which describe an earthlike world. A definition
            // overrides only what it actually carried: a planet with no thermal group at all, or a
            // pack authoring three values of eleven, keeps a climate rather than being handed zeros
            // — and a zero day and night temperature is the vacuum figure, in breathable air.
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

        /// <summary>
        /// Overlays the world's own coolant values onto a loop definition, for the ones moved off what
        /// a fresh install ships. Compared against the shipped figure rather than carrying a sentinel,
        /// so every value in the settings file is a real number someone can edit.
        /// </summary>
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

            // One world dial over both joints, and one over both grid sizes. The definition keeps
            // them apart — a fluid that behaves differently at a sink face or on a small grid says
            // so in Loops.xml — but a world tuning either moves the pair together.
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

        /// <summary>The same for a planet's climate. See <see cref="ApplyWorldLoopValues"/>.</summary>
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

        /// <summary>What a fresh install ships, to compare a world's values against.</summary>
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
