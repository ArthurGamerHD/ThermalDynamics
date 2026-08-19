using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The conduction graph's incremental half: an index of which links touch which node, and the
    /// add and remove operations built on it.
    ///
    /// <para>
    /// A placed block needs no index: nothing links to a block that was not there, so its links are
    /// appended and everything below them is untouched. A removed block is the case this file
    /// exists for. Its links must be found before they can be dropped, and scanning the link list
    /// for them costs a pass over the grid. Its node must also leave the node list, and removing it
    /// by shifting would move every index after it, invalidating every link referring to any of
    /// them.
    /// </para>
    ///
    /// <para>
    /// Both are addressed by two decisions. Links are indexed per node as an intrusive chain —
    /// three int arrays, with no per-node collections and no managed references, as
    /// <see href="../../../../docs/scale-design.md">scale-design</see> requires of the hot data —
    /// so a node's links are walked in time proportional to its degree. And a node is removed by
    /// moving the last one into its slot rather than by shifting, so exactly one index changes and
    /// only the links touching that node need rewriting.
    /// </para>
    ///
    /// <para>
    /// Nothing reads the node list in order. The solver is order-independent by construction, since
    /// every exchange is computed from the temperatures at the start of a substep and applied at
    /// the end, which is what makes the swap removal correct as well as cheap.
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

        /// <summary>Clears every chain. Used by the full rebuild, which reconstructs them all.</summary>
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
        /// Removes one link from the graph: from both chains, from the conductance totals, and from
        /// the link list by moving the last link into its slot.
        ///
        /// Every link-indexed row moves with it — the flat arrays the substep loop reads and the
        /// cached reduced mass — since the moved link has changed index. All of them are updated
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
        /// Removes a node and every link touching it, without a rebuild.
        ///
        /// Links go first, in descending index order. Removing one moves the last link into the
        /// hole, so taking the highest index first guarantees the moved link is never one still
        /// waiting to be removed.
        ///
        /// The node then moves the last node into its slot, changing exactly one index. Every holder
        /// of node indices is repaired for that change — the links touching it, the mirrored rows,
        /// the coolant loops, the room air and the heat pumps — all listed in
        /// <see cref="RepointNode"/>, which any new holder must be added to.
        /// </summary>
        private void RemoveNodeIncremental(ThermalNode node)
        {
            int index = node.Index;

            Work.NodesRemoved++;

            // Blocks can be placed and removed with no step in between, and placement does not size
            // the buffers — the step that drains the queue does — so the node arrays walked here may
            // be shorter than the node list. Reading past one throws IndexOutOfRangeException, which
            // the game's script whitelist prohibits and so cannot be caught: it would end the
            // grid's update for the session.
            EnsureBuffers();
            EnsureNodeChainCapacity(nodes.Count);

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

                // The mirrored row for the moved node is now at the wrong index. Marking it dirty
                // has SyncNodeState refill it at the top of the next step, which is where that row
                // is written from in any case.
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
        /// The links cost the moved node's degree. The coolant loops, room air and heat pumps matter
        /// more because they are rebuilt on their own schedules rather than with the graph: loops
        /// and pumps are remade on the next topology stage, but room air waits for a room mapping
        /// pass to complete, which on a large grid is thousands of ticks. A room's links pointing at
        /// a stale index for that long would deliver its air's heat to whichever block inherited it.
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
                    // A node moved index; the parcel it is bolted to did not.
                    loopLinks[i] = new LoopLink(to, loopLinks[i].Conductance, loopLinks[i].SegmentIndex);
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
