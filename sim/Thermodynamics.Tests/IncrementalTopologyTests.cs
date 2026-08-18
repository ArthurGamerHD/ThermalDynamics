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

        // ---- removal ------------------------------------------------------------------------

        /// <summary>
        /// A grid built whole and then ground down must end up as the same graph as the same
        /// blocks built from scratch.
        ///
        /// Removal is the hard direction. A block placed adds links and disturbs nothing; a block
        /// removed has to have its links found and unpicked, and its node taken out of a list
        /// whose indices every link refers to. Getting that subtly wrong does not throw — it
        /// leaves a link pointing at the wrong node, which conducts heat between two blocks that
        /// do not touch and is invisible until someone notices a cold block warming.
        /// </summary>
        [Fact]
        public void GrindingBlocksOffLeavesTheSameGraphAsNeverBuildingThem()
        {
            List<Vector3I> cells = Shape();

            // A mixture: an interior cell with many neighbours, a spur end with one, an island
            // with none, and one either side of the thin joint.
            Vector3I[] removed =
            {
                new Vector3I(1, 1, 1),
                new Vector3I(4, 1, 1),
                new Vector3I(-3, 0, 0),
                new Vector3I(0, 0, 0),
                new Vector3I(2, 2, 3),
            };

            ThermalSimulation ground = BuiltAllAtOnce(cells);
            for (int i = 0; i < removed.Length; i++)
            {
                BlockInstance block = ground.Grid.GetAtCell(removed[i]);
                Assert.NotNull(block);
                ground.RemoveBlock(block);
                ground.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());
            }
            while (ground.HasPendingWork) ground.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());

            List<Vector3I> remaining = new List<Vector3I>();
            for (int i = 0; i < cells.Count; i++)
            {
                bool dropped = false;
                for (int r = 0; r < removed.Length; r++)
                {
                    if (cells[i] == removed[r]) dropped = true;
                }
                if (!dropped) remaining.Add(cells[i]);
            }

            // Rebuilt with the same block models on the same cells, so conductances match.
            ThermalSimulation fresh = BuiltAllAtOnce(cells);
            for (int i = 0; i < removed.Length; i++)
            {
                BlockInstance block = fresh.Grid.GetAtCell(removed[i]);
                fresh.Grid.Remove(block);
                fresh.Solver.RemoveBlock(block);
                fresh.Surfaces.RemoveBlock(block);
            }
            fresh.Solver.RebuildLinks();

            Assert.Equal(remaining.Count, ground.Solver.Nodes.Count);
            Assert.Equal(LinkSignature(fresh.Solver), LinkSignature(ground.Solver));
        }

        /// <summary>
        /// Blocks placed and removed in an arbitrary order, checked against a rebuild after every
        /// change.
        ///
        /// The cases worth catching here are the ones nobody thinks to write by hand: removing
        /// the node that happens to be last in the list, removing the one that a previous removal
        /// moved, removing a link that a previous removal moved, and doing all of it while other
        /// blocks are still queued to be linked. The sequence is generated rather than chosen, and
        /// written out rather than taken from <c>Random</c>, so a failure reproduces exactly.
        /// </summary>
        [Fact]
        public void ArbitraryBuildingAndGrindingAlwaysMatchesARebuild()
        {
            GridModel grid = new GridModel(Catalog.LargeGridSize);
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), grid);

            List<Vector3I> plot = new List<Vector3I>();
            for (int z = 0; z < 5; z++)
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                        plot.Add(new Vector3I(x, y, z));

            uint state = 0x12345678u;
            int steps = 400;

            for (int step = 0; step < steps; step++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;

                Vector3I cell = plot[(int)(state % (uint)plot.Count)];
                BlockInstance occupant = grid.GetAtCell(cell);

                if (occupant != null)
                {
                    simulation.RemoveBlock(occupant);
                }
                else
                {
                    simulation.AddBlock(
                        new BlockInstance(Model(step), cell, BlockOrientation.Identity), 293.15f);
                }

                simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());

                // Compared against what a rebuild of the very same solver would produce, so the
                // check is of the incremental bookkeeping alone and not of two different grids.
                List<string> incremental = LinkSignature(simulation.Solver);
                simulation.Solver.RebuildLinks();
                List<string> rebuilt = LinkSignature(simulation.Solver);

                Assert.True(rebuilt.Count == incremental.Count && Same(rebuilt, incremental),
                    "graph diverged at step " + step + " on cell " + cell
                    + ": incremental had " + incremental.Count + " links, a rebuild "
                    + rebuilt.Count);
            }
        }

        private static bool Same(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>
        /// Every node's link count must agree with the graph after churn. It is what the substep
        /// estimate and the diagnostics read, and it is maintained by hand on both paths.
        /// </summary>
        [Fact]
        public void LinkCountsPerNodeSurviveChurn()
        {
            List<Vector3I> cells = Shape();
            ThermalSimulation simulation = BuiltAllAtOnce(cells);

            simulation.RemoveBlock(simulation.Grid.GetAtCell(new Vector3I(1, 1, 1)));
            simulation.RemoveBlock(simulation.Grid.GetAtCell(new Vector3I(4, 1, 1)));
            simulation.AddBlock(new BlockInstance(Catalog.HeavyArmor(), new Vector3I(1, 1, 1),
                BlockOrientation.Identity), 293.15f);
            simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());

            int[] counted = new int[simulation.Solver.Nodes.Count];
            IList<ThermalLink> links = simulation.Solver.Links;
            for (int i = 0; i < links.Count; i++)
            {
                counted[links[i].NodeA]++;
                counted[links[i].NodeB]++;
            }

            for (int i = 0; i < counted.Length; i++)
            {
                Assert.Equal(counted[i], simulation.Solver.Nodes[i].LinkCount);
            }
        }

        /// <summary>
        /// Grinding must not disturb the temperatures of the blocks left standing.
        ///
        /// The end-to-end check for removal, and the one that catches a link left pointing at the
        /// wrong node: a stray link conducts between two blocks that do not touch, which no
        /// structural comparison of counts would notice.
        /// </summary>
        [Fact]
        public void GrindingDoesNotDisturbWhatIsLeftStanding()
        {
            List<Vector3I> cells = Shape();
            Vector3I doomed = new Vector3I(4, 1, 1);

            ThermalSimulation ground = BuiltAllAtOnce(cells);
            SeedByPosition(ground);
            ground.RemoveBlock(ground.Grid.GetAtCell(doomed));
            ground.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());

            ThermalSimulation reference = BuiltAllAtOnce(cells);
            SeedByPosition(reference);
            BlockInstance block = reference.Grid.GetAtCell(doomed);
            reference.Grid.Remove(block);
            reference.Solver.RemoveBlock(block);
            reference.Surfaces.RemoveBlock(block);
            reference.Solver.RebuildLinks();

            EnvironmentSample sample = Worlds.Space(new Vector3(0f, 1f, 0f));
            ground.StepExact(40, sample);
            reference.StepExact(40, sample);

            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i] == doomed) continue;

                ThermalNode a = ground.Solver.GetNodeAt(cells[i]);
                ThermalNode b = reference.Solver.GetNodeAt(cells[i]);
                Assert.NotNull(a);
                Assert.NotNull(b);
                Assert.Equal(b.Temperature, a.Temperature, 3);
            }
        }

        /// <summary>
        /// Enough blocks placed and taken away without a step in between to grow the node buffers
        /// past their starting size.
        ///
        /// A placement does not size the buffers — the step that drains the queue does — so the
        /// arrays a removal walks can be shorter than the node list. Reading past one throws
        /// IndexOutOfRangeException, which the game's script whitelist prohibits, so it is not
        /// even catchable: it would take the grid's update down for the rest of the session. The
        /// small shapes elsewhere in this file never reach the first growth and would never have
        /// found it.
        /// </summary>
        [Fact]
        public void PlacingAndRemovingManyBlocksWithoutSteppingStaysInBounds()
        {
            GridModel grid = new GridModel(Catalog.LargeGridSize);
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), grid);

            // One step first, so the graph is clean and removal takes the incremental path
            // rather than the plain one a dirty graph falls back to.
            simulation.AddBlock(new BlockInstance(Catalog.HeavyArmor(), Vector3I.Zero,
                BlockOrientation.Identity), 293.15f);
            simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());

            List<BlockInstance> placed = new List<BlockInstance>();
            for (int i = 1; i < 600; i++)
            {
                BlockInstance block = new BlockInstance(Catalog.HeavyArmor(),
                    new Vector3I(0, 0, i), BlockOrientation.Identity);
                simulation.AddBlock(block, 293.15f);
                placed.Add(block);
            }

            // Forwards, not backwards. Removing the last node in the list is the one case that
            // never moves another node into the hole, so a run that grinds from the end exercises
            // none of the index repair and would pass with the bounds problem still there.
            for (int i = 0; i < placed.Count; i++)
            {
                simulation.RemoveBlock(placed[i]);
            }

            simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());

            Assert.Single(simulation.Solver.Nodes);
            Assert.Empty(simulation.Solver.Links);
        }

        /// <summary>
        /// The same, but stepping between so the links really exist before they are unpicked.
        /// </summary>
        [Fact]
        public void GrindingDownALongRunThatWasFullyLinkedLeavesNothingBehind()
        {
            GridModel grid = new GridModel(Catalog.LargeGridSize);
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), grid);

            List<BlockInstance> placed = new List<BlockInstance>();
            for (int i = 0; i < 600; i++)
            {
                BlockInstance block = new BlockInstance(Catalog.HeavyArmor(),
                    new Vector3I(0, 0, i), BlockOrientation.Identity);
                simulation.AddBlock(block, 293.15f);
                placed.Add(block);
            }

            simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());
            simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());
            Assert.Equal(599, simulation.Solver.Links.Count);

            for (int i = 0; i < placed.Count; i++)
            {
                simulation.RemoveBlock(placed[i]);
                simulation.Update(LoadBenchmarks.TickSeconds, Worlds.Shadow());
            }

            Assert.Empty(simulation.Solver.Nodes);
            Assert.Empty(simulation.Solver.Links);
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
