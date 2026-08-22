using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Where a hull puts its heat sources in space, and how far that heat has to travel to leave —
    /// the arrangement no total can express, since two ships with identical power and exposure behave
    /// differently depending on where the generators sit.
    ///
    /// <para>
    /// **Depth is measured in conduction hops, not metres**, over the solver's own link graph: cell
    /// adjacency is the wrong graph, because two blocks touching in space are not necessarily
    /// conducting. Distances are per grid, since a coordinate in one subgrid means nothing in another.
    /// See balance.md, What actually causes a hot spot.
    /// </para>
    /// </summary>
    public static class HeatGeometry
    {
        /// <summary>Metres per cell, for each grid size.</summary>
        private const float LargeCell = 2.5f;
        private const float SmallCell = 0.5f;

        /// <summary>
        /// Half-width of the neighbourhood a local density is summed over, in cells.
        ///
        /// Two cells each way is a 5x5x5 box — 12.5 m on a large grid, which is about the distance
        /// over which conduction still meaningfully shares heat within one step at the shipped
        /// settings. Larger boxes wash the clumping out; smaller ones just report the block itself.
        /// </summary>
        private const int Window = 2;

        /// <summary>
        /// Nearest-neighbour work is quadratic, so a hull with more heat sources than this is
        /// sampled rather than measured exactly. The sample is a stride through the source list,
        /// which is in build order, so it spreads across the hull rather than taking one end.
        /// </summary>
        private const int NearestNeighbourCap = 1200;

        public class Result
        {
            /// <summary>Blocks generating more than a watt at the load that was applied.</summary>
            public int Sources;

            /// <summary>Total waste watts over total exposed area, W/m2 — the whole-hull figure.</summary>
            public double WattsPerSquareMetre;

            /// <summary>Deepest node on the hull, in conduction hops from anything that radiates.</summary>
            public int MaxDepth;

            /// <summary>Watt-weighted mean depth of the heat sources. The burial number that matters.</summary>
            public double HeatDepthMean;

            /// <summary>Depth of the most deeply buried source.</summary>
            public int HeatDepthMax;

            /// <summary>
            /// Clark-Evans nearest-neighbour index over the heat sources. Below 1 is clumped,
            /// about 1 is indistinguishable from random placement, above 1 is dispersed.
            /// </summary>
            public double Clumping;

            /// <summary>Gini coefficient of watts across every node: 0 spread evenly, 1 all in one block.</summary>
            public double Gini;

            /// <summary>Most watts found inside one 5x5x5 neighbourhood.</summary>
            public double LocalWattsMax;

            /// <summary>
            /// Worst local watts per square metre of exposed area in the same neighbourhood.
            ///
            /// This is the one that should predict a hot spot: it is the local heat against the
            /// local ability to shed it, which is what a block actually experiences. A hull can
            /// have a comfortable whole-ship W/m2 and still contain a bay where it is a hundred
            /// times worse.
            /// </summary>
            public double LocalWattsPerAreaMax;

            /// <summary>
            /// Root-mean-square distance of the sources from their watt-weighted centroid, metres.
            /// Small means the heat is made in one place.
            /// </summary>
            public double SpreadMetres;

            /// <summary>Conductance away from the single hottest source, W/K.</summary>
            public double HottestSourceConductance;
        }

        /// <summary>
        /// Measures one assembly as it currently stands. The caller applies a load first: every
        /// figure here reads <c>HeatGenerationWatts</c>, which is zero until something sets it.
        /// </summary>
        public static Result Measure(ShipAssembly assembly, bool large)
        {
            Result result = new Result();
            float cell = large ? LargeCell : SmallCell;

            double totalWatts = 0d;
            double totalArea = 0d;
            double weightedDepth = 0d;
            double weightedSpread = 0d;

            List<double> allWatts = new List<double>();

            for (int g = 0; g < assembly.Simulations.Count; g++)
            {
                ThermalSolver solver = assembly.Simulations[g].Solver;
                IList<ThermalNode> nodes = solver.Nodes;
                if (nodes.Count == 0) continue;

                int[] depth = Depths(solver);

                // ---- per-node sums, and the cell maps the local window needs -------------------
                Dictionary<Vector3I, float> wattsAt = new Dictionary<Vector3I, float>();
                Dictionary<Vector3I, float> areaAt = new Dictionary<Vector3I, float>();
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

                // ---- spread about the watt-weighted centroid ------------------------------------
                Vector3D centroid = Vector3D.Zero;
                double weight = 0d;
                for (int s = 0; s < sources.Count; s++)
                {
                    ThermalNode node = nodes[sources[s]];
                    double w = node.HeatGenerationWatts;
                    Vector3I p = node.Block.Position;
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
                        Vector3D d = new Vector3D(p.X, p.Y, p.Z) - centroid;
                        sum += node.HeatGenerationWatts * d.LengthSquared();
                    }
                    weightedSpread += weight * Math.Sqrt(sum / weight) * cell;
                }

                // ---- local density, and the hottest source's way out ---------------------------
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
                        Vector3I probe = new Vector3I(at.X + x, at.Y + y, at.Z + z);
                        float w, a;
                        if (wattsAt.TryGetValue(probe, out w)) localWatts += w;
                        if (areaAt.TryGetValue(probe, out a)) localArea += a;
                    }

                    if (localWatts > result.LocalWattsMax) result.LocalWattsMax = localWatts;

                    // An enclosed neighbourhood has no area at all, and dividing by zero would
                    // report infinity for the very case that matters most. Charge it the area of
                    // one cell face instead, which is the least it could possibly shed through.
                    double area = localArea > 0.01d ? localArea : cell * cell;
                    double density = localWatts / area;
                    if (density > result.LocalWattsPerAreaMax) result.LocalWattsPerAreaMax = density;

                    if (node.HeatGenerationWatts > hottest)
                    {
                        hottest = node.HeatGenerationWatts;
                        result.HottestSourceConductance = solver.NodeConductanceTotal(i);
                    }
                }

                // ---- clumping ------------------------------------------------------------------
                double clumping = ClarkEvans(nodes, sources, nodes.Count, cell);
                if (clumping > 0d)
                {
                    // Grids are combined by their source count, so a ship's figure is dominated by
                    // the grid that carries most of the machinery rather than by a small subgrid.
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
            result.Gini = Gini(allWatts);
            return result;
        }

        /// <summary>
        /// Conduction hops from the nearest radiating node, for every node on one grid.
        ///
        /// A breadth-first walk out from every exposed node at once, over the solver's links. A
        /// node no walk reaches — sealed, with neither a face nor a link — is given the hop count
        /// one past the deepest that was reached, so it sorts as the worst case rather than as
        /// depth zero.
        /// </summary>
        private static int[] Depths(ThermalSolver solver)
        {
            IList<ThermalNode> nodes = solver.Nodes;
            int count = nodes.Count;
            int[] depth = new int[count];
            for (int i = 0; i < count; i++) depth[i] = -1;

            // Adjacency, built once from the link list.
            List<int>[] neighbours = new List<int>[count];
            IList<ThermalLink> links = solver.Links;
            for (int l = 0; l < links.Count; l++)
            {
                int a = links[l].NodeA, b = links[l].NodeB;
                if (a < 0 || a >= count || b < 0 || b >= count) continue;
                (neighbours[a] ?? (neighbours[a] = new List<int>())).Add(b);
                (neighbours[b] ?? (neighbours[b] = new List<int>())).Add(a);
            }

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

        /// <summary>
        /// The Clark-Evans nearest-neighbour index of the heat sources.
        ///
        /// Observed mean nearest-neighbour distance over what that distance would be if the same
        /// number of sources were scattered at random through the same occupied volume. Below 1
        /// they are clumped, above 1 they are more evenly spread than chance.
        /// </summary>
        private static double ClarkEvans(IList<ThermalNode> nodes, List<int> sources,
            int occupiedCells, float cell)
        {
            int n = sources.Count;
            if (n < 3 || occupiedCells <= 0) return 0d;

            // Quadratic in the source count, so a very heavily populated hull is strided rather
            // than measured exactly. Build order spreads the sample across the hull.
            List<int> sample = sources;
            if (n > NearestNeighbourCap)
            {
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

            // Expected nearest-neighbour distance for a random arrangement in three dimensions,
            // over the volume the hull actually occupies rather than its bounding box — a ship is
            // mostly not a solid cube and the box would understate the density badly.
            double volume = (double)occupiedCells * cell * cell * cell;
            double expected = 0.554d * Math.Pow(volume / n, 1d / 3d);
            return expected > 0d ? observed / expected : 0d;
        }

        /// <summary>How unequally the watts are shared across the hull. 0 is even, 1 is all in one block.</summary>
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
