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
    /// Real ships, read from Space Engineers blueprint files and turned into grids the solver can
    /// run.
    ///
    /// <para>
    /// Everything this repository has measured has been measured on a hull it built itself.
    /// <see cref="Census"/> is honest about what that costs — its tiers come from the telemetry
    /// dump of one 1,381-block ship, and its own summary says "one ship is one ship". A synthetic
    /// hull built from those tiers asks for about the substeps a real hull asks for, which is a
    /// better hypothesis than heavy armour and gratings and is still a hypothesis.
    /// </para>
    ///
    /// <para>
    /// A blueprint corpus replaces the hypothesis with a population. It is the difference between
    /// "a ship like this overheats" and "eleven per cent of the ships people actually build
    /// overheat", and only the second kind of statement can decide a default.
    /// </para>
    /// </summary>
    public static class Blueprints
    {
        /// <summary>One grid inside a blueprint: a hull, or a turret on a rotor.</summary>
        public class Grid
        {
            public string Name;
            public bool Large;
            public int Blocks;
            public GridBuilder Builder;

            /// <summary>Entity id to the cell of the block carrying it, for resolving joints.</summary>
            public readonly Dictionary<long, Vector3I> BlocksById = new Dictionary<long, Vector3I>();

            /// <summary>Mechanical bases here: the cell each sits on, and the head it holds.</summary>
            public readonly List<KeyValuePair<Vector3I, long>> Mechanical =
                new List<KeyValuePair<Vector3I, long>>();
        }

        /// <summary>
        /// One ship: **a whole blueprint, subgrids included.**
        ///
        /// A blueprint is one machine, not a pile of separate ones. A turret is a small grid on a
        /// rotor bolted to the hull, a drilling rig is a piston stack, a hangar door is a set of
        /// advanced rotors. Reading each grid as its own ship measures something that does not
        /// exist — a turret floating in space with a heat budget of its own — and gets both halves
        /// wrong at once: the subgrid has no hull to dump into, and the hull has no subgrid warming
        /// it.
        /// </summary>
        public class Ship
        {
            /// <summary>The blueprint's own name.</summary>
            public string Name;

            /// <summary>Where it came from, so a surprising result can be looked at by hand.</summary>
            public string Path;

            /// <summary>Workshop id when the file came from a subscribed or fetched item, else 0.</summary>
            public long WorkshopId;

            /// <summary>Every grid in the blueprint, largest first.</summary>
            public readonly List<Grid> Grids = new List<Grid>();

            /// <summary>Whether the biggest grid in it is a large-grid one.</summary>
            public bool Large
            {
                get { return Grids.Count > 0 && Grids[0].Large; }
            }

            /// <summary>Blocks placed across every grid.</summary>
            public int Blocks
            {
                get
                {
                    int total = 0;
                    for (int i = 0; i < Grids.Count; i++) total += Grids[i].Blocks;
                    return total;
                }
            }

            /// <summary>Grids beyond the hull: turrets, doors, drills, piston stacks.</summary>
            public int Subgrids
            {
                get { return Grids.Count == 0 ? 0 : Grids.Count - 1; }
            }

            /// <summary>
            /// Blocks skipped because no vanilla definition carries that subtype. One is enough to
            /// disqualify the blueprint: there is no telling whether it was a decorative panel or
            /// the reactor.
            /// </summary>
            public int UnknownBlocks;

            public List<string> UnknownSubtypes = new List<string>();

            public bool IsVanilla
            {
                get { return UnknownBlocks == 0 && Blocks > 0; }
            }

            /// <summary>
            /// The whole blueprint as one running assembly: every grid stepped together, with heat
            /// crossing the mechanical joints between them.
            /// </summary>
            public ShipAssembly Build(ThermalSettings settings = null, float kelvin = 293.15f)
            {
                ThermalSettings effective = settings ?? new ThermalSettings();
                ShipAssembly assembly = new ShipAssembly();

                for (int i = 0; i < Grids.Count; i++)
                {
                    assembly.Simulations.Add(Grids[i].Builder.BuildSimulation(effective, kelvin));
                }

                LinkJoints(assembly);
                return assembly;
            }

            /// <summary>
            /// Resolves every mechanical connection the blueprint records into a bridge. A stator
            /// carries the entity id of the head it holds, and the head is a block in another grid
            /// with that id, so the joints are recoverable exactly.
            /// </summary>
            private void LinkJoints(ShipAssembly assembly)
            {
                for (int i = 0; i < Grids.Count; i++)
                {
                    Grid grid = Grids[i];

                    for (int m = 0; m < grid.Mechanical.Count; m++)
                    {
                        Vector3I baseCell = grid.Mechanical[m].Key;
                        long topId = grid.Mechanical[m].Value;

                        for (int j = 0; j < Grids.Count; j++)
                        {
                            if (j == i) continue;

                            Vector3I topCell;
                            if (!Grids[j].BlocksById.TryGetValue(topId, out topCell)) continue;

                            assembly.Bridge2(
                                assembly.Simulations[i], NodeAt(assembly.Simulations[i], baseCell),
                                assembly.Simulations[j], NodeAt(assembly.Simulations[j], topCell));
                            break;
                        }
                    }
                }
            }

            private static ThermalNode NodeAt(ThermalSimulation simulation, Vector3I cell)
            {
                IList<ThermalNode> nodes = simulation.Solver.Nodes;
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (nodes[i].Block.Position == cell) return nodes[i];
                }
                return null;
            }

            /// <summary>
            /// The same blueprint, read again, with grid state of its own. See the note in
            /// <c>BatteryLab</c>: this is what lets a run be the unit of parallelism.
            /// </summary>
            public Ship Reload()
            {
                if (Path == null) return this;

                List<Ship> ships = Read(Path);
                return ships.Count > 0 ? ships[0] : this;
            }

            public override string ToString()
            {
                return Name + " (" + Blocks + " blocks, " + Grids.Count + " grids)";
            }
        }

        /// <summary>
        /// **Where the corpus lives, and it is deliberately outside the mod folder.**
        ///
        /// This repository *is* the mod folder — it is linked into the game so a change is testable
        /// without copying — which means anything sitting in it is part of what gets published. A
        /// corpus of ten thousand ships is over a hundred gigabytes of other people's blueprints,
        /// and none of it belongs in a mod. Nor do the fetch manifest or the workshop ids in it.
        ///
        /// So it lives under the user's data directory instead, and the only thing publishing has
        /// to know about it is that it is not there. Override with `--path`, or `THERMAL_CORPUS`
        /// for a machine that wants it on another disk.
        /// </summary>
        public static string CorpusPath()
        {
            string configured = Environment.GetEnvironmentVariable("THERMAL_CORPUS");
            if (!string.IsNullOrEmpty(configured)) return configured;

            string data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrEmpty(data))
            {
                string home = Environment.GetEnvironmentVariable("HOME")
                    ?? Environment.GetEnvironmentVariable("USERPROFILE") ?? ".";
                data = Path.Combine(home, ".local", "share");
            }

            return Path.Combine(data, "thermal-dynamics", "corpus");
        }

        /// <summary>
        /// The directory a command should read when it was not told one: the corpus if it has
        /// anything in it, else the subscribed workshop items, else nothing.
        /// </summary>
        public static string DefaultPath()
        {
            string corpus = CorpusPath();
            if (Directory.Exists(corpus) && Files(corpus).Count > 0) return corpus;

            return WorkshopPath();
        }

        /// <summary>
        /// The subscribed-workshop directory of an installed game, or null.
        ///
        /// Items subscribed in game land here already unpacked, so a corpus can be grown by
        /// subscribing to blueprints and letting Steam fetch them — no credentials, no API and no
        /// rate limit. It is the slow way to ten thousand ships and the free way to the first few
        /// hundred, and it is the same on-disk layout a bulk download would produce.
        /// </summary>
        public static string WorkshopPath()
        {
            string content = GameBlocks.ContentPath();
            if (content == null) return null;

            // .../steamapps/common/SpaceEngineers/Content/Data -> .../steamapps/workshop/content/244850
            DirectoryInfo directory = new DirectoryInfo(content);
            for (int i = 0; i < 4 && directory != null; i++) directory = directory.Parent;
            if (directory == null) return null;

            string workshop = Path.Combine(directory.FullName, "workshop", "content", "244850");
            return Directory.Exists(workshop) ? workshop : null;
        }

        /// <summary>Every blueprint file under a directory, searched recursively.</summary>
        public static List<string> Files(string root)
        {
            List<string> files = new List<string>();
            if (root == null || !Directory.Exists(root)) return files;

            files.AddRange(Directory.GetFiles(root, "bp.sbc", SearchOption.AllDirectories));
            return files;
        }

        /// <summary>
        /// Reads one blueprint file into **one ship**, whatever number of grids it holds.
        ///
        /// Returns a list because a file can hold more than one blueprint, which is rare. It does
        /// not return one entry per grid. Never throws: a corpus of ten thousand files will contain
        /// some that no parser should die on.
        /// </summary>
        public static List<Ship> Read(string path)
        {
            List<Ship> ships = new List<Ship>();

            XDocument document;
            try
            {
                document = XDocument.Load(path);
            }
            catch
            {
                return ships;
            }

            Dictionary<string, GameBlocks.Definition> definitions = GameBlocks.BySubtype();

            string name = null;
            foreach (XElement definition in document.Descendants("ShipBlueprint"))
            {
                XElement id = definition.Element("Id");
                if (id != null) name = (string)id.Attribute("Subtype") ?? (string)id.Element("SubtypeId");
                break;
            }

            Ship ship = new Ship
            {
                Path = path,
                WorkshopId = WorkshopIdOf(path),
                Name = name,
            };

            foreach (XElement grid in document.Descendants("CubeGrid"))
            {
                Grid part = ReadGrid(grid, definitions, ship);
                if (part != null && part.Blocks > 0) ship.Grids.Add(part);
            }

            if (ship.Grids.Count == 0) return ships;

            // Largest first, so the hull is Grids[0] and a report can name the ship by it.
            ship.Grids.Sort(delegate (Grid a, Grid b) { return b.Blocks.CompareTo(a.Blocks); });

            if (string.IsNullOrEmpty(ship.Name)) ship.Name = ship.Grids[0].Name;

            ships.Add(ship);
            return ships;
        }

        private static long WorkshopIdOf(string path)
        {
            string folder = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
            long id;
            return long.TryParse(folder, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) ? id : 0L;
        }

        private static Grid ReadGrid(XElement grid,
            Dictionary<string, GameBlocks.Definition> definitions, Ship ship)
        {
            XElement blocks = grid.Element("CubeBlocks");
            if (blocks == null) return null;

            bool large = ((string)grid.Element("GridSizeEnum") ?? "Large") == "Large";

            Grid part = new Grid
            {
                Name = (string)grid.Element("DisplayName") ?? "(unnamed)",
                Large = large,
                Builder = large ? GridBuilder.Large() : GridBuilder.Small(),
            };

            foreach (XElement block in blocks.Elements())
            {
                string subtype = (string)block.Element("SubtypeName");

                // An empty SubtypeName is how the game spells the base variant of a type — armour
                // blocks are the common case — so the type carries the identity instead.
                if (string.IsNullOrEmpty(subtype)) subtype = BaseSubtypeOf(block, large);

                GameBlocks.Definition definition;

                // A blueprint's grid size and a definition's must agree, or a small-grid ship would
                // be built out of large-grid blocks that happen to share a subtype name.
                if (subtype == null || !definitions.TryGetValue(subtype, out definition)
                    || definition.Large != large)
                {
                    ship.UnknownBlocks++;
                    if (ship.UnknownSubtypes.Count < 4 && subtype != null
                        && !ship.UnknownSubtypes.Contains(subtype))
                    {
                        ship.UnknownSubtypes.Add(subtype);
                    }
                    continue;
                }

                Vector3I cell = ParseCell(block.Element("Min"));
                part.Builder.Place(Model(definition), cell, Orientation(block));
                part.Blocks++;

                // Entity ids, and the head each mechanical base holds. The blueprint records its
                // joints exactly, so they are recoverable without guessing from geometry.
                long entityId = ParseLong(block.Element("EntityId"));
                if (entityId != 0L && !part.BlocksById.ContainsKey(entityId))
                {
                    part.BlocksById[entityId] = cell;
                }

                long topId = ParseLong(block.Element("TopBlockId"));
                if (topId != 0L) part.Mechanical.Add(new KeyValuePair<Vector3I, long>(cell, topId));
            }

            return part;
        }

        private static long ParseLong(XElement element)
        {
            long value;
            return element != null && long.TryParse((string)element,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0L;
        }

        /// <summary>
        /// The subtype an armour block carries when the blueprint leaves <c>SubtypeName</c> empty.
        ///
        /// Every such block is a cube of the grid's own size, so the fallback only has to be a
        /// block that exists and weighs the right thing. Guessing wrong here would silently make a
        /// hull out of something else, so it is deliberately narrow: the plain armour cube, and
        /// nothing clever.
        /// </summary>
        private static string BaseSubtypeOf(XElement block, bool large)
        {
            return large ? "LargeBlockArmorBlock" : "SmallBlockArmorBlock";
        }

        private static readonly Dictionary<string, BlockModel> Models =
            new Dictionary<string, BlockModel>(StringComparer.Ordinal);

        /// <summary>Guards <see cref="Models"/>, which every worker reads while parsing.</summary>
        private static readonly object ModelLock = new object();

        /// <summary>
        /// The model for a definition, built once and shared. A corpus places millions of blocks
        /// across a few thousand distinct types, so this is the difference between a pass that runs
        /// and one that does not.
        /// </summary>
        public static BlockModel Model(GameBlocks.Definition definition)
        {
            BlockModel model;
            lock (ModelLock)
            {
                if (Models.TryGetValue(definition.SubtypeId, out model)) return model;
            }

            model = BlockModel.Solid(definition.SubtypeId, definition.Size, definition.Mass,
                BlockThermalDerivation.Derive(definition.Components, definition.TypeId));

            // A definition that declares no mount points has them generated from its model, which
            // is geometry this harness cannot read. Treating that silence as "mounts nowhere" is
            // what made a battery a sealed box; the honest fallback is the one BlockModel.Solid
            // already applies, which is that every face mounts.
            bool everyFace = !definition.HasDeclaredMounts;
            if (definition.HasDeclaredMounts)
            {
                everyFace = true;
                for (int face = 0; face < Face.Count; face++)
                {
                    if (!definition.MountFaces[face]) everyFace = false;
                }
            }

            // Sealing follows the definition where it states one, and the mount points where it
            // does not. The game decides the unstated case per face from a pressurisation table
            // this harness does not read; mounting is the closest thing it has, and it is the same
            // approximation ShippedBlocks makes for the mod's own blocks.
            bool seals = definition.Airtight ?? true;

            if (!everyFace || !seals)
            {
                foreach (Vector3I cell in model.LocalCells())
                {
                    int state = seals ? CellSurface.SelfAirtightMask : 0;
                    for (int face = 0; face < Face.Count; face++)
                    {
                        state = CellSurface.WithSelfMount(state, face, definition.MountFaces[face]);
                    }
                    model.SetLocalSurface(cell, state);
                }
            }

            lock (ModelLock)
            {
                BlockModel existing;
                if (Models.TryGetValue(definition.SubtypeId, out existing)) return existing;

                Models[definition.SubtypeId] = model;
            }
            return model;
        }

        private static Vector3I ParseCell(XElement element)
        {
            if (element == null) return Vector3I.Zero;

            return new Vector3I(
                ParseInt(element.Attribute("x")),
                ParseInt(element.Attribute("y")),
                ParseInt(element.Attribute("z")));
        }

        private static int ParseInt(XAttribute attribute)
        {
            int value;
            return attribute != null && int.TryParse((string)attribute,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        /// <summary>
        /// A block's orientation, as the forward and up directions the blueprint records.
        ///
        /// A blueprint stores these as <c>Forward</c> and <c>Up</c> attributes on a
        /// <c>BlockOrientation</c> element, spelled with the same names as
        /// <c>Base6Directions.Direction</c>. A block that omits the element is identity, which is
        /// what the game assumes for it too.
        /// </summary>
        private static BlockOrientation Orientation(XElement block)
        {
            XElement element = block.Element("BlockOrientation");
            if (element == null) return BlockOrientation.Identity;

            return new BlockOrientation(
                DirectionOf((string)element.Attribute("Forward"), Base6Directions.Direction.Forward),
                DirectionOf((string)element.Attribute("Up"), Base6Directions.Direction.Up));
        }

        private static Base6Directions.Direction DirectionOf(string name, Base6Directions.Direction fallback)
        {
            switch (name)
            {
                case "Forward": return Base6Directions.Direction.Forward;
                case "Backward": return Base6Directions.Direction.Backward;
                case "Left": return Base6Directions.Direction.Left;
                case "Right": return Base6Directions.Direction.Right;
                case "Up": return Base6Directions.Direction.Up;
                case "Down": return Base6Directions.Direction.Down;
                default: return fallback;
            }
        }
    }
}
