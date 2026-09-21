using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    public class OverheatEventTests
    {
/// <summary>Cooking operation.</summary>
        private static ThermalSimulation Cooking(int blocks = 800)
        {
/// <summary>ThermalSettings operation.</summary>
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

/// <summary>DistinctBlocks operation.</summary>
        private static int DistinctBlocks(IList<OverheatEvent> events)
        {
/// <summary>HashSet operation.</summary>
            HashSet<VRageMath.Vector3I> seen = new HashSet<VRageMath.Vector3I>();
            for (int i = 0; i < events.Count; i++) seen.Add(events[i].Block.Position);
            return seen.Count;
        }

        [Fact]
/// <summary>AStepFilesOneEventPerOverheatingBlock operation.</summary>
        public void AStepFilesOneEventPerOverheatingBlock()
        {
/// <summary>Cooking operation.</summary>
            ThermalSimulation simulation = Cooking();
            simulation.StepExact(1, Worlds.Shadow());

            IList<OverheatEvent> events = simulation.Solver.Overheats;

            Assert.True(events.Count > 0,
                "no block overheated, so this asserts nothing — the fixture is not cooking");
            Assert.True(simulation.Solver.LastSubsteps > 1,
/// <summary>substep operation.</summary>
                "the hull took " + simulation.Solver.LastSubsteps + " substep(s), so a per-substep"
                + " list and a per-step list would be the same length and the test is blind");

            Assert.Equal(DistinctBlocks(events), events.Count);
        }

        [Fact]
/// <summary>CoalescingPreservesTheDamageABlockTakes operation.</summary>
        public void CoalescingPreservesTheDamageABlockTakes()
        {
/// <summary>Cooking operation.</summary>
            ThermalSimulation simulation = Cooking();
            simulation.StepExact(30, Worlds.Shadow());

            double total = 0d;
            IList<OverheatEvent> events = simulation.Overheats;
            for (int i = 0; i < events.Count; i++) total += events[i].Damage;

            Assert.True(total > 0d, "nothing took damage over thirty steps");

/// <summary>Cooking operation.</summary>
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

        [Fact]
/// <summary>AnEventCarriesTheHottestTemperatureTheBlockReached operation.</summary>
        public void AnEventCarriesTheHottestTemperatureTheBlockReached()
        {
/// <summary>Cooking operation.</summary>
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

                Assert.True(overheat.Temperature >= final - 1e-3f,
                    overheat.Block.Position + " filed " + overheat.Temperature.ToString("n2")
                    + " K against a final " + final.ToString("n2")
                    + " K, so the event is carrying neither the peak nor the last substep");
            }
        }

        [Fact]
/// <summary>TheListDoesNotGrowAcrossSteps operation.</summary>
        public void TheListDoesNotGrowAcrossSteps()
        {
/// <summary>Cooking operation.</summary>
            ThermalSimulation simulation = Cooking();
            simulation.StepExact(1, Worlds.Shadow());
            int first = simulation.Solver.Overheats.Count;

            Assert.True(first > 0, "no block overheated on the first step");

            simulation.StepExact(20, Worlds.Shadow());
            int later = simulation.Solver.Overheats.Count;

            Assert.True(later <= first, "the solver reported " + later + " overheat events after"
                + " twenty-one steps against " + first + " after one, so the list is accumulating"
                + " across steps rather than describing the step just taken");
        }

        [Fact]
/// <summary>TheLowestCriticalTemperatureIsNeverAboveANodesOwn operation.</summary>
        public void TheLowestCriticalTemperatureIsNeverAboveANodesOwn()
        {
            ThermalSimulation simulation = Hulls.Driven(Hulls.Uncapped(), 3000);

            Assert.True(!float.IsInfinity(simulation.Solver.LowestCriticalTemperature),
                "the bound was still infinite after a rebuild, so the prologue no longer settles"
                + " it on the rebuild tick");

            simulation.Solver.Step(simulation.Settings.StepSeconds, EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, Worlds.Flight(1f, 300f)));

            float bound = simulation.Solver.LowestCriticalTemperature;

            Assert.True(bound > 0f && !float.IsInfinity(bound),
                "after a step the hull still reports a lowest critical temperature of " + bound
                + ", so the apply pass would skip every node's damage test");

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int judged = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                float critical = nodes[i].Thermal.CriticalTemperature;
                if (critical <= 0f) continue;

                Assert.True(bound <= critical,
                    "node " + i + " (" + nodes[i].Block.Name + ") melts at " + critical
                    + " K but the grid's bound is " + bound
                    + " K, so the apply pass would skip it up to the bound");
                judged++;
            }

            Assert.True(judged > 1000, "only " + judged + " nodes carry a critical temperature");
        }
    }
}
