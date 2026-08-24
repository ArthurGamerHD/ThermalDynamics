using System;
using System.Collections.Generic;
using System.Diagnostics;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Keeps the timed tests off the same cores as the rest of the suite. A stopwatch reading taken
    /// while thirty other tests saturate the machine measures the machine: the first run of these
    /// reported 1.7x and then 3.2x from the same binary, all of it scheduling.
    /// </summary>
    [CollectionDefinition("load", DisableParallelization = true)]
    public class LoadCollection { }

    /// <summary>
    /// What an update costs, asserted on work counters rather than on a stopwatch — a millisecond
    /// threshold is a claim about the machine, and "placing one block must not visit every node" is a
    /// claim about the algorithm. Every test writes its figures to the test output, so a suite run is
    /// also a performance report. See load-and-hitching.md, Catching it again.
    /// </summary>
    [Collection("load")]
    public class LoadTests
    {
        /// <summary>Grid sizes the suite can afford. The benchmarks go to a million.</summary>
        private const int Small = 8000;
        private const int Large = 32000;

        /// <summary>
        /// The largest grid the shipped element-visit allowance still covers in vacuum, measured
        /// 2026-08-24 at `C24`'s pair: 6,000 blocks run at 100 % of real time, 8,000 at 94.3 % and
        /// 12,000 at 68.2 %. At the pace the conversion calibrated to, every one of those was
        /// 100 %. See <see cref="TheShippedAllowanceFitsAGridAndAHalvedOneDoesNot"/>.
        /// </summary>
        private const int Covered = 6000;

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
            while (simulation.HasPendingWork) simulation.Update(LoadBenchmarks.TickSeconds, Space());

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
            while (simulation.HasPendingWork) simulation.Update(LoadBenchmarks.TickSeconds, Space());

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
            while ((simulation.HasPendingWork || simulation.Work.TopologyRebuilds == 0)
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
            while (simulation.HasPendingWork) simulation.Update(LoadBenchmarks.TickSeconds, Space());

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
            while (simulation.HasPendingWork) simulation.Update(LoadBenchmarks.TickSeconds, Space());

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
            while (simulation.HasPendingWork) simulation.Update(LoadBenchmarks.TickSeconds, Space());

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
            while (simulation.HasPendingWork) simulation.Update(LoadBenchmarks.TickSeconds, Space());

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
            while (simulation.HasPendingWork) simulation.Update(LoadBenchmarks.TickSeconds, Space());

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

        /// <summary>
        /// Grinding a block off must cost the block, not the grid.
        ///
        /// The companion to the placement test, and it was the harder direction: a removed block's
        /// links have to be found before they can be dropped, and its node has to leave a list
        /// whose indices every link refers to. Before the per-node link index existed, this took
        /// the global path — 304 ms on a half-million-block hull against a 49 ms median, which
        /// made grinding and combat damage the largest stall the mod had.
        /// </summary>
        [Fact]
        public void GrindingOneBlockUnpicksTheBlockAndNotTheGrid()
        {
            ThermalSimulation simulation = Build(Large);
            while (simulation.HasPendingWork) simulation.Update(LoadBenchmarks.TickSeconds, Space());

            int blocks = simulation.Solver.Nodes.Count;
            int links = simulation.Solver.LinkCount;

            simulation.Work.Reset();

            // An interior block, so it is a joint with several neighbours rather than a spur.
            BlockInstance doomed = null;
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].LinkCount < 4) continue;
                doomed = nodes[i].Block;
                break;
            }
            Assert.NotNull(doomed);

            simulation.RemoveBlock(doomed);
            simulation.Update(LoadBenchmarks.TickSeconds, Space());

            output.WriteLine("one block ground off " + blocks.ToString("n0") + " blocks: "
                + simulation.Work.NodesRemoved + " nodes removed, "
                + simulation.Work.LinksRemoved + " links unpicked, "
                + simulation.Work.TopologyRebuilds + " global rebuilds, "
                + simulation.Work.TopologyNodeVisits + " nodes visited.");

            Assert.Equal(0, simulation.Work.TopologyRebuilds);
            Assert.Equal(1, simulation.Work.NodesRemoved);
            Assert.True(simulation.Work.LinksRemoved < 16,
                "unpicking one block dropped " + simulation.Work.LinksRemoved + " links");
            Assert.Equal(blocks - 1, simulation.Solver.Nodes.Count);
            Assert.True(simulation.Solver.LinkCount < links,
                "the block's links should be gone");
        }

        /// <summary>
        /// A section shot away is many removals at once, and must still cost the section.
        /// </summary>
        [Fact]
        public void GrindingABurstCostsTheBurstAndNotTheGrid()
        {
            ThermalSimulation simulation = Build(Large);
            while (simulation.HasPendingWork) simulation.Update(LoadBenchmarks.TickSeconds, Space());

            int blocks = simulation.Solver.Nodes.Count;
            simulation.Work.Reset();

            const int destroyed = 100;
            for (int i = 0; i < destroyed; i++)
            {
                simulation.RemoveBlock(simulation.Solver.Nodes[0].Block);
            }

            simulation.Update(LoadBenchmarks.TickSeconds, Space());

            output.WriteLine(destroyed + " blocks ground off " + blocks.ToString("n0") + ": "
                + simulation.Work.NodesRemoved + " nodes removed, "
                + simulation.Work.LinksRemoved + " links unpicked, "
                + simulation.Work.TopologyRebuilds + " global rebuilds.");

            Assert.Equal(0, simulation.Work.TopologyRebuilds);
            Assert.Equal(destroyed, (int)simulation.Work.NodesRemoved);
            Assert.Equal(blocks - destroyed, simulation.Solver.Nodes.Count);
        }

        /// <summary>
        /// A ship pressurising must cost one pass over its links, not one per compartment.
        ///
        /// Every node's total conductance is what the substep estimate divides by thermal mass,
        /// and a room gaining or losing air changes it. Recomputing on the spot meant a pass over
        /// every link, every coolant loop and every room — and the changes arrive in bursts, since
        /// a ship pressurising is every compartment filling within a second or two. A station with
        /// two thousand of them paid two thousand passes over two million links to learn what one
        /// pass would have said.
        /// </summary>
        [Fact]
        public void PressurisingEveryRoomCostsOnePassNotOnePerRoom()
        {
            ThermalSimulation simulation = Build(Large);
            while (simulation.HasPendingWork) simulation.Update(LoadBenchmarks.TickSeconds, Space());

            IList<RoomAirNode> air = simulation.RoomAir;
            Assert.True(air.Count > 4, "the hull should have mapped several compartments, got " + air.Count);

            simulation.Work.Reset();

            for (int i = 0; i < air.Count; i++)
            {
                simulation.SetRoomPressure(air[i].Anchor, 1f);
            }

            // Read through a step, which is what forces the totals to be current.
            simulation.Update(LoadBenchmarks.TickSeconds, Space());
            simulation.Update(LoadBenchmarks.TickSeconds, Space());

            output.WriteLine(air.Count + " rooms pressurised: "
                + simulation.Work.ConductanceRecomputes + " passes over the links.");

            Assert.True(simulation.Work.ConductanceRecomputes <= 2,
                air.Count + " rooms pressurised caused "
                + simulation.Work.ConductanceRecomputes + " passes over every link");
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
            while (simulation.HasPendingWork && ticks < 100000);

            output.WriteLine("bounding volume " + volume.ToString("n0") + ", budget " + budget
                + " cells/tick, worst tick visited " + worst.ToString("n0")
                + " over " + ticks + " ticks.");

            Assert.True(worst <= budget,
                "one tick flooded " + worst + " cells against a budget of " + budget);
        }

        /// <summary>
        /// The exposure refresh must respect a budget too, however large the grid.
        ///
        /// It used to run whole on the tick a room pass published, which put it on the same tick
        /// as the mapper's own worst call. On a 127k ship those two together were a hundred
        /// milliseconds in one tick, on a grid whose steady cost is twenty.
        /// </summary>
        [Fact]
        public void ExposureRefreshNeverExceedsItsBudgetInOneTick()
        {
            ThermalSimulation simulation = Build(Large);
            while (simulation.HasPendingWork) simulation.Update(LoadBenchmarks.TickSeconds, Space());

            int budget = SimulationScheduler.ExposureBudget(simulation.Solver.Nodes.Count);

            simulation.MarkTopologyDirty();

            long worst = 0;
            long total = 0;
            int ticks = 0;

            do
            {
                simulation.Work.Reset();
                simulation.Update(LoadBenchmarks.TickSeconds, Space());
                if (simulation.Work.ExposureNodeVisits > worst) worst = simulation.Work.ExposureNodeVisits;
                total += simulation.Work.ExposureNodeVisits;
                ticks++;
            }
            while (simulation.HasPendingWork && ticks < 100000);

            output.WriteLine(simulation.Solver.Nodes.Count.ToString("n0") + " blocks, budget "
                + budget + " nodes/tick, worst tick refreshed " + worst.ToString("n0")
                + ", " + total.ToString("n0") + " over " + ticks + " ticks.");

            Assert.True(worst <= budget,
                "one tick refreshed " + worst + " nodes against a budget of " + budget);
            Assert.True(total >= simulation.Solver.Nodes.Count,
                "the pass should still have visited every node: " + total + " of "
                + simulation.Solver.Nodes.Count);
        }

        // ---- the step's own budget ------------------------------------------------------------

        /// <summary>
        /// A step must never make more link visits than it is allowed.
        ///
        /// A step's cost is its substep count times its links, and the substep count is set by the
        /// stiffest node, which moves as the grid heats. That produced a 127k hull whose step cost
        /// fifteen milliseconds most of the time and seventy occasionally, from the same grid
        /// doing the same thing — a five-fold spike with no visible cause. The budget bounds it.
        /// </summary>
        [Fact]
        public void AStepNeverExceedsItsLinkVisitBudget()
        {
            ThermalSimulation simulation = Build(Large);
            while (simulation.HasPendingWork) simulation.Update(LoadBenchmarks.TickSeconds, Space());

            // Tight enough that this grid has to shorten its steps, so the mechanism is exercised
            // rather than merely present.
            simulation.Settings.MaxElementVisitsPerStep = simulation.Solver.LinkCount * 2;
            simulation.Settings.Derive();

            LoadBenchmarks.SeedSpread(simulation);

            long worst = 0;
            for (int tick = 0; tick < 200; tick++)
            {
                simulation.Work.Reset();
                simulation.Update(LoadBenchmarks.TickSeconds, Space());

                long visits = simulation.Work.SolverSubsteps * simulation.Solver.LinkCount;
                if (visits > worst) worst = visits;
            }

            output.WriteLine(simulation.Solver.LinkCount.ToString("n0") + " links, budget "
                + simulation.Settings.MaxElementVisitsPerStep.ToString("n0")
                + " visits/step, worst step made " + worst.ToString("n0")
                + ". Simulation rate " + (100d * simulation.SimulationRate).ToString("n1") + "%.");

            Assert.True(worst <= simulation.Settings.MaxElementVisitsPerStep,
                "a step made " + worst + " link visits against a budget of "
                + simulation.Settings.MaxElementVisitsPerStep);
        }

        /// <summary>
        /// Shortening a step must cost simulated time and nothing else.
        ///
        /// This is the distinction the setting rests on. Coarsening substeps would take steps too
        /// large for the grid's stiffness and lean on the overshoot clamp, which is an accuracy
        /// loss; shortening the step advances less time at exactly the same accuracy. So a budget
        /// tight enough to bite must leave the substep count inside its bound and report the time
        /// it did not advance, rather than clamping.
        /// </summary>
        [Fact]
        public void AShortenedStepLosesTimeAndNotAccuracy()
        {
            ThermalSimulation simulation = Build(Small);
            while (simulation.HasPendingWork) simulation.Update(LoadBenchmarks.TickSeconds, Space());

            LoadBenchmarks.SeedSpread(simulation);

            simulation.Settings.MaxElementVisitsPerStep = simulation.Solver.LinkCount;
            simulation.Settings.Derive();

            for (int tick = 0; tick < 60; tick++)
            {
                simulation.Update(LoadBenchmarks.TickSeconds, Space());
            }

            output.WriteLine("budget one substep: " + simulation.Solver.LastSubsteps
                + " substeps on the last step, clamped " + simulation.Solver.LastStepWasClamped
                + ", skipped " + simulation.SimulatedSecondsSkipped.ToString("n2")
                + " s of " + (simulation.SimulatedSecondsRun + simulation.SimulatedSecondsSkipped).ToString("n2")
                + " (rate " + (100d * simulation.SimulationRate).ToString("n1") + "%).");

            Assert.Equal(1, simulation.Solver.LastSubsteps);
            Assert.False(simulation.Solver.LastStepWasClamped,
                "the step should have been shortened to fit, not clamped to fit");
            Assert.True(simulation.SimulatedSecondsSkipped > 0d,
                "a budget this tight should have cost simulated time");
            Assert.True(simulation.SimulationRate < 1d);
        }

        /// <summary>
        /// A grid whose steps cost less than its allowance must be untouched by it — same
        /// substeps, no time skipped, same simulation rate.
        ///
        /// The allowance is set here rather than left at the default deliberately. This test is
        /// about the bound doing nothing when it is not reached, and pinning it to whatever the
        /// shipped default happens to be would make it a test of the tuning instead — which is
        /// what it became when the budget started counting nodes.
        /// </summary>
        [Fact]
        public void AGridUnderTheBudgetIsUnaffected()
        {
            ThermalSimulation simulation = Build(Small);
            while (simulation.HasPendingWork) simulation.Update(LoadBenchmarks.TickSeconds, Space());
            LoadBenchmarks.SeedSpread(simulation);

            // Comfortably above the stiffest step this rig can ask for.
            simulation.Settings.MaxElementVisitsPerStep = (int)(simulation.SubstepCost * 200);
            simulation.Settings.Derive();

            for (int tick = 0; tick < 60; tick++)
            {
                simulation.Update(LoadBenchmarks.TickSeconds, Space());
            }

            output.WriteLine(simulation.Solver.Nodes.Count.ToString("n0") + " nodes, "
                + simulation.Solver.LinkCount.ToString("n0") + " links, substep cost "
                + simulation.SubstepCost.ToString("n0") + ", demand "
                + simulation.Solver.LastRequiredSubsteps.ToString("n2") + ", budget "
                + simulation.SubstepBudget.ToString("n0") + " against the default "
                + simulation.Settings.MaxElementVisitsPerStep.ToString("n0")
                + " visit allowance: rate " + (100d * simulation.SimulationRate).ToString("n1") + "%.");

            Assert.Equal(0d, simulation.SimulatedSecondsSkipped, 6);
            Assert.Equal(1d, simulation.SimulationRate, 6);
        }

        /// <summary>
        /// What the shipped allowance does to a stiff mid-size grid, and what it takes to make it
        /// bind.
        ///
        /// <para>
        /// Counting links alone, this rig's 20,779 links bought 48 substeps against a demand of 23, so
        /// the budget did nothing. Counting nodes as well, a substep over its 8,904 nodes costs 56,395
        /// element visits, and whether the allowance covers that is a question about the step *rate*:
        /// demand is proportional to step length, so the shipped four steps a second asks for about
        /// 23 substeps where eight would ask for 12.
        /// </para>
        ///
        /// <para>
        /// **The budget bounds a step and smoothness is a property of a frame**, and the two are
        /// related by <c>Frequency</c> — a frame does <c>budget * frameSeconds * StepsPerSecond</c> of
        /// work. So the shipped allowance covers this rig at the shipped rate, and halving it, which is
        /// what the old figure came to at this rate, does not.
        /// </para>
        ///
        /// <para>
        /// **The rig is 6,000 blocks because `C24` moved where the allowance stops covering.** It
        /// was 8,000, and at the pace that now ships an 8,000-block grid in vacuum runs at 94.3 %
        /// of real time — the demand went from 23.20 substeps to 37.13 while the cost of a substep
        /// and the allowance stayed where they were.
        /// <see cref="TheShippedAllowanceNoLongerCoversAnEightThousandBlockGridInVacuum"/> pins
        /// that, and [backlog.md](../../docs/backlog.md) `C27` is what to do about it.
        /// </para>
        /// </summary>
        [Fact]
        public void TheShippedAllowanceFitsAGridAndAHalvedOneDoesNot()
        {
            ThermalSimulation shipped = Build(Covered);
            while (shipped.HasPendingWork) shipped.Update(LoadBenchmarks.TickSeconds, Space());
            LoadBenchmarks.SeedSpread(shipped);

            shipped.Settings.MaxSubsteps = 4096;
            shipped.Settings.Derive();

            for (int tick = 0; tick < 60; tick++)
            {
                shipped.Update(LoadBenchmarks.TickSeconds, Space());
            }

            output.WriteLine("shipped: cost " + shipped.SubstepCost.ToString("n0")
                + ", demand " + shipped.Solver.LastRequiredSubsteps.ToString("n2")
                + ", budget " + shipped.SubstepBudget.ToString("n0")
                + ", rate " + (100d * shipped.SimulationRate).ToString("n1") + "%.");

            // Nodes are the majority of what this rig's substep costs, which is exactly what the
            // old link-only count could not see.
            Assert.True(shipped.SubstepCost > shipped.Solver.LinkCount * 2);
            Assert.Equal(1d, shipped.SimulationRate, 6);

            // Half the allowance no longer covers it — which is the figure the default carried when
            // the shipped rate was eight steps a second, and the reason it moved with the rate.
            ThermalSimulation tighter = Build(Covered);
            while (tighter.HasPendingWork) tighter.Update(LoadBenchmarks.TickSeconds, Space());
            LoadBenchmarks.SeedSpread(tighter);

            tighter.Settings.MaxElementVisitsPerStep = shipped.Settings.MaxElementVisitsPerStep / 2;
            tighter.Settings.MaxSubsteps = 4096;
            tighter.Settings.Derive();

            for (int tick = 0; tick < 60; tick++)
            {
                tighter.Update(LoadBenchmarks.TickSeconds, Space());
            }

            output.WriteLine("half the allowance: demand "
                + tighter.Solver.LastRequiredSubsteps.ToString("n2")
                + ", budget " + tighter.SubstepBudget.ToString("n0")
                + ", rate " + (100d * tighter.SimulationRate).ToString("n1") + "%.");

            // Shortened, not clamped: the accuracy of each step is preserved and simulated time is
            // what gets traded.
            Assert.False(tighter.Solver.LastStepWasClamped);
            Assert.True(tighter.SimulationRate < 1d);
            Assert.True(tighter.SimulationRate > 0.5d);
        }

        /// <summary>
        /// **What `C24` costs the solver in vacuum, which is where it costs anything.**
        ///
        /// <para>
        /// The retune multiplies conduction by four and divides every capacity by two and a half,
        /// so a node whose demand is conduction asks 1.6 times what it did and one whose demand is
        /// convection asks 0.4 times. Vacuum is all conduction, so the same grid demands 37.13
        /// substeps where it demanded 23.20 — and air is where the demand used to be large, so the
        /// panel's p99 there went the other way, from 115 % of the substep cap to 55 %
        /// ([balance.md](../../docs/balance.md)). The retune moves solver cost from the
        /// environment a ship flies in to the one it parks in.
        /// </para>
        ///
        /// <para>
        /// What that reaches is the element-visit allowance rather than the substep cap: 2,000,000
        /// visits a step covered a 12,000-block grid before and covers about 6,000 now. Past that
        /// the solver spreads a step over more frames, which is the design — it shortens simulated
        /// time rather than coarsening a step — but simulated time is what `F23` measured a rate
        /// difference in, so the trade has a price and it is now paid on smaller grids.
        /// </para>
        /// </summary>
        [Fact]
        public void TheShippedAllowanceNoLongerCoversAnEightThousandBlockGridInVacuum()
        {
            ThermalSimulation simulation = Build(Small);
            while (simulation.HasPendingWork) simulation.Update(LoadBenchmarks.TickSeconds, Space());
            LoadBenchmarks.SeedSpread(simulation);

            simulation.Settings.MaxSubsteps = 4096;
            simulation.Settings.Derive();

            for (int tick = 0; tick < 60; tick++)
            {
                simulation.Update(LoadBenchmarks.TickSeconds, Space());
            }

            output.WriteLine("8,000 blocks: demand "
                + simulation.Solver.LastRequiredSubsteps.ToString("n2")
                + ", budget " + simulation.SubstepBudget.ToString("n0")
                + ", rate " + (100d * simulation.SimulationRate).ToString("n1") + "%.");

            // Measured 2026-08-24: 94.3 %. Bounded either side, so both a worse regression and a
            // fix announce themselves — a fix being the allowance moving, the demand coming down,
            // or `C27` closing.
            Assert.InRange(simulation.SimulationRate, 0.85d, 0.99d);

            // And it is the demand that moved rather than the cost of a substep, which is what
            // says this is the retune and not a solver that got slower.
            Assert.True(simulation.Solver.LastRequiredSubsteps > 30f,
                "the grid demands only " + simulation.Solver.LastRequiredSubsteps
                + " substeps, so the shortfall above is no longer C24's");
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
            while (simulation.HasPendingWork) simulation.Update(LoadBenchmarks.TickSeconds, Space());
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
