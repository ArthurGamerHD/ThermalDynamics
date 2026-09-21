using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
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

/// <summary>Run operation.</summary>
        public static List<Row> Run(string shape, int blocks, int repeats = 30)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build(shape, blocks));
            ThermalSimulation simulation = builder.BuildSimulation(Hulls.Uncapped(), 293.15f);

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int count = nodes.Count;

            int[] exposedFaces = new int[count];
            float[] temperature = new float[count];
            float[] radiationRow = new float[count];
            float[] convectionRow = new float[count];
            float[] sourceRow = new float[count];
            float[] wattsA = new float[count];
            float[] wattsB = new float[count];

/// <summary>List operation.</summary>
            List<int> exposedList = new List<int>();
/// <summary>List operation.</summary>
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

/// <summary>Branchy operation.</summary>
            float sumA = Branchy(exposedFaces, temperature, radiationRow, convectionRow, sourceRow, wattsA, Ambient, AmbientPow4);
/// <summary>Compact operation.</summary>
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
            if (Math.Abs(sumA - sumB) > Math.Abs(sumA) * 1e-3f + 1f)
            {
                throw new InvalidOperationException("the accumulators diverged: " + sumA + " against " + sumB);
            }

/// <summary>List operation.</summary>
            List<Row> rows = new List<Row>();
            rows.Add(Time("branchy (shipped shape)", count, exposedIndex.Length, repeats,
                () => Branchy(exposedFaces, temperature, radiationRow, convectionRow, sourceRow, wattsA, Ambient, AmbientPow4)));
            rows.Add(Time("clear + compact index", count, exposedIndex.Length, repeats,
                () => Compact(exposedIndex, buriedGenerating, temperature, radiationRow, convectionRow, sourceRow, wattsB, Ambient, AmbientPow4)));
            return rows;
        }

/// <summary>Branchy operation.</summary>
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

/// <summary>Compact operation.</summary>
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

/// <summary>Time operation.</summary>
        private static Row Time(string shape, int nodes, int exposed, int repeats, Func<float> pass)
        {
            pass();

            double best = double.MaxValue;
/// <summary>Stopwatch operation.</summary>
            Stopwatch watch = new Stopwatch();
            for (int r = 0; r < repeats; r++)
            {
                watch.Restart();
/// <summary>pass operation.</summary>
                float sink = pass();
                watch.Stop();
                if (float.IsNaN(sink)) throw new InvalidOperationException("the pass produced NaN");
                double ns = watch.Elapsed.TotalMilliseconds * 1e6;
                if (ns < best) best = ns;
            }

/// <summary>Row operation.</summary>
            Row row = new Row();
            row.Shape = shape;
            row.Nodes = nodes;
            row.ExposedNodes = exposed;
            row.BestNs = best;
            row.NsPerNode = best / nodes;
            return row;
        }

/// <summary>Report operation.</summary>
        public static string Report(string shape, int blocks, Action<string> log = null)
        {
            if (log != null) log(blocks.ToString("n0", CultureInfo.InvariantCulture) + " blocks");
            return Table(Run(shape, blocks));
        }

/// <summary>Table operation.</summary>
        public static string Table(List<Row> rows)
        {
/// <summary>StringBuilder operation.</summary>
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
