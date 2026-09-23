using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    public class PaceEquivalenceTests
    {
        private readonly ITestOutputHelper output;


        public PaceEquivalenceTests(ITestOutputHelper output)
        {
            this.output = output;
        }


        private static ThermalSimulation Build(float speed, float heatTimeScale, int frequency)
        {
            GridBuilder builder = GridBuilder.Large();

            int index = 0;
            foreach (Vector3I cell in GridShapes.Ship(fuselageLength: 20, fuselageWidth: 7, bulkheadSpacing: 6))
            {
                builder.Place((index++ % 8) == 0 ? Catalog.Grating() : Catalog.HeavyArmor(), cell);
            }


            ThermalSettings settings = new ThermalSettings();
            settings.SimulationSpeed = speed;
            settings.HeatTimeScale = heatTimeScale;
            settings.Frequency = frequency;

            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            while (simulation.HasPendingWork) simulation.Update(1f / 60f, Worlds.Shadow());

            for (int i = 0; i < simulation.Solver.Nodes.Count; i++)
            {
                simulation.Solver.Nodes[i].Temperature = 800f;
            }

            return simulation;
        }


        private static float RunForRealSeconds(ThermalSimulation simulation, float realSeconds)
        {
            EnvironmentSample sample = Worlds.Shadow();
            int frames = (int)(realSeconds * 60f);

            for (int f = 0; f < frames; f++)
            {
                simulation.Update(1f / 60f, sample);
            }

            float total = 0f;
            for (int i = 0; i < simulation.Solver.Nodes.Count; i++)
            {
                total += simulation.Solver.Nodes[i].Temperature;
            }
            return total / simulation.Solver.Nodes.Count;
        }

        [Theory]
        [InlineData(0.5f, 450f)]
        [InlineData(0.25f, 900f)]
        [InlineData(2f, 112.5f)]

        public void ConstantSpeedTimesHeatScaleKeepsTheSameThermalPace(float speed, float scale)
        {
            const float seconds = 6f;


            float reference = RunForRealSeconds(Build(1f, 225f, 4), seconds);

            float traded = RunForRealSeconds(Build(speed, scale, 4), seconds);

            float referenceDrop = 800f - reference;
            float tradedDrop = 800f - traded;
            float difference = Math.Abs(tradedDrop - referenceDrop) / Math.Max(1f, referenceDrop);

            output.WriteLine("speed " + speed + " x scale " + scale + ": cooled "
                + tradedDrop.ToString("n2") + " K against " + referenceDrop.ToString("n2")
                + " K at the default, " + (100f * difference).ToString("n2") + " % apart.");

            Assert.True(difference < 0.05f,
                "holding SimulationSpeed x HeatTimeScale constant should keep the thermal pace; "
                + "cooled " + tradedDrop + " K against " + referenceDrop + " K");
        }

        [Fact]

        public void TradingSpeedForTransferDoesNotBuyBackTheSubsteps()
        {
            const float seconds = 6f;


            ThermalSimulation reference = Build(1f, 225f, 4);

            ThermalSimulation traded = Build(0.25f, 900f, 4);

            reference.Work.Reset();
            traded.Work.Reset();

            RunForRealSeconds(reference, seconds);
            RunForRealSeconds(traded, seconds);

            long referenceSubsteps = reference.Work.SolverSubsteps;
            long tradedSubsteps = traded.Work.SolverSubsteps;

            output.WriteLine("default: " + reference.Work.SolverSteps + " steps, "
                + referenceSubsteps + " substeps. Quarter speed and quadruple transfer: "
                + traded.Work.SolverSteps + " steps, " + tradedSubsteps + " substeps.");

            Assert.True(tradedSubsteps > referenceSubsteps * 0.5,
                "quartering the speed and quadrupling the transfer cut substeps from "
                + referenceSubsteps + " to " + tradedSubsteps + "; if that is a real saving this "
                + "test is out of date and the tuning advice in configuration.md should say so");
        }

        [Fact]

        public void LoweringFrequencyReachesTheSamePlaceWithoutTouchingTheClock()
        {
            const float seconds = 6f;


            float fast = RunForRealSeconds(Build(1f, 225f, 4), seconds);

            float slow = RunForRealSeconds(Build(1f, 225f, 1), seconds);

            float difference = Math.Abs(slow - fast) / Math.Max(1f, 800f - fast);

            output.WriteLine("Frequency 4 cooled to " + fast.ToString("n2")
                + " K, Frequency 1 to " + slow.ToString("n2") + " K, "
                + (100f * difference).ToString("n2") + " % apart.");

            Assert.True(difference < 0.05f,
                "Frequency should change how the integration is divided, not where it ends up: "
                + fast + " K against " + slow + " K");
        }
    }
}
