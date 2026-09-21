using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class HeatGeometry
    {
        private const float LargeCell = 2.5f;
        private const float SmallCell = 0.5f;

        private const int Window = 2;

        private const int NearestNeighbourCap = 1200;

        public class Result
        {
            public int Sources;

            public double WattsPerSquareMetre;

            public int MaxDepth;

            public double HeatDepthMean;

            public int HeatDepthMax;

            public double Clumping;

            public double Gini;

            public double LocalWattsMax;

            public double LocalWattsPerAreaMax;

            public double SpreadMetres;

            public double HottestSourceConductance;
        }

/// <summary>Measure operation.</summary>
        public static Result Measure(ShipAssembly assembly, bool large)
        {
/// <summary>Result operation.</summary>
            Result result = new Result();
            float cell = large ? LargeCell : SmallCell;

            double totalWatts = 0d;
            double totalArea = 0d;
            double weightedDepth = 0d;
            double weightedSpread = 0d;

/// <summary>List operation.</summary>
            List<double> allWatts = new List<double>();

            for (int g = 0; g < assembly.Simulations.Count; g++)
            {
                ThermalSolver solver = assembly.Simulations[g].Solver;
                IList<ThermalNode> nodes = solver.Nodes;
                if (nodes.Count == 0) continue;

/// <summary>Depths operation.</summary>
                int[] depth = Depths(solver);

                Dictionary<Vector3I, float> wattsAt = new Dictionary<Vector3I, float>();
                Dictionary<Vector3I, float> areaAt = new Dictionary<Vector3I, float>();
/// <summary>List operation.</summary>
                List<int> sources = new List<int>();

                for (int i = 0; i < nodes.Count; i++)
                {
                    ThermalNode node = nodes[i];
                    float watts = node.HeatGenerationWatts;

                    totalArea += node.ExposedArea;
                    allWatts.Add(watts);

                    if (depth[i] > result.MaxDepth) result.MaxDepth = depth[i];

                    Vector3I at = node.Block.Position;
                    float seen;
                    areaAt[at] = areaAt.TryGetValue(at, out seen) ? seen + node.ExposedArea : node.ExposedArea;

                    if (watts <= 1f) continue;

                    sources.Add(i);
                    totalWatts += watts;
                    weightedDepth += watts * depth[i];
                    if (depth[i] > result.HeatDepthMax) result.HeatDepthMax = depth[i];

                    wattsAt[at] = wattsAt.TryGetValue(at, out seen) ? seen + watts : watts;
                }

                if (sources.Count == 0) continue;
                result.Sources += sources.Count;

                Vector3D centroid = Vector3D.Zero;
                double weight = 0d;
                for (int s = 0; s < sources.Count; s++)
                {
                    ThermalNode node = nodes[sources[s]];
                    double w = node.HeatGenerationWatts;
                    Vector3I p = node.Block.Position;
/// <summary>Vector3D operation.</summary>
                    centroid += new Vector3D(p.X, p.Y, p.Z) * w;
                    weight += w;
                }
                if (weight > 0d)
                {
                    centroid /= weight;
                    double sum = 0d;
                    for (int s = 0; s < sources.Count; s++)
                    {
                        ThermalNode node = nodes[sources[s]];
                        Vector3I p = node.Block.Position;
/// <summary>Vector3D operation.</summary>
                        Vector3D d = new Vector3D(p.X, p.Y, p.Z) - centroid;
                        sum += node.HeatGenerationWatts * d.LengthSquared();
                    }
                    weightedSpread += weight * Math.Sqrt(sum / weight) * cell;
                }

                double hottest = 0d;
                for (int s = 0; s < sources.Count; s++)
                {
                    int i = sources[s];
                    ThermalNode node = nodes[i];
                    Vector3I at = node.Block.Position;

                    double localWatts = 0d;
                    double localArea = 0d;
                    for (int x = -Window; x <= Window; x++)
                    for (int y = -Window; y <= Window; y++)
                    for (int z = -Window; z <= Window; z++)
                    {
/// <summary>Vector3I operation.</summary>
                        Vector3I probe = new Vector3I(at.X + x, at.Y + y, at.Z + z);
                        float w, a;
                        if (wattsAt.TryGetValue(probe, out w)) localWatts += w;
                        if (areaAt.TryGetValue(probe, out a)) localArea += a;
                    }

                    if (localWatts > result.LocalWattsMax) result.LocalWattsMax = localWatts;

                    double area = localArea > 0.01d ? localArea : cell * cell;
                    double density = localWatts / area;
                    if (density > result.LocalWattsPerAreaMax) result.LocalWattsPerAreaMax = density;

                    if (node.HeatGenerationWatts > hottest)
                    {
                        hottest = node.HeatGenerationWatts;
                        result.HottestSourceConductance = solver.NodeConductanceTotal(i);
                    }
                }

/// <summary>ClarkEvans operation.</summary>
                double clumping = ClarkEvans(nodes, sources, nodes.Count, cell);
                if (clumping > 0d)
                {
                    result.Clumping += clumping * sources.Count;
                }
            }

            if (result.Sources > 0)
            {
                result.Clumping /= result.Sources;
            }

            result.WattsPerSquareMetre = totalArea > 0.01d ? totalWatts / totalArea : 0d;
            result.HeatDepthMean = totalWatts > 0d ? weightedDepth / totalWatts : 0d;
            result.SpreadMetres = totalWatts > 0d ? weightedSpread / totalWatts : 0d;
/// <summary>Gini operation.</summary>
            result.Gini = Gini(allWatts);
            return result;
        }

/// <summary>Depths operation.</summary>
        private static int[] Depths(ThermalSolver solver)
        {
            IList<ThermalNode> nodes = solver.Nodes;
            int count = nodes.Count;
            int[] depth = new int[count];
            for (int i = 0; i < count; i++) depth[i] = -1;

            List<int>[] neighbours = new List<int>[count];
            IList<ThermalLink> links = solver.Links;
            for (int l = 0; l < links.Count; l++)
            {
                int a = links[l].NodeA, b = links[l].NodeB;
                if (a < 0 || a >= count || b < 0 || b >= count) continue;
                (neighbours[a] ?? (neighbours[a] = new List<int>())).Add(b);
                (neighbours[b] ?? (neighbours[b] = new List<int>())).Add(a);
            }

/// <summary>Queue operation.</summary>
            Queue<int> queue = new Queue<int>();
            for (int i = 0; i < count; i++)
            {
                if (nodes[i].ExposedArea <= 0f) continue;
                depth[i] = 0;
                queue.Enqueue(i);
            }

            int deepest = 0;
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                List<int> next = neighbours[at];
                if (next == null) continue;

                for (int n = 0; n < next.Count; n++)
                {
                    int to = next[n];
                    if (depth[to] >= 0) continue;
                    depth[to] = depth[at] + 1;
                    if (depth[to] > deepest) deepest = depth[to];
                    queue.Enqueue(to);
                }
            }

            for (int i = 0; i < count; i++)
            {
                if (depth[i] < 0) depth[i] = deepest + 1;
            }
            return depth;
        }

/// <summary>ClarkEvans operation.</summary>
        private static double ClarkEvans(IList<ThermalNode> nodes, List<int> sources,
            int occupiedCells, float cell)
        {
            int n = sources.Count;
            if (n < 3 || occupiedCells <= 0) return 0d;

            List<int> sample = sources;
            if (n > NearestNeighbourCap)
            {
/// <summary>List operation.</summary>
                sample = new List<int>(NearestNeighbourCap);
                int stride = n / NearestNeighbourCap;
                for (int i = 0; i < n && sample.Count < NearestNeighbourCap; i += stride) sample.Add(sources[i]);
                n = sample.Count;
            }

            double total = 0d;
            for (int a = 0; a < n; a++)
            {
                Vector3I pa = nodes[sample[a]].Block.Position;
                double best = double.MaxValue;

                for (int b = 0; b < n; b++)
                {
                    if (b == a) continue;
                    Vector3I pb = nodes[sample[b]].Block.Position;
                    double dx = pa.X - pb.X, dy = pa.Y - pb.Y, dz = pa.Z - pb.Z;
                    double d = dx*dx + dy*dy + dz*dz;
                    if (d < best) best = d;
                }

                if (best < double.MaxValue) total += Math.Sqrt(best) * cell;
            }

            double observed = total / n;

            double volume = (double)occupiedCells * cell * cell * cell;
            double expected = 0.554d * Math.Pow(volume / n, 1d / 3d);
            return expected > 0d ? observed / expected : 0d;
        }

/// <summary>Gini operation.</summary>
        private static double Gini(List<double> values)
        {
            if (values.Count < 2) return 0d;

            values.Sort();
            double sum = 0d, weighted = 0d;
            for (int i = 0; i < values.Count; i++)
            {
                sum += values[i];
                weighted += (i + 1) * values[i];
            }
            if (sum <= 0d) return 0d;

            int n = values.Count;
            return (2d * weighted) / (n * sum) - (n + 1d) / n;
        }
    }
}
