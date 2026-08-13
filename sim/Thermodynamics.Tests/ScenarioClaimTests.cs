using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Thermodynamics.Core;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Each scenario states a conclusion in its summary line. These check the conclusions rather
    /// than the numbers: a scenario whose headline claim can quietly invert is worse than no
    /// scenario, because it reads like evidence.
    /// </summary>
    public class ScenarioClaimTests
    {
        [Fact]
        public void RadiatorsHelpWhenTheyStandClearAndHurtWhenTheyDoNot()
        {
            // The summary reports bare, flush and clear in that order.
            string summary = Scenarios.Run("radiator").Summary;

            float bare = ExtractCelsius(summary, 0);
            float flush = ExtractCelsius(summary, 1);
            float clear = ExtractCelsius(summary, 2);

            Assert.True(clear < bare, "panels with clearance should beat a bare hull");
            Assert.True(flush > bare, "panels bolted flat cover as much area as they add");
        }

        [Fact]
        public void OpeningTheDoorLetsASealedRoomRadiate()
        {
            ScenarioResult result = Scenarios.Run("airlock");
            ThermalSimulation simulation = result.Runner.Simulation;

            ThermalNode reactor = simulation.Solver.GetNodeAt(new VRageMath.Vector3I(2, 1, 2));

            Assert.True(reactor.TotalExposedFaces > 0, "the room is open to space now");

            // Hotter while sealed than after opening: the samples run sealed then open.
            IList<Sample> samples = result.Runner.Samples;
            float sealedPeak = 0f;
            for (int i = 0; i < samples.Count / 2; i++)
            {
                if (samples[i].Tracked["interior"] > sealedPeak) sealedPeak = samples[i].Tracked["interior"];
            }

            Assert.True(reactor.Temperature < sealedPeak, "opening the door has to cool the room");
        }

        [Fact]
        public void LosingThePumpBreaksTheLoopAndHeatsTheReactor()
        {
            ScenarioResult result = Scenarios.Run("coolant-failure");
            ThermalSimulation simulation = result.Runner.Simulation;

            Assert.Empty(simulation.Solver.Loops);

            IList<Sample> samples = result.Runner.Samples;
            float cooled = samples[samples.Count / 2 - 1].Tracked["reactor"];
            float uncooled = samples[samples.Count - 1].Tracked["reactor"];

            Assert.True(uncooled > cooled,
                "the reactor should climb once circulation stops: " + cooled + " then " + uncooled);
        }

        [Fact]
        public void AnUnfinishedBlockSwingsFurtherThanAFinishedOne()
        {
            ScenarioResult result = Scenarios.Run("welding");

            IList<Sample> samples = result.Runner.Samples;

            // The first five samples are one second apart at a tenth of the mass; the rest are
            // after welding. The early rise per second is what has to be larger.
            float earlyRise = samples[4].Tracked["skeleton"] - samples[0].Tracked["skeleton"];
            float lateRise = samples[samples.Count - 1].Tracked["skeleton"]
                           - samples[samples.Count - 2].Tracked["skeleton"];

            Assert.True(earlyRise > lateRise,
                "a tenth of the thermal mass must respond faster: " + earlyRise + " vs " + lateRise);
        }

        [Fact]
        public void AStifferStepNeedsMoreSubstepsAndStillLandsInTheSamePlace()
        {
            ScenarioResult result = Scenarios.Run("stiff");

            // The summary carries all three frequencies; the run kept is the finest one, which
            // should need the fewest substeps.
            Assert.Contains("f=1", result.Summary);
            Assert.Contains("f=16", result.Summary);

            IList<Sample> samples = result.Runner.Samples;

            // Sample zero is taken before anything has stepped, so it reports no substeps.
            for (int i = 1; i < samples.Count; i++)
            {
                Assert.True(samples[i].Substeps >= 1, "sample " + i + " ran no substeps");
                Assert.False(float.IsNaN(samples[i].HottestTemperature));
            }
        }

        [Fact]
        public void TheUnitsScenarioShowsTheClockAndNotTheDestination()
        {
            ScenarioResult result = Scenarios.Run("units");

            float physical = ExtractCelsius(result.Summary, 0);
            float shipped = ExtractCelsius(result.Summary, 2);

            // Same block, same hour, three clocks: the accelerated one is much further along.
            Assert.True(shipped < physical,
                "a faster thermal clock must have cooled further: " + shipped + " vs " + physical);
        }

        /// <summary>Pulls the n-th "&lt;number&gt; C" out of a summary line.</summary>
        private static float ExtractCelsius(string summary, int index)
        {
            MatchCollection matches = Regex.Matches(summary, @"(-?[\d,]+(?:\.\d+)?) C");

            Assert.True(matches.Count > index,
                "expected at least " + (index + 1) + " temperatures in: " + summary);

            return float.Parse(
                matches[index].Groups[1].Value.Replace(",", ""),
                CultureInfo.InvariantCulture);
        }
    }
}
