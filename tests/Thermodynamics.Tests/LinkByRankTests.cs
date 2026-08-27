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
    /// **The conduction graph is the same graph whether its neighbours were found through the block
    /// table or through the occupancy set's ranks.**
    ///
    /// <para>
    /// Finding neighbours is 90 % of the link build and about fifty nanoseconds a probe, because
    /// the block table holds half a million entries and a lookup misses cache twice
    /// (performance.md, Pass 6, Iteration 1). A one-cell block — nearly every block on a hull — can
    /// be walked without it: its six candidates are its cell's index plus a per-face constant, the
    /// occupancy bit says whether anything is there, the rank says which member it is, and a row
    /// says whose node that is.
    /// </para>
    ///
    /// <para>
    /// **Order is part of the claim, not a detail.** The conduction pass accumulates watts by
    /// walking the link list, and a sum of floats depends on its order, so a walk that found the
    /// same pairs in a different sequence would move every temperature's last bit. These compare
    /// the two builds entry for entry — ends, conductance and contact count — on a hull that mixes
    /// one-cell blocks with multi-cell ones, so both the new walk and the fallback are exercised.
    /// </para>
    /// </summary>
    public class LinkByRankTests
    {
        private readonly ITestOutputHelper output;

        public LinkByRankTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static ThermalSimulation Census(bool byRank, int blocks)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", blocks));

            ThermalSimulation simulation = new ThermalSimulation(Hulls.Uncapped(), builder.Grid);
            simulation.Solver.LinkByRank = byRank;
            for (int i = 0; i < builder.Placed.Count; i++) simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            simulation.Solver.RebuildLinks();
            return simulation;
        }

        /// <summary>
        /// One-cell blocks beside bars and cubes, so a one-cell block has multi-cell neighbours and
        /// the fallback walk runs for the multi-cell blocks themselves.
        /// </summary>
        private static ThermalSimulation Mixed(bool byRank)
        {
            GridBuilder builder = GridBuilder.Large();
            BlockModel unit = Catalog.LightArmor();
            BlockModel bar = Catalog.LightArmorBar(3);
            BlockModel cube = Catalog.LightArmorCube(3);

            for (int x = 0; x < 12; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    for (int z = 0; z < 4; z++)
                    {
                        builder.Place(unit, new Vector3I(x, y, z));
                    }
                }
            }

            builder.Place(bar, new Vector3I(0, 4, 0));
            builder.Place(cube, new Vector3I(4, 4, 0));
            builder.Place(bar, new Vector3I(8, 4, 1));

            ThermalSimulation simulation = new ThermalSimulation(Hulls.Uncapped(), builder.Grid);
            simulation.Solver.LinkByRank = byRank;
            for (int i = 0; i < builder.Placed.Count; i++) simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            simulation.Solver.RebuildLinks();
            return simulation;
        }

        private static void AssertSameGraph(ThermalSimulation table, ThermalSimulation rank, string what)
        {
            IList<ThermalLink> a = table.Solver.Links;
            IList<ThermalLink> b = rank.Solver.Links;

            Assert.True(a.Count > 0, what + ": the block-table walk built no links, so nothing is compared");
            Assert.True(a.Count == b.Count,
                what + ": " + a.Count + " links by the block table and " + b.Count + " by rank");

            for (int i = 0; i < a.Count; i++)
            {
                Assert.True(a[i].NodeA == b[i].NodeA && a[i].NodeB == b[i].NodeB,
                    what + ": link " + i + " joins " + a[i].NodeA + "-" + a[i].NodeB
                    + " by the block table and " + b[i].NodeA + "-" + b[i].NodeB + " by rank,"
                    + " so the two walks are building the list in different orders");

                Assert.True(Bits(a[i].Conductance) == Bits(b[i].Conductance),
                    what + ": link " + i + " conducts " + a[i].Conductance.ToString("R")
                    + " one way and " + b[i].Conductance.ToString("R") + " the other");

                Assert.Equal(a[i].ContactFaces, b[i].ContactFaces);
            }

            // And every node agrees about how many links it carries, which is the chain the
            // solver walks rather than the list.
            IList<ThermalNode> nodesA = table.Solver.Nodes;
            IList<ThermalNode> nodesB = rank.Solver.Nodes;
            Assert.Equal(nodesA.Count, nodesB.Count);

            for (int i = 0; i < nodesA.Count; i++)
            {
                Assert.True(nodesA[i].LinkCount == nodesB[i].LinkCount,
                    what + ": node " + i + " carries " + nodesA[i].LinkCount + " links one way and "
                    + nodesB[i].LinkCount + " the other");
            }
        }

        [Fact]
        public void TheTwoWalksBuildTheSameGraphOnACensusHull()
        {
            ThermalSimulation table = Census(false, 6000);
            ThermalSimulation rank = Census(true, 6000);

            output.WriteLine(table.Solver.Links.Count.ToString("n0") + " links over "
                + table.Solver.Nodes.Count.ToString("n0") + " nodes");

            AssertSameGraph(table, rank, "census hull");
        }

        [Fact]
        public void TheTwoWalksBuildTheSameGraphWhereBlockSizesMix()
        {
            ThermalSimulation table = Mixed(false);
            ThermalSimulation rank = Mixed(true);

            int multi = 0;
            IList<ThermalNode> nodes = table.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Block.CellCount > 1) multi++;
            }

            Assert.True(multi >= 3,
                "the fixture holds " + multi + " multi-cell blocks, so the fallback walk is barely"
                + " exercised and the rank walk is not being held against it where it matters");

            AssertSameGraph(table, rank, "mixed grid");
        }

        /// <summary>
        /// And the grid the census builds really is dominated by one-cell blocks, or the walk under
        /// test is a path almost nothing takes.
        /// </summary>
        [Fact]
        public void TheCensusHullIsMostlyOneCellBlocks()
        {
            ThermalSimulation simulation = Census(true, 6000);
            IList<ThermalNode> nodes = simulation.Solver.Nodes;

            int single = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Block.CellCount == 1) single++;
            }

            double share = 100.0 * single / nodes.Count;
            output.WriteLine(single.ToString("n0") + " of " + nodes.Count.ToString("n0")
                + " blocks are one cell (" + share.ToString("n1") + " %)");

            Assert.True(share > 50.0,
                "only " + share.ToString("n1") + " % of the hull is one-cell blocks, so the walk"
                + " this iteration adds is not the one the hull takes");
        }

        private static int Bits(float value)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        }
    }
}
