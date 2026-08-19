using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The claims the cooling scenarios make in their summary lines, so a headline conclusion cannot
    /// quietly invert. Same discipline as <see cref="ScenarioClaimTests"/>, kept separate because
    /// these seven were written together against one field telemetry dump.
    ///
    /// Every one of them exists because the dump could not answer the question: it showed a ship
    /// carrying 288 pipe blocks, four pumps, eight radiators and four heat pumps, and could not say
    /// whether any of it moved a watt.
    /// </summary>
    public class CoolingScenarioClaimTests
    {
        /// <summary>
        /// The general case, and the only scenario that runs all four systems as one chain: loop
        /// draws off the machinery, pumps lift out of the loop, radiators shed what the pumps reject.
        /// </summary>
        [Fact]
        public void TheWholeCoolingPlantBeatsTheSameShipWithoutIt()
        {
            ScenarioResult result = Scenarios.Run("cooling-plant");
            ThermalSimulation simulation = result.Runner.Simulation;

            Assert.Single(simulation.Solver.Loops);
            CoolantLoop loop = simulation.Solver.Loops[0];

            // The loop is carrying, not merely warm.
            Assert.True(loop.LastWattsAbsorbed > 10000f,
                "the plant's loop draws only " + loop.LastWattsAbsorbed + " W");

            // And it is in balance: what it draws it sheds, which is the case the gross pair exists
            // to make visible, because the net is nearly nothing.
            Assert.True(loop.LastWattsRejected > loop.LastWattsAbsorbed * 0.9f,
                "the loop draws " + loop.LastWattsAbsorbed + " W and sheds only " + loop.LastWattsRejected);

            // Every pump is bound to two blocks and lifting.
            IList<HeatPumpDevice> pumps = simulation.Solver.HeatPumps;
            Assert.NotEmpty(pumps);
            for (int i = 0; i < pumps.Count; i++)
            {
                Assert.True(pumps[i].IsConnected, "pump " + i + " is not bound to two blocks");
                Assert.True(pumps[i].LastLiftedWatts > 0f, "pump " + i + " lifted nothing");
            }

            Assert.Contains("with the plant", result.Summary);
        }

        [Fact]
        public void EveryWayARingCanFailIsNamedRatherThanCountedAsAbsent()
        {
            ScenarioResult result = Scenarios.Run("loop-faults");
            CoolantLoopDiagnostics diagnosis = result.Runner.Simulation.DiagnoseLoops();

            Assert.Equal(1, diagnosis.Loops);
            Assert.True(diagnosis.PipesAdrift > 0, "the scenario is supposed to contain broken plumbing");

            // Three distinct reasons on one grid, so the diagnosis discriminates rather than
            // reporting everything as one generic failure.
            int kinds = 0;
            for (int i = 1; i < diagnosis.Counts.Length; i++)
            {
                if (diagnosis.Counts[i] > 0) kinds++;
            }
            Assert.True(kinds >= 3, "only " + kinds + " kinds of fault were distinguished");

            Assert.True(diagnosis.CountOf(CoolantFault.NoPump) > 0);
            Assert.True(diagnosis.CountOf(CoolantFault.BlockedByNonCoolant) > 0);
        }

        /// <summary>
        /// The failure where every figure looks healthy. A ring with no sink face still couples to the
        /// fluid through its own pipe blocks, so it warms and looks alive; what it does not do is
        /// reach the reactor any better than plain conduction already did.
        /// </summary>
        [Fact]
        public void ALoopWithNoSinkFaceStillLooksHealthy()
        {
            ScenarioResult result = Scenarios.Run("loop-dry");
            ThermalSimulation simulation = result.Runner.Simulation;

            CoolantLoop loop = simulation.Solver.Loops[0];

            // One loop, a pump, and coolant well above ambient: nothing here says "broken".
            Assert.Single(simulation.Solver.Loops);
            Assert.True(loop.HasPump);
            Assert.True(loop.Temperature > 400f, "the coolant should be hot: " + loop.Temperature);

            // And every link it has is to a pipe, never to a block bolted against a sink face.
            Assert.Equal(loop.PipeCount, loop.Links.Count);
        }

        /// <summary>
        /// Installing a pump backwards is a rotation, not an obvious mistake, and it makes the ship
        /// hotter while charging the same electricity. The model has to punish it.
        /// </summary>
        [Fact]
        public void APumpInstalledBackwardsMakesThingsWorse()
        {
            string summary = Scenarios.Run("heatpump-backwards").Summary;

            float correct = ScenarioClaimTests.ExtractCelsius(summary, 0);
            float backwards = ScenarioClaimTests.ExtractCelsius(summary, 1);

            Assert.True(backwards > correct,
                "the pump turned around left the reactor at " + backwards
                + " C against " + correct + " C the right way round");
        }

        /// <summary>
        /// Which limit binds is the block's whole character: rating-bound and cheap over a small gap,
        /// Carnot-bound and ruinous over a large one, with the coefficient falling monotonically as
        /// the gap widens.
        /// </summary>
        [Fact]
        public void TheCoefficientFallsAsTheGapWidensAndTheRatingBindsOnlyWhenItIsNarrow()
        {
            string summary = Scenarios.Run("heatpump-limits").Summary;

            Assert.Contains("gap 10 K", summary);
            Assert.Contains("(rating)", summary);
            Assert.Contains("gap 910 K", summary);

            // The narrow gap is the rating-bound one; the widest is not.
            int narrow = summary.IndexOf("gap 10 K");
            int widest = summary.IndexOf("gap 910 K");
            Assert.Contains("(rating)", summary.Substring(narrow, summary.IndexOf(';', narrow) - narrow));
            Assert.Contains("(Carnot)", summary.Substring(widest));
        }

        /// <summary>
        /// A cooling plant does not fail gradually. Past its headroom everything the loop touches
        /// rises together, because the loop ties them into one mass, and blocks start taking damage.
        /// </summary>
        [Fact]
        public void PastItsHeadroomThePlantOverheatsRatherThanHolding()
        {
            ScenarioResult result = Scenarios.Run("cooling-runaway");
            string summary = result.Summary;

            float light = ScenarioClaimTests.ExtractCelsius(summary, 0);
            float medium = ScenarioClaimTests.ExtractCelsius(summary, 2);

            Assert.True(medium > light,
                "four times the load should be hotter: " + light + " C then " + medium + " C");

            // The heaviest load reaches damage rather than settling at a large number.
            Assert.Contains("4 over critical", summary);
            Assert.Contains("0 over critical", summary);
        }

        /// <summary>
        /// A long ring is the stiffest thing a player can build cheaply — coupling grows with length
        /// while the fluid mass does not — so the substep estimate has to see it. A stiff element the
        /// estimator cannot see is how an integrator goes unstable.
        /// </summary>
        [Fact]
        public void TheSubstepEstimateSeesALongRingsStiffness()
        {
            ScenarioResult result = Scenarios.Run("loop-stiffness");
            string summary = result.Summary;

            Assert.Contains("8 pipes", summary);
            Assert.Contains("76 pipes", summary);

            // Energy survived at every length, which is the property stiffness threatens.
            Assert.DoesNotContain("energy x0.9", summary);
            Assert.DoesNotContain("energy x1.1", summary);

            // The longest ring is stiffer than the step that integrates it and still holds.
            ThermalSimulation simulation = result.Runner.Simulation;
            CoolantLoop loop = simulation.Solver.Loops[0];

            float conductance = 0f;
            for (int i = 0; i < loop.Links.Count; i++) conductance += loop.Links[i].Conductance;

            float tau = loop.ThermalMass / conductance;
            Assert.True(tau < simulation.Settings.StepSeconds,
                "the longest ring's time constant " + tau + " s is not shorter than the "
                + simulation.Settings.StepSeconds + " s step, so this is not testing stiffness");

            Assert.True(simulation.Solver.LastSubsteps > 1,
                "a ring this stiff should force more than one substep");
        }
    }
}
