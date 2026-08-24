using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// One solver step, spread across the frames of its simulation window. Only the arithmetic is
    /// divided — a substep is a sum, and a sum has the same value in any number of pieces — so the
    /// result is bit-identical to one call, which <c>SpreadStepTests</c> asserts.
    /// See load-and-hitching.md, 10.
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
        /// Node and link counts as they were when the step began, so a block placed mid-step joins the
        /// next one rather than being integrated against an unsynced row. A change that *moves* an
        /// index abandons the step instead. See known-issues.md, An invariant documented at three call
        /// sites.
        /// </summary>
        private int stepNodeCount;
        private int stepLinkCount;

        /// <summary>
        /// Whether the overshoot clamp can change any exchange during this step.
        ///
        /// Settled once in <see cref="BeginStep"/> from the substep length, which is what decides
        /// it, and read by every substep's conduction pass. See <c>ClampCanBind</c>. Readable so a
        /// test can tell a step that skipped the clamp from one that could not.
        ///
        /// **Not conduction alone, whatever the name says.** The per-node relaxation row it gates
        /// is what bounds a node fed by a coolant loop or a room's air as well as by its
        /// neighbours, and a grid can carry a ring with conduction switched off. Named for the
        /// setting that turns it on. See stiffness.md, What refusing the demand costs.
        /// </summary>
        public bool ConductionClampLive { get; private set; }

        /// <summary>
        /// Set false to run the clamped conduction loop whenever the setting asks for it, without
        /// first testing whether it can bind. Test hook: <c>ConductionClampGateTests</c> runs the
        /// same grid both ways and compares the results bit for bit.
        /// </summary>
        public bool GateConductionClamp = true;

        /// <summary>
        /// Whether this substep writes the per-mechanism watt diagnostics onto the node objects. Only
        /// the last substep's writes are ever observed, so only it pays.
        /// See benchmarks.md, What being measured costs.
        /// </summary>
        private bool diagnosticsSubstep;

        /// <summary>
        /// Set true to write the per-mechanism watt diagnostics on every substep rather than only
        /// the last. Test hook: <c>DiagnosticBatchingTests</c> runs the same grid both ways and
        /// compares the published figures bit for bit.
        /// </summary>
        public bool DiagnosticsOnEverySubstep;

        /// <summary>
        /// Set false to clear the watts row with its own memset rather than letting the environment
        /// pass write it outright. A test hook: <c>WattsClearFusionTests</c> pins the ordering claim
        /// the fusion rests on. See benchmarks.md, The watts row is written.
        /// </summary>
        public bool FuseWattsClear = true;

        /// <summary>True while a step has been begun and not yet finished.</summary>
        public bool StepInFlight
        {
            get { return stage != StepStage.Idle; }
        }

        /// <summary>
        /// Element visits one whole step will make; a host divides it by the frames in its window to
        /// size each slice. Exact rather than modelled — it is what the stages will charge, summed
        /// ahead of time — so the pacing divides evenly.
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
        /// Advances the whole grid by <paramref name="deltaSeconds"/> of simulated time in one call.
        ///
        /// Used by tests and scenarios, which need reproducible time rather than frame pacing. Also
        /// the reference the spread implementation is checked against.
        /// </summary>
        public void Step(float deltaSeconds, EnvironmentState environment)
        {
            Step(deltaSeconds, environment, -1f);
        }

        /// <summary>
        /// Runs a whole step in one call, reusing a stability estimate the caller already took.
        /// See <see cref="BeginStep(float, EnvironmentState, float)"/>.
        /// </summary>
        public void Step(float deltaSeconds, EnvironmentState environment, float knownRequired)
        {
            if (!BeginStep(deltaSeconds, environment, knownRequired)) return;
            while (!AdvanceStep(long.MaxValue)) { }
        }

        /// <summary>
        /// Starts a step. Nothing is integrated until <see cref="AdvanceStep"/> is called.
        /// </summary>
        /// <returns>False when there was nothing to do, in which case no step is in flight.</returns>
        public bool BeginStep(float deltaSeconds, EnvironmentState environment)
        {
            return BeginStep(deltaSeconds, environment, -1f);
        }

        /// <summary>
        /// Starts a step whose stability estimate the caller has already taken, at this step length
        /// and against this environment — which is what makes a step's fixed cost one walk over the
        /// nodes rather than two. A negative <paramref name="knownRequired"/> means no estimate.
        /// See benchmarks.md, The two step paths.
        /// </summary>
        public bool BeginStep(float deltaSeconds, EnvironmentState environment, float knownRequired)
        {
            if (deltaSeconds <= 0f) return false;

            // Any step already in flight is finished before another begins; discarding it would
            // lose the energy it had accumulated.
            if (StepInFlight) return true;

            // Set before the mass floor rather than after it. The floor and the estimate are the
            // same stability test and both read the environment's convection coefficient, so a
            // floor computed against the previous step's sample caps a figure the estimate never
            // saw.
            Environment = environment;

            float required;
            if (knownRequired >= 0f)
            {
                required = knownRequired;
            }
            else
            {
                PrepareStepState();
                required = RequiredSubstepsFromState(deltaSeconds);
            }

            // The link factors the mass floor invalidates, refreshed whichever route got here.
            RefreshLinkMassFactors();

            stepEnvironment = environment;
            stepDeltaSeconds = deltaSeconds;

            // A new step brings a new environment sample and a new substep length, so everything
            // the previous step's substeps cached must be recomputed.
            InvalidateEnvironmentRows();

            overheats.Clear();
            ClearOverheatAccumulator();
            crossings.Clear();

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

            // After the substep length is known and before any substep runs: the clamp is a
            // function of that length and of masses and conductances that cannot move mid-step.
            ConductionClampLive = settings.ClampConductionOvershoot
                && (settings.EnableConduction || settings.EnableCoolantLoops || settings.EnableRoomAir)
                && (!GateConductionClamp || ClampCanBind(substepSeconds));

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

            Work.StepAdvances++;

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

            // Decremented at the end of the apply stage, so it counts this substep as well.
            diagnosticsSubstep = CollectDiagnostics && (DiagnosticsOnEverySubstep || substepsLeft <= 1);

            // The environment pass assigns this row rather than accumulating onto it, so on the
            // fused path the memset is dead work: every node it zeroed is overwritten before
            // anything reads it. Kept behind the switch so the two can be compared.
            if (!FuseWattsClear) Array.Clear(nodeWatts, 0, nodeCount);
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

            // Every substep has run, so what each burning block owes for this step is complete.
            PublishOverheats();

            stage = StepStage.Publish;
            stageCursor = 0;
            publishHottest = -1;
            publishPeak = float.MinValue;
            return spent;
        }

        /// <summary>
        /// Copies the step's results onto the node objects, which are the simulation's public face.
        /// The reported change is measured against the start of the step, not the last substep, since
        /// every consumer describes one step. The hottest node is found in the same loop.
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
        /// How far a cursor may advance under a budget. The smaller of the budget and the room left,
        /// never <c>cursor + budget</c> clamped afterwards: <see cref="Step"/> passes
        /// <see cref="long.MaxValue"/>, which a non-zero cursor overflows to a negative index.
        /// </summary>
        private int Advance(int count, long budget)
        {
            if (budget <= 0) return stageCursor;

            long room = count - stageCursor;
            if (room <= 0) return stageCursor;

            return stageCursor + (int)(budget < room ? budget : room);
        }

        /// <summary>
        /// Abandons a step in flight. No temperature is lost — a substep's watts are applied together
        /// at its end — so this leaves every temperature where the last completed substep left it.
        /// </summary>
        public void AbandonStep()
        {
            if (stage == StepStage.Idle) return;

            // An abandoned step publishes no temperatures, so it owes no damage either. The
            // substeps that did run had filed their events directly before damage was
            // accumulated, which meant a discarded step could still burn a block for a
            // temperature nothing ever saw.
            ClearOverheatAccumulator();

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
