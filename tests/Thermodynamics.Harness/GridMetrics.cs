using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public class GridMetrics
    {
        public int NodeCount;
        public int LinkCount;
        public long BoundingVolume;
        public int ExposedNodeCount;
        public int IsolatedNodeCount;
        public int ComponentCount;
        public int Diameter;

        public float BoundingFillRatio
        {
            get { return BoundingVolume == 0 ? 0f : NodeCount / (float)BoundingVolume; }
        }

        public float ExposedFraction
        {
            get { return NodeCount == 0 ? 0f : ExposedNodeCount / (float)NodeCount; }
        }

        public float LinksPerNode
        {
            get { return NodeCount == 0 ? 0f : LinkCount / (float)NodeCount; }
        }

/// <summary>Measure operation.</summary>
        public static GridMetrics Measure(ThermalSimulation simulation)
        {
            if (simulation == null) throw new ArgumentNullException("simulation");

/// <summary>GridMetrics operation.</summary>
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

/// <summary>Builds the method table.</summary>
            List<int>[] adjacency = BuildAdjacency(nodes.Count, links);

            for (int i = 0; i < adjacency.Length; i++)
            {
                if (adjacency[i].Count == 0) m.IsolatedNodeCount++;
            }

/// <summary>CountComponents operation.</summary>
            m.ComponentCount = CountComponents(adjacency);
/// <summary>ApproximateDiameter operation.</summary>
            m.Diameter = ApproximateDiameter(adjacency);
            return m;
        }

/// <summary>Builds the API method table.</summary>
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

/// <summary>CountComponents operation.</summary>
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

/// <summary>Flood operation.</summary>
        private static void Flood(List<int>[] adjacency, int start, bool[] seen)
        {
/// <summary>Queue operation.</summary>
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

/// <summary>ApproximateDiameter operation.</summary>
        private static int ApproximateDiameter(List<int>[] adjacency)
        {
            int best = 0;
            bool[] seen = new bool[adjacency.Length];

            for (int start = 0; start < adjacency.Length; start++)
            {
                if (seen[start]) continue;

/// <summary>FurthestFrom operation.</summary>
                int far = FurthestFrom(adjacency, start, seen, true);
                int distance = 0;
                FurthestFrom(adjacency, far, null, false, out distance);
                if (distance > best) best = distance;
            }
            return best;
        }

/// <summary>FurthestFrom operation.</summary>
        private static int FurthestFrom(List<int>[] adjacency, int start, bool[] mark, bool recordMark)
        {
            int distance;
/// <summary>FurthestFrom operation.</summary>
            int furthest = FurthestFrom(adjacency, start, mark, recordMark, out distance);
            return furthest;
        }

/// <summary>FurthestFrom operation.</summary>
        private static int FurthestFrom(
            List<int>[] adjacency, int start, bool[] mark, bool recordMark, out int furthestDistance)
        {
            int[] depth = new int[adjacency.Length];
            for (int i = 0; i < depth.Length; i++) depth[i] = -1;

/// <summary>Queue operation.</summary>
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

/// <summary>ToString operation.</summary>
        public override string ToString()
        {
            return string.Format(
/// <summary>links operation.</summary>
                "{0} nodes, {1} links ({2:F2}/node), bbox {3} ({4:P1} full), {5:P1} exposed, diameter {6}, {7} component(s)",
                NodeCount, LinkCount, LinksPerNode, BoundingVolume, BoundingFillRatio,
                ExposedFraction, Diameter, ComponentCount);
        }
    }
}
