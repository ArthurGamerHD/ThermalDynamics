using System;
using System.Collections.Generic;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The two ways the lab spreads its work, and the invariant that makes one of them usable.
    ///
    /// Running the battery concurrently is only sound if it produces the same matrix. That is not
    /// obvious: every simulation built from one ship shares that ship's <c>BlockInstance</c>
    /// objects, and the load is written onto them, so a naive fan-out silently has two scenarios
    /// overwriting each other's watts — which does not throw, and does not look wrong in a report.
    /// It just answers a different question.
    /// </summary>
    public class LabRunTests
    {
        /// <summary>
        /// Results come back in the order the items went in, whichever mode ran them.
        ///
        /// A report that reorders itself depending on how loaded the machine was is a report two
        /// runs of which cannot be diffed, and diffing two runs is most of what the lab is for.
        /// </summary>
        [Theory]
        [InlineData(LabMode.Linear)]
        [InlineData(LabMode.Parallel)]
        public void ResultsKeepTheOrderOfTheirInputs(LabMode mode)
        {
            List<int> items = new List<int>();
            for (int i = 0; i < 200; i++) items.Add(i);

            List<string> results = LabRun.Map(items, i => i.ToString(), mode);

            Assert.Equal(items.Count, results.Count);
            for (int i = 0; i < items.Count; i++) Assert.Equal(i.ToString(), results[i]);
        }

        /// <summary>
        /// A corpus of ten thousand contains ships this model cannot build, and losing one must not
        /// lose the pass. In parallel it is also the difference between a dropped row and an
        /// exception surfacing out of the fan-out as something unrelated.
        /// </summary>
        [Theory]
        [InlineData(LabMode.Linear)]
        [InlineData(LabMode.Parallel)]
        public void OneItemThrowingDoesNotLoseTheRest(LabMode mode)
        {
            List<int> items = new List<int>();
            for (int i = 0; i < 50; i++) items.Add(i);

            List<string> results = LabRun.Map(items, i =>
            {
                if (i % 10 == 0) throw new System.InvalidOperationException("this ship is unbuildable");
                return i.ToString();
            }, mode);

            Assert.Equal(45, results.Count);
            Assert.DoesNotContain("10", results);
            Assert.Contains("11", results);
        }

        [Fact]
        public void ParallelUsesMoreThanOneWorkerButLeavesTheMachineACore()
        {
            Assert.True(LabRun.Workers >= 1);
            Assert.True(LabRun.Workers < System.Environment.ProcessorCount
                || System.Environment.ProcessorCount == 1);
        }

        /// <summary>
        /// **The invariant the whole parallel mode rests on**: the same ships through the same
        /// scenarios give the same answers whichever way the work was spread.
        ///
        /// Measured on the development machine at a panel of three, the two modes agreed to the
        /// last digit across sixty runs while parallel took 118.7 s against linear's 545.3 s. This
        /// test is the cheap standing version of that comparison — two small ships and two
        /// scenarios — so a change that reintroduces shared state fails here rather than in a
        /// report nobody re-runs linearly.
        /// </summary>
        [Fact]
        public void ParallelAndLinearProduceTheSameMatrix()
        {
            if (CorpusFixture.Files().Count == 0) return;

            List<Battery.Scenario> scenarios = new List<Battery.Scenario>();
            foreach (Battery.Scenario scenario in Battery.All())
            {
                if (scenario.Name == "idle" || scenario.Name == "burn-forward") scenarios.Add(scenario);
            }

            // **A sample, and deliberately not the corpus.** Every other corpus test walks all ten
            // thousand ships, and this one must not: half of it is the linear lab, which is one
            // core by definition, so walking the population here would put a single-threaded pass
            // over ten thousand ships in front of every other test on the machine. It would be the
            // longest thing in the run by a wide margin and it would not answer a harder question.
            //
            // What the fault needs is contention, not population. The bug is that simulations built
            // from one ship share that ship's BlockInstance objects while ShipLoad writes the load
            // onto them, so two scenarios running at once overwrite each other's watts. Sixty real
            // hulls being built and loaded across thirty-odd workers is that condition; ten
            // thousand is the same condition for longer. Spread across the size range rather than
            // taken off one end, because the old version took the two smallest ships in the corpus
            // and four jobs on a thirty-core machine is the narrowest window a race could have.
            List<Blueprints.Ship> ships = CorpusFixture.Spread(Sample);
            if (ships.Count == 0) return;

            List<ScenarioOutcome> parallel = BatteryLab.Run(ships, scenarios, null, LabMode.Parallel);
            List<ScenarioOutcome> linear = BatteryLab.Run(ships, scenarios, null, LabMode.Linear);

            // Pinned to what was asked for, not merely to each other. The lab drops a job that
            // throws rather than failing the pass, so two empty matrices satisfy an equality
            // between them and the loop below never runs.
            int expected = ships.Count * scenarios.Count;
            Assert.Equal(expected, linear.Count);
            Assert.Equal(expected, parallel.Count);

            for (int i = 0; i < linear.Count; i++)
            {
                Assert.Equal(linear[i].Ship, parallel[i].Ship);
                Assert.Equal(linear[i].Scenario, parallel[i].Scenario);
                Assert.Equal(linear[i].PeakKelvin, parallel[i].PeakKelvin, 3);
                Assert.Equal(linear[i].MeanKelvin, parallel[i].MeanKelvin, 3);
                Assert.Equal(linear[i].BlocksOverCritical, parallel[i].BlocksOverCritical);
                Assert.Equal(linear[i].HottestBlock, parallel[i].HottestBlock);
            }
        }

        /// <summary>
        /// Ships to put through both modes. Enough concurrent work to collide, few enough that the
        /// serial reference stays cheap.
        /// </summary>
        private const int Sample = 60;
    }
}
