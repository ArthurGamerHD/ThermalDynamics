using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class ConductionLayoutLab
    {
        public static readonly int[] DefaultThreads = { 1, 2, 4, 8, 16, 32 };

        public class Row
        {
            public string Variant;
            public int Threads;
            public StageLab.Row Timing;

            public double WorstError;
        }

        private class Graph
        {
            public int Nodes;
            public int[] LinkA;
            public int[] LinkB;
            public float[] Conductance;
            public float[] Temperature;

            public int[] Start;
            public int[] Neighbour;
            public float[] NeighbourConductance;

            public Vector3I[] Position;
        }

/// <summary>Run operation.</summary>
        public static List<Row> Run(string shape, int blocks, int[] threads, Action<string> log)
        {
            if (log != null) log("building " + blocks.ToString("n0") + " blocks");
/// <summary>Builds the method table.</summary>
            Graph graph = Build(shape, blocks);

/// <summary>List operation.</summary>
            List<Row> rows = new List<Row>();
            float[] reference = new float[graph.Nodes];
            Scatter(graph, reference);

            if (log != null) log("scatter, serial");
            rows.Add(Measure("scatter", 1, graph, reference,
/// <summary>Scatter operation.</summary>
                watts => Scatter(graph, watts)));

            if (log != null) log("gather, serial");
            rows.Add(Measure("gather", 1, graph, reference,
/// <summary>GatherRange operation.</summary>
                watts => GatherRange(graph, watts, 0, graph.Nodes)));

            for (int t = 0; t < threads.Length; t++)
            {
                int count = threads[t];
                if (count < 2 || count > Environment.ProcessorCount) continue;

                if (log != null) log("gather, " + count + " threads");
                rows.Add(Measure("gather", count, graph, reference,
/// <summary>GatherParallel operation.</summary>
                    watts => GatherParallel(graph, watts, count)));
            }

            if (log != null) log("morton reorder");
/// <summary>Reorder operation.</summary>
            Graph morton = Reorder(graph, MortonOrder(graph));
            float[] mortonReference = new float[morton.Nodes];
            Scatter(morton, mortonReference);

            rows.Add(Measure("m-scatter", 1, morton, mortonReference,
/// <summary>Scatter operation.</summary>
                watts => Scatter(morton, watts)));
            rows.Add(Measure("m-gather", 1, morton, mortonReference,
/// <summary>GatherRange operation.</summary>
                watts => GatherRange(morton, watts, 0, morton.Nodes)));

            for (int t = 0; t < threads.Length; t++)
            {
                int count = threads[t];
                if (count < 2 || count > Environment.ProcessorCount) continue;

                if (log != null) log("morton gather, " + count + " threads");
                rows.Add(Measure("m-gather", count, morton, mortonReference,
/// <summary>GatherParallel operation.</summary>
                    watts => GatherParallel(morton, watts, count)));
            }

            return rows;
        }


/// <summary>Builds the API method table.</summary>
        private static Graph Build(string shape, int blocks)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build(shape, blocks));

            ThermalSimulation simulation = builder.BuildSimulation(Hulls.Uncapped(), 293.15f);
            LoadBenchmarks.SeedSpread(simulation);
            Census.DriveCensus(simulation);

            EnvironmentState state = EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, Worlds.Flight(1f, 300f));
            for (int i = 0; i < 3; i++)
            {
                simulation.Solver.Step(simulation.Settings.StepSeconds, state);
            }

/// <summary>Graph operation.</summary>
            Graph graph = new Graph();
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            graph.Nodes = nodes.Count;
            graph.Temperature = new float[nodes.Count];
            graph.Position = new Vector3I[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                graph.Temperature[i] = nodes[i].Temperature;
                graph.Position[i] = nodes[i].Block.Min;
            }

            IList<ThermalLink> links = simulation.Solver.Links;
            graph.LinkA = new int[links.Count];
            graph.LinkB = new int[links.Count];
            graph.Conductance = new float[links.Count];
            for (int i = 0; i < links.Count; i++)
            {
                ThermalLink link = links[i];
                graph.LinkA[i] = link.NodeA;
                graph.LinkB[i] = link.NodeB;
                graph.Conductance[i] = link.Conductance;
            }

            BuildCsr(graph);
            return graph;
        }

/// <summary>Builds the API method table.</summary>
        private static void BuildCsr(Graph graph)
        {
            int[] degree = new int[graph.Nodes];
            for (int i = 0; i < graph.LinkA.Length; i++)
            {
                degree[graph.LinkA[i]]++;
                degree[graph.LinkB[i]]++;
            }

            graph.Start = new int[graph.Nodes + 1];
            for (int i = 0; i < graph.Nodes; i++)
            {
                graph.Start[i + 1] = graph.Start[i] + degree[i];
            }

            graph.Neighbour = new int[graph.LinkA.Length * 2];
            graph.NeighbourConductance = new float[graph.LinkA.Length * 2];

            int[] cursor = new int[graph.Nodes];
            for (int i = 0; i < graph.LinkA.Length; i++)
            {
                int a = graph.LinkA[i];
                int b = graph.LinkB[i];
                float c = graph.Conductance[i];

                int atA = graph.Start[a] + cursor[a]++;
                graph.Neighbour[atA] = b;
                graph.NeighbourConductance[atA] = c;

                int atB = graph.Start[b] + cursor[b]++;
                graph.Neighbour[atB] = a;
                graph.NeighbourConductance[atB] = c;
            }
        }

/// <summary>MortonOrder operation.</summary>
        private static int[] MortonOrder(Graph graph)
        {
            Vector3I min = graph.Position[0];
            for (int i = 1; i < graph.Position.Length; i++)
            {
                min = Vector3I.Min(min, graph.Position[i]);
            }

            long[] codes = new long[graph.Nodes];
            int[] order = new int[graph.Nodes];
            for (int i = 0; i < graph.Nodes; i++)
            {
                Vector3I at = graph.Position[i] - min;
/// <summary>Morton operation.</summary>
                codes[i] = Morton(at.X, at.Y, at.Z);
                order[i] = i;
            }

            Array.Sort(codes, order);
            return order;
        }

/// <summary>Morton operation.</summary>
        private static long Morton(int x, int y, int z)
        {
            long code = 0;
            for (int bit = 0; bit < 21; bit++)
            {
                code |= (long)((x >> bit) & 1) << (3 * bit);
                code |= (long)((y >> bit) & 1) << (3 * bit + 1);
                code |= (long)((z >> bit) & 1) << (3 * bit + 2);
            }
            return code;
        }

/// <summary>Reorder operation.</summary>
        private static Graph Reorder(Graph graph, int[] order)
        {
            int[] position = new int[graph.Nodes];
            for (int i = 0; i < order.Length; i++)
            {
                position[order[i]] = i;
            }

/// <summary>Graph operation.</summary>
            Graph reordered = new Graph();
            reordered.Nodes = graph.Nodes;
            reordered.Temperature = new float[graph.Nodes];
            for (int i = 0; i < graph.Nodes; i++)
            {
                reordered.Temperature[position[i]] = graph.Temperature[i];
            }

            reordered.LinkA = new int[graph.LinkA.Length];
            reordered.LinkB = new int[graph.LinkB.Length];
            reordered.Conductance = new float[graph.Conductance.Length];
            for (int i = 0; i < graph.LinkA.Length; i++)
            {
                reordered.LinkA[i] = position[graph.LinkA[i]];
                reordered.LinkB[i] = position[graph.LinkB[i]];
                reordered.Conductance[i] = graph.Conductance[i];
            }

            BuildCsr(reordered);
            return reordered;
        }


/// <summary>Scatter operation.</summary>
        private static void Scatter(Graph graph, float[] watts)
        {
            Array.Clear(watts, 0, watts.Length);

            int[] a = graph.LinkA;
            int[] b = graph.LinkB;
            float[] c = graph.Conductance;
            float[] t = graph.Temperature;

            for (int i = 0; i < a.Length; i++)
            {
                float flow = c[i] * (t[b[i]] - t[a[i]]);
                watts[a[i]] += flow;
                watts[b[i]] -= flow;
            }
        }

/// <summary>GatherRange operation.</summary>
        private static void GatherRange(Graph graph, float[] watts, int from, int toExclusive)
        {
            int[] start = graph.Start;
            int[] neighbour = graph.Neighbour;
            float[] conductance = graph.NeighbourConductance;
            float[] t = graph.Temperature;

            for (int i = from; i < toExclusive; i++)
            {
                float here = t[i];
                float sum = 0f;
                int end = start[i + 1];
                for (int at = start[i]; at < end; at++)
                {
                    sum += conductance[at] * (t[neighbour[at]] - here);
                }
                watts[i] = sum;
            }
        }

/// <summary>GatherParallel operation.</summary>
        private static void GatherParallel(Graph graph, float[] watts, int threads)
        {
            int chunk = (graph.Nodes + threads - 1) / threads;
            Parallel.For(0, threads,
                new ParallelOptions { MaxDegreeOfParallelism = threads },
                worker =>
                {
                    int from = worker * chunk;
                    int to = Math.Min(graph.Nodes, from + chunk);
                    if (from < to) GatherRange(graph, watts, from, to);
                });
        }


/// <summary>Measure operation.</summary>
        private static Row Measure(string variant, int threads, Graph graph, float[] reference,
            Action<float[]> kernel)
        {
            float[] watts = new float[graph.Nodes];
            kernel(watts);

/// <summary>Row operation.</summary>
            Row row = new Row();
            row.Variant = variant;
            row.Threads = threads;
/// <summary>WorstError operation.</summary>
            row.WorstError = WorstError(reference, watts);

            StageLab.Row timing = new StageLab.Row();
            timing.Stage = variant + (threads > 1 ? "x" + threads : "");
            timing.Blocks = graph.Nodes;
            timing.BestMs = double.MaxValue;
            timing.WorkUnit = "link visits";

            long work = variant.EndsWith("gather", StringComparison.Ordinal)
                ? graph.LinkA.Length * 2L
                : graph.LinkA.Length;

            for (int r = 0; !StageLab.Settled(timing); r++)
            {
                long allocated = r == 1 ? StageLab.Allocated() : 0;
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                kernel(watts);
                watch.Stop();
                if (r == 1) timing.AllocatedBytes = StageLab.Allocated() - allocated;
                StageLab.Take(timing, watch.Elapsed.TotalMilliseconds);
                StageLab.Work(timing, work, r);
            }

            StageLab.Summarise(timing);
            row.Timing = timing;
            return row;
        }

/// <summary>WorstError operation.</summary>
        public static double WorstError(float[] reference, float[] candidate)
        {
            double worst = 0d;
            for (int i = 0; i < reference.Length; i++)
            {
                double scale = Math.Max(1d, Math.Abs(reference[i]));
                double error = Math.Abs(reference[i] - candidate[i]) / scale;
                if (error > worst) worst = error;
            }
            return worst;
        }

/// <summary>Table operation.</summary>
        public static string Table(IList<Row> rows)
        {
/// <summary>StringBuilder operation.</summary>
            StringBuilder text = new StringBuilder();

            double serialScatter = 0d;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Variant == "scatter" && rows[i].Threads == 1)
                {
                    serialScatter = rows[i].Timing.BestMs;
                }
            }

            text.AppendLine("  kernel        threads      nodes       best ms    median ms  repeats  stopped     ns/visit   vs scatter    worst err");
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                double speedup = row.Timing.BestMs <= 0d ? 0d : serialScatter / row.Timing.BestMs;
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-12} {1,8:n0}  {2,9:n0}  {3,12:n3}  {4,11:n3}  {5,7:n0}  {6,-9}  {7,9:n2}  {8,10:n2}x  {9,11:e1}",
                    row.Timing.Stage, row.Threads, row.Timing.Blocks, row.Timing.BestMs,
                    row.Timing.MedianMs, row.Timing.Repeats, row.Timing.Stop,
                    row.Timing.NsPerWork, speedup, row.WorstError));
            }
            return text.ToString();
        }

/// <summary>Csv operation.</summary>
        public static string Csv(IList<Row> rows)
        {
/// <summary>StringBuilder operation.</summary>
            StringBuilder text = new StringBuilder();
            text.AppendLine("kernel,threads,nodes,best_ms,median_ms,repeats,stopped,work,ns_per_visit,worst_error,taken_utc,host");
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                text.AppendLine(string.Join(",",
                    row.Timing.Stage,
                    row.Threads.ToString(CultureInfo.InvariantCulture),
                    row.Timing.Blocks.ToString(CultureInfo.InvariantCulture),
                    row.Timing.BestMs.ToString("r", CultureInfo.InvariantCulture),
                    row.Timing.MedianMs.ToString("r", CultureInfo.InvariantCulture),
                    row.Timing.Repeats.ToString(CultureInfo.InvariantCulture),
                    row.Timing.Stop,
                    row.Timing.Work.ToString(CultureInfo.InvariantCulture),
                    row.Timing.NsPerWork.ToString("r", CultureInfo.InvariantCulture),
                    row.WorstError.ToString("r", CultureInfo.InvariantCulture),
                    StageLab.TakenUtc,
                    StageLab.Host));
            }
            return text.ToString();
        }
    }
}
