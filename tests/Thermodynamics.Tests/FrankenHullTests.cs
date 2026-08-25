using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The million-block grid welded out of real ships (`G5`), held to the things that make it a
    /// grid rather than a pile.
    ///
    /// <para>
    /// **Built from hulls this file makes, not from the corpus.** The tiling is the part that can
    /// be wrong — two ships in the same cells, a lattice that is a line, a target overshot by a
    /// whole ship — and none of that needs a blueprint to check. What needs the corpus is the
    /// *mixture*, and that is what `bench franken` measures rather than what a test asserts.
    /// </para>
    /// </summary>
    public class FrankenHullTests
    {
        private readonly ITestOutputHelper output;

        public FrankenHullTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>A solid box of light armour, standing in for a ship's main grid.</summary>
        private static Blueprints.Grid Part(string name, Vector3I size)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, size);

            return new Blueprints.Grid
            {
                Name = name,
                Large = true,
                Blocks = builder.Placed.Count,
                Builder = builder,
            };
        }

        /// <summary>
        /// **No two hulls may occupy the same cell**, which is the one way tiling can silently
        /// produce a smaller grid than it reports: `GridModel.Add` would keep one block per cell
        /// while the manifest counted both.
        /// </summary>
        [Fact]
        public void TiledHullsNeverLandOnTopOfEachOther()
        {
            List<Blueprints.Grid> parts = new List<Blueprints.Grid>
            {
                Part("wide", new Vector3I(6, 2, 3)),
                Part("tall", new Vector3I(2, 5, 2)),
            };

            FrankenHull.Manifest manifest = new FrankenHull.Manifest();
            GridBuilder hull = FrankenHull.Tile(parts, new[] { "wide", "tall" }, 400, manifest);

            output.WriteLine(manifest.Describe());

            // Every placement is still in the grid, so the count the manifest reports is the count
            // the grid holds.
            Assert.Equal(manifest.Blocks, hull.Placed.Count);

            HashSet<Vector3I> occupied = new HashSet<Vector3I>();
            foreach (BlockInstance block in hull.Placed)
            {
                Assert.True(occupied.Add(block.Min),
                    "two hulls were tiled into the same cell at " + block.Min);
            }

            // The pitch is the largest box, in each axis independently, so neither part overhangs.
            Assert.Equal(new Vector3I(6, 5, 3), manifest.Pitch);
        }

        /// <summary>
        /// **A cube of hulls rather than a chain.** A grid's cost is set by how many neighbours a
        /// block has, so a million blocks laid out end to end would be a million blocks with the
        /// link count of a rope and would understate everything the rig exists to measure.
        /// </summary>
        [Fact]
        public void TheLatticeIsACubeSoTheHullHasNeighboursInEveryDirection()
        {
            List<Blueprints.Grid> parts = new List<Blueprints.Grid> { Part("cube", new Vector3I(4, 4, 4)) };

            FrankenHull.Manifest manifest = new FrankenHull.Manifest();
            FrankenHull.Tile(parts, new[] { "cube" }, 64 * 27, manifest);

            output.WriteLine(manifest.Describe());

            Assert.Equal(manifest.Lattice.X, manifest.Lattice.Y);
            Assert.Equal(manifest.Lattice.Y, manifest.Lattice.Z);
            Assert.True(manifest.Lattice.X >= 3,
                "twenty-seven copies should need a lattice at least three a side");
        }

        /// <summary>
        /// It stops at the target rather than at the end of a pass, so asking for a million does not
        /// silently build one and a half.
        /// </summary>
        [Fact]
        public void ItStopsWithinOneHullOfWhatWasAskedFor()
        {
            List<Blueprints.Grid> parts = new List<Blueprints.Grid> { Part("cube", new Vector3I(4, 4, 4)) };

            FrankenHull.Manifest manifest = new FrankenHull.Manifest();
            FrankenHull.Tile(parts, new[] { "cube" }, 1000, manifest);

            output.WriteLine(manifest.Describe());

            Assert.True(manifest.Blocks >= 1000, "it stopped short of the target");
            Assert.True(manifest.Blocks < 1000 + parts[0].Blocks,
                "it overshot by more than the hull it was placing when it got there");
        }

        /// <summary>
        /// The hull it produces is one the solver can actually take: it builds, it links, and its
        /// blocks are the ones that were placed.
        /// </summary>
        [Fact]
        public void TheWeldedHullBuildsIntoOneSimulation()
        {
            List<Blueprints.Grid> parts = new List<Blueprints.Grid>
            {
                Part("a", new Vector3I(5, 3, 3)),
                Part("b", new Vector3I(3, 3, 5)),
            };

            FrankenHull.Manifest manifest = new FrankenHull.Manifest();
            GridBuilder hull = FrankenHull.Tile(parts, new[] { "a", "b" }, 2000, manifest);

            ThermalSimulation simulation = hull.BuildSimulation(new ThermalSettings().Derive());
            simulation.RebuildAll();

            output.WriteLine(manifest.Describe() + "; "
                + simulation.Solver.Nodes.Count.ToString("n0") + " nodes, "
                + simulation.Solver.LinkCount.ToString("n0") + " links");

            Assert.Equal(manifest.Blocks, simulation.Solver.Nodes.Count);

            // Tiled with no gap, so neighbouring hulls touch and the graph is not one component per
            // ship. Fewer links than nodes would mean the seams carry nothing at all.
            Assert.True(simulation.Solver.LinkCount > simulation.Solver.Nodes.Count,
                "the welded hull has fewer links than blocks, so the seams are not touching");
        }

        /// <summary>
        /// Nothing to build from is an empty grid rather than a throw: the corpus is opt-in, and a
        /// machine without it runs this file too.
        /// </summary>
        [Fact]
        public void NothingToBuildFromIsAnEmptyHull()
        {
            FrankenHull.Manifest manifest = new FrankenHull.Manifest();

            Assert.Empty(FrankenHull.Tile(null, null, 1000, manifest).Placed);
            Assert.Empty(FrankenHull.Tile(new List<Blueprints.Grid>(), null, 1000, manifest).Placed);
            Assert.Empty(FrankenHull.LargestFirst("no-such-file.csv"));
        }
    }
}
