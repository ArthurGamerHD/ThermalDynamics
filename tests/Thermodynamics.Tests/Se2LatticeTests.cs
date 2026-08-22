using System;
using System.Collections.Generic;
using System.Diagnostics;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The one constraint SE2's lattice imposes: **nothing whose cost matters may be proportional to a
    /// block's volume**, since a 5 m block spans 8,000 cells there against one here. Contact area,
    /// face area, surface area and conduction depth all fall out of two integer AABBs, and these pin
    /// that end to end on mixed-size grids.
    ///
    /// <para>
    /// **They do not claim SE2 is supported.** Block *storage* is still per cell, which is the open
    /// item — see engine-notes.md and scale-design.md, Implementation status.
    /// </para>
    /// </summary>
    public class Se2LatticeTests
    {
        /// <summary>The common lattice the eight SE2 block sizes share.</summary>
        private const float LatticeMetres = 0.25f;

        /// <summary>SE2's shipped block sizes, in lattice cells: 0.25 m through 5 m.</summary>
        private static readonly int[] SizesInCells = { 1, 2, 4, 5, 6, 10, 14, 20 };

        private static BlockThermalProperties Steel()
        {
            return new BlockThermalProperties
            {
                Conductivity = 50f,
                SpecificHeat = 450f,
                Emissivity = 0.2f,
                ExposedSurfaceMultiplier = 1f,
                ProducerWasteEnergy = 0.05f,
                ConsumerWasteEnergy = 0.05f,
                CriticalTemperature = 1400f,
                OverheatDamagePerKelvin = 1f,
            };
        }

        /// <summary>A cubic block of <paramref name="cells"/> lattice cells on a side.</summary>
        private static BlockModel Cube(int cells)
        {
            // Mass scales with volume, as it must for a size range this wide to behave.
            float mass = 30f * cells * cells * cells;
            return BlockModel.Solid(cells + "cell", new Vector3I(cells, cells, cells), mass, Steel());
        }

        // ---- the constraint: geometry from bounds, never from cells --------------------------

        /// <summary>
        /// The geometry must answer for blocks far larger than anything that could be enumerated.
        ///
        /// A 400-cell cube is 64 million lattice cells — a hundred metres on a side. Nothing in
        /// SE2 is that large, and that is the point: if any of these were walking cells the test
        /// would not finish, so completing it at all is the proof. The figures are checked as well
        /// as the timing, because a fast wrong answer is worse than a slow right one.
        /// </summary>
        [Fact]
        public void GeometryIsAnsweredFromBoundsAndNotFromCells()
        {
            Vector3I a = Vector3I.Zero;
            Vector3I aMax = new Vector3I(400, 400, 400);
            Vector3I b = new Vector3I(400, 0, 0);
            Vector3I bMax = new Vector3I(800, 400, 400);

            Stopwatch watch = Stopwatch.StartNew();

            int contact = BoxGeometry.ContactCells(a, aMax, b, bMax);
            int face = BoxGeometry.FaceAreaCells(aMax - a, Face.Right);
            int surface = BoxGeometry.SurfaceAreaCells(aMax - a);
            float depth = BoxGeometry.Depth(aMax - a, 0, LatticeMetres);

            watch.Stop();

            Assert.Equal(400 * 400, contact);
            Assert.Equal(400 * 400, face);
            Assert.Equal(6 * 400 * 400, surface);
            Assert.Equal(100f, depth, 4);

            // Sixty-four million cells apiece. Anything per-cell is minutes, not milliseconds.
            Assert.True(watch.Elapsed.TotalMilliseconds < 50d,
                "the geometry took " + watch.Elapsed.TotalMilliseconds.ToString("n2")
                + " ms for a pair of 64-million-cell boxes, which is the signature of a per-cell walk");
        }

        /// <summary>
        /// Two blocks of different sizes share exactly the area of the smaller face, whichever way
        /// round they are asked. Partial overlap is the ordinary case on a mixed lattice — a 5 m
        /// block bolted to a 0.5 m one touches over the small block's face, not the large one's.
        /// </summary>
        [Fact]
        public void ContactBetweenDifferentSizesIsTheOverlapAndIsSymmetric()
        {
            Vector3I bigMin = Vector3I.Zero;
            Vector3I bigMax = new Vector3I(20, 20, 20);

            // A 0.5 m block against the middle of the big one's +X face.
            Vector3I smallMin = new Vector3I(20, 8, 8);
            Vector3I smallMax = new Vector3I(22, 10, 10);

            int forward = BoxGeometry.ContactCells(bigMin, bigMax, smallMin, smallMax);
            int backward = BoxGeometry.ContactCells(smallMin, smallMax, bigMin, bigMax);

            Assert.Equal(4, forward);
            Assert.Equal(forward, backward);
        }

        // ---- the lattice, end to end ----------------------------------------------------------

        /// <summary>
        /// All eight SE2 sizes on one grid, touching in a row, simulated.
        ///
        /// This is the shape SE1 never produces: a single grid whose blocks differ in volume by a
        /// factor of eight thousand. Everything downstream — the conduction graph, the exposure
        /// pass, the substep estimate — has to cope with that spread rather than with the uniform
        /// cells the rest of the suite uses.
        /// </summary>
        [Fact]
        public void EveryBlockSizeSe2ShipsCoexistsOnOneLattice()
        {
            GridModel grid = new GridModel(LatticeMetres);
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), grid);

            int x = 0;
            for (int i = 0; i < SizesInCells.Length; i++)
            {
                int cells = SizesInCells[i];
                simulation.AddBlock(new BlockInstance(Cube(cells), new Vector3I(x, 0, 0),
                    BlockOrientation.Identity), 300f + (10f * i));
                x += cells;
            }

            simulation.RebuildAll();

            Assert.Equal(SizesInCells.Length, simulation.Solver.Nodes.Count);

            // A row of eight blocks is seven joints, and every neighbouring pair really touches.
            Assert.Equal(SizesInCells.Length - 1, simulation.Solver.Links.Count);

            simulation.StepExact(40, EnvironmentSample.DarkVacuum());

            for (int i = 0; i < simulation.Solver.Nodes.Count; i++)
            {
                float temperature = simulation.Solver.Nodes[i].Temperature;
                Assert.False(float.IsNaN(temperature), "block " + i + " went NaN");
                Assert.True(temperature > 0f && temperature < 10000f,
                    "block " + i + " reached " + temperature + " K");
            }
        }

        /// <summary>
        /// Conduction across a joint between two very differently sized blocks must move exactly as
        /// much heat out of one as it moves into the other.
        ///
        /// The asymmetric joint is where the original model lost energy — it derived a coefficient
        /// per block from that block's own geometry, so the two ends disagreed about how much
        /// crossed. On an SE1 grid the worst asymmetry is a 1x1x1 against a 3x3x4; on an SE2
        /// lattice it is a 1-cell block against a 20-cell one, which is the same defect with two
        /// more orders of magnitude behind it.
        /// </summary>
        [Fact]
        public void AJointBetweenTheSmallestAndLargestBlockConservesEnergy()
        {
            GridModel grid = new GridModel(LatticeMetres);
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), grid);

            simulation.AddBlock(new BlockInstance(Cube(20), Vector3I.Zero,
                BlockOrientation.Identity), 800f);
            simulation.AddBlock(new BlockInstance(Cube(1), new Vector3I(20, 0, 0),
                BlockOrientation.Identity), 300f);
            simulation.RebuildAll();

            // Conduction alone: no radiation or convection to leak energy legitimately.
            ThermalSettings settings = simulation.Settings;
            settings.EnableEnvironment = false;
            settings.EnableRadiation = false;
            settings.EnableConvection = false;
            settings.EnableSolarHeat = false;
            settings.EnableWasteHeat = false;
            settings.Derive();

            float before = simulation.Solver.TotalEnergy;
            simulation.StepExact(200, EnvironmentSample.DarkVacuum());
            float after = simulation.Solver.TotalEnergy;

            float drift = Math.Abs(after - before) / Math.Max(1f, Math.Abs(before));
            Assert.True(drift < 1e-4f,
                "energy moved by " + drift.ToString("e2") + " across a 1-cell to 20-cell joint: "
                + before + " -> " + after);
        }

        /// <summary>
        /// A tiny block bolted to a huge one is stiffer than two huge ones, and the integrator's
        /// own estimate has to see it.
        ///
        /// This is the effect <c>docs/scale-design.md</c>, Variable block size breaks the integrator, warns about: conductance over
        /// capacity goes as one over size squared, so across SE2's range of block sizes the
        /// stiffest pairing is far harder to integrate than the gentlest. If the estimate did not
        /// respond to that, a mixed-size grid would be integrated at whatever step suited its
        /// largest blocks and its smallest would oscillate.
        ///
        /// At the default step neither pairing needs more than one substep — this asserts the
        /// ratio, not that the clamp fires, and the companion test below shows the estimate
        /// crossing into real substeps once the step is long enough for it to matter.
        /// </summary>
        [Fact]
        public void TheSubstepEstimateRespondsToTheBlockSizeRatio()
        {
            float gentle = SubstepsFor(20, 20, 0.25f);
            float stiff = SubstepsFor(1, 20, 0.25f);

            Assert.True(stiff > gentle * 4f,
                "a 0.25 m block against a 5 m block estimated " + stiff
                + " substeps against " + gentle + " for two 5 m blocks; the stiffness of the size "
                + "range is not reaching the integrator");
        }

        /// <summary>
        /// And over a step long enough to matter, the small pairing really does take more substeps
        /// than the large one — the estimate turning into behaviour rather than staying a number.
        /// </summary>
        [Fact]
        public void OverALongStepTheSmallBlockForcesMoreSubstepsThanTheLargeOne()
        {
            const float longStep = 8f;

            int gentle = (int)Math.Ceiling(Math.Max(1f, SubstepsFor(20, 20, longStep)));
            int stiff = (int)Math.Ceiling(Math.Max(1f, SubstepsFor(1, 20, longStep)));

            Assert.True(stiff > gentle,
                "over an eight second step the 0.25 m pairing took " + stiff
                + " substeps and the 5 m pairing " + gentle);
            Assert.True(stiff > 1, "the stiff pairing should need real substepping, got " + stiff);
        }

        private static float SubstepsFor(int firstCells, int secondCells, float stepSeconds)
        {
            GridModel grid = new GridModel(LatticeMetres);
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), grid);

            simulation.AddBlock(new BlockInstance(Cube(firstCells), Vector3I.Zero,
                BlockOrientation.Identity), 900f);
            simulation.AddBlock(new BlockInstance(Cube(secondCells), new Vector3I(firstCells, 0, 0),
                BlockOrientation.Identity), 300f);
            simulation.RebuildAll();

            return simulation.Solver.RequiredSubsteps(stepSeconds);
        }

        /// <summary>
        /// However stiff the pairing, the integrator must stay bounded rather than oscillating
        /// away. The clamp is what makes a mixed-size lattice survivable at all.
        /// </summary>
        [Fact]
        public void TheStiffestPairingOnTheLatticeStaysBounded()
        {
            GridModel grid = new GridModel(LatticeMetres);
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), grid);

            simulation.AddBlock(new BlockInstance(Cube(1), Vector3I.Zero,
                BlockOrientation.Identity), 2000f);
            simulation.AddBlock(new BlockInstance(Cube(20), new Vector3I(1, 0, 0),
                BlockOrientation.Identity), 250f);
            simulation.RebuildAll();

            simulation.StepExact(400, EnvironmentSample.DarkVacuum());

            for (int i = 0; i < simulation.Solver.Nodes.Count; i++)
            {
                float temperature = simulation.Solver.Nodes[i].Temperature;
                Assert.False(float.IsNaN(temperature) || float.IsInfinity(temperature),
                    "block " + i + " diverged");
                Assert.InRange(temperature, 1f, 2500f);
            }
        }

        // ---- this session's changes, on SE2-shaped grids --------------------------------------

        /// <summary>
        /// Building a mixed-size grid one block at a time must produce the same conduction graph as
        /// building it whole — the same claim <c>IncrementalTopologyTests</c> makes, restated on
        /// blocks that span thousands of cells rather than one.
        ///
        /// The incremental builder reads a block's bounds and its per-face surface bits, both of
        /// which are the same shape whatever the block's volume. That is why it holds here; this
        /// asserts it rather than assuming it.
        /// </summary>
        [Fact]
        public void IncrementalTopologyHoldsOnAMixedSizeLattice()
        {
            List<Vector3I> placed = new List<Vector3I>();
            List<int> sizes = new List<int>();

            int x = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < SizesInCells.Length; i++)
                {
                    placed.Add(new Vector3I(x, 0, 0));
                    sizes.Add(SizesInCells[i]);
                    x += SizesInCells[i];
                }
            }

            ThermalSimulation whole = Assemble(placed, sizes, false);
            ThermalSimulation incremental = Assemble(placed, sizes, true);

            Assert.Equal(whole.Solver.Nodes.Count, incremental.Solver.Nodes.Count);
            Assert.Equal(Signature(whole), Signature(incremental));
        }

        /// <summary>
        /// And grinding blocks off a mixed-size grid must leave the same graph as never having
        /// built them — removal is the direction that has to unpick links rather than only append.
        /// </summary>
        [Fact]
        public void GrindingAMixedSizeLatticeLeavesTheSameGraphAsARebuild()
        {
            List<Vector3I> placed = new List<Vector3I>();
            List<int> sizes = new List<int>();

            int x = 0;
            for (int i = 0; i < SizesInCells.Length; i++)
            {
                placed.Add(new Vector3I(x, 0, 0));
                sizes.Add(SizesInCells[i]);
                x += SizesInCells[i];
            }

            ThermalSimulation simulation = Assemble(placed, sizes, false);

            // The 5 m block and a 1.25 m one, out of the middle of the row.
            simulation.RemoveBlock(simulation.Grid.GetAtCell(placed[7]));
            simulation.RemoveBlock(simulation.Grid.GetAtCell(placed[3]));
            simulation.Update(1f / 60f, Worlds.Shadow());

            List<string> afterGrinding = Signature(simulation);
            simulation.Solver.RebuildLinks();

            Assert.Equal(Signature(simulation), afterGrinding);
        }

        /// <summary>
        /// A step spread across frames must be bit-identical to one run whole on a mixed-size
        /// lattice too.
        ///
        /// <c>SpreadStepTests</c> makes this claim on a hull of one-cell blocks. The spreading
        /// slices by node and by link, and a node is a block whatever its volume — so the property
        /// should carry, and the reason it should is worth having a test behind on the shape the
        /// suite otherwise never builds.
        /// </summary>
        [Fact]
        public void ASpreadStepIsIdenticalOnAMixedSizeLattice()
        {
            List<Vector3I> placed = new List<Vector3I>();
            List<int> sizes = new List<int>();

            int x = 0;
            for (int i = 0; i < SizesInCells.Length; i++)
            {
                placed.Add(new Vector3I(x, 0, 0));
                sizes.Add(SizesInCells[i]);
                x += SizesInCells[i];
            }

            ThermalSimulation whole = Assemble(placed, sizes, false);
            ThermalSimulation spread = Assemble(placed, sizes, false);

            EnvironmentState state = EnvironmentSolver.Solve(
                whole.Settings, whole.Planet, EnvironmentSample.DarkVacuum());

            for (int s = 0; s < 20; s++)
            {
                whole.Solver.Step(whole.Settings.StepSeconds, state);

                spread.Solver.BeginStep(spread.Settings.StepSeconds, state);
                while (!spread.Solver.AdvanceStep(3)) { }
            }

            for (int i = 0; i < placed.Count; i++)
            {
                float expected = whole.Solver.GetNodeAt(placed[i]).Temperature;
                float actual = spread.Solver.GetNodeAt(placed[i]).Temperature;

                Assert.True(expected.Equals(actual),
                    "block " + i + " of " + sizes[i] + " cells differs: whole "
                    + expected.ToString("r") + ", spread " + actual.ToString("r"));
            }
        }

        // ---- helpers ---------------------------------------------------------------------------

        private static ThermalSimulation Assemble(
            List<Vector3I> placed, List<int> sizes, bool oneAtATime)
        {
            GridModel grid = new GridModel(LatticeMetres);
            ThermalSimulation simulation = new ThermalSimulation(new ThermalSettings(), grid);

            for (int i = 0; i < placed.Count; i++)
            {
                simulation.AddBlock(new BlockInstance(Cube(sizes[i]), placed[i],
                    BlockOrientation.Identity), 300f + i);

                // A frame between each, so every placement goes through the incremental path on
                // its own rather than arriving as one batch.
                if (oneAtATime) simulation.Update(1f / 60f, Worlds.Shadow());
            }

            if (oneAtATime)
            {
                while (simulation.HasPendingWork) simulation.Update(1f / 60f, Worlds.Shadow());
            }
            else
            {
                simulation.RebuildAll();
            }

            return simulation;
        }

        /// <summary>Every link as a sorted, position-keyed row, so two graphs can be compared.</summary>
        private static List<string> Signature(ThermalSimulation simulation)
        {
            List<string> rows = new List<string>();
            IList<ThermalLink> links = simulation.Solver.Links;

            for (int i = 0; i < links.Count; i++)
            {
                Vector3I a = simulation.Solver.Nodes[links[i].NodeA].Block.Position;
                Vector3I b = simulation.Solver.Nodes[links[i].NodeB].Block.Position;

                string low = a.X + "," + a.Y + "," + a.Z;
                string high = b.X + "," + b.Y + "," + b.Z;
                if (string.CompareOrdinal(low, high) > 0)
                {
                    string swap = low;
                    low = high;
                    high = swap;
                }

                rows.Add(low + "|" + high + "|" + links[i].Conductance.ToString("f6")
                    + "|" + links[i].ContactFaces);
            }

            rows.Sort(StringComparer.Ordinal);
            return rows;
        }
    }
}
