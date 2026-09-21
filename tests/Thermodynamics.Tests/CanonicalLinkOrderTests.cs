using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    public class CanonicalLinkOrderTests
    {
        private readonly ITestOutputHelper output;

/// <summary>CanonicalLinkOrderTests operation.</summary>
        public CanonicalLinkOrderTests(ITestOutputHelper output)
        {
            this.output = output;
        }

/// <summary>Census operation.</summary>
        private static ThermalSimulation Census(bool canonical, int blocks)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", blocks));

/// <summary>ThermalSimulation operation.</summary>
            ThermalSimulation simulation = new ThermalSimulation(Hulls.Uncapped(), builder.Grid);
            simulation.Solver.CanonicalLinkOrder = canonical;
            for (int i = 0; i < builder.Placed.Count; i++) simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            simulation.Solver.RebuildLinks();
            return simulation;
        }

/// <summary>FirstDescent operation.</summary>
        private static int FirstDescent(IList<ThermalLink> links)
        {
            for (int i = 1; i < links.Count; i++)
            {
                if (links[i - 1].NodeA > links[i].NodeA) return i;
                if (links[i - 1].NodeA == links[i].NodeA && links[i - 1].NodeB > links[i].NodeB) return i;
            }

            return -1;
        }

        [Fact]
/// <summary>TheLinkListComesOutSortedByItsEnds operation.</summary>
        public void TheLinkListComesOutSortedByItsEnds()
        {
/// <summary>Census operation.</summary>
            ThermalSimulation simulation = Census(true, 6000);
            IList<ThermalLink> links = simulation.Solver.Links;

            Assert.True(links.Count > 5000, "only " + links.Count + " links, so little is checked");

/// <summary>FirstDescent operation.</summary>
            int descent = FirstDescent(links);
            Assert.True(descent < 0,
                descent < 0 ? "" : "link " + descent + " joins " + links[descent].NodeA + "-"
                    + links[descent].NodeB + " after " + links[descent - 1].NodeA + "-"
                    + links[descent - 1].NodeB + ", so the list is not in the order the graph implies");
        }

        [Fact]
/// <summary>TheWalksOwnOrderIsNotAlreadySorted operation.</summary>
        public void TheWalksOwnOrderIsNotAlreadySorted()
        {
/// <summary>Census operation.</summary>
            ThermalSimulation simulation = Census(false, 6000);
            IList<ThermalLink> links = simulation.Solver.Links;

/// <summary>FirstDescent operation.</summary>
            int descent = FirstDescent(links);

            output.WriteLine(links.Count.ToString("n0") + " links; the walk's own order first"
                + " departs from sorted at " + descent);

            Assert.True(descent > 0,
                "the walk already emits links sorted by their ends, so canonicalising them reorders"
                + " nothing and buys no freedom for a walk that finds them differently");
        }

        [Fact]
/// <summary>SortingIsAPermutationOfTheSameGraph operation.</summary>
        public void SortingIsAPermutationOfTheSameGraph()
        {
/// <summary>Census operation.</summary>
            ThermalSimulation walked = Census(false, 6000);
/// <summary>Census operation.</summary>
            ThermalSimulation sorted = Census(true, 6000);

            IList<ThermalLink> a = walked.Solver.Links;
            IList<ThermalLink> b = sorted.Solver.Links;

            Assert.Equal(a.Count, b.Count);

            Dictionary<long, float> byPair = new Dictionary<long, float>(a.Count);
            for (int i = 0; i < a.Count; i++)
            {
                byPair[((long)a[i].NodeA << 32) | (uint)a[i].NodeB] = a[i].Conductance;
            }

            for (int i = 0; i < b.Count; i++)
            {
                long key = ((long)b[i].NodeA << 32) | (uint)b[i].NodeB;

                float conductance;
                Assert.True(byPair.TryGetValue(key, out conductance),
                    "the sorted list holds a link " + b[i].NodeA + "-" + b[i].NodeB
                    + " that the walk's own order does not");

                Assert.Equal(conductance, b[i].Conductance);
            }

            IList<ThermalNode> nodesA = walked.Solver.Nodes;
            IList<ThermalNode> nodesB = sorted.Solver.Nodes;
            for (int i = 0; i < nodesA.Count; i++)
            {
                Assert.Equal(nodesA[i].LinkCount, nodesB[i].LinkCount);
            }
        }
    }
}
