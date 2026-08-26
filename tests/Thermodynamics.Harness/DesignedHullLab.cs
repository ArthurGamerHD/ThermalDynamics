using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **The one claim the retrofit lab cannot test: a loop reaches the outside and a bolt joint
    /// does not.**
    ///
    /// <para>
    /// `C40` bounded every path that moves heat *inside* a grid at about 9 % of the peak, because a
    /// hot block on a finished ship already sheds 1,457 W/K into the hull it is welded to and stands
    /// only 39 K above it. That closes the transport line for retrofits and leaves one thing
    /// untested, which happens to be the whole design thesis of this mod: **a source buried in a
    /// hull cannot be cooled by bolting, because a panel bolted to it is buried too and sees no
    /// sky.** A loop can carry that heat to the skin, where a panel does see sky. Nothing measured
    /// so far could see the difference, because every rig this repository owns has an exposed
    /// source — the bench sources sit in the open, so their panels radiate wherever they are put.
    /// </para>
    ///
    /// <para>
    /// **So the arms differ in where the heat is put, not in how fast it moves.** Same hull, same
    /// source, same watts, same number of radiators, same environment, same clock; the only
    /// difference is whether the radiators are inside the armour beside the source or outside it on
    /// the skin, and whether a ring joins the two (`P6`).
    /// </para>
    /// </summary>
    public static class DesignedHullLab
    {
        /// <summary>Odd, so there is a single centre cell to bury the source in.</summary>
        public const int Side = 7;

        /// <summary>Long enough for the hull to reach steady state at this size.</summary>
        private const float Seconds = 14400f;
        private const float Step = 1200f;

        public class Row
        {
            public string Fit;

            /// <summary>Where the buried source settled, K.</summary>
            public float SourceKelvin;

            /// <summary>Kelvin below the bare hull's result. Negative is worse.</summary>
            public float SavedKelvin;

            /// <summary>Radiators fitted, so an arm cannot win by carrying more of them.</summary>
            public int Radiators;

            /// <summary>How many of those have a face on the outside of the hull.</summary>
            public int RadiatorsOnTheSkin;

            /// <summary>The hottest block anywhere on the grid, which need not be the source.</summary>
            public float HottestKelvin;

            /// <summary>
            /// Why an arm measured less than it meant to, or empty when it measured what it meant
            /// to. **A silent zero here is the failure this whole lab exists to avoid**: the first
            /// run of it fitted no panels at all in either arm and reported the design thesis as
            /// false, because the shipped radiator is ten cells and both arms offered it one.
            /// </summary>
            public string Note = "";

            /// <summary>
            /// W/K of every sink face the ring has onto the source. **The pickup, and on a buried
            /// source it is the whole path**: an exposed block sheds through its own skin and a
            /// buried one has only this. Watts over this is the gradient the source must sit at,
            /// which is what decides whether a fit can work at all.
            /// </summary>
            public float SinkWattsPerKelvin;
        }

        private static ThermalSettings Settings()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.MaxElementVisitsPerStep = 0;
            return settings.Derive();
        }

        private static readonly Vector3I Centre = new Vector3I(Side / 2, Side / 2, Side / 2);

        /// <summary>
        /// The shipped large radiator is 1x5x2 - ten cells - so a pocket cut for it has to be that
        /// shape. Read from the definition rather than written down, because a hand-typed size is
        /// how the first version of this lab came to offer a ten-cell block a one-cell hole (`E5`).
        /// </summary>
        private static Vector3I PanelSize
        {
            get { return ShippedBlocks.Model("Gauge_LG_Radiator").Size; }
        }

        /// <summary>Every cell a panel placed at <paramref name="origin"/> would occupy.</summary>
        private static List<Vector3I> Footprint(Vector3I origin)
        {
            Vector3I size = PanelSize;
            List<Vector3I> cells = new List<Vector3I>();

            for (int x = 0; x < size.X; x++)
            {
                for (int y = 0; y < size.Y; y++)
                {
                    for (int z = 0; z < size.Z; z++)
                    {
                        cells.Add(origin + new Vector3I(x, y, z));
                    }
                }
            }

            return cells;
        }

        /// <summary>
        /// The ring: a rectangle in the y = centre plane that starts beside the source and runs out
        /// through the armour to beyond the hull, so its far side is in open space. Cells outside
        /// the cube are what makes this arm different from the others.
        /// </summary>
        private static List<Vector3I> Channel(int panels)
        {
            // Long enough that the cells outside the hull can carry the panels asked for. Each
            // panel needs a straight run of its own out there, and a rectangle contributes about
            // two outside cells per unit of extra length.
            int beyond = 4 + panels;
            return PipeFitter.RectangleXZ(
                new Vector3I(Centre.X - 1, Centre.Y, Centre.Z - 1), Side / 2 + beyond, 3);
        }

        /// <summary>Whether a cell is inside the solid cube of hull.</summary>
        private static bool InHull(Vector3I cell)
        {
            return cell.X >= 0 && cell.X < Side
                && cell.Y >= 0 && cell.Y < Side
                && cell.Z >= 0 && cell.Z < Side;
        }

        /// <summary>
        /// A block has sky if any of its six faces is not occupied. Counted rather than assumed,
        /// because *the panel is on the skin* is the entire claim and an arm that quietly buries
        /// its panels would report the claim as false.
        /// </summary>
        private static bool SeesSky(GridModel grid, Vector3I cell)
        {
            for (int f = 0; f < Face.Count; f++)
            {
                if (!grid.IsOccupied(cell + Face.Offsets[f])) return true;
            }
            return false;
        }

        /// <summary>Solid armour everywhere in the cube except the cells the arm needs.</summary>
        private static GridBuilder Hull(ICollection<Vector3I> reserved)
        {
            GridBuilder builder = GridBuilder.Large();
            BlockModel armour = Catalog.LightArmor();

            for (int x = 0; x < Side; x++)
            {
                for (int y = 0; y < Side; y++)
                {
                    for (int z = 0; z < Side; z++)
                    {
                        Vector3I cell = new Vector3I(x, y, z);
                        if (cell == Centre) continue;
                        if (reserved != null && reserved.Contains(cell)) continue;

                        builder.Place(armour, cell);
                    }
                }
            }

            return builder;
        }

        private static Row Measure(string fit, GridBuilder builder, BlockInstance source,
            List<BlockInstance> radiators, float bare, LoopThermalProperties properties = null)
        {
            ThermalSimulation simulation = builder.BuildSimulation(Settings(), 293.15f);

            if (properties != null)
            {
                simulation.LoopProperties = properties;
                simulation.RebuildAll();
            }

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("source", source);
            runner.Run(Seconds, Step);

            float sourceKelvin;
            runner.Final.Tracked.TryGetValue("source", out sourceKelvin);

            float hottest = 0f;
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Temperature > hottest) hottest = nodes[i].Temperature;
            }

            float sink = 0f;
            IList<CoolantLoop> loops = simulation.Solver.Loops;
            if (loops != null)
            {
                for (int l = 0; l < loops.Count; l++)
                {
                    for (int i = 0; i < loops[l].Links.Count; i++)
                    {
                        int index = loops[l].Links[i].NodeIndex;
                        if (index < 0 || index >= simulation.Solver.Nodes.Count) continue;
                        if (simulation.Solver.Nodes[index].Block != source) continue;

                        sink += loops[l].LinkConductance(i);
                    }
                }
            }

            int onSkin = 0;
            for (int i = 0; i < radiators.Count; i++)
            {
                if (SeesSky(builder.Grid, radiators[i].Position)) onSkin++;
            }

            return new Row
            {
                Fit = fit,
                SourceKelvin = sourceKelvin,
                SavedKelvin = bare > 0f ? bare - sourceKelvin : 0f,
                Radiators = radiators.Count,
                RadiatorsOnTheSkin = onSkin,
                HottestKelvin = hottest,
                SinkWattsPerKelvin = sink,
            };
        }

        /// <summary>The source, buried at the centre of a solid cube, and nothing else.</summary>
        private static Row Bare(float watts, out float settled)
        {
            GridBuilder builder = Hull(null);
            builder.Place(BalanceLab.Heater(), Centre);
            BlockInstance source = builder.Last;
            builder.Last.PowerConsumedWatts = watts;

            Row row = Measure("buried, bare", builder, source, new List<BlockInstance>(), 0f);
            settled = row.SourceKelvin;
            return row;
        }

        /// <summary>
        /// Radiators replacing the armour immediately around the source. **This is the retrofit
        /// answer and it is buried**: the panels are inside a solid hull, so whatever they can shed
        /// they have nowhere to shed it to.
        /// </summary>
        private static Row Bolted(float watts, int panels, float bare)
        {
            Vector3I size = PanelSize;

            // Pockets stacked along Z beside the source's +X face, each the panel's own shape, all
            // of them inside the cube. Centred on the source so the first one touches it.
            List<Vector3I> origins = new List<Vector3I>();
            List<Vector3I> reserved = new List<Vector3I>();

            for (int i = 0; i < panels; i++)
            {
                Vector3I origin = new Vector3I(
                    Centre.X + 1,
                    Centre.Y - (size.Y / 2),
                    Centre.Z - (size.Z / 2) + (i * size.Z));

                List<Vector3I> cells = Footprint(origin);

                bool fits = true;
                for (int c = 0; c < cells.Count && fits; c++) fits = InHull(cells[c]);
                if (!fits) continue;

                origins.Add(origin);
                reserved.AddRange(cells);
            }

            GridBuilder builder = Hull(reserved);
            builder.Place(BalanceLab.Heater(), Centre);
            BlockInstance source = builder.Last;
            builder.Last.PowerConsumedWatts = watts;

            BlockModel radiator = ShippedBlocks.Model("Gauge_LG_Radiator");
            List<BlockInstance> radiators = new List<BlockInstance>();
            string note = "";

            for (int i = 0; i < origins.Count; i++)
            {
                try
                {
                    builder.Place(radiator, origins[i]);
                    radiators.Add(builder.Last);
                }
                catch (Exception error)
                {
                    if (note.Length == 0) note = error.Message;
                }
            }

            if (radiators.Count < panels && note.Length == 0)
            {
                note = "only " + radiators.Count + " of " + panels + " pockets fit inside the hull";
            }

            Row row = Measure("bolted, buried", builder, source, radiators, bare);
            row.Note = note;
            return row;
        }

        /// <summary>
        /// A ring from the source out through the hull, with the panels on the outside end of it.
        /// **The only arm whose radiators see sky.**
        /// </summary>
        private static Row Plumbed(float watts, int panels, float bare, LoopThermalProperties properties = null,
            bool everyFace = false)
        {
            List<Vector3I> ring = Channel(panels);

            GridBuilder builder = Hull(ring);
            builder.Place(BalanceLab.Heater(), Centre);
            BlockInstance source = builder.Last;
            builder.Last.PowerConsumedWatts = watts;

            // **The sinks onto the source, and how many is the question.** A rectangle laid past a
            // one-cell source runs alongside three of its faces, not one; taking only the first is a
            // choice, and it turns out to be the choice that decides whether the fit can work at
            // all, because watts over the pickup is the gradient the source is forced to sit at.
            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            for (int i = 0; i < ring.Count; i++)
            {
                Vector3I toSource = Centre - ring[i];
                if (Face.IndexOf(toSource) < 0) continue;

                sinks[i] = toSource;
                if (!everyFace) break;
            }

            string note = sinks.Count == 0 ? "no ring cell touches the source" : "";

            BlockModel radiator = ShippedBlocks.Model("Gauge_LG_Radiator");
            Vector3I size = PanelSize;
            List<BlockInstance> radiators = new List<BlockInstance>();

            // Panels hang off the ring cells that are outside the hull - the whole point of the arm
            // - and each is placed before the ring is, so a panel that will not fit does not leave
            // a sink face pointing at nothing.
            for (int i = 0; i < ring.Count && radiators.Count < panels; i++)
            {
                if (sinks.ContainsKey(i)) continue;
                if (InHull(ring[i])) continue;
                if (!IsStraightRun(ring, i)) continue;

                for (int f = 0; f < Face.Count; f++)
                {
                    Vector3I direction = Face.Offsets[f];
                    Vector3I origin = ring[i] + direction;

                    List<Vector3I> footprint = Footprint(origin);

                    bool clear = true;
                    for (int c = 0; c < footprint.Count && clear; c++)
                    {
                        clear = !InHull(footprint[c])
                            && !ring.Contains(footprint[c])
                            && !builder.Grid.IsOccupied(footprint[c]);
                    }

                    if (!clear) continue;

                    try
                    {
                        builder.Place(radiator, origin);
                        radiators.Add(builder.Last);
                        sinks[i] = direction;
                    }
                    catch (Exception error)
                    {
                        if (note.Length == 0) note = error.Message;
                    }

                    break;
                }
            }

            if (radiators.Count < panels && note.Length == 0)
            {
                note = "only " + radiators.Count + " of " + panels + " panels found room on the ring";
            }

            try
            {
                PipeFitter.BuildRing(builder, ring, -1, sinks);
            }
            catch (Exception error)
            {
                return new Row
                {
                    Fit = "plumbed, to the skin",
                    SourceKelvin = bare,
                    Note = "ring refused: " + error.Message,
                };
            }

            Row row = Measure("plumbed, to the skin", builder, source, radiators, bare, properties);
            row.Note = note;
            return row;
        }

        /// <summary>Whether a ring cell is a straight run; only those carry a sink port cleanly.</summary>
        private static bool IsStraightRun(List<Vector3I> ring, int index)
        {
            Vector3I cell = ring[index];
            Vector3I previous = ring[(index - 1 + ring.Count) % ring.Count];
            Vector3I next = ring[(index + 1) % ring.Count];

            return (previous - cell) == -(next - cell);
        }

        /// <summary>
        /// The three arms, **with the two fitted ones carrying the same number of panels**.
        ///
        /// <para>
        /// The hull offers the two fits different amounts of room — pockets inside a 7-cube run out
        /// before straight ring cells outside it do — so asking for four gets one arm two panels and
        /// the other three, and a third of the difference between them is then a difference in how
        /// much radiator each is carrying. Both are re-run at whichever fitted fewer, so the
        /// comparison is about *where* the panels are and nothing else (`P6`). Enforced here rather
        /// than left to whoever runs the lab, because a fair comparison nobody has to remember is
        /// the only kind that stays fair.
        /// </para>
        /// </summary>
        public static List<Row> Run(float watts, int panels)
        {
            float bare;
            Row bareRow = Bare(watts, out bare);

            Row bolted = Bolted(watts, panels, bare);
            Row plumbed = Plumbed(watts, panels, bare);

            int fair = Math.Min(bolted.Radiators, plumbed.Radiators);

            if (fair > 0 && bolted.Radiators != plumbed.Radiators)
            {
                if (bolted.Radiators > fair) bolted = Bolted(watts, fair, bare);
                if (plumbed.Radiators > fair) plumbed = Plumbed(watts, fair, bare);

                string levelled = "levelled to " + fair + " panels, the most both fits had room for";
                bolted.Note = string.IsNullOrEmpty(bolted.Note) ? levelled : bolted.Note;
                plumbed.Note = string.IsNullOrEmpty(plumbed.Note) ? levelled : plumbed.Note;
            }

            List<Row> rows = new List<Row>();
            rows.Add(bareRow);
            rows.Add(bolted);
            rows.Add(plumbed);
            return rows;
        }

        /// <summary>
        /// **How much skin-plumbed radiator a given heat load needs to stay under a rating.**
        ///
        /// <para>
        /// `C34` sorted the population by *self index* — heat made over what a block's own skin
        /// sheds — and found the runaways are blocks above about three. What that number cannot say
        /// is whether anything can be done about them, and the triage table's other column can: a
        /// block's *index*, against every face radiating and a path to armour at ambient, is below
        /// one for all but one of 323 heat-making blocks. **So the runaways are coolable in
        /// principle and the question is what it costs**, which is this sweep. A jump drive wastes
        /// 6.4 MW against a 689 K rating; this says how many panels that is.
        /// </para>
        ///
        /// <para>
        /// The source is a one-cell heater rather than the block itself, so the answer is about the
        /// cooling path and not about the block's own skin (`P6`) — and it is conservative, since a
        /// real drive is 27 cells of radiating surface this rig does not give it (`P2`).
        /// </para>
        /// </summary>
        public static string Sweep(float watts, float criticalKelvin, int maxPanels)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("SKIN LADDER  (how much radiator a buried load needs, plumbed to the hull)");
            sb.AppendLine();
            sb.AppendLine("  " + (watts / 1000000f).ToString("n2") + " MW buried in a " + Side
                + "-cube, shadow, against a " + criticalKelvin.ToString("n0") + " K rating");
            sb.AppendLine();
            sb.AppendLine("  loop                 panels   on the skin   sink W/K   needs K   source K   under");

            float bare;
            Bare(watts, out bare);
            sb.AppendLine(string.Format("  {0,-19}  {1,6}   {2,11}   {3,8}   {4,7}   {5,8}   {6,5}",
                "none", 0, 0, "-", "-", bare.ToString("n1"), bare < criticalKelvin ? "yes" : "no"));

            for (int arm = 0; arm < 4; arm++)
            {
                bool candidate = (arm & 1) != 0;
                bool everyFace = (arm & 2) != 0;

                LoopThermalProperties properties = candidate ? LoopCandidate.For(2.5f) : null;
                string label = (candidate ? "candidate" : "shipped")
                    + (everyFace ? ", 3 faces" : ", 1 face");

                for (int panels = 1; panels <= maxPanels; panels++)
                {
                    Row row = Plumbed(watts, panels, bare, properties, everyFace);

                    // **Watts over the pickup is the gradient the source is forced to sit at.**
                    // A fit whose `needs K` exceeds the rating cannot work however much radiator is
                    // hung off the far end, and printing it is the difference between a null result
                    // and an explained one (`O5`).
                    string needs = row.SinkWattsPerKelvin > 0f
                        ? (watts / row.SinkWattsPerKelvin).ToString("n0")
                        : "-";

                    sb.AppendLine(string.Format("  {0,-19}  {1,6}   {2,11}   {3,8}   {4,7}   {5,8}   {6,5}",
                        label, panels, row.RadiatorsOnTheSkin,
                        row.SinkWattsPerKelvin.ToString("n0"), needs,
                        row.SourceKelvin.ToString("n1"),
                        row.SourceKelvin < criticalKelvin ? "yes" : "no"));
                }
            }

            sb.AppendLine();
            sb.AppendLine("  `sink W/K` is the pickup: every sink face the ring has onto the source.");
            sb.AppendLine("  `needs K` is the watts over it - the gradient the source is forced to");
            sb.AppendLine("  sit at whatever is hung off the other end. When that exceeds the rating,");
            sb.AppendLine("  adding radiator cannot help, and the panel column shows it saturating.");
            sb.AppendLine("  `candidate` is LoopCandidate: the C38 package, which does nothing for a");
            sb.AppendLine("  retrofit and is measured here because a buried source has no other path.");

            return sb.ToString();
        }

        public static string Report(float watts = 200000f, int panels = 4)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("DESIGNED HULL  (a source buried in a " + Side + "-cube of light armour)");
            sb.AppendLine();
            sb.AppendLine("  " + (watts / 1000f).ToString("n0") + " kW into the centre cell, shadow, "
                + (Seconds / 3600f).ToString("n0") + " h to steady state");
            sb.AppendLine("  the arms differ in WHERE the heat is put, not in how fast it moves");
            sb.AppendLine();

            List<Row> rows = Run(watts, panels);
            LastRows = rows;

            sb.AppendLine("  fit                    source K   saved K   hottest K   panels   on the skin");
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                sb.AppendLine(string.Format("  {0,-20} {1,10} {2,9} {3,11} {4,8} {5,13}",
                    row.Fit, row.SourceKelvin.ToString("n1"), row.SavedKelvin.ToString("n1"),
                    row.HottestKelvin.ToString("n1"), row.Radiators, row.RadiatorsOnTheSkin));

                if (!string.IsNullOrEmpty(row.Note))
                {
                    sb.AppendLine("      note: " + row.Note);
                }
            }

            sb.AppendLine();
            sb.AppendLine("  `on the skin` is the whole comparison: a panel with no unoccupied face");
            sb.AppendLine("  radiates to nothing, however good a radiator it is. If the two arms carry");
            sb.AppendLine("  the same panels and only one arm's panels see sky, the difference is the");
            sb.AppendLine("  loop reaching somewhere a bolt joint cannot - which is the mod's design");
            sb.AppendLine("  thesis, and was untested until this lab (backlog.md C41).");

            return sb.ToString();
        }

        public static List<Row> LastRows { get; private set; }
    }
}
