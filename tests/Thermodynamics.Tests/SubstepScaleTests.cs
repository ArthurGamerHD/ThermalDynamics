using Thermodynamics.Core;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    public class SubstepScaleTests
    {
/// <summary>Hull operation.</summary>
        private static ThermalSimulation Hull(int frequency, int cap = 0, int blocks = 4000)
        {
            ThermalSettings settings = new ThermalSettings { Frequency = frequency };
            settings.MaxSubstepsPerBlock = cap;

            settings.MaxSubsteps = 4096;
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", blocks));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.RebuildAll();
            return simulation;
        }

/// <summary>Demand operation.</summary>
        private static float Demand(ThermalSimulation simulation)
        {
            return simulation.Solver.RequiredSubsteps(simulation.Settings.StepSeconds);
        }

        [Theory]
        [InlineData(1, 2)]
        [InlineData(2, 4)]
        [InlineData(4, 8)]
        [InlineData(8, 16)]
/// <summary>DemandIsProportionalToTheStepLength operation.</summary>
        public void DemandIsProportionalToTheStepLength(int slow, int fast)
        {
/// <summary>Demand operation.</summary>
            float slower = Demand(Hull(slow));
/// <summary>Demand operation.</summary>
            float faster = Demand(Hull(fast));

            Assert.True(slower > 0f, "the census hull asked for no substeps at Frequency " + slow
                + ", so there is no proportionality to test");

            float ratio = slower / faster;
            Assert.True(ratio > 1.98f && ratio < 2.02f,
                "Frequency " + slow + " asked for " + slower.ToString("n3")
                + " substeps and Frequency " + fast + " for " + faster.ToString("n3")
                + " — a ratio of " + ratio.ToString("n4") + " where halving the step should halve"
                + " the demand exactly. A substep count is no longer proportional to the step it"
                + " is counted against, which makes every cap table in the docs unreadable.");
        }

        [Theory]
        [InlineData(2, 4)]
        [InlineData(1, 2)]
/// <summary>ACapMeansTheSameFloorAtTwiceTheRateAndTwiceTheCap operation.</summary>
        public void ACapMeansTheSameFloorAtTwiceTheRateAndTwiceTheCap(int fastCap, int slowCap)
        {
/// <summary>Hull operation.</summary>
            ThermalSimulation fast = Hull(8, fastCap);
/// <summary>Hull operation.</summary>
            ThermalSimulation slow = Hull(4, slowCap);

            Demand(fast);
            Demand(slow);

            Assert.Equal(fast.Solver.FlooredNodes, slow.Solver.FlooredNodes);

            Assert.True(fast.Solver.FlooredNodes > 0,
                "cap " + fastCap + " at Frequency 8 raised no blocks at all, so the two sides agree"
                + " on nothing rather than on a floor");
        }

        [Fact]
/// <summary>ACapAboveWhatTheHullAsksForDoesNothingAtTheShippedRate operation.</summary>
        public void ACapAboveWhatTheHullAsksForDoesNothingAtTheShippedRate()
        {
/// <summary>Hull operation.</summary>
            ThermalSimulation uncapped = Hull(8);
/// <summary>Demand operation.</summary>
            float demand = Demand(uncapped);

            Assert.InRange(demand, 2f, 4f);

/// <summary>Hull operation.</summary>
            ThermalSimulation capped = Hull(8, 4);

            Assert.Equal(demand, Demand(capped), 3);
            Assert.Equal(0, capped.Solver.FlooredNodes);
        }
    }
}
