using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    public class FrankenHullTests
    {
        private readonly ITestOutputHelper output;

/// <summary>FrankenHullTests operation.</summary>
        public FrankenHullTests(ITestOutputHelper output)
        {
            this.output = output;
        }

/// <summary>Part operation.</summary>
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

        [Fact]
/// <summary>TiledHullsNeverLandOnTopOfEachOther operation.</summary>
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

            Assert.Equal(manifest.Blocks, hull.Placed.Count);

/// <summary>HashSet operation.</summary>
            HashSet<Vector3I> occupied = new HashSet<Vector3I>();
            foreach (BlockInstance block in hull.Placed)
            {
                Assert.True(occupied.Add(block.Min),
                    "two hulls were tiled into the same cell at " + block.Min);
            }

            Assert.Equal(new Vector3I(6, 5, 3), manifest.Pitch);
        }

        [Fact]
/// <summary>TheLatticeIsACubeSoTheHullHasNeighboursInEveryDirection operation.</summary>
        public void TheLatticeIsACubeSoTheHullHasNeighboursInEveryDirection()
        {
/// <summary>Part operation.</summary>
            List<Blueprints.Grid> parts = new List<Blueprints.Grid> { Part("cube", new Vector3I(4, 4, 4)) };

            FrankenHull.Manifest manifest = new FrankenHull.Manifest();
            FrankenHull.Tile(parts, new[] { "cube" }, 64 * 27, manifest);

            output.WriteLine(manifest.Describe());

            Assert.Equal(manifest.Lattice.X, manifest.Lattice.Y);
            Assert.Equal(manifest.Lattice.Y, manifest.Lattice.Z);
            Assert.True(manifest.Lattice.X >= 3,
                "twenty-seven copies should need a lattice at least three a side");
        }

        [Fact]
/// <summary>ItStopsWithinOneHullOfWhatWasAskedFor operation.</summary>
        public void ItStopsWithinOneHullOfWhatWasAskedFor()
        {
/// <summary>Part operation.</summary>
            List<Blueprints.Grid> parts = new List<Blueprints.Grid> { Part("cube", new Vector3I(4, 4, 4)) };

            FrankenHull.Manifest manifest = new FrankenHull.Manifest();
            FrankenHull.Tile(parts, new[] { "cube" }, 1000, manifest);

            output.WriteLine(manifest.Describe());

            Assert.True(manifest.Blocks >= 1000, "it stopped short of the target");
            Assert.True(manifest.Blocks < 1000 + parts[0].Blocks,
                "it overshot by more than the hull it was placing when it got there");
        }

        [Fact]
/// <summary>TheWeldedHullBuildsIntoOneSimulation operation.</summary>
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

            Assert.True(simulation.Solver.LinkCount > simulation.Solver.Nodes.Count,
                "the welded hull has fewer links than blocks, so the seams are not touching");
        }

        [Fact]
/// <summary>NothingToBuildFromIsAnEmptyHull operation.</summary>
        public void NothingToBuildFromIsAnEmptyHull()
        {
            FrankenHull.Manifest manifest = new FrankenHull.Manifest();

            Assert.Empty(FrankenHull.Tile(null, null, 1000, manifest).Placed);
            Assert.Empty(FrankenHull.Tile(new List<Blueprints.Grid>(), null, 1000, manifest).Placed);
            Assert.Empty(FrankenHull.LargestFirst("no-such-file.csv"));
        }
    }
}
