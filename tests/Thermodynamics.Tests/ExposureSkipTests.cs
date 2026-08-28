using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The exposure refresh writes only the nodes whose faces moved, and what makes that safe is
    /// that writing all of them would have produced **the same bits** (`D8`).
    ///
    /// <para>
    /// The refresh runs over every node whenever the room map republishes. On a hull nobody is
    /// building, every one of those nodes gets the answer it already had — and before Pass 9's
    /// seventh iteration each was written anyway and marked `StateDirty`, so the next
    /// `SyncNodeState` re-mirrored the whole grid and, on a server, resent it. Skipping is only
    /// correct if a node that was skipped was genuinely identical, and the failure it would cause is
    /// silent: a stale radiating area looks like physics.
    /// </para>
    ///
    /// <para>
    /// The reference here is the code the skip replaced, not the skip's own intent — six per-face
    /// writes and a `RefreshExposure`, which is unconditional and therefore cannot skip anything.
    /// The last two tests are what keep the rest honest: without them a skip that never engaged, or
    /// one that swallowed a real change, would pass every equivalence assertion above (`E8`).
    /// </para>
    /// </summary>
    [Trait("speed", "slow")]
    public class ExposureSkipTests
    {
        /// <summary>
        /// Applies the *replaced* code to every node: six single-face writes and an unconditional
        /// refresh, which is what `FacePackingTests` pins the packed overload against. Reading the
        /// counts back out and writing them straight in again is deliberate — it is a no-op for a
        /// node that has not moved, and it must be a no-op for the skip too, or the two runs will
        /// not agree.
        /// </summary>
        private static void RewriteEveryNodeTheOldWay(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                ThermalNode node = nodes[i];
                for (int f = 0; f < Face.Count; f++)
                {
                    node.SetExposedFaces(f, node.GetExposedFaces(f));
                }
                node.RefreshExposure();
            }
        }

        private static void Step(ThermalSimulation simulation, int steps)
        {
            EnvironmentState state = EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, Worlds.Flight(1f, 300f));

            for (int i = 0; i < steps; i++)
            {
                simulation.Solver.RefreshExposure(simulation.Rooms.Map);
                simulation.Solver.Step(simulation.Settings.StepSeconds, state);
            }
        }

        /// <summary>
        /// The case the skip exists for. Twenty refreshes and steps on a hull whose exposure never
        /// changes: one run lets the skip do its work, the other forces every node through the code
        /// it replaced first, so nothing in it can be skipped. Every temperature and every published
        /// watt figure has to agree bit for bit.
        /// </summary>
        [Fact]
        public void SkippingAnUnchangedNodeProducesTheSameStepAsWritingIt()
        {
            ThermalSimulation skipping = Hulls.Driven(Hulls.Uncapped());
            ThermalSimulation writing = Hulls.Driven(Hulls.Uncapped());

            // The per-mechanism watts are published only when they are asked for, and a grid of
            // zeros agrees with a grid of zeros — `AssertVaried` catches that, and this is what
            // stops it having to.
            skipping.Solver.CollectDiagnostics = true;
            writing.Solver.CollectDiagnostics = true;

            Step(skipping, 20);

            EnvironmentState state = EnvironmentSolver.Solve(
                writing.Settings, writing.Planet, Worlds.Flight(1f, 300f));
            for (int i = 0; i < 20; i++)
            {
                RewriteEveryNodeTheOldWay(writing);
                writing.Solver.RefreshExposure(writing.Rooms.Map);
                writing.Solver.Step(writing.Settings.StepSeconds, state);
            }

            SolverAb.AssertIdentical(
                SolverAb.Temperatures(writing), SolverAb.Temperatures(skipping),
                "twenty steps over a hull whose exposure never moves",
                "writing every node every refresh", "skipping the unchanged ones");

            SolverAb.AssertIdentical(
                SolverAb.Diagnostics(writing), SolverAb.Diagnostics(skipping),
                "the per-mechanism watts after twenty such steps",
                "writing every node every refresh", "skipping the unchanged ones",
                SolverAb.Mechanisms);
        }

        /// <summary>
        /// **The skip engages, and it engages on nearly everything.** A first refresh over a hull
        /// that has just been mapped writes what it finds; every refresh after it writes nothing at
        /// all, which is the saving stated as a number rather than as a timing.
        /// </summary>
        [Fact]
        public void ASecondRefreshOverAnUnchangedHullWritesNothing()
        {
            ThermalSimulation simulation = Hulls.Driven(Hulls.Uncapped());

            // Stepped first, for the same reason as below: building and driving the hull marks
            // nodes for reasons that have nothing to do with exposure — mass, heat generation, the
            // seeded temperatures — and a step is what clears them. Without it this test would be
            // reading somebody else's dirty flags.
            Step(simulation, 1);
            Assert.Equal(0, DirtyNodes(simulation));

            long visitsBefore = simulation.Work.ExposureNodeVisits;
            long writesBefore = simulation.Work.ExposureNodeWrites;

            simulation.Solver.RefreshExposure(simulation.Rooms.Map);

            long visited = simulation.Work.ExposureNodeVisits - visitsBefore;
            long written = simulation.Work.ExposureNodeWrites - writesBefore;

            Assert.Equal(simulation.Solver.Nodes.Count, (int)visited);
            Assert.Equal(0L, written);

            // And no node is left asking to be re-mirrored, which is the half of the saving that is
            // network traffic rather than milliseconds.
            Assert.Equal(0, DirtyNodes(simulation));
        }

        /// <summary>
        /// The other half of `E8`: a skip that swallowed a real change would pass every test above.
        /// One node's faces are moved by hand and the next refresh has to put them back — writing
        /// exactly that one node, and marking exactly that one for resync.
        /// </summary>
        [Fact]
        public void AFaceThatMovedIsStillWritten()
        {
            ThermalSimulation simulation = Hulls.Driven(Hulls.Uncapped());

            // A step is what clears `StateDirty`, so the hull is stepped first and the counts
            // below are against a grid with nothing outstanding.
            Step(simulation, 1);
            Assert.Equal(0, DirtyNodes(simulation));

            ThermalNode node = FirstExposedNode(simulation);
            int[] counts = new int[Face.Count];
            for (int f = 0; f < Face.Count; f++) counts[f] = node.GetExposedFaces(f) + 1;
            Assert.True(node.SetExposedFaces(counts), "moving every face is a change");

            long writesBefore = simulation.Work.ExposureNodeWrites;
            simulation.Solver.RefreshExposure(simulation.Rooms.Map);

            Assert.Equal(1L, simulation.Work.ExposureNodeWrites - writesBefore);
            Assert.Equal(1, DirtyNodes(simulation));

            // Put back to what the surfaces actually say, not to some remembered number.
            for (int f = 0; f < Face.Count; f++)
            {
                Assert.Equal(counts[f] - 1, node.GetExposedFaces(f));
            }
        }

        private static int DirtyNodes(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int dirty = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].StateDirty) dirty++;
            }
            return dirty;
        }

        private static ThermalNode FirstExposedNode(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].TotalExposedFaces > 0) return nodes[i];
            }

            throw new System.InvalidOperationException(
                "no node on the hull has an exposed face, so this suite would assert nothing");
        }
    }
}
