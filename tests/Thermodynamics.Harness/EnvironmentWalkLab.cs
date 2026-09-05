using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// What the environment read's buried nodes still cost after the branch that already
    /// short-circuits them — priced against the alternative shape, a bulk clear plus a walk over
    /// a compacted index of exposed nodes only.
    ///
    /// <para>
    /// This is the evaluation instrument for the second-sweep candidate on redesign.md. The
    /// obvious version of "visit fewer elements" for the environment pass — skip the buried
    /// nodes, which are 30 to 70 per cent of a hull — is mostly collected already: the shipped
    /// loop tests <c>nodeExposedFaces[i] &lt;= 0</c> and a buried node costs one branch, one row
    /// load and one store. What is left on the table is exactly that residue, times twenty-seven
    /// read substeps a step, and whether it is worth a design is a number, not an argument.
    /// **The criterion, fixed before the first run** (`E1`): the compacted shape earns a design
    /// row if it beats the shipped shape by ten per cent or more at 505,566 blocks, where two
    /// thirds of the hull is buried; anything less is a refusal with the figure attached.
    /// </para>
    ///
    /// <para>
    /// Both shapes run on a real hull's exposure pattern, not a synthetic one — the whole
    /// question is how the buried cells interleave with the exposed ones, and a made-up pattern
    /// would answer about itself (the `StepFloorLab` rule). Both shapes must produce the same
    /// watts row exactly, which the lab asserts before it reports a single time: two loops that
    /// disagree are two different passes, not two shapes of one (`E8`, `P4`).
    /// </para>
    /// </summary>
    public static class EnvironmentWalkLab
    {
        public class Row
        {
            public string Shape;
            public int Nodes;
            public int ExposedNodes;
            public double BestNs;
            public double NsPerNode;
        }

        public static List<Row> Run(string shape, int blocks, int repeats = 30)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build(shape, blocks));
            ThermalSimulation simulation = builder.BuildSimulation(Hulls.Uncapped(), 293.15f);

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int count = nodes.Count;

            // The rows the read touches, with the real hull's sparsity: buried rows are zero the
            // way the fill leaves them, exposed rows carry plausible magnitudes, and generation
            // sits wherever the census put a producer.
            int[] exposedFaces = new int[count];
            float[] temperature = new float[count];
            float[] radiationRow = new float[count];
            float[] convectionRow = new float[count];
            float[] sourceRow = new float[count];
            float[] wattsA = new float[count];
            float[] wattsB = new float[count];

            List<int> exposedList = new List<int>();
            List<int> buriedGeneratingList = new List<int>();
            for (int i = 0; i < count; i++)
            {
                exposedFaces[i] = nodes[i].TotalExposedFaces;
                temperature[i] = 250f + ((i * 37) % 500);
                float generation = nodes[i].HeatGenerationWatts;

                if (exposedFaces[i] > 0)
                {
                    exposedList.Add(i);
                    radiationRow[i] = 0.05f + ((i % 7) * 0.01f);
                    convectionRow[i] = -1.5f - (i % 5);
                    sourceRow[i] = generation + ((i % 3) * 40f);
                }
                else
                {
                    sourceRow[i] = generation;
                    if (generation != 0f) buriedGeneratingList.Add(i);
                }
            }
            int[] exposedIndex = exposedList.ToArray();
            int[] buriedGenerating = buriedGeneratingList.ToArray();

            const float Ambient = 220f;
            const float AmbientPow4 = Ambient * Ambient * Ambient * Ambient;

            // Prove the two shapes are one pass before timing either: identical watts per node,
            // exactly — every node's result is independent, so nothing may differ by a bit.
            float sumA = Branchy(exposedFaces, temperature, radiationRow, convectionRow, sourceRow, wattsA, Ambient, AmbientPow4);
            float sumB = Compact(exposedIndex, buriedGenerating, temperature, radiationRow, convectionRow, sourceRow, wattsB, Ambient, AmbientPow4);
            for (int i = 0; i < count; i++)
            {
                if (wattsA[i] != wattsB[i])
                {
                    throw new InvalidOperationException(
                        "the two shapes disagree at node " + i + ": " + wattsA[i] + " against " + wattsB[i]
                        + " — two different passes, not two shapes of one");
                }
            }
            // The accumulator may differ in the last bits: the compact shape sums in a different
            // order. It is checked loosely here and belongs to the design if one is ever built.
            if (Math.Abs(sumA - sumB) > Math.Abs(sumA) * 1e-3f + 1f)
            {
                throw new InvalidOperationException("the accumulators diverged: " + sumA + " against " + sumB);
            }

            List<Row> rows = new List<Row>();
            rows.Add(Time("branchy (shipped shape)", count, exposedIndex.Length, repeats,
                () => Branchy(exposedFaces, temperature, radiationRow, convectionRow, sourceRow, wattsA, Ambient, AmbientPow4)));
            rows.Add(Time("clear + compact index", count, exposedIndex.Length, repeats,
                () => Compact(exposedIndex, buriedGenerating, temperature, radiationRow, convectionRow, sourceRow, wattsB, Ambient, AmbientPow4)));
            return rows;
        }

        /// <summary>
        /// The shipped read shape: one loop over every node, the buried branch first — one
        /// compare, one row load and one store for a buried node, full arithmetic for an exposed
        /// one. The arithmetic mirrors the radiation and convection terms of the real read.
        /// </summary>
        private static float Branchy(int[] exposedFaces, float[] temperature, float[] radiationRow,
            float[] convectionRow, float[] sourceRow, float[] watts, float ambient, float ambientPow4)
        {
            float accumulator = 0f;
            for (int i = 0; i < exposedFaces.Length; i++)
            {
                if (exposedFaces[i] <= 0)
                {
                    float buried = sourceRow[i];
                    watts[i] = buried;
                    accumulator += buried;
                    continue;
                }

                float t = temperature[i];
                float squared = t * t;
                float radiation = -radiationRow[i] * ((squared * squared) - ambientPow4);
                float convection = convectionRow[i] * (t - ambient);
                float w = sourceRow[i] + radiation + convection;
                watts[i] = w;
                accumulator += w;
            }
            return accumulator;
        }

        /// <summary>
        /// The candidate shape: the watts row cleared in bulk, the buried producers written from
        /// a sparse list, and the arithmetic walked over a compacted index of exposed nodes.
        /// </summary>
        private static float Compact(int[] exposedIndex, int[] buriedGenerating, float[] temperature,
            float[] radiationRow, float[] convectionRow, float[] sourceRow, float[] watts,
            float ambient, float ambientPow4)
        {
            Array.Clear(watts, 0, watts.Length);

            float accumulator = 0f;
            for (int g = 0; g < buriedGenerating.Length; g++)
            {
                int i = buriedGenerating[g];
                float buried = sourceRow[i];
                watts[i] = buried;
                accumulator += buried;
            }

            for (int k = 0; k < exposedIndex.Length; k++)
            {
                int i = exposedIndex[k];
                float t = temperature[i];
                float squared = t * t;
                float radiation = -radiationRow[i] * ((squared * squared) - ambientPow4);
                float convection = convectionRow[i] * (t - ambient);
                float w = sourceRow[i] + radiation + convection;
                watts[i] = w;
                accumulator += w;
            }
            return accumulator;
        }

        private static Row Time(string shape, int nodes, int exposed, int repeats, Func<float> pass)
        {
            // Once untimed, so neither shape pays the JIT inside its clock.
            pass();

            double best = double.MaxValue;
            Stopwatch watch = new Stopwatch();
            for (int r = 0; r < repeats; r++)
            {
                watch.Restart();
                float sink = pass();
                watch.Stop();
                if (float.IsNaN(sink)) throw new InvalidOperationException("the pass produced NaN");
                double ns = watch.Elapsed.TotalMilliseconds * 1e6;
                if (ns < best) best = ns;
            }

            Row row = new Row();
            row.Shape = shape;
            row.Nodes = nodes;
            row.ExposedNodes = exposed;
            row.BestNs = best;
            row.NsPerNode = best / nodes;
            return row;
        }

        public static string Report(string shape, int blocks, Action<string> log = null)
        {
            if (log != null) log(blocks.ToString("n0", CultureInfo.InvariantCulture) + " blocks");
            return Table(Run(shape, blocks));
        }

        public static string Table(List<Row> rows)
        {
            StringBuilder text = new StringBuilder();
            text.Append("shape".PadRight(26))
                .Append("nodes".PadLeft(10))
                .Append("exposed".PadLeft(10))
                .Append("best ms".PadLeft(11))
                .Append("ns/node".PadLeft(10))
                .Append('\n');

            foreach (Row row in rows)
            {
                text.Append(row.Shape.PadRight(26))
                    .Append(row.Nodes.ToString("n0", CultureInfo.InvariantCulture).PadLeft(10))
                    .Append(row.ExposedNodes.ToString("n0", CultureInfo.InvariantCulture).PadLeft(10))
                    .Append((row.BestNs / 1e6).ToString("n3", CultureInfo.InvariantCulture).PadLeft(11))
                    .Append(row.NsPerNode.ToString("n2", CultureInfo.InvariantCulture).PadLeft(10))
                    .Append('\n');
            }

            if (rows.Count == 2 && rows[0].BestNs > 0)
            {
                text.Append('\n')
                    .Append("compact / branchy: ")
                    .Append((rows[1].BestNs / rows[0].BestNs).ToString("n3", CultureInfo.InvariantCulture))
                    .Append("  (buried share ")
                    .Append((1d - ((double)rows[0].ExposedNodes / rows[0].Nodes)).ToString("p1", CultureInfo.InvariantCulture))
                    .Append(")\n");
            }

            return text.ToString();
        }
    }
}
