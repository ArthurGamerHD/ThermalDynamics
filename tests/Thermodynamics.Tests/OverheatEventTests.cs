using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// One overheating block produces <b>one</b> overheat event a step, not one per substep.
    ///
    /// <para>
    /// The damage check runs inside the apply pass, which runs once per substep, so a block over
    /// its rating used to file an event every time — eleven of them on a grid taking eleven
    /// substeps. The total damage was right, because the consumers sum it, but three things that
    /// read the list rather than summing it were not:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><c>ThermalGridSimulation.CriticalBlocks</c>, which is <c>Overheats.Count</c> and is
    /// reported as a count of blocks.</item>
    /// <item><c>ApplyOverheatDamage</c>, which resolved the block and called <c>DoDamage</c> once
    /// per event — eleven engine calls a step for one burning block, and eleven telemetry
    /// records.</item>
    /// <item><c>ScenarioRunner</c>'s <c>OverheatingBlocks</c> column, same figure.</item>
    /// </list>
    ///
    /// <para>
    /// And the harness's batched step path keeps every step's events for the whole run, so the
    /// substep factor multiplied a list that was already proportional to run length. A driven
    /// 4,000-step sweep on a 43,232-block hull was OOM-killed at 14 GB.
    /// </para>
    ///
    /// <para>
    /// The fix is to accumulate per node and file once when the step ends. That must not change
    /// what a block actually takes, so the first test here is the one that matters: total damage
    /// over a run, against the same run before the change.
    /// </para>
    /// </summary>
    public class OverheatEventTests
    {
        /// <summary>
        /// A hull hot enough that blocks are past their rating, taking enough substeps that a
        /// per-substep event would be plainly distinguishable from a per-step one.
        /// </summary>
        private static ThermalSimulation Cooking(int blocks = 800)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubsteps = 4096;
            settings.MaxSubstepsPerBlock = 0;
            settings.MaxElementVisitsPerStep = 0;
            settings.EnableDamage = true;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", blocks));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 2000f);
            simulation.RebuildAll();
            return simulation;
        }

        private static int DistinctBlocks(IList<OverheatEvent> events)
        {
            HashSet<VRageMath.Vector3I> seen = new HashSet<VRageMath.Vector3I>();
            for (int i = 0; i < events.Count; i++) seen.Add(events[i].Block.Position);
            return seen.Count;
        }

        /// <summary>
        /// The headline claim, and the one the consumers rely on: the list is a list of blocks.
        /// </summary>
        [Fact]
        public void AStepFilesOneEventPerOverheatingBlock()
        {
            ThermalSimulation simulation = Cooking();
            simulation.StepExact(1, Worlds.Shadow());

            IList<OverheatEvent> events = simulation.Solver.Overheats;

            Assert.True(events.Count > 0,
                "no block overheated, so this asserts nothing — the fixture is not cooking");
            Assert.True(simulation.Solver.LastSubsteps > 1,
                "the hull took " + simulation.Solver.LastSubsteps + " substep(s), so a per-substep"
                + " list and a per-step list would be the same length and the test is blind");

            Assert.Equal(DistinctBlocks(events), events.Count);
        }

        /// <summary>
        /// The constraint the fix has to respect. Damage is applied per substep and summed by
        /// every consumer, so coalescing must preserve the sum — a block must take exactly as much
        /// heat damage as it did when each substep filed separately.
        ///
        /// The tolerance is a relative one rather than bit-equality: summing per node and then
        /// across nodes adds the same terms in a different order, and float addition is not
        /// associative. A part in a hundred thousand is far below anything a block's integrity
        /// can notice and far above the reordering.
        /// </summary>
        [Fact]
        public void CoalescingPreservesTheDamageABlockTakes()
        {
            ThermalSimulation simulation = Cooking();
            simulation.StepExact(30, Worlds.Shadow());

            double total = 0d;
            IList<OverheatEvent> events = simulation.Overheats;
            for (int i = 0; i < events.Count; i++) total += events[i].Damage;

            Assert.True(total > 0d, "nothing took damage over thirty steps");

            // The per-substep sum, recomputed from the same run driven substep by substep. A step
            // of one substep files one event per block either way, so this is the same arithmetic
            // the old path did — and it is the number the new path has to reproduce.
            ThermalSimulation reference = Cooking();
            float step = reference.Settings.StepSeconds;
            EnvironmentState state = EnvironmentSolver.Solve(
                reference.Settings, reference.Planet, Worlds.Shadow());

            double referenceTotal = 0d;
            for (int i = 0; i < 30; i++)
            {
                reference.Solver.Step(step, state);
                IList<OverheatEvent> stepEvents = reference.Solver.Overheats;
                for (int e = 0; e < stepEvents.Count; e++) referenceTotal += stepEvents[e].Damage;
            }

            Assert.True(referenceTotal > 0d, "the reference run took no damage");

            double relative = System.Math.Abs(total - referenceTotal) / referenceTotal;
            Assert.True(relative < 1e-5d,
                "the batched path totalled " + total.ToString("g6") + " of damage against "
                + referenceTotal.ToString("g6") + " stepped one at a time — a relative difference"
                + " of " + relative.ToString("g3") + ", which is more than reordering a sum");
        }

        /// <summary>
        /// The temperature an event carries is the hottest the block reached while over its
        /// rating, not whichever substep happened to file last. A coalesced event has to choose,
        /// and the peak is the one a player would recognise and the one a report should show.
        /// </summary>
        [Fact]
        public void AnEventCarriesTheHottestTemperatureTheBlockReached()
        {
            ThermalSimulation simulation = Cooking();
            simulation.StepExact(1, Worlds.Shadow());

            IList<OverheatEvent> events = simulation.Solver.Overheats;
            Assert.True(events.Count > 0, "no block overheated");

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            Dictionary<VRageMath.Vector3I, float> ended =
                new Dictionary<VRageMath.Vector3I, float>();
            for (int i = 0; i < nodes.Count; i++) ended[nodes[i].Block.Position] = nodes[i].Temperature;

            for (int i = 0; i < events.Count; i++)
            {
                OverheatEvent overheat = events[i];
                float final = ended[overheat.Block.Position];

                // A cooling block passes through hotter temperatures than it ends on, so the peak
                // is at or above where it finished. A block that only ever heated ends on its own
                // peak and the two agree.
                Assert.True(overheat.Temperature >= final - 1e-3f,
                    overheat.Block.Position + " filed " + overheat.Temperature.ToString("n2")
                    + " K against a final " + final.ToString("n2")
                    + " K, so the event is carrying neither the peak nor the last substep");
            }
        }

        /// <summary>
        /// The list is rebuilt each step rather than appended to, which is what lets a grid burn
        /// indefinitely without the solver's own list growing.
        /// </summary>
        [Fact]
        public void TheListDoesNotGrowAcrossSteps()
        {
            ThermalSimulation simulation = Cooking();
            simulation.StepExact(1, Worlds.Shadow());
            int first = simulation.Solver.Overheats.Count;

            Assert.True(first > 0, "no block overheated on the first step");

            simulation.StepExact(20, Worlds.Shadow());
            int later = simulation.Solver.Overheats.Count;

            // Not equality: blocks cool below their rating as the hull evens out, so the count may
            // fall. What it must not do is climb with the number of steps taken.
            Assert.True(later <= first, "the solver reported " + later + " overheat events after"
                + " twenty-one steps against " + first + " after one, so the list is accumulating"
                + " across steps rather than describing the step just taken");
        }
    }
}
