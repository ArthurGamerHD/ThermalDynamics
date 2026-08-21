using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Structural measurements of a built simulation.
    ///
    /// These are the numbers that separate one grid shape from another, and each one drives a
    /// specific cost in the simulation:
    ///
    /// <list type="bullet">
    /// <item><see cref="BoundingFillRatio"/> — the room mapper floods the bounding volume, so a
    /// sparse shape pays many times more per block than a solid one.</item>
    /// <item><see cref="ExposedFraction"/> — how much of the grid the environment pass does real
    /// work for, and how much of it responds to a change in ambient conditions.</item>
    /// <item><see cref="LinksPerNode"/> — the conduction pass cost per block.</item>
    /// <item><see cref="Diameter"/> — conduction hops across the grid, which sets how long a
    /// transient takes to settle and therefore how long any activity-based scheduler must keep
    /// working.</item>
    /// </list>
    /// </summary>
    public class GridMetrics
    {
        public int NodeCount;
        public int LinkCount;
        public long BoundingVolume;
        public int ExposedNodeCount;
        public int IsolatedNodeCount;
        public int ComponentCount;
        public int Diameter;

        /// <summary>Occupied cells as a fraction of the bounding box. 1.0 for a solid box.</summary>
        public float BoundingFillRatio
        {
            get { return BoundingVolume == 0 ? 0f : NodeCount / (float)BoundingVolume; }
        }

        /// <summary>Fraction of nodes with at least one exposed face.</summary>
        public float ExposedFraction
        {
            get { return NodeCount == 0 ? 0f : ExposedNodeCount / (float)NodeCount; }
        }

        public float LinksPerNode
        {
            get { return NodeCount == 0 ? 0f : LinkCount / (float)NodeCount; }
        }

        public static GridMetrics Measure(ThermalSimulation simulation)
        {
            if (simulation == null) throw new ArgumentNullException("simulation");

            GridMetrics m = new GridMetrics();
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            IList<ThermalLink> links = simulation.Solver.Links;

            m.NodeCount = nodes.Count;
            m.LinkCount = links.Count;

            if (m.NodeCount == 0) return m;

            Vector3I extents = (simulation.Grid.Max - simulation.Grid.Min) + Vector3I.One;
            m.BoundingVolume = (long)extents.X * extents.Y * extents.Z;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].TotalExposedFaces > 0) m.ExposedNodeCount++;
            }

            List<int>[] adjacency = BuildAdjacency(nodes.Count, links);

            for (int i = 0; i < adjacency.Length; i++)
            {
                if (adjacency[i].Count == 0) m.IsolatedNodeCount++;
            }

            m.ComponentCount = CountComponents(adjacency);
            m.Diameter = ApproximateDiameter(adjacency);
            return m;
        }

        private static List<int>[] BuildAdjacency(int nodeCount, IList<ThermalLink> links)
        {
            List<int>[] adjacency = new List<int>[nodeCount];
            for (int i = 0; i < nodeCount; i++) adjacency[i] = new List<int>();

            for (int i = 0; i < links.Count; i++)
            {
                ThermalLink link = links[i];
                adjacency[link.NodeA].Add(link.NodeB);
                adjacency[link.NodeB].Add(link.NodeA);
            }
            return adjacency;
        }

        private static int CountComponents(List<int>[] adjacency)
        {
            bool[] seen = new bool[adjacency.Length];
            int components = 0;

            for (int i = 0; i < adjacency.Length; i++)
            {
                if (seen[i]) continue;
                components++;
                Flood(adjacency, i, seen);
            }
            return components;
        }

        private static void Flood(List<int>[] adjacency, int start, bool[] seen)
        {
            Queue<int> queue = new Queue<int>();
            seen[start] = true;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                List<int> neighbours = adjacency[current];
                for (int i = 0; i < neighbours.Count; i++)
                {
                    if (seen[neighbours[i]]) continue;
                    seen[neighbours[i]] = true;
                    queue.Enqueue(neighbours[i]);
                }
            }
        }

        /// <summary>
        /// Double-sweep estimate: BFS from an arbitrary node to find a far one, then BFS from
        /// there. Exact for trees and a tight lower bound in practice, at O(n) instead of the
        /// O(n^2) an exact diameter would cost.
        /// </summary>
        private static int ApproximateDiameter(List<int>[] adjacency)
        {
            int best = 0;
            bool[] seen = new bool[adjacency.Length];

            for (int start = 0; start < adjacency.Length; start++)
            {
                if (seen[start]) continue;

                int far = FurthestFrom(adjacency, start, seen, true);
                int distance = 0;
                FurthestFrom(adjacency, far, null, false, out distance);
                if (distance > best) best = distance;
            }
            return best;
        }

        private static int FurthestFrom(List<int>[] adjacency, int start, bool[] mark, bool recordMark)
        {
            int distance;
            int furthest = FurthestFrom(adjacency, start, mark, recordMark, out distance);
            return furthest;
        }

        private static int FurthestFrom(
            List<int>[] adjacency, int start, bool[] mark, bool recordMark, out int furthestDistance)
        {
            int[] depth = new int[adjacency.Length];
            for (int i = 0; i < depth.Length; i++) depth[i] = -1;

            Queue<int> queue = new Queue<int>();
            depth[start] = 0;
            queue.Enqueue(start);
            if (recordMark && mark != null) mark[start] = true;

            int furthest = start;
            furthestDistance = 0;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                List<int> neighbours = adjacency[current];

                for (int i = 0; i < neighbours.Count; i++)
                {
                    int next = neighbours[i];
                    if (depth[next] >= 0) continue;

                    depth[next] = depth[current] + 1;
                    if (recordMark && mark != null) mark[next] = true;

                    if (depth[next] > furthestDistance)
                    {
                        furthestDistance = depth[next];
                        furthest = next;
                    }
                    queue.Enqueue(next);
                }
            }
            return furthest;
        }

        public override string ToString()
        {
            return string.Format(
                "{0} nodes, {1} links ({2:F2}/node), bbox {3} ({4:P1} full), {5:P1} exposed, diameter {6}, {7} component(s)",
                NodeCount, LinkCount, LinksPerNode, BoundingVolume, BoundingFillRatio,
                ExposedFraction, Diameter, ComponentCount);
        }

        /// <summary>A fixed-width row for tabular output, matching <see cref="Header"/>.</summary>
        public string ToRow(string name)
        {
            return string.Format(
                "{0,-12} {1,7} {2,8} {3,6:F2} {4,9:P0} {5,9:P0} {6,7} {7,6}",
                name, NodeCount, LinkCount, LinksPerNode, BoundingFillRatio,
                ExposedFraction, Diameter, ComponentCount);
        }

        public static string Header()
        {
            return string.Format(
                "{0,-12} {1,7} {2,8} {3,6} {4,9} {5,9} {6,7} {7,6}",
                "shape", "nodes", "links", "l/n", "fill", "exposed", "diam", "parts");
        }
    }
}
