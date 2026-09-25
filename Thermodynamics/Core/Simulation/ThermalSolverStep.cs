using System;
using System.Diagnostics;

namespace Thermodynamics.Core
{
    public partial class ThermalSolver
    {
        private enum StepStage
        {
            Idle,

            Begin,

            Environment,

            Conduction,

            Coupled,

            Apply,

            Publish
        }

        private StepStage stage = StepStage.Idle;
        private int stageCursor;

        public class StepPhaseProfile
        {
            public readonly long[] Ticks = new long[PhaseCount];
            public readonly long[] Visits = new long[PhaseCount];
            public readonly long[] Slices = new long[PhaseCount];

            public const int PhaseCount = 7;

            public static readonly string[] Names =
                { "begin", "environment", "conduction", "coupled", "apply", "publish", "env fill" };

            public const int EnvironmentFill = 6;


            public void Reset()
            {
                for (int i = 0; i < PhaseCount; i++)
                {
                    Ticks[i] = 0;
                    Visits[i] = 0;
                    Slices[i] = 0;
                }
            }


            public double MillisecondsOf(int phase)
            {
                return Ticks[phase] * 1000d / Stopwatch.Frequency;
            }
        }

        public bool ProfileStepPhases;


        public readonly StepPhaseProfile StepPhases = new StepPhaseProfile();

        private EnvironmentState stepEnvironment;
        private EnvironmentPlan stepPlan;
        private float stepDeltaSeconds;
        private float substepSeconds;
        private int substepsLeft;

        private int publishHottest;
        private float publishPeak;

        private int stepNodeCount;
        private int stepLinkCount;

        public bool ConductionClampLive { get; private set; }

        public bool GateConductionClamp = true;

        private bool diagnosticsSubstep;

        public bool DiagnosticsOnEverySubstep;

        public bool FuseWattsClear = true;

        public bool StepInFlight
        {
            get { return stage != StepStage.Idle; }
        }

        public long StepWorkUnits { get; private set; }

        public long StepWorkRemaining { get; private set; }

        public long LastAdvanceWork { get; private set; }


        private long SubstepWork(int nodeCount, int linkCount, int sources)
        {
            return (nodeCount / 8)
                + nodeCount
                + ((long)sources * nodeCount)
                + linkCount
                + loops.Count + roomAir.Count + heatPumps.Count
                + nodeCount;
        }


        public void Step(float deltaSeconds, EnvironmentState environment)
        {
            Step(deltaSeconds, environment, -1f);
        }


        public void Step(float deltaSeconds, EnvironmentState environment, float knownRequired)
        {
            if (!BeginStep(deltaSeconds, environment, knownRequired)) return;
            while (!AdvanceStep(long.MaxValue)) { }
        }


        public bool BeginStep(float deltaSeconds, EnvironmentState environment)
        {

            return BeginStep(deltaSeconds, environment, -1f);
        }


        public bool BeginStep(float deltaSeconds, EnvironmentState environment, float knownRequired)
        {
            if (deltaSeconds <= 0f) return false;

            if (StepInFlight) return true;

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

            RefreshLinkMassFactors();

            stepEnvironment = environment;
            stepDeltaSeconds = deltaSeconds;

            InvalidateEnvironmentRows();

            overheats.Clear();
            ClearOverheatAccumulator();
            crossings.Clear();


            int substeps = ClampSubsteps(required);
            LastSubsteps = substeps;
            LastStepWasClamped = required > MaxSubsteps;

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

                int phase = (int)stage - 1;

                if (stage == StepStage.Environment && (!environmentRowsValid || !PrecomputeEnvironment))
                {
                    phase = StepPhaseProfile.EnvironmentFill;
                }

                long started = ProfileStepPhases ? Stopwatch.GetTimestamp() : 0L;
                long before = spent;

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

                if (ProfileStepPhases && phase >= 0 && phase < StepPhaseProfile.PhaseCount)
                {
                    StepPhases.Ticks[phase] += Stopwatch.GetTimestamp() - started;
                    StepPhases.Visits[phase] += spent - before;
                    StepPhases.Slices[phase]++;
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

            diagnosticsSubstep = CollectDiagnostics && (DiagnosticsOnEverySubstep || substepsLeft <= 1);

            if (!FuseWattsClear) Array.Clear(nodeWatts, 0, nodeCount);
            for (int i = 0; i < loops.Count; i++) loops[i].ClearSegmentWatts();
            for (int i = 0; i < roomAir.Count; i++) roomWatts[i] = 0f;

            ClearConductionDiagnostics();
            ResetEnvironmentTotals();


            stepPlan = PlanEnvironment(ref stepEnvironment);

            stage = StepStage.Environment;
            stageCursor = 0;

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
                if (stepPlan.SourcesEnabled)
                {
                    AccumulateHeatSources(ref stepEnvironment, stepPlan.Diagnostics);
                    spent += (long)stepEnvironment.HeatSourceCount * count;
                }

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

            PublishOverheats();

            stage = StepStage.Publish;
            stageCursor = 0;
            publishHottest = -1;
            publishPeak = float.MinValue;
            return spent;
        }


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


        private int Advance(int count, long budget)
        {
            if (budget <= 0) return stageCursor;

            long room = count - stageCursor;
            if (room <= 0) return stageCursor;

            return stageCursor + (int)(budget < room ? budget : room);
        }


        public void AbandonStep()
        {
            if (stage == StepStage.Idle) return;

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
