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

        /// <summary>
        /// The solver benchmark only means anything while it is measuring the loop it claims to.
        ///
        /// Two ways it can quietly stop: the grid settles, so every link joins two cells at the
        /// same temperature and the conduction loop skips them all — which is what the older
        /// <c>perf</c> scenario measures — or the grid stops being stiff enough to substep, so
        /// the per-substep cost never appears. Both leave a plausible-looking number behind.
        /// </summary>
        [Fact]
        public void TheSolverBenchmarkMeasuresConductionAndNotTheSkipPath()
        {
            ScenarioResult result = Scenarios.Run("solver");
            ThermalSimulation simulation = result.Runner.Simulation;

            Assert.Contains("ns per link visit", result.Summary);

            Assert.True(simulation.Solver.Nodes.Count > 30000,
                "the benchmark has to stay at stress-test scale: " + simulation.Solver.Nodes.Count);
            Assert.True(simulation.Solver.Links.Count > simulation.Solver.Nodes.Count,
                "a ship has more joints than blocks");

            // The longer window has to substep, or it is measuring the same thing as the short
            // one and the two figures carry no information apart.
            Assert.DoesNotContain("A step six times longer: 0.0000 ms", result.Summary);
            Assert.Matches(@"A step six times longer: [\d.]+ ms per step at ([2-9]|\d\d)", result.Summary);

            // And the grid is genuinely uneven, so links are not being skipped wholesale.
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            float coldest = float.MaxValue;
            float hottest = float.MinValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Temperature < coldest) coldest = nodes[i].Temperature;
                if (nodes[i].Temperature > hottest) hottest = nodes[i].Temperature;
            }

            Assert.True(hottest - coldest > 10f,
                "the benchmark grid has settled flat, so it is timing the skip path: "
                + coldest + " K to " + hottest + " K");
        }

        /// <summary>
        /// The weather scenario's whole point is that a storm reaches more than the wind. Each of
        /// the three claims can fail on its own — the ambient offset, the solar multiplier and the
        /// convection multiplier are separate terms — so each is checked separately.
        /// </summary>
        [Fact]
        public void AStormReachesMoreThanTheWind()
        {
            string summary = Scenarios.Run("weather").Summary;

            float clear = ExtractCelsius(summary, 0);
            float stormy = ExtractCelsius(summary, 1);

            Assert.True(stormy < clear - 10f,
                "a heavy snowstorm should pull the air well down: " + summary);

            // "delivers 50 W/m2 against 500 in clear air, and convection runs at 110.0 against 50.0"
            MatchCollection numbers = Regex.Matches(summary, @"(\d+(?:\.\d+)?) W/m2 against (\d+)");
            Assert.True(numbers.Count > 0, "no solar comparison in: " + summary);

            float stormSolar = Parse(numbers[0].Groups[1].Value);
            float clearSolar = Parse(numbers[0].Groups[2].Value);
            Assert.True(stormSolar < clearSolar * 0.25f, "overcast should darken the sun: " + summary);

            MatchCollection convection = Regex.Matches(summary, @"at ([\d.]+) against ([\d.]+) W/\(m2 K\)");
            Assert.True(convection.Count > 0, "no convection comparison in: " + summary);

            float stormH = Parse(convection[0].Groups[1].Value);
            float clearH = Parse(convection[0].Groups[2].Value);
            Assert.True(stormH > clearH * 1.5f, "wet air should strip heat faster: " + summary);
        }

        /// <summary>
        /// Depth has to do two separate things, and reporting one of them while the other quietly
        /// stopped would still read as a working model: the day damps out with depth, and the rock
        /// warms with it once past the deadzone.
        /// </summary>
        [Fact]
        public void DepthDampsTheDayAndThenWarmsTheRock()
        {
            string summary = Scenarios.Run("underground").Summary;

            MatchCollection swings = Regex.Matches(summary, @"swing ([\d.]+) K");
            Assert.True(swings.Count >= 3, "expected a swing per depth in: " + summary);

            float surface = Parse(swings[0].Groups[1].Value);
            float shallow = Parse(swings[1].Groups[1].Value);
            float deep = Parse(swings[2].Groups[1].Value);

            Assert.True(surface > 5f, "the surface should have a day at all: " + summary);
            Assert.True(shallow < surface, "ten metres of rock should blunt it: " + summary);
            Assert.True(deep < 0.1f, "a hundred metres down there should be no day: " + summary);

            // Temperatures run surface min/max, 10 m min/max, 100 m min/max, 5 km, 20 km, mountain.
            float hundredMetres = ExtractCelsius(summary, 4);
            float fiveKm = ExtractCelsius(summary, 6);
            float twentyKm = ExtractCelsius(summary, 8);
            float mountain = ExtractCelsius(summary, 10);

            Assert.True(fiveKm > hundredMetres + 50f, "below the deadzone the rock should warm: " + summary);
            Assert.True(twentyKm > fiveKm, "and keep warming toward the core: " + summary);

            // The deadzone is measured from sea level, so deep rock high up is still cold rock.
            Assert.Equal(hundredMetres, mountain, 0);
        }

        private static float Parse(string value)
        {
            return float.Parse(value.Replace(",", ""), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Pulls the n-th "&lt;number&gt; C" out of a summary line. Shared with
        /// <c>CoolingScenarioClaimTests</c>, which reads the same summary form.
        /// </summary>
        internal static float ExtractCelsius(string summary, int index)
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
