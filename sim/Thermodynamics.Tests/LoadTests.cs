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
    /// <summary>
    /// Keeps the timed tests off the same cores as the rest of the suite.
    ///
    /// xUnit runs collections in parallel, and a stopwatch reading taken while thirty other
    /// tests are saturating the machine measures the machine, not the code. The first run of
    /// these reported the cost per link visit growing 1.7x across four times the blocks and then
    /// 3.2x on the next run, from the same binary — all of the difference was scheduling. A
    /// collection that disables parallelisation runs alone, which is what a measurement needs.
    /// </summary>
    [CollectionDefinition("load", DisableParallelization = true)]
    public class LoadCollection { }

    [Collection("load")]
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
            double small = BestNanosecondsPerLinkVisit(Small);
            double large = BestNanosecondsPerLinkVisit(Large);

            double ratio = small <= 0d ? 0d : large / small;
            output.WriteLine(Small.ToString("n0") + " blocks: " + small.ToString("n2")
                + " ns/link visit; " + Large.ToString("n0") + " blocks: " + large.ToString("n2")
                + " ns/link visit; ratio " + ratio.ToString("n2") + "x.");

            Assert.True(ratio < 3d,
                "cost per link visit grew " + ratio.ToString("n2") + "x over four times the blocks; "
                + "the conduction pass should be close to linear in link count");
        }

        /// <summary>
        /// The best of three runs, not the mean of them.
        ///
        /// Interference only ever makes a measurement slower — a cache eviction, a context
        /// switch, a collection — so the fastest run is the one least contaminated by things
        /// that are not the code. Averaging folds the noise in and then reports it as signal.
        /// </summary>
        private static double BestNanosecondsPerLinkVisit(int blocks)
        {
            double best = double.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                double sample = NanosecondsPerLinkVisit(blocks);
                if (sample > 0d && sample < best) best = sample;
            }
            return best == double.MaxValue ? 0d : best;
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

        /// <summary>
        /// A grid with no plumbing and no pumps on it must not walk its blocks looking for them.
        ///
        /// Both searches ask a question that almost every block answers no to, and they run on
        /// every topology change. On a ship the size this session is aiming at, that is two
        /// passes over a million blocks to discover there is nothing to find.
        /// </summary>
        [Fact]
        public void SearchesForPlumbingSkipAGridThatHasNone()
        {
            ThermalSimulation simulation = Build(Small);
            while (simulation.Rooms.HasWorkPending) simulation.Update(LoadBenchmarks.TickSeconds, Space());

            Assert.Equal(0, simulation.Grid.CoolantBlockCount);
            Assert.Equal(0, simulation.Grid.HeatPumpBlockCount);

            simulation.Work.Reset();

            simulation.AddBlock(
                new BlockInstance(Catalog.HeavyArmor(), simulation.Grid.Min - new Vector3I(2, 0, 0),
                    BlockOrientation.Identity),
                293.15f);

            simulation.Update(LoadBenchmarks.TickSeconds, Space());

            output.WriteLine(simulation.Solver.Nodes.Count.ToString("n0")
                + " blocks with no plumbing, one block placed: "
                + simulation.Work.LoopSearches + " loop searches visiting "
                + simulation.Work.LoopSearchCells + " cells, "
                + simulation.Work.HeatPumpRebuilds + " heat pump rebuilds visiting "
                + simulation.Work.HeatPumpNodeVisits + " nodes.");

            Assert.True(simulation.Work.LoopSearches > 0, "the search should still have been asked for");
            Assert.Equal(0, simulation.Work.LoopSearchCells);
            Assert.Equal(0, simulation.Work.HeatPumpNodeVisits);
        }

        /// <summary>
        /// The conduction rebuild must be billed to the topology stage, not to the solver.
        ///
        /// It used to be left as a dirty flag for the solver to notice, so a report of a
        /// stalling ship showed a solver stage with a huge worst call and a topology stage of
        /// nearly nothing — which is exactly backwards, and would send anyone reading it to
        /// optimise the wrong loop.
        /// </summary>
        [Fact]
        public void ARebuildIsBilledToTopologyAndNotToTheSolver()
        {
            ThermalSimulation simulation = Build(Small);
            while (simulation.Rooms.HasWorkPending) simulation.Update(LoadBenchmarks.TickSeconds, Space());

            StageTimings timings = new StageTimings();
            simulation.Profiler = timings;

            simulation.Work.Reset();
            simulation.AddBlock(
                new BlockInstance(Catalog.HeavyArmor(), simulation.Grid.Min - new Vector3I(2, 0, 0),
                    BlockOrientation.Identity),
                293.15f);

            int topologyCallsBefore = timings.Calls(SimulationPhase.Topology);
            simulation.Update(LoadBenchmarks.TickSeconds, Space());

            output.WriteLine("one block placed: " + simulation.Work.TopologyRebuilds
                + " topology rebuilds during " + (timings.Calls(SimulationPhase.Topology) - topologyCallsBefore)
                + " topology stages, worst " + timings.WorstMs(SimulationPhase.Topology).ToString("n2")
                + " ms; solver worst " + timings.WorstMs(SimulationPhase.Solver).ToString("n2") + " ms.");

            Assert.Equal(1, simulation.Work.TopologyRebuilds);
            Assert.True(timings.Calls(SimulationPhase.Topology) > topologyCallsBefore,
                "the rebuild should have happened inside a topology stage");
        }

        /// <summary>
        /// Reading the link count must not rebuild the graph.
        ///
        /// <c>Solver.Links</c> rebuilds a stale graph before answering, which is right for a
        /// caller that needs the graph and wrong for one that needs an integer. The telemetry
        /// sample wanted the integer, so switching collection on made a welding ship pay for an
        /// extra full rebuild per sample — untimed, because no stage bracket was open around it,
        /// and therefore invisible in the very report it was being collected for.
        /// </summary>
        [Fact]
        public void ReadingTheLinkCountDoesNotRebuildTheGraph()
        {
            ThermalSimulation simulation = Build(Small);
            while (simulation.Rooms.HasWorkPending) simulation.Update(LoadBenchmarks.TickSeconds, Space());

            simulation.AddBlock(
                new BlockInstance(Catalog.HeavyArmor(), simulation.Grid.Min - new Vector3I(2, 0, 0),
                    BlockOrientation.Identity),
                293.15f);

            simulation.Work.Reset();

            int count = simulation.Solver.LinkCount;

            output.WriteLine("link count read on a dirty graph: " + count.ToString("n0")
                + " links, " + simulation.Work.TopologyRebuilds + " rebuilds caused.");

            Assert.True(count > 0);
            Assert.Equal(0, simulation.Work.TopologyRebuilds);
        }

        /// <summary>
        /// Placing a block must cost the block, not the grid.
        ///
        /// This is the assertion the whole session is about. It is written against the node-visit
        /// counter rather than a stopwatch because that is what makes it a claim about the
        /// algorithm: before the incremental path existed, one block placed on a 33k ship visited
        /// 32,801 nodes and stalled the tick for 22 ms, and on a million-block grid it was 456 ms.
        /// A handful of visits is the block and its neighbours; anything proportional to the grid
        /// is the old behaviour come back.
        /// </summary>
        [Fact]
        public void PlacingOneBlockLinksTheBlockAndNotTheGrid()
        {
            ThermalSimulation simulation = Build(Large);
            while (simulation.Rooms.HasWorkPending) simulation.Update(LoadBenchmarks.TickSeconds, Space());

            int blocks = simulation.Solver.Nodes.Count;
            simulation.Work.Reset();

            simulation.AddBlock(
                new BlockInstance(Catalog.HeavyArmor(), simulation.Grid.Min - new Vector3I(2, 0, 0),
                    BlockOrientation.Identity),
                293.15f);
            simulation.Update(LoadBenchmarks.TickSeconds, Space());

            output.WriteLine("one block placed on " + blocks.ToString("n0") + " blocks: "
                + simulation.Work.TopologyNodeVisits + " nodes visited for topology, "
                + simulation.Work.LinksBuilt + " links built.");

            Assert.Equal(1, simulation.Work.TopologyRebuilds);
            Assert.True(simulation.Work.TopologyNodeVisits < 16,
                "linking one block visited " + simulation.Work.TopologyNodeVisits
                + " nodes on a grid of " + blocks + "; it should visit the block and its neighbours");
        }

        /// <summary>
        /// A hundred blocks welded in one burst must still cost the hundred, not the grid.
        ///
        /// The batch is the case where the incremental builder has to get the pairing right —
        /// two new blocks placed against each other are both looking at each other — so it is
        /// worth asserting separately from the single placement above.
        /// </summary>
        [Fact]
        public void WeldingABurstCostsTheBurstAndNotTheGrid()
        {
            ThermalSimulation simulation = Build(Large);
            while (simulation.Rooms.HasWorkPending) simulation.Update(LoadBenchmarks.TickSeconds, Space());

            int blocks = simulation.Solver.Nodes.Count;
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

            simulation.Update(LoadBenchmarks.TickSeconds, Space());

            output.WriteLine(placed + " blocks welded onto " + blocks.ToString("n0") + ": "
                + simulation.Work.TopologyNodeVisits + " nodes visited, "
                + simulation.Work.LinksBuilt + " links built.");

            Assert.Equal(placed, (int)simulation.Work.TopologyNodeVisits);
            Assert.True(simulation.Work.LinksBuilt >= placed - 1,
                "a run of blocks laid end to end should link to each other");
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
