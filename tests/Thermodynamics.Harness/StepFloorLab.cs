using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class StepFloorLab
    {
        public class Row
        {
            public string Pass;
            public long Elements;
            public double FloorNs;
            public double BytesPerElement;

            public double GigabytesPerSecond
            {
                get { return FloorNs <= 0d ? 0d : BytesPerElement / FloorNs; }
            }
        }

/// <summary>Run operation.</summary>
        public static List<Row> Run(string shape, int blocks, int repeats = 15)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build(shape, blocks));
            ThermalSimulation simulation = builder.BuildSimulation(Hulls.Uncapped(), 293.15f);

            int nodes = simulation.Solver.Nodes.Count;

            int[] linkA;
            int[] linkB;
            LinkIndices(simulation, out linkA, out linkB);

/// <summary>Filled operation.</summary>
            float[] temperature = Filled(nodes, 293.15f);
/// <summary>Filled operation.</summary>
            float[] watts = Filled(nodes, 1f);
/// <summary>Filled operation.</summary>
            float[] mass = Filled(nodes, 2f);
/// <summary>Filled operation.</summary>
            float[] critical = Filled(nodes, 1400f);
/// <summary>Filled operation.</summary>
            float[] source = Filled(nodes, 3f);
/// <summary>Filled operation.</summary>
            float[] radiation = Filled(nodes, 0.5f);
/// <summary>Filled operation.</summary>
            float[] convection = Filled(nodes, 0.25f);
/// <summary>Filled operation.</summary>
            float[] conductance = Filled(linkA.Length, 0.1f);

/// <summary>List operation.</summary>
            List<Row> rows = new List<Row>();
            rows.Add(Measure("environment", nodes, 24, repeats, delegate
            {
                for (int i = 0; i < temperature.Length; i++)
                {
                    watts[i] = source[i] + radiation[i] + convection[i] + temperature[i];
                }
            }));

            rows.Add(Measure("conduction", linkA.Length, 24, repeats, delegate
            {
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

/// <summary>BytesOf operation.</summary>
        private static double BytesOf(string pass)
        {
            switch (pass)
            {
                case "environment": return 4 * 5;          // four in, one out
/// <summary>return operation.</summary>
                case "conduction": return (4 * 3) + (4 * 4); // two indices and a conductance in, two gathers and two scatters
                default: return 4 * 5;                     // temperature in and out, watts, mass, critical
            }
        }

/// <summary>Measure operation.</summary>
        private static Row Measure(string pass, long elements, int substeps, int repeats, Action body)
        {
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

/// <summary>Row operation.</summary>
            Row row = new Row();
            row.Pass = pass;
            row.Elements = elements;
            row.FloorNs = best;
/// <summary>BytesOf operation.</summary>
            row.BytesPerElement = BytesOf(pass);
            return row;
        }

/// <summary>Filled operation.</summary>
        private static float[] Filled(int count, float value)
        {
            float[] row = new float[count];
            for (int i = 0; i < count; i++) row[i] = value;
            return row;
        }

/// <summary>LinkIndices operation.</summary>
        private static void LinkIndices(ThermalSimulation simulation, out int[] a, out int[] b)
        {
/// <summary>List operation.</summary>
            List<int> ends = new List<int>();
/// <summary>List operation.</summary>
            List<int> others = new List<int>();
/// <summary>List operation.</summary>
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

/// <summary>Table operation.</summary>
        public static string Table(IList<Row> rows, IList<StageLab.Row> measured)
        {
/// <summary>StringBuilder operation.</summary>
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
