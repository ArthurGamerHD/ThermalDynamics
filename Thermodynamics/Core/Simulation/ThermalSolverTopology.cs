using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    public partial class ThermalSolver
    {
        private int[] nodeFirstLink = new int[0];

        private int[] linkNextA = new int[0];
        private int[] linkNextB = new int[0];

/// <summary>List operation.</summary>
        private readonly List<int> doomedLinks = new List<int>();

/// <summary>EnsureNodeChainCapacity operation.</summary>
        private void EnsureNodeChainCapacity(int nodeCount)
        {
            if (nodeFirstLink.Length >= nodeCount) return;

            int size = Math.Max(16, nodeCount * 2);
            int previous = nodeFirstLink.Length;
            Array.Resize(ref nodeFirstLink, size);
            for (int i = previous; i < size; i++) nodeFirstLink[i] = -1;
        }

/// <summary>EnsureLinkChainCapacity operation.</summary>
        private void EnsureLinkChainCapacity(int linkCount)
        {
            if (linkNextA.Length >= linkCount) return;

            int size = Math.Max(16, linkCount * 2);
            Array.Resize(ref linkNextA, size);
            Array.Resize(ref linkNextB, size);
        }

/// <summary>ResetLinkChains operation.</summary>
        private void ResetLinkChains()
        {
            EnsureNodeChainCapacity(nodes.Count);
            for (int i = 0; i < nodeFirstLink.Length; i++) nodeFirstLink[i] = -1;
        }

/// <summary>NextLink operation.</summary>
        private int NextLink(int link, int node)
        {
            return links[link].NodeA == node ? linkNextA[link] : linkNextB[link];
        }

/// <summary>Sets the nextlink.</summary>
        private void SetNextLink(int link, int node, int next)
        {
            if (links[link].NodeA == node) linkNextA[link] = next;
            else linkNextB[link] = next;
        }

/// <summary>ChainLink operation.</summary>
        private void ChainLink(int link)
        {
            ThermalLink entry = links[link];

            EnsureNodeChainCapacity(nodes.Count);
            EnsureLinkChainCapacity(links.Count);

            linkNextA[link] = nodeFirstLink[entry.NodeA];
            nodeFirstLink[entry.NodeA] = link;

            linkNextB[link] = nodeFirstLink[entry.NodeB];
            nodeFirstLink[entry.NodeB] = link;
        }

/// <summary>UnchainFrom operation.</summary>
        private void UnchainFrom(int node, int link)
        {
            if (node < 0 || node >= nodeFirstLink.Length) return;

            int current = nodeFirstLink[node];
            if (current == link)
            {
/// <summary>NextLink operation.</summary>
                nodeFirstLink[node] = NextLink(link, node);
                return;
            }

            while (current != -1)
            {
/// <summary>NextLink operation.</summary>
                int next = NextLink(current, node);
                if (next == link)
                {
                    SetNextLink(current, node, NextLink(link, node));
                    return;
                }
                current = next;
            }
        }

/// <summary>RetargetLink operation.</summary>
        private void RetargetLink(int node, int from, int to)
        {
            if (node < 0 || node >= nodeFirstLink.Length) return;

            if (nodeFirstLink[node] == from)
            {
                nodeFirstLink[node] = to;
                return;
            }

            int current = nodeFirstLink[node];
            while (current != -1)
            {
/// <summary>NextLink operation.</summary>
                int next = NextLink(current, node);
                if (next == from)
                {
                    SetNextLink(current, node, to);
                    return;
                }
                current = next;
            }
        }

/// <summary>Removes the linkat.</summary>
        private void RemoveLinkAt(int link)
        {
            ThermalLink entry = links[link];

            nodeConductanceTotal[entry.NodeA] -= entry.Conductance;
            nodeConductanceTotal[entry.NodeB] -= entry.Conductance;

            nodes[entry.NodeA].LinkCount--;
            nodes[entry.NodeB].LinkCount--;

            UnchainFrom(entry.NodeA, link);
            UnchainFrom(entry.NodeB, link);

            int last = links.Count - 1;
            if (link != last)
            {
                ThermalLink moved = links[last];

                links[link] = moved;
                linkNextA[link] = linkNextA[last];
                linkNextB[link] = linkNextB[last];

                linkA[link] = linkA[last];
                linkB[link] = linkB[last];
                linkConductance[link] = linkConductance[last];
                if (last < linkMassFactor.Length) linkMassFactor[link] = linkMassFactor[last];

                RetargetLink(moved.NodeA, last, link);
                RetargetLink(moved.NodeB, last, link);
            }

            links.RemoveAt(last);
            if (syncedLinks > links.Count) syncedLinks = links.Count;
        }

/// <summary>DropLinksOf operation.</summary>
        private void DropLinksOf(ThermalNode node)
        {
            int index = node.Index;

            doomedLinks.Clear();
            for (int link = nodeFirstLink[index]; link != -1; link = NextLink(link, index))
            {
                doomedLinks.Add(link);
            }

            Work.LinksRemoved += doomedLinks.Count;

            doomedLinks.Sort();
            for (int i = doomedLinks.Count - 1; i >= 0; i--)
            {
                RemoveLinkAt(doomedLinks[i]);
            }

            nodeFirstLink[index] = -1;
        }

/// <summary>SpillEnergyOf operation.</summary>
        private float SpillEnergyOf(ThermalNode node)
        {
            int index = node.Index;

            float capacity = 0f;
            spillTargets.Clear();

            for (int link = nodeFirstLink[index]; link != -1; link = NextLink(link, index))
            {
                ThermalLink edge = links[link];
                int other = edge.NodeA == index ? edge.NodeB : edge.NodeA;
                if (other == index) continue;

                ThermalNode neighbour = nodes[other];
                if (neighbour.ThermalMass <= 0f) continue;

                spillTargets.Add(neighbour);
                capacity += neighbour.ThermalMass;
            }

            if (capacity <= 0f || spillTargets.Count == 0) return 0f;

            float energy = node.Energy;
            if (energy <= 0f) return 0f;

            float rise = energy / capacity;
            for (int i = 0; i < spillTargets.Count; i++)
            {
                ThermalNode neighbour = spillTargets[i];
                neighbour.Temperature += rise;
                neighbour.StateDirty = true;
            }

            node.Temperature = 0f;
            node.StateDirty = true;

            return energy;
        }

/// <summary>List operation.</summary>
        private readonly List<ThermalNode> spillTargets = new List<ThermalNode>();

/// <summary>Removes the nodeincremental.</summary>
        private void RemoveNodeIncremental(ThermalNode node)
        {
            int index = node.Index;

            Work.NodesRemoved++;

            EnsureBuffers();
            EnsureNodeChainCapacity(nodes.Count);

            DropLinksOf(node);

            int last = nodes.Count - 1;
            if (index != last)
            {
                ThermalNode moved = nodes[last];

                nodes[index] = moved;
                moved.Index = index;
                moved.Block.NodeIndex = index;

                nodeFirstLink[index] = nodeFirstLink[last];
                nodeConductanceTotal[index] = nodeConductanceTotal[last];

                moved.StateDirty = true;

                RepointNode(last, index);
            }

            nodeFirstLink[last] = -1;
            nodes.RemoveAt(last);
        }

/// <summary>RepointNode operation.</summary>
        private void RepointNode(int from, int to)
        {
            for (int link = nodeFirstLink[to]; link != -1; )
            {
                ThermalLink entry = links[link];
                int next;

                if (entry.NodeA == from)
                {
                    next = linkNextA[link];
                    entry.NodeA = to;
                    linkA[link] = to;
                }
                else
                {
                    next = linkNextB[link];
                    entry.NodeB = to;
                    linkB[link] = to;
                }

                links[link] = entry;
                link = next;
            }

            for (int l = 0; l < loops.Count; l++)
            {
                List<LoopLink> loopLinks = loops[l].Links;
                for (int i = 0; i < loopLinks.Count; i++)
                {
                    if (loopLinks[i].NodeIndex != from) continue;
/// <summary>LoopLink operation.</summary>
                    loopLinks[i] = new LoopLink(to, loopLinks[i].Conductance, loopLinks[i].SegmentIndex);
                }
            }

            for (int r = 0; r < roomAir.Count; r++)
            {
                List<RoomLink> roomLinks = roomAir[r].Links;
                for (int i = 0; i < roomLinks.Count; i++)
                {
                    if (roomLinks[i].NodeIndex != from) continue;
/// <summary>RoomLink operation.</summary>
                    roomLinks[i] = new RoomLink(to, roomLinks[i].Conductance);
                }
            }

            for (int p = 0; p < heatPumps.Count; p++)
            {
                HeatPumpDevice device = heatPumps[p];
                if (device.ColdNodeIndex == from) device.ColdNodeIndex = to;
                if (device.HotNodeIndex == from) device.HotNodeIndex = to;
            }
        }
    }
}
