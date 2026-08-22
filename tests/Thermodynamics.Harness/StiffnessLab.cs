using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **Is the hull every benchmark is built on a ship anybody has built?**
    ///
    /// <para>
    /// A step takes as many substeps as its stiffest block needs, so a grid's cost is set by its
    /// lightest fitting rather than by its size, and every performance figure this repository
    /// publishes is taken on <see cref="Census"/>'s synthetic hull. That hull is held to
    /// <c>Census.Field</c> — 21.35 and 31.25 substeps, from **two ships in two live sessions**.
    /// Two is not a population, and a figure a running game reported is the one kind of evidence
    /// that cannot be re-examined: the ships are gone, the world is gone, and what else was true of
    /// them is unrecorded.
    /// </para>
    ///
    /// <para>
    /// This measures the same quantity over real workshop blueprints in the lab, where every input
    /// is visible and the run can be repeated. It steps nothing — stiffness is a property of a
    /// built grid and the world it is asked about — so a thousand real ships cost seconds.
    /// </para>
    ///
    /// <para>
    /// <b>Everything is reported against one stated step length.</b> Demand is proportional to it:
    /// the same ship asks twice as much of a quarter-second step as of an eighth-second one, and
    /// the field figures are quarter-second figures while the screening pass reports whatever the
    /// settings it was handed imply. Two numbers on this page that are not on the same basis are
    /// worse than one number, so the basis is a column and not a footnote.
    /// </para>
    /// </summary>
    public static class StiffnessLab
    {
        /// <summary>
        /// The step length everything here is quoted at: a quarter of a second, which is
        /// <c>Frequency 4</c>.
        ///
        /// Chosen because it is what <c>Census.Field</c>'s two observations were taken at, and the
        /// whole point of this lab is to put them beside the population. The shipped default is
        /// <c>Frequency 8</c> and asks exactly half as much.
        /// </summary>
        public const int FieldFrequency = 4;

        /// <summary>One ship, in both worlds, on one step length.</summary>
        public class Row
        {
            public string Ship;
            public bool Large;
            public int Blocks;

            /// <summary>Substeps the stiffest block demands of a step, in vacuum.</summary>
            public float Vacuum;

            /// <summary>The same, at sea level in still air.</summary>
            public float Air;

            /// <summary>The subtype that set <see cref="Air"/> — what there is to tune.</summary>
            public string StiffestInAir;

            /// <summary>Nodes on the ship, and how many of them each cap would hold back.</summary>
            public int Nodes;
            public int[] FlooredAtCap;
        }

        /// <summary>
        /// The per-block caps to project, and the four a field dump reported so the two can be laid
        /// side by side. <c>MaxSubstepsPerBlock</c> is chosen from this curve.
        /// </summary>
        public static readonly int[] Caps = { 8, 4, 2, 1 };

        private static ThermalSettings Settings()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.Frequency = FieldFrequency;
            return settings.Derive();
        }

        /// <summary>
        /// Measures one built simulation. Separate from the corpus walk so the census hull can be
        /// put through exactly the same arithmetic as a workshop ship — a comparison where the two
        /// sides are measured by different code is not a comparison.
        /// </summary>
        public static void MeasureSolver(ThermalSolver solver, ref EnvironmentState air,
            ref float vacuum, ref float inAir, ref string stiffest, int[] flooredAtCap, ref int nodes)
        {
            for (int i = 0; i < solver.Nodes.Count; i++)
            {
                nodes++;

                float dry = solver.NodeSubstepDemand(i);
                if (dry > vacuum) vacuum = dry;

                float wet = solver.NodeSubstepDemand(i, ref air);
                if (wet > inAir)
                {
                    inAir = wet;
                    stiffest = solver.Nodes[i].Block.Name;
                }

                // A per-block cap holds a node back when the node asks for more substeps than the
                // cap grants, so the share of blocks a cap reaches is a count over this demand —
                // and it is the curve MaxSubstepsPerBlock is chosen from.
                if (flooredAtCap == null) continue;
                for (int c = 0; c < Caps.Length; c++)
                {
                    if (wet > Caps[c]) flooredAtCap[c]++;
                }
            }
        }

        private static EnvironmentState SeaLevelAir(ThermalSettings settings)
        {
            return EnvironmentSolver.Solve(
                settings, PlanetThermalProperties.Default(), Worlds.PlanetSurface(1f, 0.5f));
        }

        /// <summary>The census hull the benchmarks are built on, measured the same way.</summary>
        public static Row Census(int blocks)
        {
            ThermalSettings settings = Settings();
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", blocks));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.RebuildAll();

            EnvironmentState air = SeaLevelAir(settings);
            float vacuum = 0f, inAir = 0f;
            string stiffest = "";
            int[] floored = new int[Caps.Length];
            int nodes = 0;
            MeasureSolver(simulation.Solver, ref air, ref vacuum, ref inAir, ref stiffest, floored, ref nodes);

            return new Row
            {
                Ship = "(census hull, " + blocks.ToString("n0") + " blocks)",
                Large = true,
                Blocks = simulation.Solver.Nodes.Count,
                Vacuum = vacuum,
                Air = inAir,
                StiffestInAir = stiffest,
                Nodes = nodes,
                FlooredAtCap = floored,
            };
        }

        /// <summary>One workshop blueprint, every grid in it included.</summary>
        public static Row Measure(Blueprints.Ship ship)
        {
            ThermalSettings settings = Settings();
            ShipAssembly assembly = ship.Build(settings);

            EnvironmentState air = SeaLevelAir(settings);
            float vacuum = 0f, inAir = 0f;
            string stiffest = "";
            int[] floored = new int[Caps.Length];
            int nodes = 0;

            for (int g = 0; g < assembly.Simulations.Count; g++)
            {
                MeasureSolver(assembly.Simulations[g].Solver, ref air, ref vacuum, ref inAir,
                    ref stiffest, floored, ref nodes);
            }

            return new Row
            {
                Ship = ship.Name,
                Large = ship.Large,
                Blocks = ship.Blocks,
                Vacuum = vacuum,
                Air = inAir,
                StiffestInAir = stiffest,
                Nodes = nodes,
                FlooredAtCap = floored,
            };
        }

        public static List<Row> Run(IList<Blueprints.Ship> ships, LabMode mode = LabMode.Parallel)
        {
            GameBlocks.Warm();
            return LabRun.Map(ships, Measure, mode);
        }

        /// <summary>The share of a sorted list at or below <paramref name="value"/>, 0..1.</summary>
        private static double ShareBelow(List<float> sorted, float value)
        {
            int below = 0;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i] <= value) below++;
            }
            return sorted.Count == 0 ? 0d : (double)below / sorted.Count;
        }

        private static float At(List<float> sorted, double q)
        {
            if (sorted.Count == 0) return 0f;
            int index = (int)Math.Round(q * (sorted.Count - 1));
            if (index < 0) index = 0;
            if (index >= sorted.Count) index = sorted.Count - 1;
            return sorted[index];
        }

        public static string Report(string path, LabMode mode = LabMode.Parallel)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("STIFFNESS AGAINST REAL SHIPS  (nothing stepped)");
            sb.AppendLine();
            sb.Append("  substeps demanded of a ")
                .Append((1f / FieldFrequency).ToString("n2"))
                .Append(" s step, which is Frequency ").Append(FieldFrequency)
                .AppendLine(" — the basis Census.Field was measured on.");
            sb.AppendLine("  The shipped default is Frequency 8 and asks half of every figure here.");
            sb.AppendLine();

            string root = path ?? Blueprints.DefaultPath();
            if (root == null)
            {
                sb.AppendLine("  No blueprints anywhere. The corpus lives at:");
                sb.Append("    ").AppendLine(Blueprints.CorpusPath());
                return sb.ToString();
            }

            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            Walk walk = new Walk();
            List<Row> rows = Stream(root, walk, mode);
            clock.Stop();
            LastRows = rows;

            if (rows.Count == 0)
            {
                sb.Append("  No usable ships under ").AppendLine(root);
                return sb.ToString();
            }

            List<float> vacuum = new List<float>();
            List<float> air = new List<float>();
            Dictionary<string, List<float>> byBlock = new Dictionary<string, List<float>>();

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null) continue;
                vacuum.Add(rows[i].Vacuum);
                air.Add(rows[i].Air);

                string name = rows[i].StiffestInAir ?? "?";
                if (!byBlock.ContainsKey(name)) byBlock[name] = new List<float>();
                byBlock[name].Add(rows[i].Air);
            }

            vacuum.Sort();
            air.Sort();

            sb.Append("  ").Append(rows.Count.ToString("n0")).Append(" ships measured of ")
                .Append(walk.Files.ToString("n0")).Append(" blueprints under ").AppendLine(root);
            sb.Append("  in ").Append(clock.Elapsed.TotalSeconds.ToString("n1"))
                .Append(" s, ").AppendLine(LabRun.Describe(mode));

            // **Nothing is dropped silently.** A population figure quoted over an unstated subset
            // is the failure this whole lab exists to correct, so every reason a blueprint did not
            // reach the table is counted and named.
            sb.Append("  not measured: ").Append(walk.Modded.ToString("n0")).Append(" modded, ")
                .Append(walk.TooSmall.ToString("n0")).Append(" under ")
                .Append(CorpusLab.MinimumBlocks).Append(" blocks, ")
                .Append(walk.TooLarge.ToString("n0")).Append(" over ")
                .Append(MaxMegabytes()).Append(" MB of XML, ")
                .Append(walk.Failed.ToString("n0")).AppendLine(" unreadable");
            sb.AppendLine();

            sb.AppendLine("  world       min     p10     p50     p90     p95     p99     max");
            Band(sb, "vacuum", vacuum);
            Band(sb, "air", air);
            sb.AppendLine();

            // The instrument, put in the population it claims to describe.
            sb.AppendLine("  the hull every benchmark is built on, measured the same way");
            sb.AppendLine("  hull                          vacuum     air   percentile of the corpus, in air");

            int[] sizes = { 2000, 4000, 32000 };
            for (int i = 0; i < sizes.Length; i++)
            {
                Row hull = Census(sizes[i]);
                sb.Append("  ").Append(Trim(hull.Ship, 28).PadRight(30));
                sb.Append(hull.Vacuum.ToString("n2").PadLeft(7));
                sb.Append(hull.Air.ToString("n2").PadLeft(8));
                sb.Append((100d * ShareBelow(air, hull.Air)).ToString("n1").PadLeft(9)).Append(" %");
                sb.Append("   stiffest: ").AppendLine(Trim(hull.StiffestInAir, 30));
            }

            sb.AppendLine();
            sb.AppendLine("  and the two ships a live session reported, on the same basis");
            sb.Append("    STR Hound      ").Append(Harness.Census.Field.LeastDemand.ToString("n2").PadLeft(7))
                .Append("   at the ")
                .Append((100d * ShareBelow(air, Harness.Census.Field.LeastDemand)).ToString("n1"))
                .AppendLine(" percentile of the corpus in air");
            sb.Append("    UNSC Infinity  ").Append(Harness.Census.Field.MostDemand.ToString("n2").PadLeft(7))
                .Append("   at the ")
                .Append((100d * ShareBelow(air, Harness.Census.Field.MostDemand)).ToString("n1"))
                .AppendLine(" percentile of the corpus in air");

            sb.AppendLine();
            sb.AppendLine("  what a per-block cap would hold back, over every block of every ship");
            sb.AppendLine("    cap    corpus   field dump   (share of blocks floored, in air)");

            long totalNodes = 0;
            long[] flooredTotal = new long[Caps.Length];
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null || rows[i].FlooredAtCap == null) continue;
                totalNodes += rows[i].Nodes;
                for (int c = 0; c < Caps.Length; c++) flooredTotal[c] += rows[i].FlooredAtCap[c];
            }

            float[] field =
            {
                Harness.Census.Field.RaisedAtCap8, Harness.Census.Field.RaisedAtCap4,
                Harness.Census.Field.RaisedAtCap2, Harness.Census.Field.RaisedAtCap1,
            };

            for (int c = 0; c < Caps.Length; c++)
            {
                double share = totalNodes == 0 ? 0d : (double)flooredTotal[c] / totalNodes;
                sb.Append("    ").Append(Caps[c].ToString().PadLeft(3));
                sb.Append((100d * share).ToString("n2").PadLeft(10)).Append(" %");
                sb.Append((100d * field[c]).ToString("n2").PadLeft(11)).Append(" %");
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("  what sets a real ship's substep count in air");
            sb.AppendLine("    block                              ships   share   median   worst");

            List<KeyValuePair<string, List<float>>> ranked =
                new List<KeyValuePair<string, List<float>>>(byBlock);
            ranked.Sort(delegate (KeyValuePair<string, List<float>> a, KeyValuePair<string, List<float>> b)
            {
                return b.Value.Count.CompareTo(a.Value.Count);
            });

            for (int i = 0; i < ranked.Count && i < 12; i++)
            {
                List<float> values = ranked[i].Value;
                values.Sort();

                sb.Append("    ").Append(Trim(ranked[i].Key, 32).PadRight(34));
                sb.Append(values.Count.ToString().PadLeft(6));
                sb.Append((100d * values.Count / Math.Max(1, air.Count)).ToString("n1").PadLeft(7)).Append(" %");
                sb.Append(At(values, 0.5).ToString("n1").PadLeft(8));
                sb.Append(values[values.Count - 1].ToString("n1").PadLeft(8));
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>What a walk did not measure, and why. Counted, never dropped in silence.</summary>
        public class Walk
        {
            public int Files;
            public int Modded;
            public int TooSmall;
            public int TooLarge;
            public int Failed;
        }

        /// <summary>
        /// The largest blueprint this will open, in megabytes of XML.
        ///
        /// <para>
        /// A corpus holds a 1.9 GB blueprint and several over 400 MB, and a ship is held in memory
        /// as every grid and every block in it. Reading thirty-one of those at once is how an
        /// uncapped run on this corpus took a machine down. <c>THERMAL_CORPUS_MAX_MB</c> raises or
        /// lowers it — the same name <c>CorpusFixture</c> uses — and whatever it excludes is
        /// counted and reported rather than quietly missing.
        /// </para>
        /// </summary>
        public static int MaxMegabytes()
        {
            string configured = Environment.GetEnvironmentVariable("THERMAL_CORPUS_MAX_MB");
            int megabytes;
            if (!string.IsNullOrEmpty(configured)
                && int.TryParse(configured, out megabytes) && megabytes > 0) return megabytes;

            return 64;
        }

        /// <summary>
        /// Reads and measures the corpus in batches, keeping one row per ship and nothing else.
        ///
        /// <para>
        /// The screening pass parses every blueprint into a list of ships and then measures the
        /// list, so a fifty-gigabyte corpus is resident all at once. Nothing here needs that: a
        /// ship's stiffness is a number, and once it is taken the ship can go. Batching keeps the
        /// parallelism and bounds the memory at a batch, which is what lets this run over the whole
        /// corpus rather than over a sample of it.
        /// </para>
        /// </summary>
        public static List<Row> Stream(string root, Walk walk, LabMode mode = LabMode.Parallel)
        {
            GameBlocks.Warm();

            List<Row> rows = new List<Row>();
            List<string> files = Blueprints.Files(root);
            walk.Files = files.Count;

            long limit = (long)MaxMegabytes() * 1024L * 1024L;
            const int Batch = 64;

            for (int start = 0; start < files.Count; start += Batch)
            {
                List<Blueprints.Ship> batch = new List<Blueprints.Ship>();

                for (int i = start; i < files.Count && i < start + Batch; i++)
                {
                    long size;
                    try { size = new System.IO.FileInfo(files[i]).Length; }
                    catch (Exception) { walk.Failed++; continue; }

                    if (size > limit) { walk.TooLarge++; continue; }

                    List<Blueprints.Ship> read = Blueprints.Read(files[i]);
                    if (read.Count == 0) { walk.Failed++; continue; }

                    for (int s = 0; s < read.Count; s++)
                    {
                        if (!read[s].IsVanilla) { walk.Modded++; continue; }
                        if (read[s].Blocks < CorpusLab.MinimumBlocks) { walk.TooSmall++; continue; }
                        batch.Add(read[s]);
                    }
                }

                if (batch.Count == 0) continue;

                List<Row> measured = LabRun.Map(batch, Measure, mode);
                for (int i = 0; i < measured.Count; i++)
                {
                    if (measured[i] != null) rows.Add(measured[i]);
                    else walk.Failed++;
                }
            }

            return rows;
        }

        /// <summary>
        /// One row per ship, so the distribution can be examined rather than taken on the summary's
        /// word. The bimodality below is only visible in the rows.
        /// </summary>
        public static string Csv(IList<Row> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ship,large,blocks,vacuum,air,stiffest_in_air");

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                if (row == null) continue;

                sb.Append('"').Append((row.Ship ?? "").Replace("\"", "\"\"")).Append('"').Append(',');
                sb.Append(row.Large ? 1 : 0).Append(',');
                sb.Append(row.Blocks).Append(',');
                sb.Append(row.Vacuum.ToString("r", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.Air.ToString("r", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                sb.Append('"').Append((row.StiffestInAir ?? "").Replace("\"", "\"\"")).AppendLine("\"");
            }

            return sb.ToString();
        }

        /// <summary>The rows of the last <see cref="Report"/>, for a caller that wants the CSV.</summary>
        public static List<Row> LastRows { get; private set; }

        private static void Band(StringBuilder sb, string label, List<float> sorted)
        {
            sb.Append("  ").Append(label.PadRight(10));
            double[] quantiles = { 0d, 0.10d, 0.50d, 0.90d, 0.95d, 0.99d, 1d };
            for (int i = 0; i < quantiles.Length; i++)
            {
                sb.Append(At(sorted, quantiles[i]).ToString("n2").PadLeft(8));
            }
            sb.AppendLine();
        }

        private static string Trim(string value, int length)
        {
            if (value == null) return "";
            return value.Length <= length ? value : value.Substring(0, length - 1) + "…";
        }
    }
}
