using System;
using System.Diagnostics;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// What an update costs, asserted rather than eyeballed.
    ///
    /// The load benchmarks in the harness produce a table someone has to read. These are the
    /// same measurements turned into a claim that fails the build, so a change that makes a
    /// large grid stutter is caught by the suite instead of by a player.
    ///
    /// Almost every assertion here is on a <see cref="SimulationWork"/> counter rather than on a
    /// stopwatch. A millisecond threshold is a statement about the machine that ran it; a claim
    /// that placing one block must not visit every node on the grid is a statement about the
    /// algorithm, and it holds identically on a laptop, a build server and a dedicated host.
    /// The handful of wall-clock tests that remain are there to catch a change of the order of
    /// magnitude, and their ceilings are set loose enough to say so.
    ///
    /// Every test writes its figures to the test output, so a run of the suite is also a
    /// performance report — which is the point of having them here rather than in a benchmark
    /// nobody runs.
    /// </summary>
    public class LoadTests
    {
        /// <summary>Grid sizes the suite can afford. The benchmarks go to a million.</summary>
        private const int Small = 8000;
        private const int Large = 32000;

        private readonly ITestOutputHelper output;

        public LoadTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static ThermalSimulation Build(int blocks, string shape = "ship")
        {
            return LoadBenchmarks.BuildSettled(shape, blocks);
        }

        private static EnvironmentSample Space()
        {
            return Worlds.Space(new Vector3(0f, 1f, 0f));
        }

        // ---- the steady state ---------------------------------------------------------------

        /// <summary>
        /// A tick on a grid where nothing changed must do no rebuilding at all.
        ///
        /// This is the floor the whole design stands on: if a settled ship pays for topology,
        /// exposure or a loop search on an ordinary tick, no amount of making those stages
        /// cheaper will help, because they are being run for nothing.
        /// </summary>
        [Fact]
        public void ASettledGridRebuildsNothing()
        {
            ThermalSimulation simulation = Build(Small);

            // Let the map converge first — a fresh grid legitimately has work to do.
            while (simulation.Rooms.HasWorkPending) simulation.Update(LoadBenchmarks.TickSeconds, Space());

            simulation.Work.Reset();

            for (int tick = 0; tick < 60; tick++)
            {
                simulation.Update(LoadBenchmarks.TickSeconds, Space());
            }

            SimulationWork work = simulation.Work;
            output.WriteLine(simulation.Solver.Nodes.Count.ToString("n0")
                + " blocks, 60 settled ticks: " + work.SolverSteps + " solver steps, "
                + work.TopologyRebuilds + " topology rebuilds, "
                + work.ExposureRefreshes + " exposure refreshes, "
                + work.LoopSearches + " loop searches, "
                + work.RoomPassesBegun + " room passes.");

            Assert.True(work.SolverSteps > 0, "sixty ticks should have stepped the solver");
            Assert.Equal(0, work.TopologyRebuilds);
            Assert.Equal(0, work.ExposureRefreshes);
            Assert.Equal(0, work.LoopSearches);
            Assert.Equal(0, work.HeatPumpRebuilds);
            Assert.Equal(0, work.RoomPassesBegun);
        }

        /// <summary>
        /// The solver's cost per link visit must not run away as the grid grows.
        ///
        /// Per link visit rather than per step, because that is the figure a change of grid size
        /// leaves alone. It does climb — a working set that no longer fits in cache costs more
        /// per element than one that does, and that is real — but a climb of more than about
        /// three times over four times the blocks is a different shape of curve and means
        /// something is quadratic.
        /// </summary>
        [Fact]
        public void SolverCostPerLinkStaysProportional()
        {
            double small = NanosecondsPerLinkVisit(Small);
            double large = NanosecondsPerLinkVisit(Large);

            double ratio = small <= 0d ? 0d : large / small;
            output.WriteLine(Small.ToString("n0") + " blocks: " + small.ToString("n2")
                + " ns/link visit; " + Large.ToString("n0") + " blocks: " + large.ToString("n2")
                + " ns/link visit; ratio " + ratio.ToString("n2") + "x.");

            Assert.True(ratio < 3d,
                "cost per link visit grew " + ratio.ToString("n2") + "x over four times the blocks; "
                + "the conduction pass should be close to linear in link count");
        }

        private static double NanosecondsPerLinkVisit(int blocks)
        {
            ThermalSimulation simulation = Build(blocks);
            LoadBenchmarks.SeedSpread(simulation);

            EnvironmentState state = EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, Space());

            float step = simulation.Settings.StepSeconds;
            for (int i = 0; i < 5; i++) simulation.Solver.Step(step, state);

            const int measured = 20;
            Stopwatch watch = Stopwatch.StartNew();
            for (int i = 0; i < measured; i++) simulation.Solver.Step(step, state);
            watch.Stop();

            long visits = (long)simulation.Solver.Links.Count
                * Math.Max(1, simulation.Solver.LastSubsteps) * measured;

            return visits == 0 ? 0d : watch.Elapsed.TotalMilliseconds * 1e6d / visits;
        }

        // ---- coalescing ---------------------------------------------------------------------

        /// <summary>
        /// A hundred blocks placed between two ticks must cost one rebuild, not a hundred.
        ///
        /// This is what makes welding, pasting and grinding survivable, and it is a property of
        /// the dirty flags rather than of how fast any stage is. Losing it would not show up as
        /// a slow stage in a profile — it would show up as the same stage run a hundred times.
        /// </summary>
        [Fact]
        public void ManyPlacementsInOneTickCoalesceIntoOneRebuild()
        {
            ThermalSimulation simulation = Build(Small);
            while (simulation.Rooms.HasWorkPending) simulation.Update(LoadBenchmarks.TickSeconds, Space());

            simulation.Work.Reset();

            BlockModel armour = Catalog.HeavyArmor();
            Vector3I start = simulation.Grid.Min - new Vector3I(2, 0, 0);

            const int placed = 100;
            for (int i = 0; i < placed; i++)
            {
                simulation.AddBlock(
                    new BlockInstance(armour, start + new Vector3I(0, 0, i), BlockOrientation.Identity),
                    293.15f);
            }

            // Ticked until the map converges rather than exactly once, because the conduction
            // rebuild is deferred to the first tick that actually steps — the dirty flag is set
            // during the update and acted on inside the step, and only two ticks in three are a
            // step. What is asserted is that the whole burst cost one of each stage, not when.
            int ticks = 0;
            do
            {
                simulation.Update(LoadBenchmarks.TickSeconds, Space());
                ticks++;
            }
            while ((simulation.Rooms.HasWorkPending || simulation.Work.TopologyRebuilds == 0)
                && ticks < 10000);

            output.WriteLine(placed + " blocks placed, settled over " + ticks + " ticks: "
                + simulation.Work.TopologyRebuilds + " topology rebuilds, "
                + simulation.Work.RoomPassesBegun + " room passes, "
                + simulation.Work.LoopSearches + " loop searches.");

            Assert.Equal(1, simulation.Work.TopologyRebuilds);
            Assert.Equal(1, simulation.Work.RoomPassesBegun);
            Assert.Equal(1, simulation.Work.LoopSearches);
            Assert.Equal(1, simulation.Work.HeatPumpRebuilds);
        }

        // ---- the budgeted stages ------------------------------------------------------------

        /// <summary>
        /// The room mapper must respect its budget on every tick, however large the grid.
        ///
        /// The mapper is the one stage that is already incremental, and its whole value is that
        /// a tick's share of it is bounded by the budget rather than by the bounding volume. A
        /// change that lets one tick run the flood fill to completion would restore a stall that
        /// grows with the cube of ship size.
        /// </summary>
        [Fact]
        public void RoomMappingNeverExceedsItsBudgetInOneTick()
        {
            ThermalSimulation simulation = Build(Large);

            Vector3I extents = (simulation.Grid.Max - simulation.Grid.Min) + Vector3I.One;
            int volume = Math.Max(1, extents.X * extents.Y * extents.Z);
            int budget = SimulationScheduler.RoomMappingBudget(volume);

            simulation.MarkTopologyDirty();

            long worst = 0;
            int ticks = 0;

            do
            {
                simulation.Work.Reset();
                simulation.Update(LoadBenchmarks.TickSeconds, Space());
                if (simulation.Work.RoomCellsVisited > worst) worst = simulation.Work.RoomCellsVisited;
                ticks++;
            }
            while (simulation.Rooms.HasWorkPending && ticks < 100000);

            output.WriteLine("bounding volume " + volume.ToString("n0") + ", budget " + budget
                + " cells/tick, worst tick visited " + worst.ToString("n0")
                + " over " + ticks + " ticks.");

            Assert.True(worst <= budget,
                "one tick flooded " + worst + " cells against a budget of " + budget);
        }

        // ---- wall clock, loosely ------------------------------------------------------------

        /// <summary>
        /// A settled tick on a thirty-thousand block ship has to fit inside a frame.
        ///
        /// The ceiling is deliberately far above what the stage costs today. This is not a
        /// target — it is a tripwire for a change that moves the cost by an order of magnitude,
        /// and it is the only kind of wall-clock claim that survives running on someone else's
        /// machine.
        /// </summary>
        [Fact]
        public void ASettledTickFitsInAFrame()
        {
            ThermalSimulation simulation = Build(Large);
            while (simulation.Rooms.HasWorkPending) simulation.Update(LoadBenchmarks.TickSeconds, Space());
            LoadBenchmarks.SeedSpread(simulation);

            FrameTrace trace = new FrameTrace("settled " + Large.ToString("n0"));
            Stopwatch watch = new Stopwatch();

            for (int tick = 0; tick < 120; tick++)
            {
                watch.Restart();
                simulation.Update(LoadBenchmarks.TickSeconds, Space());
                watch.Stop();
                trace.Add(watch.Elapsed.TotalMilliseconds);
            }

            output.WriteLine(simulation.Solver.Nodes.Count.ToString("n0") + " blocks, "
                + simulation.Solver.Links.Count.ToString("n0") + " links. " + trace.Describe());

            const double ceiling = 8d * FrameTrace.FrameBudgetMs;
            Assert.True(trace.Percentile(0.95) < ceiling,
                "p95 settled tick was " + trace.Percentile(0.95).ToString("n2")
                + " ms against a ceiling of " + ceiling.ToString("n1") + " ms");
        }
    }
}
