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
    /// Turns Space Engineers block definitions into the simulation's <see cref="BlockModel"/>,
    /// once per definition for the life of the session.
    ///
    /// This is the whole definition half of the game adapter. Everything expensive about
    /// describing a block — reading the Definition Extensions properties, walking the
    /// pressurisation table, intersecting mount rectangles — happens here, for the block
    /// <em>type</em>. Placing a block is then a dictionary hit and a rotation.
    /// </summary>
    public static class ThermalBlockCatalog
    {
        private static readonly Dictionary<MyDefinitionId, BlockModel> Models =
            new Dictionary<MyDefinitionId, BlockModel>(MyDefinitionId.Comparer);

        /// <summary>
        /// Guards <see cref="Models"/> and the counters beside it.
        ///
        /// Block placement is not a main-thread-only path: the game builds pasted and projected
        /// grids on worker threads, so <see cref="Get"/> runs concurrently with itself. An
        /// unguarded <c>Dictionary</c> torn by two writers throws out of the *reader* — a field
        /// run pasting twenty capital ships lost 442 blocks to
        /// <c>ArgumentOutOfRangeException</c> raised inside <c>List.EnsureCapacity</c>, which is
        /// what a shared scratch list looks like from the far side of a race.
        ///
        /// The lock is affordable precisely because this is a cache: it is taken once per block
        /// type per session on the miss path and for a dictionary probe on the hit path, never
        /// per step and never per cell.
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
            // worker thread the game is pasting with. Two threads racing on the same new
            // definition build the same model twice and one of them is discarded, which costs a
            // duplicate build and nothing else.
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
        /// How a door seals when it is closed. The game does not answer this from the
        /// pressurisation table — a closed door's way through is sealed by a rule per door
        /// family, in <c>MyGridGasSystem.IsDoorAirtight</c> — so neither can this.
        /// </summary>
        public enum DoorKind
        {
            /// <summary>Not a door. Sealing comes from the definition and nothing else.</summary>
            None = 0,

            /// <summary>A sliding airtight door: seals its local forward face when fully closed.</summary>
            AirtightSlide,

            /// <summary>A generic airtight door, e.g. a hangar door: forward and backward.</summary>
            AirtightGeneric,

            /// <summary>Any other door: seals every face that carries no mount point.</summary>
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

            // Modded doors need not use the stock definition types, but they do have to present
            // as one to the game's own door handling.
            return block.FatBlock is IMyDoor ? DoorKind.Plain : DoorKind.None;
        }

        private static BlockModel Build(MyCubeBlockDefinition definition, MyDefinitionId id, DoorKind door)
        {
            BlockModel model = new BlockModel();
            model.Name = id.SubtypeName;
            if (string.IsNullOrEmpty(model.Name)) model.Name = id.TypeId.ToString();

            model.Thermal = ToThermalProperties(ThermalCellDefinition.GetDefinition(id));

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

            // A door that is open seals only what it seals whatever its state: the sides it is
            // bolted in by. That is the definition's own table with the door rule left out.
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
            // Deliberately a local, not pooled scratch: see the note on ModelLock. This runs once
            // per block type for the life of the session, so the allocation is not on any hot
            // path, and sharing one list across threads is what broke the field run.
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

            // An explicit IsAirTight is the whole answer either way: the game's
            // IsAirtightFromDefinition returns it before the table is ever consulted, so a
            // definition that says false does not seal even where its mount points cover a face.
            bool airtight = definition.IsAirTight == true;
            bool decided = definition.IsAirTight.HasValue;

            if (decided || definition.IsCubePressurized == null)
            {
                // Nothing left to read per face. Mounts everywhere so conduction still works.
                return BlockSurfaceBuilder.BuildFallbackSurfaces(definition.Size, airtight);
            }

            MyCubeBlockDefinition captured = definition;
            DoorKind capturedDoor = door;
            SealTest seals = delegate (Vector3I localCell, int face)
            {
                return Seals(captured, localCell, face, capturedDoor);
            };

            int[] surfaces = BlockSurfaceBuilder.BuildSurfaces(definition.Size, airtight, seals, mounts);

            // A block with no mount surface anywhere conducts to nothing, which is a far worse
            // failure than over-reporting contact. If the definition's mount rectangles produced
            // nothing at all, fall back to mounting everywhere and count it, so the report says
            // which definitions the reader could not describe.
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
        /// Whether one face of one cell of the definition seals when the block is complete, and
        /// — for a door — closed.
        ///
        /// This mirrors <c>MyGridGasSystem.IsAirtightBlock</c>: the pressurisation table first,
        /// and then, for a door, the rule per door family. The table alone is not enough. An
        /// airtight sliding door's mount points along the way through are the 5% frame either
        /// side, which does not cover the face, so the table marks it not pressurised — while the
        /// game seals it by door rule and the player watches it hold air. Reading only the table
        /// leaves every room with a door in it unsealed.
        ///
        /// The open state belongs to the placed block, through
        /// <see cref="BlockInstance.IsSealedByDoorState"/> and the model's open surface set.
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

        /// <summary>
        /// The closed-door half of the game's rule, in the block's own local space.
        /// </summary>
        private static bool SealsAsDoor(MyCubeBlockDefinition definition, int face, DoorKind door)
        {
            switch (door)
            {
                case DoorKind.AirtightSlide:
                    return face == Face.Forward;

                case DoorKind.AirtightGeneric:
                    return face == Face.Forward || face == Face.Backward;

                case DoorKind.Plain:
                    // Everything the door does not mount through: a closed door is a wall
                    // except where it is bolted to one.
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

        /// <summary>Copies a definition read through Definition Extensions into the model's own type.</summary>
        public static BlockThermalProperties ToThermalProperties(ThermalCellDefinition definition)
        {
            BlockThermalProperties properties = new BlockThermalProperties();
            if (definition == null) return properties.Clamp();

            properties.IgnoreThermals = definition.IgnoreThermals;
            properties.Conductivity = definition.Conductivity;
            properties.SpecificHeat = definition.SpecificHeat;
            properties.Emissivity = definition.Emissivity;
            properties.SurfaceAreaScaler = definition.SurfaceAreaScaler;
            properties.ProducerWasteEnergy = definition.ProducerWasteEnergy;
            properties.ConsumerWasteEnergy = definition.ConsumerWasteEnergy;
            properties.CriticalTemperature = definition.CriticalTemperature;
            properties.CriticalTemperatureScaler = definition.CriticalTemperatureScaler;

            return properties.Clamp();
        }

        /// <summary>Copies a loop definition into the model's own type.</summary>
        public static LoopThermalProperties ToLoopProperties(ThermalLoopDefintion definition)
        {
            LoopThermalProperties properties = new LoopThermalProperties();
            if (definition == null) return properties.Clamp();

            properties.Mass = definition.Mass;
            properties.Conductivity = definition.Conductivity;
            properties.SpecificHeat = definition.SpecificHeat;
            properties.PipeSurfaceAreaScaler = definition.PipeSurfaceAreaScaler;
            properties.PlateSurfaceAreaScaler = definition.PlateSurfaceAreaScaler;

            return properties.Clamp();
        }

        /// <summary>Copies a planet definition into the model's own type.</summary>
        public static PlanetThermalProperties ToPlanetProperties(PlanetDefinition definition)
        {
            PlanetThermalProperties properties = new PlanetThermalProperties();
            if (definition == null) return properties.Clamp();

            properties.NightTemperature = definition.NightTemperature;
            properties.DayTemperature = definition.DayTemperature;
            properties.UndergroundTemperature = definition.UndergroundTemperature;
            properties.CoreTemperature = definition.CoreTemperature;
            properties.SealevelDeadzone = definition.SealevelDeadzone;
            properties.SolarDecay = definition.SolarDecay;
            properties.ConvectionCoefficient = definition.ConvectionCoefficient;

            return properties.Clamp();
        }
    }
}
