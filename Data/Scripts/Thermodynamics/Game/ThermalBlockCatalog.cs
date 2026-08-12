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

        /// <summary>Definitions resolved so far. One entry per block type ever placed.</summary>
        public static int ModelCount
        {
            get { return Models.Count; }
        }

        /// <summary>Definitions built, i.e. cache misses. One per block type, not per block.</summary>
        public static int ModelsBuilt;

        private static readonly List<MountRect> MountScratch = new List<MountRect>();

        /// <summary>
        /// The model for a block's type. Never null; a definition the game cannot describe falls
        /// back to a solid cube of the right size.
        /// </summary>
        public static BlockModel Get(IMySlimBlock block)
        {
            if (block == null || block.BlockDefinition == null) return null;

            MyDefinitionId id = block.BlockDefinition.Id;

            BlockModel model;
            if (Models.TryGetValue(id, out model)) return model;

            model = Build(block.BlockDefinition as MyCubeBlockDefinition, id);
            Models[id] = model;
            ModelsBuilt++;
            return model;
        }

        /// <summary>Drops every cached model. Called when a session ends.</summary>
        public static void Clear()
        {
            Models.Clear();
            ModelsBuilt = 0;
        }

        private static BlockModel Build(MyCubeBlockDefinition definition, MyDefinitionId id)
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
            model.LocalSurfaces = BuildSurfaces(definition);
            model.Coolant = ThermalCoolantShapes.Get(id.SubtypeName, definition.Size);

            return model;
        }

        /// <summary>
        /// Reads the block's airtightness and mount points out of the definition, in the block's
        /// own local space. Orientation is applied per placed block by
        /// <see cref="BlockInstance"/>, so nothing here needs to know how the block is turned.
        /// </summary>
        private static int[] BuildSurfaces(MyCubeBlockDefinition definition)
        {
            MountScratch.Clear();
            if (definition.MountPoints != null)
            {
                for (int i = 0; i < definition.MountPoints.Length; i++)
                {
                    MyCubeBlockDefinition.MountPoint mount = definition.MountPoints[i];
                    MountRect rect = new MountRect(mount.Normal, mount.Start, mount.End);
                    rect.Enabled = mount.Enabled;
                    MountScratch.Add(rect);
                }
            }

            bool airtight = definition.IsAirTight == true;

            if (definition.IsCubePressurized == null)
            {
                // No pressurisation table: fall back to mounts everywhere so conduction still
                // works, and let the airtight flag decide sealing.
                return BlockSurfaceBuilder.BuildFallbackSurfaces(definition.Size, airtight);
            }

            MyCubeBlockDefinition captured = definition;
            SealTest seals = delegate (Vector3I localCell, int face)
            {
                return Seals(captured, localCell, face);
            };

            int[] surfaces = BlockSurfaceBuilder.BuildSurfaces(definition.Size, airtight, seals, MountScratch);

            // A block with no mount surface anywhere conducts to nothing, which is a far worse
            // failure than over-reporting contact. If the definition's mount rectangles produced
            // nothing at all, fall back to mounting everywhere and count it, so the report says
            // which definitions the reader could not describe.
            if (!HasAnyMount(surfaces))
            {
                MountFallbacks++;
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
        /// Whether one face of one cell of the definition seals when the block is complete.
        ///
        /// A door face that only seals when closed counts as sealing here; the open state is a
        /// property of the placed block, and clears its sealing through
        /// <see cref="BlockInstance.IsSealedByDoorState"/>.
        /// </summary>
        private static bool Seals(MyCubeBlockDefinition definition, Vector3I localCell, int face)
        {
            try
            {
                Dictionary<Vector3I, MyCubeBlockDefinition.MyCubePressurizationMark> byDirection;
                if (!definition.IsCubePressurized.TryGetValue(localCell, out byDirection)) return false;

                MyCubeBlockDefinition.MyCubePressurizationMark mark;
                if (!byDirection.TryGetValue(Face.Offsets[face], out mark)) return false;

                return mark == MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedAlways
                    || mark == MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedClosed;
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalBlockCatalog.Seals", e);
                return false;
            }
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
