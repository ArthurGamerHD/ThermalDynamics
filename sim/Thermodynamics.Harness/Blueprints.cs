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
        /// <summary>One ship out of one blueprint file. A blueprint may hold several.</summary>
        public class Ship
        {
            /// <summary>The blueprint's own name, and the grid's within it.</summary>
            public string Blueprint;
            public string Name;

            /// <summary>Where it came from, so a surprising result can be looked at by hand.</summary>
            public string Path;

            /// <summary>Workshop id when the file came from a subscribed item, else 0.</summary>
            public long WorkshopId;

            public bool Large;

            /// <summary>Which grid of its blueprint this is, so it can be read again.</summary>
            public int GridIndex;

            /// <summary>Blocks placed, i.e. those whose subtype resolved to a definition.</summary>
            public int Blocks;

            /// <summary>
            /// Blocks skipped because no vanilla definition carries that subtype.
            ///
            /// The count matters more than the names: a ship with any of these is *modded*, and a
            /// modded ship is not a measurement of vanilla balance because the blocks doing the
            /// heating are ones this model has never seen. <see cref="IsVanilla"/> is the filter.
            /// </summary>
            public int UnknownBlocks;

            /// <summary>Up to a few names of what was missing, for a report to be specific.</summary>
            public List<string> UnknownSubtypes = new List<string>();

            public GridModel Grid;

            /// <summary>
            /// The builder the grid was placed through, kept because a simulation is built from the
            /// list of placed blocks rather than from the grid alone.
            /// </summary>
            public GridBuilder Builder;

            /// <summary>This ship as a simulation, ready to step.</summary>
            public ThermalSimulation Build(ThermalSettings settings = null, float kelvin = 293.15f)
            {
                return Builder.BuildSimulation(settings ?? new ThermalSettings(), kelvin);
            }

            /// <summary>
            /// The same ship, read again from its file, with grid state of its own.
            ///
            /// **This is what lets a run be the unit of parallelism rather than a ship.** Every
            /// simulation built from one <c>Ship</c> shares that ship's <c>BlockInstance</c>
            /// objects, and the load is written onto them, so two scenarios on one ship at once
            /// overwrite each other. Reading the blueprint again is the cheap way out: block
            /// *models* are cached and shared, so only the per-block instances are rebuilt, and
            /// that costs a fraction of the settling run it enables.
            ///
            /// Without it a panel of six ships uses six cores of however many the machine has.
            /// </summary>
            public Ship Reload()
            {
                if (Path == null) return this;

                List<Ship> ships = Read(Path);
                return GridIndex >= 0 && GridIndex < ships.Count ? ships[GridIndex] : this;
            }

            /// <summary>
            /// True when every block on the ship resolved. Deliberately strict: one unknown block
            /// is enough to disqualify a ship, because there is no way to know whether the block
            /// that did not resolve was a decorative panel or the reactor.
            /// </summary>
            public bool IsVanilla
            {
                get { return UnknownBlocks == 0 && Blocks > 0; }
            }

            public override string ToString()
            {
                return Name + " (" + Blocks + " blocks)";
            }
        }

        /// <summary>
        /// **Where the corpus lives: `<repo>/corpus`.**
        ///
        /// One explicit, absolute location rather than a path relative to whatever directory a
        /// command happened to be run from. Every tool here writes to it and reads from it, and
        /// every report prints it, so there is never a question of which ships a figure came from.
        /// It is gitignored — ten thousand blueprints is gigabytes and none of it is source.
        /// </summary>
        public static string CorpusPath()
        {
            return Path.Combine(ShippedBlocks.RepoRoot(), "corpus");
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
        /// Reads one blueprint file into its ships. Returns an empty list rather than throwing:
        /// a corpus of ten thousand files will contain some that no parser should die on.
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
            string blueprintName = null;

            foreach (XElement definition in document.Descendants("ShipBlueprint"))
            {
                XElement id = definition.Element("Id");
                if (id != null) blueprintName = (string)id.Attribute("Subtype") ?? (string)id.Element("SubtypeId");
                break;
            }

            int index = 0;
            foreach (XElement grid in document.Descendants("CubeGrid"))
            {
                Ship ship = ReadGrid(grid, definitions);
                if (ship == null) { index++; continue; }

                ship.GridIndex = index++;
                ship.Path = path;
                ship.Blueprint = blueprintName ?? Path.GetFileName(Path.GetDirectoryName(path));
                ship.WorkshopId = WorkshopIdOf(path);

                ships.Add(ship);
            }

            return ships;
        }

        private static long WorkshopIdOf(string path)
        {
            string folder = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
            long id;
            return long.TryParse(folder, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) ? id : 0L;
        }

        private static Ship ReadGrid(XElement grid, Dictionary<string, GameBlocks.Definition> definitions)
        {
            XElement blocks = grid.Element("CubeBlocks");
            if (blocks == null) return null;

            bool large = ((string)grid.Element("GridSizeEnum") ?? "Large") == "Large";

            Ship ship = new Ship
            {
                Name = (string)grid.Element("DisplayName") ?? "(unnamed)",
                Large = large,
            };

            GridBuilder builder = large ? GridBuilder.Large() : GridBuilder.Small();

            foreach (XElement block in blocks.Elements())
            {
                string subtype = (string)block.Element("SubtypeName");

                // An empty SubtypeName is how the game spells the base variant of a type — armour
                // blocks are the common case — so the type carries the identity instead.
                if (string.IsNullOrEmpty(subtype)) subtype = BaseSubtypeOf(block, large);

                GameBlocks.Definition definition;
                if (subtype == null || !definitions.TryGetValue(subtype, out definition))
                {
                    ship.UnknownBlocks++;
                    if (ship.UnknownSubtypes.Count < 4 && subtype != null
                        && !ship.UnknownSubtypes.Contains(subtype))
                    {
                        ship.UnknownSubtypes.Add(subtype);
                    }
                    continue;
                }

                // A blueprint's grid size and a definition's must agree, or a small-grid ship would
                // be built out of large-grid blocks that happen to share a subtype name.
                if (definition.Large != large)
                {
                    ship.UnknownBlocks++;
                    continue;
                }

                builder.Place(Model(definition), ParseCell(block.Element("Min")), Orientation(block));
                ship.Blocks++;
            }

            ship.Grid = builder.Grid;
            ship.Builder = builder;
            return ship;
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

            bool everyFace = true;
            for (int face = 0; face < Face.Count; face++)
            {
                if (!definition.MountFaces[face]) everyFace = false;
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
