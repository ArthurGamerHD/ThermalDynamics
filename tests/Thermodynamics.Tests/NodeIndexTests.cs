using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The block carries its node's index, and the dictionary that used to is the oracle.**
    ///
    /// <para>
    /// The solver kept a `Dictionary&lt;long, ThermalNode&gt;` from block key to node — about 49
    /// bytes a block, measured, for an answer the block can hold in four
    /// ([backlog.md](../../docs/backlog.md) `E1`). A change whose whole purpose is cost has to be
    /// pinned against the code it replaced (`D8`), and here that code is reconstructible: the
    /// dictionary is exactly `node.Block.Key → node` over the live node list, so these build it and
    /// require the index to agree with it.
    /// </para>
    ///
    /// <para>
    /// **The agreement has to survive index motion**, which is where a hand-maintained index fails:
    /// a removal moves the last node into the hole through one of two paths, and both are exercised
    /// here. The fixture asserts its own preconditions for the reason `D8` gives — two lookups over
    /// a grid that built nothing agree perfectly.
    /// </para>
    /// </summary>
    public class NodeIndexTests
    {
        private readonly ITestOutputHelper output;

        public NodeIndexTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static GridBuilder Hull(int side)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(side, side, side));
            return builder;
        }

        /// <summary>The dictionary the solver used to keep, rebuilt from what it keeps now.</summary>
        private static Dictionary<long, ThermalNode> ByKey(ThermalSimulation simulation)
        {
            Dictionary<long, ThermalNode> byKey = new Dictionary<long, ThermalNode>();
            IList<ThermalNode> nodes = simulation.Solver.Nodes;

            for (int i = 0; i < nodes.Count; i++)
            {
                byKey[nodes[i].Block.Key] = nodes[i];
            }

            return byKey;
        }

        /// <summary>Every block's lookup agrees with the dictionary, and there were blocks to judge.</summary>
        private void RequireAgreement(GridBuilder builder, ThermalSimulation simulation, string when)
        {
            Dictionary<long, ThermalNode> byKey = ByKey(simulation);
            int judged = 0;

            for (int i = 0; i < builder.Placed.Count; i++)
            {
                BlockInstance block = builder.Placed[i];

                ThermalNode expected;
                if (!byKey.TryGetValue(block.Key, out expected)) expected = null;

                // A key the dictionary holds for a *different* instance is not this block's node —
                // which is the one case where a key and an index disagree by construction.
                if (expected != null && !ReferenceEquals(expected.Block, block)) expected = null;

                ThermalNode actual = simulation.Solver.GetNode(block);
                judged++;

                Assert.True(ReferenceEquals(expected, actual),
                    when + ": block at " + block.Position + " resolves to "
                    + (actual == null ? "nothing" : "node " + actual.Index)
                    + " and the dictionary says "
                    + (expected == null ? "nothing" : "node " + expected.Index));
            }

            Assert.True(judged > 0, when + ": no block was judged (`E8`)");
            output.WriteLine("{0}: {1} blocks agree over {2} nodes", when, judged, simulation.Solver.Nodes.Count);
        }

        /// <summary>
        /// The index agrees with the dictionary on a built hull, which is the base case.
        /// </summary>
        [Fact]
        public void EveryBlockResolvesToTheNodeTheDictionaryWouldHaveGiven()
        {
            GridBuilder builder = Hull(4);
            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            simulation.RebuildAll();

            Assert.True(simulation.Solver.Nodes.Count > 50,
                "the hull built " + simulation.Solver.Nodes.Count + " nodes, which judges too little");

            RequireAgreement(builder, simulation, "as built");
        }

        /// <summary>
        /// **And it survives the removal path that moves the last node into the hole.** This is
        /// where a hand-maintained index goes stale: the moved node's block keeps pointing at the
        /// slot it came from unless the move repairs it.
        /// </summary>
        [Fact]
        public void TheIndexFollowsANodeMovedByAnIncrementalRemoval()
        {
            GridBuilder builder = Hull(4);
            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            simulation.RebuildAll();

            // Stepping first, so the links are built and the removal takes the incremental path
            // rather than the rebuild one.
            simulation.StepExact(2, Worlds.Shadow());

            int before = simulation.Solver.Nodes.Count;

            // Removing from the front is what forces a move: the last node fills the hole.
            for (int i = 0; i < 5; i++)
            {
                BlockInstance victim = builder.Placed[0];
                builder.Placed.RemoveAt(0);
                Assert.NotNull(simulation.Solver.GetNode(victim));
                simulation.RemoveBlock(victim);

                // A removed block must stop resolving, or a caller holding it reads someone else's.
                Assert.Null(simulation.Solver.GetNode(victim));

                RequireAgreement(builder, simulation, "after removal " + (i + 1));
            }

            Assert.Equal(before - 5, simulation.Solver.Nodes.Count);
        }

        /// <summary>
        /// The other removal path: with a rebuild already due, the node list is compacted and every
        /// index after the hole shifts down by one.
        /// </summary>
        [Fact]
        public void TheIndexFollowsTheShiftWhenARebuildIsAlreadyDue()
        {
            GridBuilder builder = Hull(4);
            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);

            // No step and no rebuild: the links are still dirty, which is the branch under test.
            BlockInstance victim = builder.Placed[0];
            builder.Placed.RemoveAt(0);
            Assert.NotNull(simulation.Solver.GetNode(victim));
            simulation.RemoveBlock(victim);

            RequireAgreement(builder, simulation, "after a dirty-graph removal");

            simulation.RebuildAll();
            RequireAgreement(builder, simulation, "after the rebuild");
        }

        /// <summary>
        /// **A block registered with a second solver does not make the first one wrong.** One
        /// instance holds one index, so the second registration overwrites it — and the guard turns
        /// that into *no node* rather than into somebody else's node, which is what a dictionary
        /// miss did. The behaviour is asserted rather than assumed because it is the price of the
        /// change and a reader should be able to find it.
        /// </summary>
        [Fact]
        public void AnInstanceSharedWithASecondSolverResolvesToNothingRatherThanToTheWrongNode()
        {
            GridBuilder builder = Hull(3);
            ThermalSimulation first = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            first.RebuildAll();

            BlockInstance shared = builder.Placed[0];
            Assert.NotNull(first.Solver.GetNode(shared));

            ThermalSimulation second = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            second.RebuildAll();

            ThermalNode inSecond = second.Solver.GetNode(shared);
            Assert.NotNull(inSecond);
            Assert.True(ReferenceEquals(inSecond.Block, shared));

            ThermalNode inFirst = first.Solver.GetNode(shared);
            if (inFirst != null)
            {
                // Allowed only if the two solvers happen to agree on the slot, and then it must
                // still be this block's node rather than another's.
                Assert.True(ReferenceEquals(inFirst.Block, shared),
                    "the first solver resolved a shared instance to a different block's node");
            }
        }
    }
}
