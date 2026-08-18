using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The step, spread across the frames of its simulation window.
    ///
    /// <para>
    /// A grid advances by one solver step every <c>1 / StepsPerSecond</c> of a second — a window
    /// of about fifteen rendered frames at the default settings. Running the whole step on one of
    /// those frames and nothing on the other fourteen is what a player feels as a stutter, and it
    /// is what the simulation did: the cost of a ship arrived in a single lump, fifteen frames
    /// apart. Spreading the same work evenly over the window costs the same total and is felt as
    /// a steady frame rate instead.
    /// </para>
    ///
    /// <para>
    /// This is safe here in a way it was not in the original mod, and for exactly the reason the
    /// original's version of it was a defect. That one advanced <em>different blocks on different
    /// frames</em>, so a block's neighbours could be a frame ahead of or behind it; the result
    /// depended on iteration order, which is why it had to alternate sweep direction to keep the
    /// bias fair. This does not move any block ahead of any other. A substep is an accumulation —
    /// every exchange computed from the temperatures at the start of the substep, summed into a
    /// watts buffer, and applied to every node together at the end — and a sum can be computed in
    /// any order, in any number of pieces, without changing its value. What is spread is the
    /// arithmetic, not the simulation: the answer is identical to computing it all at once, and
    /// <c>SpreadStepTests</c> asserts exactly that.
    /// </para>
    ///
    /// <para>
    /// The order-independence the rewrite introduced is therefore not a reason the whole grid
    /// <em>must</em> step at once, as the scheduler used to claim. It is the property that makes
    /// stepping it in pieces correct.
    /// </para>
    /// </summary>
    public partial class ThermalSolver
    {
        /// <summary>Where a step in flight has got to.</summary>
        private enum StepStage
        {
            /// <summary>No step in flight.</summary>
            Idle,

            /// <summary>Zero the watts buffers and plan the substep.</summary>
            Begin,

            /// <summary>Radiation, convection, solar, friction and waste heat, per node.</summary>
            Environment,

            /// <summary>Conduction, per link.</summary>
            Conduction,

            /// <summary>Coolant loops, room air and heat pumps — small, so run whole.</summary>
            Coupled,

            /// <summary>Turn accumulated watts into temperatures, per node.</summary>
            Apply,

            /// <summary>Publish the step's results onto the node objects, per node.</summary>
            Publish
        }

        private StepStage stage = StepStage.Idle;
        private int stageCursor;

        private EnvironmentState stepEnvironment;
        private EnvironmentPlan stepPlan;
        private float stepDeltaSeconds;
        private float substepSeconds;
        private int substepsLeft;

        private int publishHottest;
        private float publishPeak;

        /// <summary>
        /// Nodes and links as they were when the step began.
        ///
        /// A step spans many frames, and a block can be placed during one of them. The passes walk
        /// the counts they started with, so a node appended mid-step simply is not in this step —
        /// its row has not been synced and its thermal mass is still zero, and dividing by that is
        /// how a temperature becomes NaN. It joins the next step, one sixtieth of a second later.
        ///
        /// Anything that <em>moves</em> an index rather than appending — a block removed, a graph
        /// rebuilt — abandons the step instead, because no snapshot can rescue a buffer whose
        /// entries now refer to different blocks.
        /// </summary>
        private int stepNodeCount;
        private int stepLinkCount;

        /// <summary>True while a step has been begun and not yet finished.</summary>
        public bool StepInFlight
        {
            get { return stage != StepStage.Idle; }
        }

        /// <summary>
        /// Element visits one whole step will make, from the substep count and the size of the
        /// grid. This is the figure a host divides by the frames in its window to decide how much
        /// of the step to do on each of them.
        ///
        /// This is not an approximation of the work: it is exactly the number the stages will
        /// charge, summed ahead of time. It has to be, because a host paces the step by dividing
        /// it across the frames of the window — an estimate 12 % under what the step really costs
        /// makes every step take 12 % longer than its window, and a grid configured for four
        /// steps a second quietly runs at three.
        ///
        /// It is still not a <em>cost</em> model. A link visit and a node visit are not the same
        /// price in nanoseconds, and nothing here pretends otherwise; what matters is that the
        /// units the budget is spent in are the units it was measured in, so the pacing divides
        /// evenly.
        /// </summary>
        public long StepWorkUnits { get; private set; }

        /// <summary>Element visits still to make before the step in flight completes.</summary>
        public long StepWorkRemaining { get; private set; }

        /// <summary>
        /// What the last <see cref="AdvanceStep"/> actually spent of the budget it was given.
        ///
        /// A step finishes on whichever slice crosses its last element, which is rarely the exact
        /// budget that slice was handed — the work estimate is proportional rather than exact. A
        /// host that discards the difference loses a little of every step and ends up running
        /// slower than the frequency it was configured with; one that carries it forward keeps
        /// the rate exact.
        /// </summary>
        public long LastAdvanceWork { get; private set; }

        /// <summary>
        /// Work one substep charges, in element visits: the buffers cleared, the environment pass
        /// and the apply pass over the nodes, the conduction pass over the links, and one visit
        /// each for the coolant loops, rooms and heat pumps.
        /// </summary>
        private long SubstepWork(int nodeCount, int linkCount, int sources)
        {
            return (nodeCount / 8)
                + nodeCount
                + ((long)sources * nodeCount)
                + linkCount
                + loops.Count + roomAir.Count + heatPumps.Count
                + nodeCount;
        }

        /// <summary>
        /// Work one substep makes on the grid as it stands. Public so a host can size a budget
        /// before any step has begun.
        /// </summary>
        public long SubstepWorkUnits
        {
            get { return SubstepWork(nodes.Count, links.Count, 0); }
        }

        /// <summary>
        /// Advances the whole grid by <paramref name="deltaSeconds"/> of simulated time, in one
        /// go.
        ///
        /// The direct route, kept because tests and scenarios want reproducible time rather than
        /// frame pacing, and because it is the definition the spread version is checked against.
        /// </summary>
        public void Step(float deltaSeconds, EnvironmentState environment)
        {
            if (!BeginStep(deltaSeconds, environment)) return;
            while (!AdvanceStep(long.MaxValue)) { }
        }

        /// <summary>
        /// Starts a step. Nothing is integrated until <see cref="AdvanceStep"/> is called.
        /// </summary>
        /// <returns>False when there was nothing to do, in which case no step is in flight.</returns>
        public bool BeginStep(float deltaSeconds, EnvironmentState environment)
        {
            if (deltaSeconds <= 0f) return false;

            // A step already running is finished before another begins. A host that asks for one
            // while one is in flight has lost track, and silently discarding the part-finished
            // step would lose the energy it had already accumulated.
            if (StepInFlight) return true;

            RebuildLinksIfNeeded();
            EnsureBuffers();
            SyncNodeState();
            RefreshLinkMassFactors();
            RecomputeConductanceTotalsIfNeeded();

            Environment = environment;
            stepEnvironment = environment;
            stepDeltaSeconds = deltaSeconds;

            overheats.Clear();
            crossings.Clear();

            // One estimate, used for both answers. It walks every node cubing a temperature, so
            // asking twice per step doubled the cost of the cheapest thing the solver does for no
            // new information.
            float required = RequiredSubstepsFromState(deltaSeconds);
            int substeps = ClampSubsteps(required);
            LastSubsteps = substeps;
            LastStepWasClamped = required > MaxSubsteps;

            for (int p = 0; p < heatPumps.Count; p++)
            {
                heatPumps[p].BeginStep();
            }

            Work.SolverSteps++;
            Work.SolverSubsteps += substeps;

            substepSeconds = deltaSeconds / substeps;
            substepsLeft = substeps;

            stepNodeCount = nodes.Count;
            stepLinkCount = links.Count;

            int sources = settings.EnableHeatSources ? environment.HeatSourceCount : 0;
            StepWorkUnits = (SubstepWork(stepNodeCount, stepLinkCount, sources) * substeps)
                + stepNodeCount;
            StepWorkRemaining = StepWorkUnits;

            stage = StepStage.Begin;
            stageCursor = 0;
            return true;
        }

        /// <summary>
        /// Does at most <paramref name="workBudget"/> element visits of the step in flight.
        /// </summary>
        /// <returns>True when the step completed, or when none was in flight.</returns>
        public bool AdvanceStep(long workBudget)
        {
            LastAdvanceWork = 0;

            if (stage == StepStage.Idle) return true;
            if (workBudget <= 0) return false;

            long spent = 0;

            while (stage != StepStage.Idle && spent < workBudget)
            {
                long remaining = workBudget - spent;

                switch (stage)
                {
                    case StepStage.Begin:
                        spent += BeginSubstep();
                        break;

                    case StepStage.Environment:
                        spent += AdvanceEnvironment(remaining);
                        break;

                    case StepStage.Conduction:
                        spent += AdvanceConduction(remaining);
                        break;

                    case StepStage.Coupled:
                        spent += AdvanceCoupled();
                        break;

                    case StepStage.Apply:
                        spent += AdvanceApply(remaining);
                        break;

                    default:
                        spent += AdvancePublish(remaining);
                        break;
                }
            }

            LastAdvanceWork = spent;

            StepWorkRemaining -= spent;
            if (StepWorkRemaining < 0) StepWorkRemaining = 0;

            return stage == StepStage.Idle;
        }

        private long BeginSubstep()
        {
            int nodeCount = stepNodeCount;

            Array.Clear(nodeWatts, 0, nodeCount);
            for (int i = 0; i < loops.Count; i++) loopWatts[i] = 0f;
            for (int i = 0; i < roomAir.Count; i++) roomWatts[i] = 0f;

            ClearConductionDiagnostics();

            // Once per substep, not once per slice: it resolves the sun and wind into per-face
            // weights and advances the shadow map's own budget, and doing either per slice would
            // run them many times faster than intended.
            stepPlan = PlanEnvironment(ref stepEnvironment);

            stage = StepStage.Environment;
            stageCursor = 0;

            // The clearing is a memset over the same nodes the passes walk; charging it as a
            // fraction of them keeps the budget honest without pretending it is free.
            return nodeCount / 8;
        }

        private long AdvanceEnvironment(long budget)
        {
            int count = stepNodeCount;
            int end = Advance(count, budget);

            AccumulateEnvironmentRange(ref stepEnvironment, ref stepPlan, stageCursor, end);

            long spent = end - stageCursor;
            stageCursor = end;

            if (stageCursor >= count)
            {
                // Registered point sources are per source per node, and there is normally not one
                // in a world. Run whole rather than sliced: slicing would need a cursor over two
                // dimensions to spread something that is usually zero work.
                if (stepPlan.SourcesEnabled)
                {
                    AccumulateHeatSources(ref stepEnvironment, stepPlan.Diagnostics);
                    spent += (long)stepEnvironment.HeatSourceCount * count;
                }

                stage = StepStage.Conduction;
                stageCursor = 0;
            }

            return spent;
        }

        private long AdvanceConduction(long budget)
        {
            int count = stepLinkCount;
            int end = Advance(count, budget);

            AccumulateConductionRange(substepSeconds, stageCursor, end);

            long spent = end - stageCursor;
            stageCursor = end;

            if (stageCursor >= count)
            {
                stage = StepStage.Coupled;
                stageCursor = 0;
            }

            return spent;
        }

        private long AdvanceCoupled()
        {
            AccumulateLoops(substepSeconds);
            AccumulateRoomAir(substepSeconds);
            AccumulateHeatPumps(substepSeconds);

            stage = StepStage.Apply;
            stageCursor = 0;

            return loops.Count + roomAir.Count + heatPumps.Count;
        }

        private long AdvanceApply(long budget)
        {
            int count = stepNodeCount;
            int end = Advance(count, budget);

            ApplyNodeWattsRange(substepSeconds, stageCursor, end);

            long spent = end - stageCursor;
            stageCursor = end;

            if (stageCursor < count) return spent;

            ApplyCoupledWatts(substepSeconds);

            substepsLeft--;
            if (substepsLeft > 0)
            {
                stage = StepStage.Begin;
                stageCursor = 0;
                return spent;
            }

            for (int p = 0; p < heatPumps.Count; p++)
            {
                heatPumps[p].EndStep(stepDeltaSeconds);
            }

            stage = StepStage.Publish;
            stageCursor = 0;
            publishHottest = -1;
            publishPeak = float.MinValue;
            return spent;
        }

        /// <summary>
        /// Copies the step's results onto the node objects, which are the simulation's public
        /// face.
        ///
        /// The reported change is measured against the top of the step, not against the last
        /// substep. Everything that reads it — the HUD's rate of change, the anomaly classifier
        /// recovering the previous temperature, the per-type distribution — is describing one
        /// step; a six-substep grid otherwise reports roughly a sixth of the movement it made.
        ///
        /// The hottest block falls out of the same loop for one comparison per node, so the
        /// readouts that want it cost nothing.
        /// </summary>
        private long AdvancePublish(long budget)
        {
            int count = stepNodeCount;
            int end = Advance(count, budget);

            bool watching = thresholds.Count > 0;

            for (int i = stageCursor; i < end; i++)
            {
                float updated = nodeTemperatures[i];
                ThermalNode node = nodes[i];
                float previous = nodeStepStart[i];

                // A step now spans many frames, so a host can write a temperature part way
                // through one — loading a save, a grid split, another mod through the API. It
                // used to be impossible to lose such a write, because a step began and ended
                // inside one call; now overwriting would silently discard it.
                //
                // Where nothing wrote, the node still holds exactly the float the step started
                // from and the result is the step's own answer, bit for bit. Where something did,
                // the step's change is applied on top of what it wrote, which is what a step
                // means: an amount to move by.
                if (node.Temperature == previous)
                {
                    node.Temperature = updated;
                }
                else
                {
                    node.Temperature += updated - previous;
                }

                node.LastDeltaTemperature = updated - previous;

                if (updated > publishPeak)
                {
                    publishPeak = updated;
                    publishHottest = i;
                }

                if (watching) thresholds.Collect(node.Block, previous, updated, crossings);
            }

            long spent = end - stageCursor;
            stageCursor = end;

            if (stageCursor >= count)
            {
                hottestNode = publishHottest;
                StepCount++;
                stage = StepStage.Idle;
                stageCursor = 0;
                StepWorkRemaining = 0;
            }

            return spent;
        }

        /// <summary>
        /// How far a cursor may move given a budget, clamped to what is left of the collection.
        ///
        /// Written as "take the smaller of the budget and the room remaining" rather than as
        /// <c>cursor + budget</c> clamped afterwards, because the budget can legitimately be
        /// <see cref="long.MaxValue"/> — that is what <see cref="Step"/> passes to run a step in
        /// one go — and adding a non-zero cursor to it overflows to a negative index. The cursor
        /// is only non-zero on that path when an unbounded call is finishing a step that a paced
        /// one left part way through a stage, which is exactly what the harness does when a
        /// scenario takes over a simulation the host had been updating.
        /// </summary>
        private int Advance(int count, long budget)
        {
            if (budget <= 0) return stageCursor;

            long room = count - stageCursor;
            if (room <= 0) return stageCursor;

            return stageCursor + (int)(budget < room ? budget : room);
        }

        /// <summary>
        /// Abandons a step in flight, discarding whatever it had accumulated.
        ///
        /// Nothing is lost by doing so: a substep's watts are applied all at once at its end, so
        /// a step abandoned part way leaves every temperature exactly where the last completed
        /// substep left it. The host calls this when the grid changes shape underneath a step —
        /// node indices move, links appear and vanish, and the half-summed buffer refers to a grid
        /// that no longer exists.
        /// </summary>
        public void AbandonStep()
        {
            if (stage == StepStage.Idle) return;

            for (int p = 0; p < heatPumps.Count; p++)
            {
                heatPumps[p].EndStep(stepDeltaSeconds);
            }

            stage = StepStage.Idle;
            stageCursor = 0;
            substepsLeft = 0;
            StepWorkRemaining = 0;
        }
    }
}
