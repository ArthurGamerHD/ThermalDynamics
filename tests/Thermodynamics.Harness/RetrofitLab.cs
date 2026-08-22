using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **Does fitting cooling to a ship somebody actually built do anything?**
    ///
    /// <para>
    /// This is criterion G3 of the balance lab, and it has been open since the lab was designed
    /// because nothing could answer it: no corpus ship carries a radiator — the reader rejects a
    /// blueprint with a modded block in it, and this mod's blocks are modded blocks — so the
    /// population says only what an *uncooled* ship does. <see cref="CoolingLadder"/> answered half
    /// of it on a synthetic rig, one reactor with blocks stacked against it. A rig is not a ship.
    /// </para>
    ///
    /// <para>
    /// So the cooling is fitted here instead: a real workshop hull is parsed, run under load to
    /// find where its heat actually is, and then the mod's own blocks are placed against that spot
    /// in cells the hull leaves free — which is the constraint that makes this a measurement of
    /// ships rather than of blocks. A reactor buried behind six other blocks cannot be cooled
    /// without demolishing something, and how often that is true is one of the answers.
    /// </para>
    ///
    /// <para>
    /// Two fits, because the model prices them six times apart and only one has ever been
    /// measured: <b>bolted</b>, radiators against the hot block, and <b>plumbed</b>, a coolant ring
    /// with a pump and a sink face against it. Both use the shipped definitions read from
    /// <c>Cubes.xml</c>, not stand-ins.
    /// </para>
    /// </summary>
    public static class RetrofitLab
    {
        /// <summary>Simulated seconds each run is given. Long enough for a hull to settle.</summary>
        public const float Seconds = 1800f;

        /// <summary>
        /// What 427 real workshop ships said, measured 2026-08-22. Reproduce with
        /// <c>dotnet run --project Thermodynamics.Sim -- retrofit --ships 500</c>, about seven
        /// minutes.
        ///
        /// <para>
        /// <b>The two fits fail in opposite ways, and that is the answer to G3.</b> Bolting fits
        /// almost anywhere and does not work. Plumbing works and almost never fits. A player
        /// holding a finished ship has, in practice, neither.
        /// </para>
        /// </summary>
        public static class Corpus
        {
            /// <summary>Ships that got warm enough under load for cooling them to mean anything.</summary>
            public const int Warm = 332;

            // ---- bolted: radiators against the hot block --------------------------------------

            /// <summary>Of the warm ships, how many had free cells to bolt to. Nearly all of them.</summary>
            public const int BoltedFitted = 281;

            /// <summary>
            /// Per cent of peak taken off, at the median: **negative**. The median ship is very
            /// slightly worse for having radiators bolted to its hot block, and 85 of 281 are worse
            /// by more than a per cent against 60 better by more than a per cent.
            ///
            /// The mechanism is the one <c>CoolingLadder</c> found on a rig and <c>blocks.md</c>
            /// states: a block against a face is a face that was radiating to the sky and now
            /// radiates into a neighbour. On a block already saturated it is a large loss — the
            /// worst hull here went from 2,941 K to 4,319 K.
            /// </summary>
            public const float BoltedMedianPercent = -0.11f;
            public const int BoltedHelped = 60;
            public const int BoltedHurt = 85;

            // ---- plumbed: a ring, a pump and a sink face on the hot block ---------------------

            /// <summary>
            /// Of the warm ships, how many had room for a closed ring of free cells with one of
            /// them face-adjacent to the hot block. **Fifteen per cent.**
            ///
            /// This is the finding, and it is about ships rather than about blocks: a finished hull
            /// does not leave a loop of empty cells around the thing that gets hot. Requiring the
            /// sink face — which is the whole difference between plumbing and bolting, 1,000 W/K
            /// against 167 — more than halves what can be fitted, from 108 rings to 49.
            /// </summary>
            public const int PlumbedFitted = 49;

            /// <summary>
            /// Where it does fit, it works: better on 26 of 49 by more than a per cent, worse on 5,
            /// and better by more than five per cent on 13.
            /// </summary>
            public const float PlumbedMedianPercent = 1.35f;
            public const int PlumbedHelped = 26;
            public const int PlumbedHurt = 5;

            /// <summary>
            /// The ninetieth percentile, over hulls under 3,000 K — above that a peak is an
            /// artefact of the harness never destroying an overheating block rather than a
            /// temperature, so a per cent of it is a per cent of nothing.
            /// </summary>
            public const float PlumbedP90Percent = 8.94f;
        }

        /// <summary>Cooling blocks a fit will place at most, before it runs out of room anyway.</summary>
        public const int MaxBlocks = 8;

        /// <summary>What one ship did, as built and after each fit.</summary>
        public class Row
        {
            public string Ship;
            public int Blocks;
            public bool Large;

            /// <summary>Peak block temperature under load, as the ship was published.</summary>
            public float BareK;

            /// <summary>The block that reached it, which is what a fit is aimed at.</summary>
            public string HottestBlock;

            /// <summary>Radiators bolted against the hot block, and the peak that produced.</summary>
            public int BoltedPlaced;
            public float BoltedK;

            /// <summary>A ring with a pump and a sink face on the hot block, and its peak.</summary>
            public int PlumbedPipes;
            public float PlumbedK;

            public float BoltedSaved { get { return BareK - BoltedK; } }
            public float PlumbedSaved { get { return BareK - PlumbedK; } }
        }

        private static ThermalSettings Settings()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.MaxElementVisitsPerStep = 0;
            return settings.Derive();
        }

        /// <summary>
        /// Runs a ship under full electrical load in shadow and reports the hottest block it
        /// reached, and where.
        ///
        /// Shadow rather than sunlight so the only heat in the answer is the ship's own — a fit
        /// that merely shades a block from the sun is not cooling.
        /// </summary>
        private static float Load(ShipAssembly assembly, out ThermalNode hottest)
        {
            ShipLoad.Apply(assembly, ShipLoad.State.Full);

            AssemblyRunner runner = new AssemblyRunner(assembly);
            runner.Environment = t => Worlds.Shadow();
            runner.Run(Seconds);

            hottest = assembly.Hottest();
            return hottest == null ? 0f : hottest.Temperature;
        }

        /// <summary>
        /// Free cells touching a block, nearest first, in the grid that holds it.
        ///
        /// <para>
        /// A retrofit can only go where the builder left room. Searching outward from the hot block
        /// rather than anywhere on the hull is the whole point: cooling bolted to the far end of a
        /// ship is cooling bolted to nothing, and the model prices the joint rather than the block.
        /// </para>
        /// </summary>
        private static List<Vector3I> FreeCellsAround(GridModel grid, Vector3I centre, int reach)
        {
            List<Vector3I> free = new List<Vector3I>();

            for (int radius = 1; radius <= reach; radius++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    for (int y = -radius; y <= radius; y++)
                    {
                        for (int z = -radius; z <= radius; z++)
                        {
                            // The shell of this radius only; the inner ones were taken already.
                            if (Math.Abs(x) != radius && Math.Abs(y) != radius && Math.Abs(z) != radius) continue;

                            Vector3I cell = centre + new Vector3I(x, y, z);
                            if (grid.IsOccupied(cell)) continue;

                            // Only a cell that touches something already built: a block floating
                            // free of the hull conducts nothing and would read as cooling that does
                            // not work rather than as cooling that was never attached.
                            bool touches = false;
                            for (int face = 0; face < Face.Count && !touches; face++)
                            {
                                touches = grid.IsOccupied(cell + Face.Offsets[face]);
                            }

                            if (touches) free.Add(cell);
                        }
                    }
                }
            }

            return free;
        }

        /// <summary>Which grid of the assembly holds a node, so the fit goes on the right hull.</summary>
        private static GridBuilder BuilderOf(Blueprints.Ship ship, ShipAssembly assembly, ThermalNode node)
        {
            for (int i = 0; i < assembly.Simulations.Count && i < ship.Grids.Count; i++)
            {
                IList<ThermalNode> nodes = assembly.Simulations[i].Solver.Nodes;
                for (int n = 0; n < nodes.Count; n++)
                {
                    if (ReferenceEquals(nodes[n], node)) return ship.Grids[i].Builder;
                }
            }

            return null;
        }

        /// <summary>
        /// Bolts radiators into the free cells nearest the hot block. Returns how many landed —
        /// which is often zero, and that is a result about the ship.
        /// </summary>
        private static int Bolt(GridBuilder builder, Vector3I hot, BlockModel radiator)
        {
            List<Vector3I> free = FreeCellsAround(builder.Grid, hot, 3);
            int placed = 0;

            for (int i = 0; i < free.Count && placed < MaxBlocks; i++)
            {
                try
                {
                    builder.Place(radiator, free[i]);
                    placed++;
                }
                catch (Exception)
                {
                    // A multi-cell block whose other cells are taken. Try the next free cell.
                }
            }

            return placed;
        }

        /// <summary>
        /// Lays a coolant ring in free cells beside the hot block, with a pump and a sink face
        /// against it, and radiators on the far side of the ring.
        ///
        /// <para>
        /// A ring needs a closed loop of adjacent free cells, which a finished hull rarely offers
        /// near anything worth cooling. Every rectangle in the neighbourhood is tried and the first
        /// that fits is taken; a ship where none fits is counted rather than skipped, because
        /// "there was nowhere to put it" is the answer G3 most needs.
        /// </para>
        /// </summary>
        private static int Plumb(GridBuilder builder, Vector3I hot, BlockModel radiator)
        {
            GridModel grid = builder.Grid;

            // Rectangles in each of the three planes, offset so one straight run passes the hot
            // block. Small: a ring a hull has room for is a small ring.
            for (int plane = 0; plane < 3; plane++)
            {
                for (int offset = -3; offset <= 1; offset++)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3I origin = Origin(hot, plane, offset, side);
                        List<Vector3I> ring = Ring(origin, plane);
                        if (ring == null) continue;

                        bool clear = true;
                        for (int i = 0; i < ring.Count && clear; i++)
                        {
                            clear = !grid.IsOccupied(ring[i]);
                        }

                        if (!clear) continue;

                        // **The sink face is the whole fit.** A ring laid beside a hot block with
                        // no sink requested couples to it through ordinary block-to-block
                        // conduction at 167 W/K, which is the bolted case with extra pipes; a sink
                        // face carries 1,000. PipeFitter was written because a scenario made
                        // exactly this mistake and passed. So a rectangle is only usable if one of
                        // its cells is face-adjacent to the hot block, and that cell is told which
                        // way to point.
                        Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
                        for (int i = 0; i < ring.Count; i++)
                        {
                            Vector3I toHot = hot - ring[i];
                            if (Face.IndexOf(toHot) >= 0) sinks[i] = toHot;
                        }

                        if (sinks.Count == 0) continue;

                        try
                        {
                            PipeFitter.BuildRing(builder, ring, -1, sinks);
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        // Radiators wherever there is still room beside the ring, so the heat the
                        // loop collects has somewhere to go rather than circulating.
                        Bolt(builder, hot, radiator);
                        return ring.Count;
                    }
                }
            }

            return 0;
        }

        /// <summary>
        /// Where to try a ring, relative to the block being cooled.
        ///
        /// <para>
        /// Offsets are swept so that some cell of the rectangle lands face-adjacent to the hot
        /// block — a ring that merely passes nearby has no sink face and is not a plumbed fit. The
        /// sweep is wide because a hull leaves room where it leaves room, and narrow enough that
        /// every candidate still touches.
        /// </para>
        /// </summary>
        private static Vector3I Origin(Vector3I hot, int plane, int offset, int side)
        {
            switch (plane)
            {
                case 0: return hot + new Vector3I(offset, side, -1);
                case 1: return hot + new Vector3I(-1, offset, side);
                default: return hot + new Vector3I(side, -1, offset);
            }
        }

        private static List<Vector3I> Ring(Vector3I origin, int plane)
        {
            switch (plane)
            {
                case 0: return PipeFitter.RectangleXZ(origin, 3, 3);
                case 1: return PipeFitter.RectangleXY(origin, 3, 3);
                default: return PipeFitter.RectangleYZ(origin, 3, 3);
            }
        }

        /// <summary>One ship: as built, then bolted, then plumbed.</summary>
        public static Row Measure(Blueprints.Ship ship)
        {
            ThermalSettings settings = Settings();

            ShipAssembly bare = ship.Build(settings);
            ThermalNode hottest;
            float bareK = Load(bare, out hottest);

            Row row = new Row
            {
                Ship = ship.Name,
                Blocks = ship.Blocks,
                Large = ship.Large,
                BareK = bareK,
                HottestBlock = hottest == null ? "" : hottest.Block.Name,
                BoltedK = bareK,
                PlumbedK = bareK,
            };

            if (hottest == null) return row;

            GridBuilder builder = BuilderOf(ship, bare, hottest);
            if (builder == null) return row;

            Vector3I hot = hottest.Block.Position;
            BlockModel radiator = ShippedBlocks.Model(ship.Large ? "Gauge_LG_Radiator" : "Gauge_SG_Radiator");

            // Bolted first, on its own, and measured before anything else is added.
            row.BoltedPlaced = Bolt(builder, hot, radiator);
            if (row.BoltedPlaced > 0)
            {
                ShipAssembly bolted = ship.Build(settings);
                ThermalNode after;
                row.BoltedK = Load(bolted, out after);
            }

            // Then the ring, on top of the radiators, which is what a player fitting cooling to a
            // finished ship would end up with — the panels are what the loop sheds through.
            row.PlumbedPipes = Plumb(builder, hot, radiator);
            if (row.PlumbedPipes > 0)
            {
                ShipAssembly plumbed = ship.Build(settings);
                ThermalNode after;
                row.PlumbedK = Load(plumbed, out after);
            }

            return row;
        }

        public static List<Row> Run(IList<Blueprints.Ship> ships, LabMode mode = LabMode.Parallel)
        {
            GameBlocks.Warm();
            return LabRun.Map(ships, Measure, mode);
        }

        /// <summary>The rows of the last <see cref="Report"/>, for a caller that wants the CSV.</summary>
        public static List<Row> LastRows { get; private set; }

        private static float At(List<float> sorted, double q)
        {
            if (sorted.Count == 0) return 0f;
            int index = (int)Math.Round(q * (sorted.Count - 1));
            if (index < 0) index = 0;
            if (index >= sorted.Count) index = sorted.Count - 1;
            return sorted[index];
        }

        public static string Report(string path, int ships, LabMode mode = LabMode.Parallel)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("RETROFIT  (cooling fitted to ships somebody built)");
            sb.AppendLine();
            sb.Append("  full electrical load in shadow, ").Append(Seconds.ToString("n0"))
                .AppendLine(" simulated seconds, peak block temperature");
            sb.AppendLine("  cooling goes in the free cells nearest the hottest block, or nowhere");
            sb.AppendLine();

            string root = path ?? Blueprints.DefaultPath();
            if (root == null)
            {
                sb.AppendLine("  No blueprints anywhere. The corpus lives at:");
                sb.Append("    ").AppendLine(Blueprints.CorpusPath());
                return sb.ToString();
            }

            List<Blueprints.Ship> usable = Sample(root, ships);
            if (usable.Count == 0)
            {
                sb.Append("  No usable ships under ").AppendLine(root);
                return sb.ToString();
            }

            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            List<Row> rows = Run(usable, mode);
            clock.Stop();
            LastRows = rows;

            List<float> bolted = new List<float>();
            List<float> plumbed = new List<float>();
            int noRoomBolted = 0, noRoomPlumbed = 0, measured = 0, cold = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                if (row == null) continue;
                measured++;

                // A ship that never got warm cannot show whether cooling works, and averaging it
                // in would report the fit as useless rather than as untested.
                if (row.BareK < 320f) { cold++; continue; }

                if (row.BoltedPlaced == 0) noRoomBolted++;
                else bolted.Add(100f * row.BoltedSaved / row.BareK);

                if (row.PlumbedPipes == 0) noRoomPlumbed++;
                else plumbed.Add(100f * row.PlumbedSaved / row.BareK);
            }

            bolted.Sort();
            plumbed.Sort();

            sb.Append("  ").Append(measured.ToString("n0")).Append(" ships measured in ")
                .Append(clock.Elapsed.TotalSeconds.ToString("n0")).Append(" s, ")
                .AppendLine(LabRun.Describe(mode));
            sb.Append("  ").Append(cold.ToString("n0"))
                .AppendLine(" never passed 320 K under load, so cooling them proves nothing");
            sb.AppendLine();

            sb.AppendLine("  fit            ships   no room     p10     p50     p90     max   (% of peak taken off)");
            Band(sb, "bolted", bolted, noRoomBolted);
            Band(sb, "plumbed", plumbed, noRoomPlumbed);

            sb.AppendLine();
            sb.AppendLine("  A fit that takes single-figure percentages off a ship that is burning is");
            sb.AppendLine("  not a lever a player has. `no room` is the other half of the answer: cooling");
            sb.AppendLine("  that cannot be installed without demolishing the hull is not cooling either.");

            return sb.ToString();
        }

        private static void Band(StringBuilder sb, string label, List<float> sorted, int noRoom)
        {
            sb.Append("  ").Append(label.PadRight(12));
            sb.Append(sorted.Count.ToString("n0").PadLeft(7));
            sb.Append(noRoom.ToString("n0").PadLeft(10));

            if (sorted.Count == 0)
            {
                sb.AppendLine("        —       —       —       —");
                return;
            }

            double[] quantiles = { 0.10d, 0.50d, 0.90d, 1d };
            for (int i = 0; i < quantiles.Length; i++)
            {
                sb.Append(At(sorted, quantiles[i]).ToString("n2").PadLeft(8));
            }
            sb.AppendLine();
        }

        /// <summary>
        /// A stride through the corpus rather than the first <paramref name="ships"/> of it.
        ///
        /// The corpus walks largest-first, so a prefix is a fleet of capital ships and reads
        /// nothing like the population. A stride keeps the size spread whatever the sample size.
        /// </summary>
        private static List<Blueprints.Ship> Sample(string root, int ships)
        {
            List<string> files = Blueprints.Files(root);
            files.Sort(StringComparer.Ordinal);

            long limit = (long)StiffnessLab.MaxMegabytes() * 1024L * 1024L;
            List<Blueprints.Ship> usable = new List<Blueprints.Ship>();
            int stride = ships > 0 && files.Count > ships ? files.Count / ships : 1;

            for (int i = 0; i < files.Count; i += stride)
            {
                if (ships > 0 && usable.Count >= ships) break;

                try
                {
                    if (new System.IO.FileInfo(files[i]).Length > limit) continue;
                }
                catch (Exception) { continue; }

                List<Blueprints.Ship> read = Blueprints.Read(files[i]);
                for (int s = 0; s < read.Count; s++)
                {
                    if (!read[s].IsVanilla) continue;
                    if (read[s].Blocks < CorpusLab.MinimumBlocks) continue;
                    usable.Add(read[s]);
                    break;
                }
            }

            return usable;
        }

        /// <summary>
        /// A number for a CSV cell: no thousands separator, invariant point.
        ///
        /// <c>ToString("n2")</c> groups thousands even in the invariant culture, so a ship at
        /// 1,012.83 K wrote a comma into the middle of a comma-separated file and every column
        /// after it shifted by one. The first read of this file reported a third of the ships as
        /// cold that were not.
        /// </summary>
        private static string Cell(float value)
        {
            return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>One row per ship, so a surprising figure can be traced to a hull.</summary>
        public static string Csv(IList<Row> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ship,large,blocks,bare_k,hottest_block,bolted_n,bolted_k,plumbed_pipes,plumbed_k");

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                if (row == null) continue;

                sb.Append('"').Append((row.Ship ?? "").Replace("\"", "\"\"")).Append('"').Append(',');
                sb.Append(row.Large ? 1 : 0).Append(',').Append(row.Blocks).Append(',');
                sb.Append(Cell(row.BareK)).Append(',');
                sb.Append('"').Append((row.HottestBlock ?? "").Replace("\"", "\"\"")).Append('"').Append(',');
                sb.Append(row.BoltedPlaced).Append(',');
                sb.Append(Cell(row.BoltedK)).Append(',');
                sb.Append(row.PlumbedPipes).Append(',');
                sb.AppendLine(Cell(row.PlumbedK));
            }

            return sb.ToString();
        }
    }
}
