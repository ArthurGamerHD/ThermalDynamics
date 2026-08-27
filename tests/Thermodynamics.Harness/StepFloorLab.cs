using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **What the three passes of a substep would cost if they did no arithmetic at all.**
    ///
    /// <para>
    /// `backlog.md` `D1` asks whether what is left of a step is near the memory-bandwidth floor, and
    /// records it as an open question — the last time it was claimed, two passes were reaching
    /// through the node objects instead. This answers it by measurement: the same arrays, at the
    /// same sizes, walked in the same pattern, doing the least arithmetic that still forces every
    /// load and store to happen.
    /// </para>
    ///
    /// <para>
    /// **The link indices are a real grid's**, not a synthetic pattern, because the whole question
    /// for the conduction pass is where its gathers land — and a made-up index stream would answer
    /// about itself rather than about this model. See performance.md, Pass 5, Iteration 4.
    /// </para>
    /// </summary>
    public static class StepFloorLab
    {
        public class Row
        {
            public string Pass;
            public long Elements;
            public double FloorNs;
            public double BytesPerElement;

            /// <summary>Bytes a second the floor loop moved, which is the figure to compare against a machine's own.</summary>
            public double GigabytesPerSecond
            {
                get { return FloorNs <= 0d ? 0d : BytesPerElement / FloorNs; }
            }
        }

        public static List<Row> Run(string shape, int blocks, int repeats = 15)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build(shape, blocks));
            ThermalSimulation simulation = builder.BuildSimulation(Hulls.Uncapped(), 293.15f);

            int nodes = simulation.Solver.Nodes.Count;

            int[] linkA;
            int[] linkB;
            LinkIndices(simulation, out linkA, out linkB);

            // The rows a substep touches, at the sizes it touches them.
            float[] temperature = Filled(nodes, 293.15f);
            float[] watts = Filled(nodes, 1f);
            float[] mass = Filled(nodes, 2f);
            float[] critical = Filled(nodes, 1400f);
            float[] source = Filled(nodes, 3f);
            float[] radiation = Filled(nodes, 0.5f);
            float[] convection = Filled(nodes, 0.25f);
            float[] conductance = Filled(linkA.Length, 0.1f);

            List<Row> rows = new List<Row>();
            rows.Add(Measure("environment", nodes, 24, repeats, delegate
            {
                // Four rows in, one out: the shape of the environment read.
                for (int i = 0; i < temperature.Length; i++)
                {
                    watts[i] = source[i] + radiation[i] + convection[i] + temperature[i];
                }
            }));

            rows.Add(Measure("conduction", linkA.Length, 24, repeats, delegate
            {
                // Two index rows and a conductance row in; two scattered read-modify-writes out.
                for (int i = 0; i < linkA.Length; i++)
                {
                    int a = linkA[i];
                    int b = linkB[i];
                    float exchange = conductance[i] * (temperature[b] - temperature[a]);
                    watts[a] += exchange;
                    watts[b] -= exchange;
                }
            }));

            rows.Add(Measure("apply", nodes, 24, repeats, delegate
            {
                for (int i = 0; i < temperature.Length; i++)
                {
                    temperature[i] = temperature[i] + watts[i] * 0.001f / mass[i] + critical[i] * 0f;
                }
            }));

            GC.KeepAlive(critical);
            return rows;
        }

        /// <summary>Bytes each pass moves per element, counted from the rows above.</summary>
        private static double BytesOf(string pass)
        {
            switch (pass)
            {
                case "environment": return 4 * 5;          // four in, one out
                case "conduction": return (4 * 3) + (4 * 4); // two indices and a conductance in, two gathers and two scatters
                default: return 4 * 5;                     // temperature in and out, watts, mass, critical
            }
        }

        private static Row Measure(string pass, long elements, int substeps, int repeats, Action body)
        {
            // Warm, then the fastest of N, as every other lab here does.
            body();

            double best = double.MaxValue;
            for (int r = 0; r < Math.Max(1, repeats); r++)
            {
                Stopwatch watch = Stopwatch.StartNew();
                for (int s = 0; s < substeps; s++) body();
                watch.Stop();

                double ns = watch.Elapsed.TotalMilliseconds * 1e6d / (elements * substeps);
                if (ns < best) best = ns;
            }

            Row row = new Row();
            row.Pass = pass;
            row.Elements = elements;
            row.FloorNs = best;
            row.BytesPerElement = BytesOf(pass);
            return row;
        }

        private static float[] Filled(int count, float value)
        {
            float[] row = new float[count];
            for (int i = 0; i < count; i++) row[i] = value;
            return row;
        }

        /// <summary>The grid's own link ends, in the order the solver builds them.</summary>
        private static void LinkIndices(ThermalSimulation simulation, out int[] a, out int[] b)
        {
            List<int> ends = new List<int>();
            List<int> others = new List<int>();
            List<BlockInstance> scratch = new List<BlockInstance>();

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                scratch.Clear();
                simulation.Grid.GetNeighbours(nodes[i].Block, scratch);

                for (int n = 0; n < scratch.Count; n++)
                {
                    ThermalNode other = simulation.Solver.GetNode(scratch[n]);
                    if (other == null || other.Index <= i) continue;

                    ends.Add(i);
                    others.Add(other.Index);
                }
            }

            a = ends.ToArray();
            b = others.ToArray();
        }

        public static string Table(IList<Row> rows, IList<StageLab.Row> measured)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("  pass          elements     floor ns/el    step ns/el   over floor      floor GB/s");

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                double step = 0d;
                for (int m = 0; measured != null && m < measured.Count; m++)
                {
                    if (measured[m].Stage != row.Pass) continue;
                    step = measured[m].NsPerWork;
                }

                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-12} {1,9:n0}  {2,14:n2}  {3,12:n2}  {4,11:n2}x  {5,14:n1}",
                    row.Pass, row.Elements, row.FloorNs, step,
                    row.FloorNs <= 0d ? 0d : step / row.FloorNs, row.GigabytesPerSecond));
            }

            return text.ToString();
        }
    }
}
