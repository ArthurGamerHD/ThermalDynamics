using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class ThresholdTests
    {
/// <summary>OneBlock operation.</summary>
        private static ThermalSimulation OneBlock(float initial, ThermalSettings settings = null)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings effective = settings ?? new ThermalSettings();
            effective.EnableEnvironment = false;
            effective.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.Reactor(), Vector3I.Zero);
            return builder.BuildSimulation(effective, initial);
        }

        [Fact]
/// <summary>NothingRegisteredReportsNothing operation.</summary>
        public void NothingRegisteredReportsNothing()
        {
/// <summary>OneBlock operation.</summary>
            ThermalSimulation simulation = OneBlock(300f);
            simulation.Solver.Nodes[0].Block.PowerProducedWatts = 15e6f;
            simulation.Solver.RefreshHeatGeneration();

            simulation.StepExact(20, Worlds.Shadow());

            Assert.Empty(simulation.Crossings);
        }

        [Fact]
/// <summary>ARisingBlockReportsOnceAsItPasses operation.</summary>
        public void ARisingBlockReportsOnceAsItPasses()
        {
/// <summary>OneBlock operation.</summary>
            ThermalSimulation simulation = OneBlock(300f);
            int id = simulation.Thresholds.Add(400f, ThresholdDirection.Rising);

            simulation.Solver.Nodes[0].Block.PowerProducedWatts = 15e6f;
            simulation.Solver.RefreshHeatGeneration();

            int seen = 0;
            for (int i = 0; i < 200; i++)
            {
                simulation.StepExact(1, Worlds.Shadow());
                for (int c = 0; c < simulation.Crossings.Count; c++)
                {
                    Assert.Equal(id, simulation.Crossings[c].ThresholdId);
                    Assert.True(simulation.Crossings[c].Rising);
                    Assert.Equal(400f, simulation.Crossings[c].Threshold, 3);
                    seen++;
                }
            }

            Assert.True(simulation.Solver.Nodes[0].Temperature > 400f);
            Assert.Equal(1, seen);
        }

        [Fact]
/// <summary>ARisingThresholdIgnoresABlockCoolingThroughIt operation.</summary>
        public void ARisingThresholdIgnoresABlockCoolingThroughIt()
        {
/// <summary>OneBlock operation.</summary>
            ThermalSimulation simulation = OneBlock(500f);
            simulation.Thresholds.Add(400f, ThresholdDirection.Rising);

            simulation.Settings.EnableEnvironment = true;
            simulation.Settings.Derive();

            int seen = 0;
            for (int i = 0; i < LabClock.Steps(400); i++)
            {
                simulation.StepExact(1, Worlds.Shadow());
                seen += simulation.Crossings.Count;
            }

            Assert.True(simulation.Solver.Nodes[0].Temperature < 400f);
            Assert.Equal(0, seen);
        }

        [Fact]
/// <summary>AFallingThresholdCatchesIt operation.</summary>
        public void AFallingThresholdCatchesIt()
        {
/// <summary>OneBlock operation.</summary>
            ThermalSimulation simulation = OneBlock(500f);
            simulation.Thresholds.Add(400f, ThresholdDirection.Falling);
            simulation.Settings.EnableEnvironment = true;
            simulation.Settings.Derive();

            int seen = 0;
            bool rising = true;
            for (int i = 0; i < LabClock.Steps(400); i++)
            {
                simulation.StepExact(1, Worlds.Shadow());
                for (int c = 0; c < simulation.Crossings.Count; c++)
                {
                    rising = simulation.Crossings[c].Rising;
                    seen++;
                }
            }

            Assert.Equal(1, seen);
            Assert.False(rising);
        }

        [Fact]
/// <summary>BothCatchesEitherDirection operation.</summary>
        public void BothCatchesEitherDirection()
        {
/// <summary>ThermalThresholds operation.</summary>
            ThermalThresholds thresholds = new ThermalThresholds();
            int id = thresholds.Add(400f, ThresholdDirection.Both);

/// <summary>List operation.</summary>
            List<ThresholdCrossing> results = new List<ThresholdCrossing>();
            thresholds.Collect(null, 390f, 410f, results);
            thresholds.Collect(null, 410f, 390f, results);

            Assert.Equal(2, results.Count);
            Assert.True(results[0].Rising);
            Assert.False(results[1].Rising);
            Assert.Equal(id, results[0].ThresholdId);
        }

        [Fact]
/// <summary>SittingOnAThresholdDoesNotReportTwice operation.</summary>
        public void SittingOnAThresholdDoesNotReportTwice()
        {
/// <summary>ThermalThresholds operation.</summary>
            ThermalThresholds thresholds = new ThermalThresholds();
            thresholds.Add(400f, ThresholdDirection.Both);

/// <summary>List operation.</summary>
            List<ThresholdCrossing> results = new List<ThresholdCrossing>();

            thresholds.Collect(null, 399f, 400f, results);
            thresholds.Collect(null, 400f, 401f, results);

            Assert.Single(results);
        }

        [Fact]
/// <summary>CrossingsSurviveAMultiStepUpdate operation.</summary>
        public void CrossingsSurviveAMultiStepUpdate()
        {
/// <summary>OneBlock operation.</summary>
            ThermalSimulation simulation = OneBlock(300f);
            simulation.Thresholds.Add(310f, ThresholdDirection.Rising);
            simulation.Thresholds.Add(320f, ThresholdDirection.Rising);

            simulation.Solver.Nodes[0].Block.PowerProducedWatts = 15e6f;
            simulation.Solver.RefreshHeatGeneration();

            simulation.StepExact(400, Worlds.Shadow());

            Assert.Equal(2, simulation.Crossings.Count);
        }

        [Fact]
/// <summary>RemovingAThresholdStopsIt operation.</summary>
        public void RemovingAThresholdStopsIt()
        {
/// <summary>OneBlock operation.</summary>
            ThermalSimulation simulation = OneBlock(300f);
            int id = simulation.Thresholds.Add(310f, ThresholdDirection.Rising);
            Assert.True(simulation.Thresholds.Remove(id));

            simulation.Solver.Nodes[0].Block.PowerProducedWatts = 15e6f;
            simulation.Solver.RefreshHeatGeneration();
            simulation.StepExact(400, Worlds.Shadow());

            Assert.Empty(simulation.Crossings);
        }

        [Fact]
/// <summary>CrossingCarriesTheBlockThatCrossed operation.</summary>
        public void CrossingCarriesTheBlockThatCrossed()
        {
/// <summary>OneBlock operation.</summary>
            ThermalSimulation simulation = OneBlock(300f);
            simulation.Thresholds.Add(310f, ThresholdDirection.Rising);

            BlockInstance block = simulation.Solver.Nodes[0].Block;
            block.PowerProducedWatts = 15e6f;
            simulation.Solver.RefreshHeatGeneration();

            simulation.StepExact(400, Worlds.Shadow());

            Assert.Single(simulation.Crossings);
            Assert.Same(block, simulation.Crossings[0].Block);
        }
    }
}
