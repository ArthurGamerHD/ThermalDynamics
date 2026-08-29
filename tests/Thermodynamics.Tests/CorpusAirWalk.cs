using System;
using System.Collections.Generic;
using System.Globalization;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// One pass over every ship in the corpus in **air**, which is the environment both halves of
    /// `G6` are decided in and the only one the survey has never run.
    ///
    /// <para>
    /// **Why it exists.** `CorpusSurvey`'s five scenarios are all vacuum. `G6`'s *demand* half read
    /// as passing for months on that dataset — corpus p99 6.02 against 64 granted — and failed the
    /// moment air was measured on 49 hulls, which is what `C19` and then `C24` came out of. Its
    /// *cost* half is in exactly the same position now: scored at p99 883,675 element visits in
    /// vacuum, and projected through the panel's own air-to-vacuum ratio it lands near four million
    /// against the four million the allowance grants (`C27`). A projection of a projection is
    /// standing where a measurement belongs, and this is the measurement.
    /// backlog.md `F11`.
    /// </para>
    ///
    /// <para>
    /// **Why a separate walk rather than three more scenarios on the survey.** Every figure quoted
    /// from the survey was taken on a run of five scenarios; adding to it makes each of those rows
    /// a different run and nothing quoted from the old one is comparable with the new (`M1`, `P6`).
    /// A walk of its own has its own resume record and its own data directory, is read by the same
    /// `verdict.py`, and can be run and re-run without touching what the survey means.
    /// </para>
    ///
    /// <para>
    /// **What it judges, and what it only records.** The population criteria are scored downstream
    /// by `verdict.py` against the markers written in balance-lab.md,
    /// which is where they belong and where they were written before any of this data existed
    /// (`E1`, `E11`). What this walk asserts is the two things only a run in air can check — that
    /// the convection path actually carried watts, and that no ship is *cheaper* in air than in
    /// vacuum — plus the accounting every walk owes its own dataset.
    /// </para>
    /// </summary>
    [Collection("alone")]
    public class CorpusAirWalk
    {
        /// <summary>
        /// The four the panel's air pass runs, so the population and the panel are one experiment
        /// at two scales rather than two experiments (`P6`).
        ///
        /// <para>
        /// `vacuum-shadow` is in the set and is not padding: it is the anchor the other three are
        /// read against, and having it *inside this walk* makes the air-to-vacuum ratio a
        /// measurement over one set of ships in one run rather than a join across two datasets
        /// taken at different times on different clocks (`M1`). It is also the cheapest of the four
        /// by a wide margin.
        /// </para>
        /// </summary>
        private static List<Battery.Scenario> Scenarios()
        {
            Dictionary<string, Battery.Scenario> byName =
                new Dictionary<string, Battery.Scenario>(StringComparer.Ordinal);
            foreach (Battery.Scenario scenario in Battery.All()) byName[scenario.Name] = scenario;

            List<Battery.Scenario> chosen = new List<Battery.Scenario>();
            foreach (string name in PairLab.AirScenarios)
            {
                Assert.True(byName.ContainsKey(name),
                    "the air walk asks for scenario '" + name + "' and the battery has no such case");
                chosen.Add(Ceiling(byName[name]));
            }

            return chosen;
        }

        /// <summary>
        /// A scenario with its clock shortened by <c>THERMAL_SCENARIO_CEILING</c>, for measuring
        /// what a shorter walk would cost in fidelity.
        ///
        /// <para>
        /// **This exists to price a saving, not to take one.** A walk of the whole corpus is hours
        /// and `vacuum-shadow` is a third of it — measured at **33.7 %** of the 2026-08-28 air
        /// walk's modelled cost — because it is the one scenario of the four that never satisfies
        /// the settle rule: a hull radiating in shadow is still moving faster than 0.25 K a minute
        /// when its 1,800 s run ends, while the other three stop at a median 120 s.
        /// </para>
        ///
        /// <para>
        /// **Shortening it is not free and the reason is where the reading is taken.**
        /// `ScenarioOutcome` is read at the *end* of a run, so a truncated `vacuum-shadow` reports a
        /// hull caught on its way down rather than one that has arrived — the median run is still
        /// 5 K from where it finishes at 1,620 s of its 1,800. So this is a knob for a paired
        /// experiment against a full-ceiling walk of the same ships on the same build, and what it
        /// costs is a measurement rather than an assumption (`M1`, `P6`, `E11`).
        /// </para>
        ///
        /// <para>
        /// Unset, or set to nothing usable, it changes nothing. A ceiling *above* a scenario's own
        /// is not applied either: this shortens walks and never lengthens them, so a value cannot
        /// quietly turn one scenario into a longer experiment than the battery defines.
        /// </para>
        /// </summary>
        private static Battery.Scenario Ceiling(Battery.Scenario scenario)
        {
            string set = Environment.GetEnvironmentVariable("THERMAL_SCENARIO_CEILING");
            float seconds;
            if (string.IsNullOrEmpty(set)
                || !float.TryParse(set, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out seconds)
                || seconds <= 0f
                || seconds >= scenario.Seconds)
            {
                return scenario;
            }

            return new Battery.Scenario
            {
                Name = scenario.Name,
                Question = scenario.Question,
                Environment = scenario.Environment,
                Load = scenario.Load,
                Then = scenario.Then,
                ThenAfterSeconds = scenario.ThenAfterSeconds,
                Seconds = seconds,
            };
        }

        /// <summary>The walk's name: its resume record, and the `walk` column of every row.</summary>
        private const string Label = "air";

        /// <summary>The one scenario of the four with no air in it.</summary>
        private const string Anchor = "vacuum-shadow";

        /// <summary>
        /// Substeps of slack allowed before a ship counts as cheaper in air than in vacuum.
        ///
        /// The estimate is a maximum over nodes of a ratio, taken at the end of a run, so two runs
        /// of the same hull that settled at slightly different temperatures do not produce
        /// identical numbers. A hundredth of a substep is far below anything the claim is about and
        /// far above what a settled difference produces.
        /// </summary>
        private const float Slack = 0.01f;

        /// <summary>What the walk learned about one ship.</summary>
        private class Walked
        {
            public string Ship;
            public bool Convected;
            public readonly List<ScenarioOutcome> Outcomes = new List<ScenarioOutcome>();
            public readonly List<string> Violations = new List<string>();
        }

        [Fact]
        public void EveryShipInTheCorpusIsMeasuredInAir()
        {
            if (CorpusFixture.Files().Count == 0) return;

            List<Battery.Scenario> scenarios = Scenarios();

            List<Walked> results = CorpusFixture.Sweep(Label,
                delegate (Blueprints.Ship ship) { return Walk(ship, scenarios); });

            Assert.True(results.Count > 0, "the corpus yielded no ships to walk in air");

            List<string> violations = new List<string>();
            int convected = 0;

            foreach (Walked ship in results)
            {
                if (ship.Convected) convected++;
                violations.AddRange(ship.Violations);
            }

            // **A walk that judged nothing reports success** (`E8`). If the convection path is
            // never delivering watts then every figure this walk produced is a vacuum figure under
            // an atmospheric name, which is precisely the failure benchmarks.md records: a report
            // that dutifully measured convection as costing nothing, having never run it.
            Assert.True(convected > 0,
                "no ship of " + results.Count + " recorded any convection, so this walk measured "
                + "vacuum under three atmospheric names");

            Assert.True(violations.Count == 0,
                violations.Count + " violations across " + results.Count + " ships:\n  "
                + string.Join("\n  ", violations.ToArray()));
        }

        private static Walked Walk(Blueprints.Ship ship, List<Battery.Scenario> scenarios)
        {
            Walked walked = new Walked { Ship = ship.Name };

            // **Four builds a ship, not five.** `Battery.Run` builds its own assembly, so the
            // structural pass the survey does first would be a fifth build of every hull in the
            // corpus for one integer. The accounting claim is made off the outcome's own node count
            // instead, which is the same number read from the run that produced the row.
            Dictionary<string, ScenarioOutcome> byName =
                new Dictionary<string, ScenarioOutcome>(StringComparer.Ordinal);

            foreach (Battery.Scenario scenario in scenarios)
            {
                ScenarioOutcome outcome = Battery.Run(ship, scenario);
                byName[scenario.Name] = outcome;
                walked.Outcomes.Add(outcome);
            }

            CorpusRecord.Outcomes(Label, walked.Outcomes);

            // **No `ships.csv`.** A hull's structure is a property of the hull and not of the world
            // it is run in, and `CorpusSurvey` already records it for this same population. A second
            // copy is a copy that can drift, and this walk has no honest value for the sealed-block
            // column it would have to fill.

            ScenarioOutcome anchor = byName[Anchor];

            // Every block the reader counted became a node, or every number below describes a
            // different hull than the file holds.
            if (anchor.Blocks != ship.Blocks)
            {
                walked.Violations.Add("accounting: " + ship.Name + " read " + ship.Blocks
                    + " blocks and built " + anchor.Blocks + " nodes");
                return walked;
            }

            foreach (ScenarioOutcome outcome in walked.Outcomes)
            {
                if (outcome.Scenario == Anchor) continue;

                // Direct evidence that this run had air in it, read off the solver's own ledger
                // rather than inferred from a temperature. One ship's worth is enough to mark it;
                // the population count is what the walk asserts.
                if (outcome.ConvectionWatts != 0f) walked.Convected = true;

                // **Air cannot make a hull softer.** Substep demand is conductance over capacity,
                // and convection adds a term to every exposed node without touching either
                // denominator — so a ship that reads cheaper in air than in the dark has a
                // convection path that did not engage, which is a defect this repository has
                // shipped once already and which no vacuum walk can see.
                if (outcome.SubstepsDemanded + Slack < anchor.SubstepsDemanded)
                {
                    walked.Violations.Add("air-softer: " + ship.Name + " / " + outcome.Scenario
                        + " demands " + outcome.SubstepsDemanded.ToString("n2")
                        + " substeps against " + anchor.SubstepsDemanded.ToString("n2")
                        + " in vacuum");
                }

                if (float.IsNaN(outcome.PeakKelvin) || float.IsInfinity(outcome.PeakKelvin))
                {
                    walked.Violations.Add("not-a-number: " + ship.Name + " / " + outcome.Scenario
                        + " peaked at " + outcome.PeakKelvin);
                }
            }

            return walked;
        }
    }
}
