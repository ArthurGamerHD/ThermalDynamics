using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// The segmented coolant model against the well-mixed one it replaced, on the same grid.
    ///
    /// Segmenting the fluid turns one temperature and one integration per ring into one per pipe, and
    /// adds a pass that carries the fluid round. That is a real cost and it lands entirely on grids
    /// carrying a lot of plumbing, which is exactly where nobody had measured: every performance
    /// report in this repository ran on hulls with no pipe in them at all.
    ///
    /// The measurement that matters is not the ratio at one size but whether the ratio grows. A fixed
    /// multiple is a tuning question; a multiple that climbs with the amount of pipe a player lays is
    /// a design problem.
    /// </summary>
    public static class CoolantBenchmarks
    {
        public class Row
        {
            public int Rings;
            public int Pipes;
            public int Loops;
            public int Blocks;
            public int Links;

            public double SegmentedMsPerStep;
            public double MixedMsPerStep;

            public float SegmentedSubsteps;
            public float MixedSubsteps;

            /// <summary>Share of transport served by mixing rather than carrying, 0..1.</summary>
            public float Mixing;

            public double Ratio
            {
                get { return MixedMsPerStep <= 0d ? 0d : SegmentedMsPerStep / MixedMsPerStep; }
            }
        }

        /// <summary>
        /// Builds a hull with <paramref name="rings"/> rings of pipe plumbed against it, and steps it
        /// under both models.
        ///
        /// Each ring gets sink faces onto the hull, because a sink is a link and links are the cost.
        /// A ring with no sinks would understate the segmented model's disadvantage.
        /// </summary>
        public static Row Measure(int hullSize, int rings, int steps)
        {
            return Measure(hullSize, rings, steps, 0f);
        }

        /// <param name="flowOverride">
        /// Parcels per second at full flow, or zero for the shipped figure. Set it high to force the
        /// mixing path, which only runs once the flow outruns the substep and so is absent from a
        /// default measurement entirely.
        /// </param>
        public static Row Measure(int hullSize, int rings, int steps, float flowOverride)
        {
            Row row = new Row();
            row.Rings = rings;

            ThermalSimulation segmented = Build(hullSize, rings, false, flowOverride);
            ThermalSimulation mixed = Build(hullSize, rings, true, flowOverride);

            row.Blocks = segmented.Solver.Nodes.Count;
            row.Links = segmented.Solver.LinkCount;
            row.Loops = segmented.Solver.Loops.Count;

            int pipes = 0;
            for (int i = 0; i < segmented.Solver.Loops.Count; i++)
            {
                pipes += segmented.Solver.Loops[i].PipeCount;
            }
            row.Pipes = pipes;

            // Best of several passes, alternating between the two models. A mean would report the
            // machine's background noise as a difference between them, and the first attempt at this
            // did exactly that: it timed a four-ring grid as faster than a one-ring grid, which is
            // impossible. The minimum is the closest thing to the work actually required.
            if (segmented.Solver.Loops.Count > 0)
            {
                CoolantLoop first = segmented.Solver.Loops[0];
                row.Mixing = first.MixingFraction(
                    segmented.Settings.StepSeconds / Math.Max(1, segmented.Solver.LastSubsteps));
            }

            row.SegmentedMsPerStep = double.MaxValue;
            row.MixedMsPerStep = double.MaxValue;

            for (int pass = 0; pass < Repeats; pass++)
            {
                float substeps;

                double a = TimeSteps(segmented, steps, out substeps);
                if (a < row.SegmentedMsPerStep) row.SegmentedMsPerStep = a;
                row.SegmentedSubsteps = substeps;

                double b = TimeSteps(mixed, steps, out substeps);
                if (b < row.MixedMsPerStep) row.MixedMsPerStep = b;
                row.MixedSubsteps = substeps;
            }

            return row;
        }

        /// <summary>Passes per model per row. The best of them is reported.</summary>
        public const int Repeats = 5;

        private static ThermalSimulation Build(int hullSize, int rings, bool wellMixed, float flowOverride)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.WellMixedCoolant = wellMixed;

            // The bounds that would otherwise shorten a step and hide the difference.
            settings.MaxSubsteps = 4096;
            settings.MaxLinkVisitsPerStep = 0;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            HashSet<Vector3I> cells = LoadShapes.Build("ship", hullSize);
            builder.PlaceCensus(cells);

            Vector3I max = Vector3I.Zero;
            foreach (Vector3I cell in cells)
            {
                if (cell.X > max.X) max.X = cell.X;
                if (cell.Y > max.Y) max.Y = cell.Y;
                if (cell.Z > max.Z) max.Z = cell.Z;
            }

            // Rings stacked in clear space beside the hull, each with a slab of armour under it for
            // its sink faces to reach. What is being timed is the loop's own passes, which do not
            // care where the ring sits.
            for (int r = 0; r < rings; r++)
            {
                Vector3I origin = new Vector3I(max.X + 3, max.Y - (r * 3), max.Z + 3);
                List<Vector3I> ring = PipeFitter.RectangleXZ(origin, 8, 6);

                Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
                for (int i = 2; i < ring.Count; i += 3) sinks[i] = Vector3I.Down;

                try
                {
                    PipeFitter.BuildRing(builder, ring, -1, sinks);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (KeyValuePair<int, Vector3I> sink in sinks)
                {
                    Vector3I at = ring[sink.Key] + Vector3I.Down;
                    if (builder.Grid.GetAtCell(at) != null) continue;
                    builder.Place(Catalog.HeavyArmor(), at);
                }
            }

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.RebuildAll();

            // A gradient across everything, so no link is skipped for having equal ends and no loop
            // sits at the temperature of what it touches.
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].Temperature = 250f + ((i * 37) % 500);
            }
            for (int i = 0; i < simulation.Solver.Loops.Count; i++)
            {
                CoolantLoop loop = simulation.Solver.Loops[i];
                loop.Temperature = 300f + (i * 11);

                if (flowOverride <= 0f) continue;
                loop.Properties.LargeGridFlowRate = flowOverride * loop.ParcelLengthMetres;
                loop.Properties.SmallGridFlowRate = loop.Properties.LargeGridFlowRate;
                loop.RefreshFlow();
            }

            return simulation;
        }

        private static double TimeSteps(ThermalSimulation simulation, int steps, out float substeps)
        {
            EnvironmentSample sample = Worlds.Shadow();

            // Warm every array and JIT every path before the clock starts.
            simulation.StepExact(4, sample);

            Stopwatch clock = Stopwatch.StartNew();
            simulation.StepExact(steps, sample);
            clock.Stop();

            substeps = simulation.Solver.LastSubsteps;
            return clock.Elapsed.TotalMilliseconds / steps;
        }

        public static string Table(IList<Row> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("  rings  pipes  loops   blocks    links   segmented   well-mixed   ratio  substeps   mixing\n");
            sb.Append("  -----  -----  -----   ------    -----   ---------   ----------   -----  --------   ------\n");

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                sb.Append("  ").Append(row.Rings.ToString().PadLeft(5))
                  .Append("  ").Append(row.Pipes.ToString().PadLeft(5))
                  .Append("  ").Append(row.Loops.ToString().PadLeft(5))
                  .Append("   ").Append(row.Blocks.ToString("n0").PadLeft(6))
                  .Append("   ").Append(row.Links.ToString("n0").PadLeft(6))
                  .Append("   ").Append(row.SegmentedMsPerStep.ToString("n4").PadLeft(9))
                  .Append("   ").Append(row.MixedMsPerStep.ToString("n4").PadLeft(10))
                  .Append("   ").Append(row.Ratio.ToString("n2").PadLeft(5))
                  .Append("  ").Append((row.SegmentedSubsteps + " / " + row.MixedSubsteps).PadLeft(8))
                  .Append("   ").Append(row.Mixing.ToString("n2").PadLeft(6))
                  .Append('\n');
            }

            return sb.ToString();
        }

        public static string Csv(IList<Row> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("rings,pipes,loops,blocks,links,segmented_ms,mixed_ms,ratio,segmented_substeps,mixed_substeps\n");

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                sb.Append(row.Rings).Append(',').Append(row.Pipes).Append(',').Append(row.Loops)
                  .Append(',').Append(row.Blocks).Append(',').Append(row.Links)
                  .Append(',').Append(row.SegmentedMsPerStep.ToString("n6"))
                  .Append(',').Append(row.MixedMsPerStep.ToString("n6"))
                  .Append(',').Append(row.Ratio.ToString("n4"))
                  .Append(',').Append(row.SegmentedSubsteps)
                  .Append(',').Append(row.MixedSubsteps)
                  .Append('\n');
            }

            return sb.ToString();
        }
    }
}
