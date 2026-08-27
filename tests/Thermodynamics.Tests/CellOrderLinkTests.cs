using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **Walking the grid's cells in the box's own index order finds the same conduction graph as
    /// asking the block table about every block's faces.**
    ///
    /// <para>
    /// The previous pass established that the link build is bound by one unpredictable touch of
    /// grid-sized memory per neighbour, and that no cheaper structure fixes it, because the cost is
    /// the randomness (backlog.md `D3b`). Walking cells in index order removes it: a cell's
    /// neighbours are at fixed offsets from its own index, so the lookups become ascending scans.
    /// It was refused until the link list's order stopped being part of the answer, which is this
    /// pass's first iteration.
    /// </para>
    ///
    /// <para>
    /// **Two cases decide whether it is right.** A block's own cells are adjacent to each other and
    /// must not link the block to itself; and two multi-cell blocks can touch across several cell
    /// pairs, which would make several links out of one adjacency — those are left to the walk that
    /// deduplicates. Both are exercised here, and a fixture that failed to contain them would be
    /// checking nothing.
    /// </para>
    /// </summary>
    public class CellOrderLinkTests
    {
        private readonly ITestOutputHelper output;

        public CellOrderLinkTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static ThermalSimulation Built(GridBuilder builder, bool byCell)
        {
            ThermalSimulation simulation = new ThermalSimulation(Hulls.Uncapped(), builder.Grid);
            simulation.Solver.CellOrderLinkWalk = byCell;
            for (int i = 0; i < builder.Placed.Count; i++) simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            simulation.Solver.RebuildLinks();
            return simulation;
        }

        private static GridBuilder CensusGrid(int blocks)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", blocks));
            return builder;
        }

        /// <summary>
        /// Bars and cubes packed against each other and against unit blocks, so multi-cell blocks
        /// touch multi-cell blocks — the case the cell walk deliberately does not handle — as well
        /// as one-cell ones.
        /// </summary>
        private static GridBuilder MixedGrid()
        {
            GridBuilder builder = GridBuilder.Large();
            BlockModel unit = Catalog.LightArmor();
            BlockModel bar = Catalog.LightArmorBar(3);
            BlockModel cube = Catalog.LightArmorCube(3);

            for (int x = 0; x < 9; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    for (int z = 0; z < 3; z++)
                    {
                        builder.Place(unit, new Vector3I(x, y, z));
                    }
                }
            }

            // Two cubes side by side, so a multi-cell block touches a multi-cell block across a
            // nine-cell face, and a bar against one of them.
            builder.Place(cube, new Vector3I(0, 3, 0));
            builder.Place(cube, new Vector3I(3, 3, 0));
            builder.Place(bar, new Vector3I(6, 3, 0));
            builder.Place(bar, new Vector3I(6, 4, 0));

            return builder;
        }

        private static Dictionary<long, ThermalLink> ByPair(IList<ThermalLink> links)
        {
            Dictionary<long, ThermalLink> map = new Dictionary<long, ThermalLink>(links.Count);
            for (int i = 0; i < links.Count; i++)
            {
                long key = ((long)links[i].NodeA << 32) | (uint)links[i].NodeB;
                map[key] = links[i];
            }

            return map;
        }

        private void AssertSameGraph(GridBuilder builder, string what)
        {
            ThermalSimulation asked = Built(builder, false);
            ThermalSimulation walked = Built(builder, true);

            IList<ThermalLink> a = asked.Solver.Links;
            IList<ThermalLink> b = walked.Solver.Links;

            Assert.True(a.Count > 0, what + ": the block-table walk built no links");
            Assert.True(a.Count == b.Count,
                what + ": " + a.Count + " links by the block table and " + b.Count + " by cell order"
                + " — a duplicate or a missing pair, not a reordering");

            // Sorted by their ends, both, so entry for entry is the whole comparison.
            for (int i = 0; i < a.Count; i++)
            {
                Assert.True(a[i].NodeA == b[i].NodeA && a[i].NodeB == b[i].NodeB,
                    what + ": link " + i + " joins " + a[i].NodeA + "-" + a[i].NodeB
                    + " one way and " + b[i].NodeA + "-" + b[i].NodeB + " the other");

                Assert.True(Bits(a[i].Conductance) == Bits(b[i].Conductance),
                    what + ": link " + i + " conducts " + a[i].Conductance.ToString("R")
                    + " one way and " + b[i].Conductance.ToString("R") + " the other");

                Assert.Equal(a[i].ContactFaces, b[i].ContactFaces);
            }

            IList<ThermalNode> nodesA = asked.Solver.Nodes;
            IList<ThermalNode> nodesB = walked.Solver.Nodes;
            for (int i = 0; i < nodesA.Count; i++)
            {
                Assert.True(nodesA[i].LinkCount == nodesB[i].LinkCount,
                    what + ": node " + i + " carries " + nodesA[i].LinkCount + " links one way and "
                    + nodesB[i].LinkCount + " the other");
            }

            output.WriteLine(what + ": " + a.Count.ToString("n0") + " links over "
                + nodesA.Count.ToString("n0") + " nodes agree");
        }

        [Fact]
        public void TheTwoWalksFindTheSameGraphOnACensusHull()
        {
            AssertSameGraph(CensusGrid(6000), "census hull");
        }

        [Fact]
        public void TheTwoWalksFindTheSameGraphWhereMultiCellBlocksTouchEachOther()
        {
            GridBuilder builder = MixedGrid();
            ThermalSimulation simulation = Built(builder, true);

            int multi = 0;
            int multiTouchingMulti = 0;
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            IList<ThermalLink> links = simulation.Solver.Links;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Block.CellCount > 1) multi++;
            }

            for (int i = 0; i < links.Count; i++)
            {
                if (nodes[links[i].NodeA].Block.CellCount > 1
                    && nodes[links[i].NodeB].Block.CellCount > 1)
                {
                    multiTouchingMulti++;
                }
            }

            Assert.True(multi >= 4, "the fixture holds " + multi + " multi-cell blocks");
            Assert.True(multiTouchingMulti > 0,
                "no multi-cell block touches another in the fixture, so the case the cell walk"
                + " deliberately leaves to the deduplicating walk is never exercised");

            AssertSameGraph(builder, "mixed grid");
        }

        /// <summary>
        /// A block never links to itself, which is what a walk over cells would do at every
        /// internal face of a multi-cell block if it did not check.
        /// </summary>
        [Fact]
        public void NoBlockIsLinkedToItself()
        {
            ThermalSimulation simulation = Built(MixedGrid(), true);
            IList<ThermalLink> links = simulation.Solver.Links;

            Assert.True(links.Count > 50, "only " + links.Count + " links");

            for (int i = 0; i < links.Count; i++)
            {
                Assert.True(links[i].NodeA != links[i].NodeB,
                    "link " + i + " joins node " + links[i].NodeA + " to itself");
            }
        }

        /// <summary>
        /// And no pair appears twice, which is what a walk over cells would do for two multi-cell
        /// blocks sharing a face of several cells.
        /// </summary>
        [Fact]
        public void NoPairIsLinkedTwice()
        {
            ThermalSimulation simulation = Built(MixedGrid(), true);
            IList<ThermalLink> links = simulation.Solver.Links;

            Dictionary<long, ThermalLink> seen = ByPair(links);
            Assert.True(seen.Count == links.Count,
                links.Count + " links describe only " + seen.Count + " distinct pairs, so an"
                + " adjacency has been made into more than one link");
        }

        private static int Bits(float value)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        }
    }
}
