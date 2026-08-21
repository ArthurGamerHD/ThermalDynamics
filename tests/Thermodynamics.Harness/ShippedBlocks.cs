using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// The mod's own blocks, read from the shipped XML at run time and turned into
    /// <see cref="BlockModel"/>s the simulation can be built from.
    ///
    /// <see cref="Catalog"/> holds hand-written stand-ins, and says so: its masses and properties
    /// are approximations that exist so scenarios behave plausibly. That is the right thing for a
    /// scenario asking a qualitative question. It is the wrong thing for a balance pass, where the
    /// entire point is what the numbers *are*. This class closes the gap: mass comes from the
    /// block's component list priced through <see cref="Vanilla.ComponentMasses"/>, size and mount
    /// faces from its <c>.sbc</c>, thermal properties from <c>Data/Cubes.xml</c>, and coolant and
    /// heat-pump geometry from the same two tables the game adapter uses.
    ///
    /// Nothing here is transcribed. Change a definition and the next run measures the change,
    /// which is what makes a balance figure worth quoting.
    /// </summary>
    public static class ShippedBlocks
    {
        /// <summary>Everything the definitions say about one shipped block.</summary>
        public class Definition
        {
            public string Subtype;
            public string TypeId;
            public bool Large;
            public Vector3I Size;

            /// <summary>Kilograms, summed from the component list.</summary>
            public float Mass;

            /// <summary>Build components, in definition order.</summary>
            public readonly List<KeyValuePair<string, int>> Components = new List<KeyValuePair<string, int>>();

            /// <summary>Declared PCU, or 0 when the definition does not say.</summary>
            public int Pcu;

            /// <summary>Declared build time in seconds, or 0 when the definition does not say.</summary>
            public float BuildSeconds;

            /// <summary>Which of the six faces carry at least one mount point.</summary>
            public readonly bool[] MountFaces = new bool[Face.Count];

            /// <summary>Thermal properties resolved from Cubes.xml, falling back as the game does.</summary>
            public BlockThermalProperties Thermal;

            /// <summary>True when this block's properties came from its own entry rather than the fallback.</summary>
            public bool HasOwnThermalEntry;

            public int CellCount
            {
                get { return Size.X * Size.Y * Size.Z; }
            }

            public float GridSize
            {
                get { return Large ? Catalog.LargeGridSize : Catalog.SmallGridSize; }
            }

            /// <summary>Heat capacity in J/K at the given thermal clock — what a kelvin costs.</summary>
            public float CapacityJoulesPerKelvin(float heatTimeScale)
            {
                float scale = heatTimeScale > 0f ? heatTimeScale : 1f;
                return Mass * Thermal.SpecificHeat / scale;
            }

            public override string ToString()
            {
                return Subtype;
            }
        }

        private static readonly object Gate = new object();
        private static Dictionary<string, Definition> cache;
        private static Dictionary<string, BlockThermalProperties> byTypeCache;
        private static BlockThermalProperties fallbackCache;
        private static string repoRoot;

        /// <summary>
        /// Walks up from the running assembly for the repository root, identified by the data
        /// files themselves rather than by a fixed depth, so this survives a change of target
        /// framework or output layout. Same approach as <c>ShippedDefinitionTests</c>.
        /// </summary>
        public static string RepoRoot()
        {
            lock (Gate)
            {
                if (repoRoot != null) return repoRoot;

                // From the assembly first, which is where it is when the build output sits inside
                // the repository, and from this file's own compiled-in path second, which is where
                // it is when the output has been sent elsewhere — as it now is, so that build
                // artifacts do not end up in the published mod. See Directory.Build.props.
                repoRoot = Above(AppContext.BaseDirectory) ?? Above(SourceDirectory());
                if (repoRoot != null) return repoRoot;

                throw new InvalidOperationException(
                    "Could not find the repository root from " + AppContext.BaseDirectory
                    + " or from " + SourceDirectory());
            }
        }

        /// <summary>The first directory at or above <paramref name="start"/> holding the mod's data.</summary>
        private static string Above(string start)
        {
            if (string.IsNullOrEmpty(start)) return null;

            DirectoryInfo directory = new DirectoryInfo(start);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Data", "Cubes.xml")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            return null;
        }

        /// <summary>
        /// The directory this source file was compiled from.
        ///
        /// Baked in at build time, so it survives the output assembly being written anywhere at
        /// all — which it now is, because build artifacts inside the mod folder are build artifacts
        /// inside whatever gets published.
        /// </summary>
        private static string SourceDirectory([CallerFilePath] string file = "")
        {
            return string.IsNullOrEmpty(file) ? null : Path.GetDirectoryName(file);
        }

        /// <summary>Every block the mod ships, keyed by subtype.</summary>
        public static Dictionary<string, Definition> All()
        {
            lock (Gate)
            {
                if (cache != null) return cache;
                cache = Load();
                return cache;
            }
        }

        /// <summary>
        /// The properties a block of this object-builder type resolves to when it has no entry of
        /// its own — the mod's per-type entry if Cubes.xml carries one, otherwise the environment
        /// default.
        ///
        /// This is how every vanilla block in the game is handed thermal properties without being
        /// named, so it is also the only honest way to ask what a vanilla reactor will be given.
        /// </summary>
        public static BlockThermalProperties ThermalForType(string typeId)
        {
            All();
            BlockThermalProperties properties;
            lock (Gate)
            {
                if (byTypeCache != null && byTypeCache.TryGetValue(typeId, out properties)) return properties;
                return fallbackCache ?? BlockThermalProperties.Default();
            }
        }

        public static Definition Get(string subtype)
        {
            Definition definition;
            if (!All().TryGetValue(subtype, out definition))
            {
                throw new KeyNotFoundException("No shipped block definition for " + subtype);
            }
            return definition;
        }

        /// <summary>Subtypes of every shipped block, in a stable order so reports are diffable.</summary>
        public static List<string> Subtypes()
        {
            List<string> names = new List<string>(All().Keys);
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        // ---- building a model ----------------------------------------------------------------

        /// <summary>
        /// A <see cref="BlockModel"/> carrying this block's real mass, thermal properties, mount
        /// faces and — where it has them — coolant ports or heat-pump hardware.
        ///
        /// Mount faces matter more than they look. Conduction only crosses a joint where *both*
        /// blocks carry a mount surface, so a radiator that mounts on two faces conducts on two
        /// faces however large the panel is. Copying that from the definition rather than assuming
        /// a solid block is the difference between measuring the shipped radiator and measuring a
        /// cube of aluminium.
        /// </summary>
        public static BlockModel Model(string subtype)
        {
            Definition definition = Get(subtype);
            BlockModel model = BlockModel.Solid(subtype, definition.Size, definition.Mass, definition.Thermal);

            bool everyFace = true;
            for (int face = 0; face < Face.Count; face++)
            {
                if (!definition.MountFaces[face]) everyFace = false;
            }

            if (!everyFace)
            {
                foreach (Vector3I cell in model.LocalCells())
                {
                    int state = CellSurface.SelfAirtightMask;
                    for (int face = 0; face < Face.Count; face++)
                    {
                        state = CellSurface.WithSelfMount(state, face, definition.MountFaces[face]);
                    }
                    model.SetLocalSurface(cell, state);
                }
            }

            CoolantShape coolant = ThermalCoolantShapes.Get(subtype, definition.Size);
            if (coolant != null) model.WithCoolant(coolant);

            HeatPumpShape pump = ThermalHeatPumpShapes.Get(subtype, definition.Size);
            if (pump != null) model.WithHeatPump(pump);

            return model;
        }

        // ---- parsing -------------------------------------------------------------------------

        private static Dictionary<string, Definition> Load()
        {
            Dictionary<string, Definition> blocks = new Dictionary<string, Definition>(StringComparer.Ordinal);
            string folder = Path.Combine(RepoRoot(), "Data", "CubeBlocks");

            foreach (string file in Directory.GetFiles(folder, "*.sbc"))
            {
                XDocument document = XDocument.Load(file);
                foreach (XElement element in document.Descendants("Definition"))
                {
                    Definition definition = ParseBlock(element);
                    if (definition != null) blocks[definition.Subtype] = definition;
                }
            }

            ApplyThermal(blocks);
            return blocks;
        }

        private static Definition ParseBlock(XElement element)
        {
            XElement id = element.Element("Id");
            if (id == null) return null;

            XElement subtype = id.Element("SubtypeId");
            XElement type = id.Element("TypeId");
            if (subtype == null || type == null) return null;

            Definition definition = new Definition
            {
                Subtype = subtype.Value.Trim(),
                TypeId = type.Value.Trim(),
                Large = (string)element.Element("CubeSize") == "Large",
                Size = ParseSize(element.Element("Size")),
                Pcu = ParseInt(element.Element("PCU")),
                BuildSeconds = ParseFloat(element.Element("BuildTimeSeconds")),
            };

            XElement components = element.Element("Components");
            if (components != null)
            {
                foreach (XElement component in components.Elements("Component"))
                {
                    string name = (string)component.Attribute("Subtype");
                    int count = ParseInt(component.Attribute("Count"));
                    if (name != null && count > 0)
                    {
                        definition.Components.Add(new KeyValuePair<string, int>(name, count));
                    }
                }
            }
            definition.Mass = Vanilla.MassOf(definition.Components);

            XElement mounts = element.Element("MountPoints");
            if (mounts != null)
            {
                foreach (XElement mount in mounts.Elements("MountPoint"))
                {
                    int face = FaceOf((string)mount.Attribute("Side"));
                    if (face >= 0) definition.MountFaces[face] = true;
                }
            }

            return definition;
        }

        /// <summary>
        /// Attaches thermal properties from Cubes.xml, resolving as the game's Definition
        /// Extensions do: a block's own entry if it has one, otherwise the entry for its object
        /// builder type, otherwise <c>DefaultThermodynamics</c>.
        /// </summary>
        private static void ApplyThermal(Dictionary<string, Definition> blocks)
        {
            Dictionary<string, BlockThermalProperties> bySubtype =
                new Dictionary<string, BlockThermalProperties>(StringComparer.Ordinal);
            Dictionary<string, BlockThermalProperties> byType =
                new Dictionary<string, BlockThermalProperties>(StringComparer.Ordinal);
            BlockThermalProperties fallback = null;

            XDocument cubes = XDocument.Load(Path.Combine(RepoRoot(), "Data", "Cubes.xml"));
            foreach (XElement element in cubes.Descendants("Definition"))
            {
                XElement id = element.Element("Id");
                if (id == null) continue;

                string type = ((string)id.Element("TypeId") ?? "").Trim();
                string subtype = ((string)id.Element("SubtypeId") ?? "").Trim();
                BlockThermalProperties properties = ParseThermal(element);
                if (properties == null) continue;

                if (subtype == "DefaultThermodynamics")
                {
                    if (type == "EnvironmentDefinition") fallback = properties;
                    else byType[type] = properties;
                }
                else
                {
                    bySubtype[subtype] = properties;
                }
            }

            if (fallback == null) fallback = BlockThermalProperties.Default();
            byTypeCache = byType;
            fallbackCache = fallback;

            foreach (Definition definition in blocks.Values)
            {
                BlockThermalProperties properties;
                if (bySubtype.TryGetValue(definition.Subtype, out properties))
                {
                    definition.Thermal = properties;
                    definition.HasOwnThermalEntry = true;
                }
                else if (byType.TryGetValue(definition.TypeId, out properties))
                {
                    definition.Thermal = properties;
                }
                else
                {
                    definition.Thermal = fallback;
                }
            }
        }

        private static BlockThermalProperties ParseThermal(XElement definition)
        {
            XElement group = null;
            foreach (XElement candidate in definition.Descendants("Group"))
            {
                if ((string)candidate.Attribute("Name") == "ThermalBlockProperties") group = candidate;
            }
            if (group == null) return null;

            BlockThermalProperties properties = BlockThermalProperties.Default();
            foreach (XElement value in group.Elements())
            {
                string name = (string)value.Attribute("Name");
                string raw = (string)value.Attribute("Value");
                if (name == null || raw == null) continue;

                float number;
                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) continue;

                // The legacy spellings are accepted here for the same reason the game accepts
                // them: a definition written before the rename must still mean what it said.
                switch (name)
                {
                    case "Conductivity": properties.Conductivity = number; break;
                    case "SpecificHeat": properties.SpecificHeat = number; break;
                    case "Emissivity": properties.Emissivity = number; break;
                    case "ExposedSurfaceMultiplier":
                    case "SurfaceAreaScaler": properties.ExposedSurfaceMultiplier = number; break;
                    case "ProducerWasteEnergy": properties.ProducerWasteEnergy = number; break;
                    case "ConsumerWasteEnergy": properties.ConsumerWasteEnergy = number; break;
                    case "CriticalTemperature": properties.CriticalTemperature = number; break;
                    case "OverheatDamagePerKelvin":
                    case "CriticalTemperatureScaler": properties.OverheatDamagePerKelvin = number; break;
                }
            }
            return properties;
        }

        // ---- small helpers -------------------------------------------------------------------

        private static Vector3I ParseSize(XElement size)
        {
            if (size == null) return Vector3I.One;
            return new Vector3I(
                Math.Max(1, ParseInt(size.Attribute("x"))),
                Math.Max(1, ParseInt(size.Attribute("y"))),
                Math.Max(1, ParseInt(size.Attribute("z"))));
        }

        private static int ParseInt(XAttribute attribute)
        {
            int value;
            return attribute != null
                && int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value : 0;
        }

        private static int ParseInt(XElement element)
        {
            int value;
            return element != null
                && int.TryParse(element.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value : 0;
        }

        private static float ParseFloat(XElement element)
        {
            float value;
            return element != null
                && float.TryParse(element.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value : 0f;
        }

        /// <summary>
        /// Maps a definition's mount-point side name onto a simulation face.
        ///
        /// Space Engineers names a block's sides from the outside looking in, and the simulation
        /// names directions from the block outward, so <c>Front</c> is the face pointing
        /// <c>Forward</c> and so on. The two agree on all six; the mapping is written out because
        /// "obviously they line up" is how a silent off-by-one gets in.
        /// </summary>
        private static int FaceOf(string side)
        {
            switch (side)
            {
                case "Front": return Face.Forward;
                case "Back": return Face.Backward;
                case "Left": return Face.Left;
                case "Right": return Face.Right;
                case "Top": return Face.Up;
                case "Bottom": return Face.Down;
                default: return -1;
            }
        }
    }
}
