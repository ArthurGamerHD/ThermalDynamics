using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Regressions written from an in-game block-count stress test: twenty 44,632 cell capital
    /// ships plus sixty projections, run to a world close.
    ///
    /// Each test here pins one thing that report got wrong or nearly got wrong. The report itself
    /// is the specification — where a number is quoted in a comment, it came out of that run.
    /// </summary>
    public class StressFindingsTests
    {
        // ---- cold start --------------------------------------------------------------------

        /// <summary>
        /// Every simulated grid in the run reported a minimum ambient of 0.0 K and a minimum
        /// solar figure of 0 W, exactly once each, on its first sample — the host reads the
        /// environment the solver used, and before the first step that was a default-constructed
        /// state. It dragged the reported mean from 2.700 K to 2.645 K (48/49 samples) and made
        /// the coldest sky of the session a physical impossibility.
        /// </summary>
        [Fact]
        public void AFreshSolverReportsEmptySpaceAndNotAbsoluteZero()
        {
            ThermalSettings settings = new ThermalSettings().Derive();
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);

            Assert.Equal(settings.VacuumTemperature, simulation.Solver.Environment.AmbientTemperature, 3);
            Assert.True(simulation.Solver.Environment.AmbientTemperature > 0f,
                "a grid that has not stepped yet still sits in empty space, not at 0 K");
        }

        /// <summary>
        /// The substep estimate reads the last environment. Asking before the first step is a
        /// real call path — the host uses it to decide whether a step is affordable — and it has
        /// to answer with the vacuum, not with a zeroed struct.
        /// </summary>
        [Fact]
        public void SubstepEstimateBeforeTheFirstStepIsUsable()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 800f);

            float required = simulation.Solver.RequiredSubsteps(simulation.Settings.StepSeconds);

            Assert.False(float.IsNaN(required));
            Assert.True(required >= 0f, "substep estimate went negative on a fresh solver");

            // Finite, and not by luck. The estimate divides each node's conductance by its
            // thermal mass, and thermal mass lived only in the arrays a step fills in — so a
            // solver that had never stepped divided by zero and answered infinity. "Not NaN" is
            // exactly the assertion that let that through.
            Assert.False(float.IsInfinity(required),
                "a fresh solver has to answer with a number a scheduler can act on");

            // And it is the same answer the first step reaches, since nothing has changed
            // between the two.
            float duringStep = simulation.Solver.RequiredSubsteps(simulation.Settings.StepSeconds);
            simulation.StepExact(1, Worlds.Shadow());

            Assert.Equal(duringStep, required, 3);
            Assert.Equal(
                required <= 1f ? 1 : (int)Math.Ceiling(required),
                simulation.Solver.LastSubsteps);
        }

        /// <summary>
        /// The first sample a scenario records is taken before anything has stepped, and it is
        /// the row a reader compares everything else against.
        /// </summary>
        [Fact]
        public void TheFirstRecordedSampleHasAPhysicalAmbient()
        {
            ScenarioResult result = Scenarios.Run("vacuum-soak");

            Assert.True(result.Runner.Samples[0].AmbientTemperature > 0f,
                "sample zero reported " + result.Runner.Samples[0].AmbientTemperature + " K");
        }

        // ---- shared scratch ----------------------------------------------------------------

        /// <summary>
        /// The run threw 442 ArgumentOutOfRangeExceptions out of List.EnsureCapacity inside the
        /// definition reader, which is what a List looks like when two threads push onto it at
        /// once: the game builds pasted grids off the main thread, and the adapter's mount
        /// scratch list is static.
        ///
        /// The builder itself is the half that has to stay clean: given its own list per call it
        /// must be pure, so the fix on the adapter side is a local list rather than a lock.
        /// </summary>
        [Fact]
        public void BuildingSurfacesConcurrentlyMatchesTheSingleThreadedAnswer()
        {
            Vector3I size = new Vector3I(1, 1, 2);
            SealTest seals = delegate (Vector3I cell, int face) { return face != Face.Up; };

            int[] expected = BlockSurfaceBuilder.BuildSurfaces(size, false, seals, Mounts());

            const int workers = 8;
            const int perWorker = 200;
            int[][] results = new int[workers][];

            Parallel.For(0, workers, w =>
            {
                int[] last = null;
                for (int i = 0; i < perWorker; i++)
                {
                    // Each caller owns its scratch. That is the contract the adapter has to keep.
                    last = BlockSurfaceBuilder.BuildSurfaces(size, false, seals, Mounts());
                }
                results[w] = last;
            });

            for (int w = 0; w < workers; w++)
            {
                Assert.Equal(expected, results[w]);
            }
        }

        /// <summary>
        /// The builder must not hold onto the caller's list, or a pooled scratch list becomes
        /// shared mutable state the next call trips over.
        /// </summary>
        [Fact]
        public void BuildingSurfacesDoesNotMutateOrRetainTheCallersMounts()
        {
            List<MountRect> mounts = Mounts();
            int before = mounts.Count;

            SealTest seals = delegate (Vector3I cell, int face) { return true; };
            int[] first = BlockSurfaceBuilder.BuildSurfaces(Vector3I.One, true, seals, mounts);

            Assert.Equal(before, mounts.Count);

            // Emptying the list afterwards must not change what was already built.
            int[] copy = (int[])first.Clone();
            mounts.Clear();

            Assert.Equal(copy, first);
        }

        private static List<MountRect> Mounts()
        {
            List<MountRect> mounts = new List<MountRect>();
            for (int face = 0; face < Face.Count; face++)
            {
                Vector3I normal = Face.Offsets[face];
                MountRect rect = new MountRect(normal, Vector3.Zero, Vector3.One);
                rect.Enabled = true;
                mounts.Add(rect);
            }
            return mounts;
        }

        // ---- scale -------------------------------------------------------------------------

        /// <summary>
        /// The stress test's real cost was not the solver. On the 44,632 cell ship a solver step
        /// averaged 23 ms while one room-mapping call took 162 ms and one topology rebuild 151 ms
        /// — nine mapping calls over 100 ms across the session, each of them a visible stall.
        ///
        /// This pins the shape of that: the one-shot rebuild has to stay far more expensive than
        /// a step, because that is what makes it worth budgeting across frames rather than
        /// optimising the step.
        /// </summary>
        [Fact]
        public void ACapitalShipCostsFarMoreToMapOnceThanToStep()
        {
            ScenarioResult result = Scenarios.Run("capital");

            Assert.Contains("cells", result.Summary);
            Assert.Contains("sealed rooms", result.Summary);

            ThermalSimulation simulation = result.Runner.Simulation;

            Assert.True(simulation.Solver.Nodes.Count > 30000,
                "the capital scenario has to stay at stress-test scale: "
                + simulation.Solver.Nodes.Count + " cells");
            Assert.True(simulation.Rooms.Map.RoomCount > 1, "bulkheads should seal compartments");
            Assert.False(simulation.Rooms.HasWorkPending, "the map should be finished before stepping");
        }

        /// <summary>
        /// Nothing in the mod shares a budget between grids, so twenty ships cost twenty times
        /// one. The stress test's twenty battlestars are what turned a 23 ms step into 27.7 s of
        /// grid simulation across the session.
        /// </summary>
        [Fact]
        public void FleetCostScalesWithTheNumberOfGrids()
        {
            ScenarioResult result = Scenarios.Run("fleet");

            Assert.Contains("20 ships", result.Summary);

            // Every ship carries its own reactor and none of them stall or go non-finite.
            IList<Sample> samples = result.Runner.Samples;
            for (int i = 1; i < samples.Count; i++)
            {
                Assert.True(samples[i].Substeps >= 1);
                Assert.False(float.IsNaN(samples[i].HottestTemperature));
            }
        }

        /// <summary>
        /// Whole block types in the report — 5,399 hydrogen thrusters, 1,600 lights — showed a
        /// mean exposed area of exactly zero across every instance. That is legitimate for a
        /// buried block, and this is what it has to mean: no radiation, no sun, and a path out
        /// through conduction that is good enough that burying a heat source cools it rather than
        /// trapping it.
        /// </summary>
        [Fact]
        public void ABuriedHeatSourceHasNoExposedFaceAndStillShedsItsHeat()
        {
            ScenarioResult result = Scenarios.Run("interior");
            ThermalSimulation simulation = result.Runner.Simulation;

            ThermalNode buried = simulation.Solver.GetNodeAt(Vector3I.Zero);
            ThermalNode skin = simulation.Solver.GetNodeAt(new Vector3I(0, 4, 0));

            Assert.Equal(0, buried.TotalExposedFaces);
            Assert.True(skin.TotalExposedFaces > 0, "the skin block should see space");

            IList<Sample> samples = result.Runner.Samples;
            float peak = 0f;
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i].Tracked["buried"] > peak) peak = samples[i].Tracked["buried"];
            }

            Assert.True(peak < 400f,
                "a 10 kW waste load in armour must not run away: peaked at " + peak + " K");

            // The hull directly above it tracks it closely: conduction is the only way out.
            float gap = Math.Abs(result.Runner.Final.Tracked["buried"] - result.Runner.Final.Tracked["hull"]);
            Assert.True(gap < 5f, "buried block and hull should equilibrate, gap was " + gap + " K");
        }
    }
}
