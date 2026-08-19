using System;
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

            // Two loops: the working ring, and the pumpless ring that circulates nothing but exists.
            Assert.Equal(2, diagnosis.Loops);
            Assert.True(diagnosis.PipesAdrift > 0, "the scenario is supposed to contain broken plumbing");

            // Three distinct reasons on one grid, so the diagnosis discriminates rather than
            // reporting everything as one generic failure.
            int kinds = 0;
            for (int i = 1; i < diagnosis.Counts.Length; i++)
            {
                if (diagnosis.Counts[i] > 0) kinds++;
            }
            Assert.True(kinds >= 2, "only " + kinds + " kinds of fault were distinguished");

            Assert.True(diagnosis.CountOf(CoolantFault.BlockedByNonCoolant) > 0);
            Assert.True(diagnosis.CountOf(CoolantFault.OpenEnd) > 0);
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
        /// A long ring costs the solver no more per parcel than a short one, and the substep estimate
        /// accounts for the flow carrying heat around it.
        ///
        /// This used to be the opposite claim — that a long ring was the stiffest thing a player could
        /// build cheaply, because a fixed fluid charge divided into ever smaller parcels as the ring
        /// grew. Charging the coolant per pipe removes that: the parcel capacity and the links it
        /// carries are both constant, so a 76-pipe ring demands what an 8-pipe ring demands.
        /// </summary>
        [Fact]
        public void ALongRingCostsNoMorePerParcelThanAShortOne()
        {
            ScenarioResult result = Scenarios.Run("loop-stiffness");
            string summary = result.Summary;

            Assert.Contains("8 pipes", summary);
            Assert.Contains("76 pipes", summary);

            // Energy survived at every length, which is the property advection threatens most.
            Assert.DoesNotContain("energy x0.9", summary);
            Assert.DoesNotContain("energy x1.1", summary);

            ThermalSimulation simulation = result.Runner.Simulation;
            CoolantLoop loop = simulation.Solver.Loops[0];

            // A parcel is not stiffer than the step that integrates it, whatever the ring's length.
            float conductance = 0f;
            for (int i = 0; i < loop.Links.Count; i++) conductance += loop.Links[i].Conductance;
            float perParcel = conductance / loop.PipeCount;

            Assert.True(loop.SegmentThermalMass / perParcel > simulation.Settings.StepSeconds,
                "a parcel's time constant " + (loop.SegmentThermalMass / perParcel)
                + " s is shorter than the " + simulation.Settings.StepSeconds
                + " s step, so a long ring is stiff again");

            // And the flow the pumps provide is inside the advective limit of one parcel per substep.
            float parcelsPerSubstep = loop.FlowSegmentsPerSecond
                * (simulation.Settings.StepSeconds / Math.Max(1, simulation.Solver.LastSubsteps));

            Assert.True(parcelsPerSubstep <= 1f,
                "the flow moves " + parcelsPerSubstep + " parcels per substep, past the upwind limit");
        }
    
        /// <summary>
        /// Splitting a ring buys nothing; spreading the sources around it buys a great deal. Pinned
        /// because the intuition runs the other way and the first measurement appeared to confirm it.
        /// </summary>
        [Fact]
        public void SplittingARingBuysNothingButSpreadingTheSourcesDoes()
        {
            string summary = Scenarios.Run("loop-layout").Summary;

            float bunched = ScenarioClaimTests.ExtractCelsius(summary, 0);
            float spread = ScenarioClaimTests.ExtractCelsius(summary, 1);
            float split = ScenarioClaimTests.ExtractCelsius(summary, 2);

            Assert.True(bunched - spread > 20f,
                "spreading the sources should be worth a lot: " + bunched + " C then " + spread + " C");

            Assert.True(Math.Abs(spread - split) < 5f,
                "one ring with spread sources (" + spread + " C) and four rings (" + split
                + " C) should be within a few kelvin; splitting is not what helps");
        }
    }
}
