using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Placing a block links the block, not the grid — and the graph that comes out has to be
    /// the same graph a full rebuild would have produced.
    ///
    /// That equivalence is the whole safety argument for the incremental path, and it is not
    /// obvious: two blocks placed against each other each see the other as a neighbour, so the
    /// pair can be added twice and the joint conduct at double rate; a block placed against an
    /// existing one must not disturb links that already exist; and the conductance totals the
    /// substep estimate divides by are accumulated rather than recomputed, so an error in them
    /// shows up as a stability change rather than as a wrong temperature.
    ///
    /// Every test here therefore compares against the global builder rather than against a
    /// number written down by hand.
    /// </summary>
    public class IncrementalTopologyTests
    {
        /// <summary>Every link as an ordered triple, sorted, so two graphs can be compared.</summary>
        private static List<string> LinkSignature(ThermalSolver solver)
        {
            List<string> rows = new List<string>();
            IList<ThermalLink> links = solver.Links;

            for (int i = 0; i < links.Count; i++)
            {
                ThermalLink link = links[i];

                // By block position, not by node index: the two builders are free to number
                // nodes differently and still be the same graph.
                Vector3I a = solver.Nodes[link.NodeA].Block.Position;
                Vector3I b = solver.Nodes[link.NodeB].Block.Position;

                string low = Key(a);
                string high = Key(b);
                if (string.CompareOrdinal(low, high) > 0)
                {
                    string swap = low;
                    low = high;
                    high = swap;
                }

                rows.Add(low + "|" + high + "|" + link.Conductance.ToString("f6")
                    + "|" + link.ContactFaces);
            }

            rows.Sort(StringComparer.Ordinal);
            return rows;
        }

        private static string Key(Vector3I cell)
        {
            return cell.X + "," + cell.Y + "," + cell.Z;
        }

        /// <summary>A shape with joints of every kind: touching, corner-only, and detached.</summary>
        private static List<Vector3I> Shape()
        {
            List<Vector3I> cells = new List<Vector3I>();

            for (int z = 0; z < 4; z++)
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 3; x++)
                        cells.Add(new Vector3I(x, y, z));

            // A spur, a diagonal that touches nothing, and an island.
            cells.Add(new Vector3I(3, 1, 1));
            cells.Add(new Vector3I(4, 1, 1));
            cells.Add(new Vector3I(5, 2, 2));
            cells.Add(new Vector3I(-3, 0, 0));

            return cells;
        }

        private static ThermalSimulation BuiltAllAtOnce(List<Vector3I> cells)
        {
            GridBuilder builder = GridBuilder.Large();
            for (int i = 0; i < cells.Count; i++)
            {
                builder.Place(Model(i), cells[i]);
            }

            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), builder.Grid);
            for (int i = 0; i < builder.Placed.Count; i++)
            {
                simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            }

            simulation.RebuildAll();
            return simulation;
        }

        private static ThermalSimulation BuiltOneAtATime(List<Vector3I> cells)
        {
            GridModel grid = new GridModel(Catalog.LargeGridSize);
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), grid);

            for (int i = 0; i < cells.Count; i++)
            {
                simulation.AddBlock(
                    new BlockInstance(Model(i), cells[i], BlockOrientation.Identity), 293.15f);

                // A tick between each, so every placement goes through the incremental path on
                // its own rather than arriving as one batch.
                simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());
            }

            while (simulation.HasPendingWork)
            {
                simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());
            }

            return simulation;
        }

        /// <summary>Mixed block types, so conductance differs joint by joint.</summary>
        private static BlockModel Model(int index)
        {
            return (index % 3) == 0 ? Catalog.Grating() : Catalog.HeavyArmor();
        }

        [Fact]
        public void PlacingOneBlockAtATimeBuildsTheSameGraphAsBuildingItAllAtOnce()
        {
            List<Vector3I> cells = Shape();

            List<string> incremental = LinkSignature(BuiltOneAtATime(cells).Solver);
            List<string> global = LinkSignature(BuiltAllAtOnce(cells).Solver);

            Assert.Equal(global.Count, incremental.Count);
            Assert.Equal(global, incremental);
        }

        /// <summary>
        /// Two blocks placed in the same batch, against each other. Each finds the other as a
        /// neighbour, and only one of them may add the link between them.
        /// </summary>
        [Fact]
        public void TwoBlocksPlacedTogetherAgainstEachOtherGetOneLinkNotTwo()
        {
            GridModel grid = new GridModel(Catalog.LargeGridSize);
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), grid);

            simulation.AddBlock(new BlockInstance(Catalog.HeavyArmor(), Vector3I.Zero,
                BlockOrientation.Identity), 293.15f);
            simulation.AddBlock(new BlockInstance(Catalog.HeavyArmor(), new Vector3I(1, 0, 0),
                BlockOrientation.Identity), 293.15f);

            simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());

            Assert.Single(simulation.Solver.Links);
            Assert.Equal(1, simulation.Solver.Nodes[0].LinkCount);
            Assert.Equal(1, simulation.Solver.Nodes[1].LinkCount);
        }

        /// <summary>
        /// The conductance totals the substep estimate reads are accumulated on the incremental
        /// path rather than recomputed. They must land on the same figures.
        ///
        /// This one is worth having separately from the graph comparison, because an error here
        /// does not change any link — it changes how many substeps the grid asks for, which
        /// looks like a performance change or an instability rather than like a bug.
        ///
        /// Compared as a relative difference rather than to a fixed number of decimal places,
        /// because the two routes add the same conductances in a different order and single
        /// precision is not associative. The claim being made is "the same to within float
        /// accumulation order", and saying it that way is honest where rounding to three places
        /// would only be hiding it. Drift stays bounded because the incremental path only ever
        /// adds — anything that could subtract takes the global route, which starts from zero.
        /// </summary>
        [Fact]
        public void ConductanceTotalsMatchTheGlobalBuild()
        {
            List<Vector3I> cells = Shape();

            ThermalSimulation incremental = BuiltOneAtATime(cells);
            ThermalSimulation global = BuiltAllAtOnce(cells);

            float step = incremental.Settings.StepSeconds;
            float a = incremental.Solver.RequiredSubsteps(step);
            float b = global.Solver.RequiredSubsteps(step);

            float difference = Math.Abs(a - b) / Math.Max(1e-6f, Math.Abs(b));
            Assert.True(difference < 1e-4f,
                "substep estimate differed by " + difference.ToString("e2")
                + " relative: incremental " + a + ", global " + b);
        }

        /// <summary>
        /// The same grid, built either way, must simulate to the same temperatures.
        ///
        /// The end-to-end check: it would catch a difference in the graph, in the totals, in the
        /// mirrored arrays the substep loop reads, or in the cached reduced masses the overshoot
        /// clamp uses — and unlike the comparisons above it does not need to know which of those
        /// went wrong to fail.
        /// </summary>
        [Fact]
        public void BothRoutesSimulateToTheSameTemperatures()
        {
            List<Vector3I> cells = Shape();

            ThermalSimulation incremental = BuiltOneAtATime(cells);
            ThermalSimulation global = BuiltAllAtOnce(cells);

            SeedByPosition(incremental);
            SeedByPosition(global);

            EnvironmentSample sample = Worlds.Space(new Vector3(0f, 1f, 0f));
            incremental.StepExact(40, sample);
            global.StepExact(40, sample);

            for (int i = 0; i < cells.Count; i++)
            {
                ThermalNode fromIncremental = incremental.Solver.GetNodeAt(cells[i]);
                ThermalNode fromGlobal = global.Solver.GetNodeAt(cells[i]);

                Assert.NotNull(fromIncremental);
                Assert.NotNull(fromGlobal);
                Assert.Equal(fromGlobal.Temperature, fromIncremental.Temperature, 3);
            }
        }

        /// <summary>
        /// Seeds a spread that depends only on where a block is, so two grids whose nodes are in
        /// different orders still start from the same physical state.
        /// </summary>
        private static void SeedByPosition(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                Vector3I at = nodes[i].Block.Position;
                nodes[i].Temperature = 300f + 40f * ((at.X * 7 + at.Y * 13 + at.Z * 23) % 11);
            }
        }

        /// <summary>
        /// A block placed and then removed again before anything stepped must leave no trace.
        ///
        /// The queue of blocks waiting to be linked holds node objects, and one of them can be
        /// taken off the grid before the queue is drained. Draining it then has to notice, or it
        /// links a node that no longer exists.
        /// </summary>
        [Fact]
        public void APlacementUndoneBeforeTheNextTickLeavesNoLink()
        {
            GridModel grid = new GridModel(Catalog.LargeGridSize);
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), grid);

            simulation.AddBlock(new BlockInstance(Catalog.HeavyArmor(), Vector3I.Zero,
                BlockOrientation.Identity), 293.15f);
            simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());

            BlockInstance transient = new BlockInstance(Catalog.HeavyArmor(), new Vector3I(1, 0, 0),
                BlockOrientation.Identity);
            simulation.AddBlock(transient, 293.15f);
            simulation.RemoveBlock(transient);

            simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());

            Assert.Single(simulation.Solver.Nodes);
            Assert.Empty(simulation.Solver.Links);
        }
    }
}
