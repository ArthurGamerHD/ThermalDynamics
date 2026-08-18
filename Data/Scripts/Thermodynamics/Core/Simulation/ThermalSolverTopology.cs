using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The conduction graph's incremental half: an index of which links touch which node, and the
    /// add and remove operations built on it.
    ///
    /// <para>
    /// A block placed needs no index — nothing links to a block that was not there, so its links
    /// are appended and everything below them is left alone. A block <em>removed</em> is the hard
    /// direction, and the whole reason this file exists. Its links have to be found before they
    /// can be dropped, and finding them by scanning the link list is proportional to the grid,
    /// which is the cost being removed. Its node also has to leave the node list, and taking it
    /// out by shifting everything after it moves every one of those indices — invalidating every
    /// link that referred to any of them, which is what forced the global rebuild.
    /// </para>
    ///
    /// <para>
    /// Both are solved by the same two decisions. Links are indexed per node as an intrusive
    /// chain — three int arrays, no per-node collections and no managed references, which is what
    /// <see href="../../../../docs/scale-design.md">scale-design</see> asks for in the hot data —
    /// so a node's links are walked in time proportional to how many it has. And a node is taken
    /// out by moving the last one into its place rather than by shifting, so exactly one index
    /// changes and only the links touching that one node have to be rewritten.
    /// </para>
    ///
    /// <para>
    /// Nothing reads the node list in order. The solver is order-independent by construction —
    /// every exchange is computed from the temperatures at the start of a substep and applied at
    /// the end — which is what makes the cheap removal the correct one rather than merely the
    /// fast one.
    /// </para>
    /// </summary>
    public partial class ThermalSolver
    {
        /// <summary>Head of each node's link chain, by node index. -1 when it has none.</summary>
        private int[] nodeFirstLink = new int[0];

        /// <summary>
        /// Next link in the chain, one entry per link per endpoint. Which of the two applies is
        /// decided by whether the link's <c>NodeA</c> is the node being walked.
        /// </summary>
        private int[] linkNextA = new int[0];
        private int[] linkNextB = new int[0];

        /// <summary>Scratch for collecting a node's links before they are removed.</summary>
        private readonly List<int> doomedLinks = new List<int>();

        private void EnsureNodeChainCapacity(int nodeCount)
        {
            if (nodeFirstLink.Length >= nodeCount) return;

            int size = Math.Max(16, nodeCount * 2);
            int previous = nodeFirstLink.Length;
            Array.Resize(ref nodeFirstLink, size);
            for (int i = previous; i < size; i++) nodeFirstLink[i] = -1;
        }

        private void EnsureLinkChainCapacity(int linkCount)
        {
            if (linkNextA.Length >= linkCount) return;

            int size = Math.Max(16, linkCount * 2);
            Array.Resize(ref linkNextA, size);
            Array.Resize(ref linkNextB, size);
        }

        /// <summary>Clears every chain. Used by the global rebuild, which makes them all again.</summary>
        private void ResetLinkChains()
        {
            EnsureNodeChainCapacity(nodes.Count);
            for (int i = 0; i < nodeFirstLink.Length; i++) nodeFirstLink[i] = -1;
        }

        /// <summary>The next link after <paramref name="link"/> in <paramref name="node"/>'s chain.</summary>
        private int NextLink(int link, int node)
        {
            return links[link].NodeA == node ? linkNextA[link] : linkNextB[link];
        }

        private void SetNextLink(int link, int node, int next)
        {
            if (links[link].NodeA == node) linkNextA[link] = next;
            else linkNextB[link] = next;
        }

        /// <summary>Pushes a link onto both its endpoints' chains.</summary>
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

        /// <summary>Splices a link out of one endpoint's chain.</summary>
        private void UnchainFrom(int node, int link)
        {
            if (node < 0 || node >= nodeFirstLink.Length) return;

            int current = nodeFirstLink[node];
            if (current == link)
            {
                nodeFirstLink[node] = NextLink(link, node);
                return;
            }

            while (current != -1)
            {
                int next = NextLink(current, node);
                if (next == link)
                {
                    SetNextLink(current, node, NextLink(link, node));
                    return;
                }
                current = next;
            }
        }

        /// <summary>
        /// Rewrites a chain pointer that referred to a link which has moved. Used after a link is
        /// swapped into a hole left by a removal.
        /// </summary>
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
                int next = NextLink(current, node);
                if (next == from)
                {
                    SetNextLink(current, node, to);
                    return;
                }
                current = next;
            }
        }

        /// <summary>
        /// Takes one link out of the graph: out of both chains, out of the conductance totals, and
        /// out of the link list by moving the last link into its place.
        ///
        /// Every mirrored row moves with it — the flat arrays the substep loop reads, and the
        /// cached reduced mass — because they are indexed by link and the link has changed index.
        /// Forgetting one of those is the failure this arrangement invites, so they are all done
        /// here and nowhere else.
        /// </summary>
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

        /// <summary>
        /// Removes a node and every link touching it, without rebuilding anything.
        ///
        /// The links first, in descending index order. That order is not cosmetic: removing one
        /// moves the last link into the hole, and taking the highest index first guarantees the
        /// link that moves is never one still waiting to be removed.
        ///
        /// Then the node itself, by moving the last node into its place. That changes exactly one
        /// index, and everything holding node indices is repaired for that one change — the links
        /// touching it, the mirrored rows, the coolant loops, the room air and the heat pumps.
        /// Everything holding an index is listed in <see cref="RepointNode"/>; anything added
        /// later that keeps one has to be added there too.
        /// </summary>
        private void RemoveNodeIncremental(ThermalNode node)
        {
            int index = node.Index;

            Work.NodesRemoved++;

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

            int last = nodes.Count - 1;
            if (index != last)
            {
                ThermalNode moved = nodes[last];

                nodes[index] = moved;
                moved.Index = index;

                nodeFirstLink[index] = nodeFirstLink[last];
                nodeConductanceTotal[index] = nodeConductanceTotal[last];

                // The mirrored row for the moved node is now in the wrong place. Marking it
                // dirty has SyncNodeState refill it at the top of the next step, which is where
                // that row is written from anyway.
                moved.StateDirty = true;

                RepointNode(last, index);
            }

            nodeFirstLink[last] = -1;
            nodes.RemoveAt(last);
        }

        /// <summary>
        /// Rewrites every stored reference to node index <paramref name="from"/> as
        /// <paramref name="to"/>.
        ///
        /// The links are the obvious ones and the cheap ones — the moved node's own chain, which
        /// is as long as its degree. The other three are the ones worth being careful about,
        /// because they are rebuilt on their own schedules rather than with the graph: coolant
        /// loops and heat pumps are remade on the next topology stage, but room air is not remade
        /// until a room mapping pass completes, which on a large grid is thousands of ticks after
        /// the block was removed. A room's links pointing at the wrong node for that long would
        /// pour its air's heat into whichever block happened to inherit the index.
        /// </summary>
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
                    loopLinks[i] = new LoopLink(to, loopLinks[i].Conductance);
                }
            }

            for (int r = 0; r < roomAir.Count; r++)
            {
                List<RoomLink> roomLinks = roomAir[r].Links;
                for (int i = 0; i < roomLinks.Count; i++)
                {
                    if (roomLinks[i].NodeIndex != from) continue;
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
