using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Every block Space Engineers ships, read from the installed game's own definitions.
    ///
    /// <see cref="Vanilla"/> transcribes fourteen reference blocks because the balance report has
    /// to run on a machine with no install. This does not transcribe: it is the authoring side of
    /// the definition pass, and there is no honest way to hand-copy 1,503 definitions. Everything
    /// here returns empty when the game is absent, and the tests that use it skip rather than fail
    /// — the *output* of the derivation is checked in as `Data/Cubes.xml` and pinned by a test that
    /// needs no install at all.
    /// </summary>
    public static class GameBlocks
    {
        public class Definition
        {
            public string TypeId;
            public string SubtypeId;
            public bool Large;

            /// <summary>Cells the block occupies, from its own <c>Size</c> element.</summary>
            public Vector3I Size = Vector3I.One;

            /// <summary>Which of the six faces carry a mount point, so a joint can be found.</summary>
            public readonly bool[] MountFaces = new bool[Face.Count];

            /// <summary>
            /// Whether the definition listed any mount points at all.
            ///
            /// **A definition that lists none is not a block that mounts nowhere** — it is a block
            /// whose mount points the game derives from its model geometry, which this harness
            /// cannot read. Six per cent of the game's definitions are in that state, and reading
            /// their silence as "no mounts" builds a block with no conduction links and no exposed
            /// faces: a thermally sealed box, which heats without bound and without a symptom
            /// beyond the temperature. `LargeBlockBatteryBlock` is one of them.
            /// </summary>
            public bool HasDeclaredMounts;

            /// <summary>
            /// Whether the definition seals. Read from <c>IsAirTight</c>, which is a tri-state in
            /// the game: absent means "decide per face from the pressurisation table", which this
            /// harness approximates as sealing wherever the block mounts.
            /// </summary>
            public bool? Airtight;

            /// <summary>
            /// Rated electrical output in watts, for a block that delivers power through
            /// <c>MyResourceSourceComponent</c> — reactors, engines, batteries, panels, turbines.
            /// </summary>
            public float PowerOutputWatts;

            /// <summary>
            /// Rated electrical draw in watts. The definitions spell this four different ways
            /// depending on the block's age, and all four are read.
            /// </summary>
            public float PowerDrawWatts;

            /// <summary>
            /// Thrust in newtons, used as a watt-equivalent by the waste-heat model. It is what
            /// makes a hydrogen thruster heat at all: it draws no electricity, so thrust is the
            /// only term that can represent it. See docs/thermal-model.md.
            ///
            /// **Only a <c>Thrust</c> block has one.** Gyros carry the same
            /// <c>ForceMagnitude</c> element, and it means torque in newton-metres rather than
            /// thrust in newtons — a large gyro reads 3.36e7 and a prototech one 2.016e8 against a
            /// real draw of ten kilowatts. Reading it off every block that has the element turned
            /// one gyro into 33.6 MW of waste heat and drove a real hull to 342,000 K, which looked
            /// convincingly like solver instability and was arithmetic.
            /// </summary>
            public float ThrustNewtons;

            /// <summary>Build cost, priced with the game's own component masses.</summary>
            public List<BlockComponent> Components = new List<BlockComponent>();

            /// <summary>
            /// The block's full hit points, summed from its components' <c>MaxIntegrity</c> exactly
            /// as the game sums them.
            ///
            /// <para>
            /// This is the denominator of every damage figure. The solver hands
            /// <c>DoDamage</c> a number in these units — <c>(T - critical) x
            /// OverheatDamagePerKelvin</c> per simulated second — so without the integrity a damage
            /// rate says nothing about how long the block has. See balance.md, How long a block has
            /// after it crosses.
            /// </para>
            /// </summary>
            public float Integrity;

            /// <summary>Kilograms, summed from the components.</summary>
            public float Mass
            {
                get
                {
                    float total = 0f;
                    for (int i = 0; i < Components.Count; i++) total += Components[i].Mass;
                    return total;
                }
            }

            public override string ToString()
            {
                return TypeId + "/" + SubtypeId;
            }
        }

        /// <summary>
        /// The installed game's `Content/Data`, or null. The same candidate list
        /// <c>BalanceTests.GameContentPath</c> walks, kept here so the harness can be used from the
        /// command line rather than only from a test.
        /// </summary>
        public static string ContentPath()
        {
            List<string> candidates = new List<string>();

            string bin = Environment.GetEnvironmentVariable("SE_BIN");
            if (!string.IsNullOrEmpty(bin))
            {
                DirectoryInfo parent = Directory.GetParent(bin.TrimEnd('/', '\\'));
                if (parent != null) candidates.Add(Path.Combine(parent.FullName, "Content", "Data"));
            }

            string home = Environment.GetEnvironmentVariable("HOME") ?? "";
            candidates.Add(Path.Combine(home,
                "Steam/SteamLibrary/steamapps/common/SpaceEngineers/Content/Data"));
            candidates.Add("C:/Program Files (x86)/Steam/steamapps/common/SpaceEngineers/Content/Data");

            foreach (string candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "Components.sbc"))) return candidate;
            }
            return null;
        }

        public static bool IsInstalled
        {
            get { return ContentPath() != null; }
        }

        private static List<Definition> _all;
        private static Dictionary<string, float> _componentMasses;
        private static Dictionary<string, float> _componentIntegrities;

        /// <summary>
        /// Guards the three lazy caches below.
        ///
        /// The lab runs ships concurrently, so every one of these is read from several workers at
        /// once and built by whichever gets there first. An unguarded lazy field is the classic way
        /// to hand one thread a half-built dictionary.
        /// </summary>
        private static readonly object CacheLock = new object();

        /// <summary>
        /// Builds every cache up front, on one thread.
        ///
        /// Called before a parallel region so the workers find them warm. Correctness does not
        /// depend on it — the locks cover that — but without it every worker blocks on the first
        /// one to arrive, which on a corpus of thousands is the whole first minute.
        /// </summary>
        public static void Warm()
        {
            All();
            BySubtype();
        }

        /// <summary>Component name to kilograms, from the installed `Components.sbc`.</summary>
        public static Dictionary<string, float> ComponentMasses()
        {
            lock (CacheLock)
            {
            if (_componentMasses != null) return _componentMasses;

            Dictionary<string, float> masses = new Dictionary<string, float>();
            string content = ContentPath();

            if (content != null)
            {
                foreach (XElement component in XDocument.Load(Path.Combine(content, "Components.sbc"))
                             .Descendants("Component"))
                {
                    XElement id = component.Element("Id");
                    if (id == null) continue;

                    string subtype = (string)id.Element("SubtypeId");
                    float mass;
                    if (subtype != null && float.TryParse((string)component.Element("Mass"),
                            NumberStyles.Float, CultureInfo.InvariantCulture, out mass))
                    {
                        masses[subtype] = mass;
                    }
                }
            }

            _componentMasses = masses;
            return masses;
            }
        }

        /// <summary>
        /// Component name to hit points, from the installed `Components.sbc`.
        ///
        /// The counterpart of <see cref="ComponentMasses"/>, and read from the same file in the same
        /// pass shape: a block's integrity is the sum of its components' <c>MaxIntegrity</c>, which
        /// is how the game itself builds <c>MyCubeBlockDefinition.MaxIntegrity</c>.
        /// </summary>
        public static Dictionary<string, float> ComponentIntegrities()
        {
            lock (CacheLock)
            {
            if (_componentIntegrities != null) return _componentIntegrities;

            Dictionary<string, float> integrities = new Dictionary<string, float>();
            string content = ContentPath();

            if (content != null)
            {
                foreach (XElement component in XDocument.Load(Path.Combine(content, "Components.sbc"))
                             .Descendants("Component"))
                {
                    XElement id = component.Element("Id");
                    if (id == null) continue;

                    string subtype = (string)id.Element("SubtypeId");
                    float integrity;
                    if (subtype != null && float.TryParse((string)component.Element("MaxIntegrity"),
                            NumberStyles.Float, CultureInfo.InvariantCulture, out integrity))
                    {
                        integrities[subtype] = integrity;
                    }
                }
            }

            _componentIntegrities = integrities;
            return integrities;
            }
        }

        /// <summary>
        /// Hit points a block of this subtype has, or zero where the install is absent or the
        /// subtype unknown. The lookup a lab uses to turn a stream of damage into a block lost.
        /// </summary>
        public static float IntegrityOf(string subtype)
        {
            if (string.IsNullOrEmpty(subtype)) return 0f;

            Definition definition;
            return BySubtype().TryGetValue(subtype, out definition) ? definition.Integrity : 0f;
        }

        /// <summary>Every block definition in the installed game, or an empty list.</summary>
        public static List<Definition> All()
        {
            lock (CacheLock)
            {
            if (_all != null) return _all;

            List<Definition> blocks = new List<Definition>();
            string content = ContentPath();
            if (content == null)
            {
                _all = blocks;
                return blocks;
            }

            Dictionary<string, float> masses = ComponentMasses();
            Dictionary<string, float> integrities = ComponentIntegrities();
            string directory = Path.Combine(content, "CubeBlocks");
            if (!Directory.Exists(directory))
            {
                _all = blocks;
                return blocks;
            }

            foreach (string file in Directory.GetFiles(directory, "*.sbc"))
            {
                XDocument document;
                try
                {
                    document = XDocument.Load(file);
                }
                catch
                {
                    // A definition file the game itself tolerates but XDocument will not is not a
                    // reason to fail the pass; the block simply keeps its fallback.
                    continue;
                }

                foreach (XElement definition in document.Descendants("Definition"))
                {
                    Definition block = Read(definition, masses, integrities);
                    if (block != null) blocks.Add(block);
                }
            }

            _all = blocks;
            return blocks;
            }
        }

        private static Definition Read(XElement definition, Dictionary<string, float> masses,
            Dictionary<string, float> integrities)
        {
            XElement id = definition.Element("Id");
            if (id == null) return null;

            string type = (string)id.Element("TypeId");
            string subtype = (string)id.Element("SubtypeId");
            if (string.IsNullOrEmpty(type)) return null;

            // The .sbc files spell the type both ways depending on their age.
            if (type.StartsWith("MyObjectBuilder_")) type = type.Substring("MyObjectBuilder_".Length);

            Definition block = new Definition
            {
                TypeId = type,
                SubtypeId = subtype ?? "",
                Large = ((string)definition.Element("CubeSize") ?? "Large") == "Large",
                Size = ParseSize(definition.Element("Size")),
            };

            // Megawatts in the definitions, watts everywhere in this model.
            block.PowerOutputWatts = Megawatts(definition, "MaxPowerOutput");
            block.PowerDrawWatts = Math.Max(
                Megawatts(definition, "RequiredPowerInput"),
                Math.Max(Megawatts(definition, "MaxRequiredPowerInput"),
                    Math.Max(Megawatts(definition, "MaxPowerConsumption"),
                        Megawatts(definition, "OperationalPowerConsumption"))));
            if (block.TypeId == "Thrust") block.ThrustNewtons = Number(definition, "ForceMagnitude");

            bool airtight;
            string airtightText = (string)definition.Element("IsAirTight");
            if (airtightText != null && bool.TryParse(airtightText, out airtight)) block.Airtight = airtight;

            XElement mounts = definition.Element("MountPoints");
            if (mounts != null)
            {
                foreach (XElement mount in mounts.Elements("MountPoint"))
                {
                    int face = FaceOf((string)mount.Attribute("Side"));
                    if (face < 0) continue;

                    block.MountFaces[face] = true;
                    block.HasDeclaredMounts = true;
                }
            }

            XElement components = definition.Element("Components");
            if (components != null)
            {
                foreach (XElement component in components.Elements("Component"))
                {
                    string name = (string)component.Attribute("Subtype");
                    if (name == null) continue;

                    int count;
                    if (!int.TryParse((string)component.Attribute("Count"),
                            NumberStyles.Integer, CultureInfo.InvariantCulture, out count)) continue;

                    // Integrity is summed before the mass lookup can reject the component: the
                    // game prices hit points off every component in the list, whether or not this
                    // harness knows what it weighs.
                    float integrityEach;
                    if (integrities.TryGetValue(name, out integrityEach))
                    {
                        block.Integrity += count * integrityEach;
                    }

                    float mass;
                    if (!masses.TryGetValue(name, out mass)) continue;

                    block.Components.Add(new BlockComponent(name, count, mass));
                }
            }

            return block;
        }

        private static float Megawatts(XElement definition, string name)
        {
            return Number(definition, name) * ThermalConstants.MegawattsToWatts;
        }

        private static float Number(XElement definition, string name)
        {
            float value;
            string text = (string)definition.Element(name);
            return text != null && float.TryParse(text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) ? value : 0f;
        }

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
            return attribute != null && int.TryParse((string)attribute,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 1;
        }

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

        /// <summary>Every definition by subtype, for a blueprint to look its blocks up in.</summary>
        public static Dictionary<string, Definition> BySubtype()
        {
            lock (CacheLock)
            {
            if (_bySubtype != null) return _bySubtype;

            Dictionary<string, Definition> map = new Dictionary<string, Definition>(StringComparer.Ordinal);
            foreach (Definition block in All())
            {
                // A subtype can appear under more than one type across the files; first wins, which
                // matches the order the game loads them in.
                if (!map.ContainsKey(block.SubtypeId)) map[block.SubtypeId] = block;
            }

            _bySubtype = map;
            return map;
            }
        }

        private static Dictionary<string, Definition> _bySubtype;

        /// <summary>Definitions grouped by type id, in the order the files list them.</summary>
        public static Dictionary<string, List<Definition>> ByType()
        {
            Dictionary<string, List<Definition>> types = new Dictionary<string, List<Definition>>();

            foreach (Definition block in All())
            {
                List<Definition> list;
                if (!types.TryGetValue(block.TypeId, out list))
                {
                    list = new List<Definition>();
                    types[block.TypeId] = list;
                }
                list.Add(block);
            }

            return types;
        }

        /// <summary>
        /// The build cost of a whole type, summed over every subtype of it.
        ///
        /// This is what a type's fallback entry describes: not any one block, but the material a
        /// block of that type is typically made of, weighted so the common subtypes count for more
        /// than the rare ones — which is the right weighting for an entry whose job is to be a
        /// reasonable answer for whatever is not named individually.
        /// </summary>
        public static List<BlockComponent> TypeComponents(IList<Definition> blocks)
        {
            Dictionary<string, BlockComponent> total = new Dictionary<string, BlockComponent>();

            for (int i = 0; i < blocks.Count; i++)
            {
                List<BlockComponent> components = blocks[i].Components;
                for (int j = 0; j < components.Count; j++)
                {
                    BlockComponent line = components[j];

                    BlockComponent running;
                    if (total.TryGetValue(line.Component, out running))
                    {
                        running.Count += line.Count;
                        total[line.Component] = running;
                    }
                    else
                    {
                        total[line.Component] = line;
                    }
                }
            }

            return new List<BlockComponent>(total.Values);
        }
    }
}
