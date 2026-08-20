using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// One solver step, spread across the frames of its simulation window.
    ///
    /// <para>
    /// A grid advances by one step every <c>1 / StepsPerSecond</c> of a second, about fifteen
    /// rendered frames at the default settings. Running the whole step on one of those frames costs
    /// the same total as spreading it evenly but arrives as a stutter, so the work is divided
    /// across the window.
    /// </para>
    ///
    /// <para>
    /// Spreading is exact rather than approximate, and does not advance any block ahead of any
    /// other. A substep is an accumulation: every exchange is computed from the temperatures at the
    /// start of the substep, summed into a watts buffer, and applied to all nodes together at the
    /// end. A sum may be computed in any order and in any number of pieces without changing its
    /// value, so only the arithmetic is divided. <c>SpreadStepTests</c> asserts the result is
    /// identical to computing the step in one call.
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

            /// <summary>Coolant loops, room air and heat pumps. Few enough to run whole.</summary>
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
        /// Node and link counts as they were when the step began.
        ///
        /// A step spans many frames and a block may be placed during one of them. The passes walk
        /// the counts they started with, so a node appended mid-step is excluded from this step —
        /// its mirrored row is unsynced and its thermal mass is zero, which would produce NaN — and
        /// joins the next one.
        ///
        /// A change that moves an index rather than appending, such as a removal or a graph
        /// rebuild, abandons the step instead: no snapshot can repair a buffer whose entries now
        /// refer to different blocks.
        /// </summary>
        private int stepNodeCount;
        private int stepLinkCount;

        /// <summary>True while a step has been begun and not yet finished.</summary>
        public bool StepInFlight
        {
            get { return stage != StepStage.Idle; }
        }

        /// <summary>
        /// Element visits one whole step will make, from the substep count and the size of the grid.
        /// A host divides this by the frames in its window to size each frame's slice.
        ///
        /// Exact rather than approximate: it is the number the stages will charge, summed ahead of
        /// time. An estimate below the real figure would make every step overrun its window by the
        /// same proportion, so a grid configured for four steps a second would run slower.
        ///
        /// It is not a cost model. A link visit and a node visit differ in real cost; what matters
        /// is that the budget is spent in the units it was measured in, so the pacing divides
        /// evenly.
        /// </summary>
        public long StepWorkUnits { get; private set; }

        /// <summary>Element visits still to make before the step in flight completes.</summary>
        public long StepWorkRemaining { get; private set; }

        /// <summary>
        /// Budget the last <see cref="AdvanceStep"/> spent of what it was given.
        ///
        /// A step finishes on whichever slice crosses its last element, rarely spending that
        /// slice's budget exactly. A host that discards the remainder loses part of every step and
        /// runs below its configured frequency; one that carries it forward holds the rate exact.
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
        /// Advances the whole grid by <paramref name="deltaSeconds"/> of simulated time in one call.
        ///
        /// Used by tests and scenarios, which need reproducible time rather than frame pacing. Also
        /// the reference the spread implementation is checked against.
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

            // Any step already in flight is finished before another begins; discarding it would
            // lose the energy it had accumulated.
            if (StepInFlight) return true;

            RebuildLinksIfNeeded();
            EnsureBuffers();
            SyncNodeState();

            // Order matters: the conductance totals, then the mass floor that reads them, then the
            // link factors the floor invalidates.
            RecomputeConductanceTotalsIfNeeded();
            ApplyThermalMassFloor();
            RefreshLinkMassFactors();

            Environment = environment;
            stepEnvironment = environment;
            stepDeltaSeconds = deltaSeconds;

            // A new step brings a new environment sample and a new substep length, so everything
            // the previous step's substeps cached must be recomputed.
            InvalidateEnvironmentRows();

            overheats.Clear();
            crossings.Clear();

            // One estimate serves both figures. It walks every node cubing a temperature, so asking
            // twice per step would double its cost for no new information.
            float required = RequiredSubstepsFromState(deltaSeconds);
            int substeps = ClampSubsteps(required);
            LastSubsteps = substeps;
            LastStepWasClamped = required > MaxSubsteps;

            // Reported against a full step rather than the shortened one this call was handed. The
            // estimate is proportional to step length, so a step already cut to fit the visit budget
            // demands roughly what it was granted by construction. The useful figure is what the
            // grid would demand at its configured rate, which keeps moving after the budget binds.
            LastRequiredSubsteps = deltaSeconds > 0f
                ? required * (settings.StepSeconds / deltaSeconds)
                : required;

            for (int p = 0; p < heatPumps.Count; p++)
            {
                heatPumps[p].BeginStep();
            }

            for (int l = 0; l < loops.Count; l++)
            {
                loops[l].BeginStep();
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
            for (int i = 0; i < loops.Count; i++) loops[i].ClearSegmentWatts();
            for (int i = 0; i < roomAir.Count; i++) roomWatts[i] = 0f;

            ClearConductionDiagnostics();
            ResetEnvironmentTotals();

            // Once per substep rather than once per slice: it resolves the sun and wind into
            // per-face weights and advances the shadow map's budget, both of which would otherwise
            // run many times faster than intended.
            stepPlan = PlanEnvironment(ref stepEnvironment);

            stage = StepStage.Environment;
            stageCursor = 0;

            // Clearing is a memset over the same nodes the passes walk, so it is charged as a
            // fraction of a node visit rather than as free.
            return nodeCount / 8;
        }

        private long AdvanceEnvironment(long budget)
        {
            int count = stepNodeCount;
            int end = Advance(count, budget);

            AccumulateEnvironmentRange(ref stepEnvironment, ref stepPlan, substepSeconds,
                stageCursor, end);

            long spent = end - stageCursor;
            stageCursor = end;

            if (stageCursor >= count)
            {
                // Registered point sources cost one visit per source per node, and a world normally
                // has none. Run whole rather than sliced: slicing would need a two-dimensional
                // cursor to spread work that is usually zero.
                if (stepPlan.SourcesEnabled)
                {
                    AccumulateHeatSources(ref stepEnvironment, stepPlan.Diagnostics);
                    spent += (long)stepEnvironment.HeatSourceCount * count;
                }

                // Every node has been visited and the sources are in, so this substep's heat
                // totals are complete.
                PublishEnvironmentTotals();

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

            for (int l = 0; l < loops.Count; l++)
            {
                loops[l].EndStep(stepDeltaSeconds);
            }

            stage = StepStage.Publish;
            stageCursor = 0;
            publishHottest = -1;
            publishPeak = float.MinValue;
            return spent;
        }

        /// <summary>
        /// Copies the step's results onto the node objects, which are the simulation's public face.
        ///
        /// The reported change is measured against the start of the step rather than the last
        /// substep, since every consumer — the HUD's rate of change, the anomaly classifier, the
        /// per-type distribution — describes one step. Measured per substep, a six-substep grid
        /// would report about a sixth of the movement it made.
        ///
        /// The hottest node is found in the same loop for one comparison per node.
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

                // The step's change is applied on top of the node's current temperature rather than
                // overwriting it. A step spans many frames, so a host can write a temperature
                // mid-step — a save load, a grid split, another mod through the API — and
                // overwriting would discard that write.
                //
                // Where nothing wrote, the node still holds the float the step started from and the
                // result is the step's own answer bit for bit.
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
        /// How far a cursor may advance under a budget, clamped to the elements remaining.
        ///
        /// Computed as the smaller of the budget and the remaining room rather than as
        /// <c>cursor + budget</c> clamped afterwards: the budget may be <see cref="long.MaxValue"/>,
        /// which <see cref="Step"/> passes to run a step in one call, and adding a non-zero cursor
        /// to that overflows to a negative index. The cursor is non-zero on that path when an
        /// unbounded call finishes a step a paced one left part way through a stage.
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
        /// No temperature is lost: a substep's watts are applied together at its end, so an
        /// abandoned step leaves every temperature where the last completed substep left it. Called
        /// when the grid changes shape under a step, which moves node indices and invalidates the
        /// half-summed buffers.
        /// </summary>
        public void AbandonStep()
        {
            if (stage == StepStage.Idle) return;

            for (int p = 0; p < heatPumps.Count; p++)
            {
                heatPumps[p].EndStep(stepDeltaSeconds);
            }

            for (int l = 0; l < loops.Count; l++)
            {
                loops[l].EndStep(stepDeltaSeconds);
            }

            stage = StepStage.Idle;
            stageCursor = 0;
            substepsLeft = 0;
            StepWorkRemaining = 0;
        }
    }
}
