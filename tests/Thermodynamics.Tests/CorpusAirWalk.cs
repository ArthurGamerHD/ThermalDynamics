using System;
using System.Collections.Generic;
using System.Globalization;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    [Collection("alone")]
    public class CorpusAirWalk
    {

        private static List<Battery.Scenario> Scenarios()
        {
            List<Battery.Scenario> chosen = ScenarioIndex.Resolve(PairLab.AirScenarios, "the air walk");
            for (int i = 0; i < chosen.Count; i++) chosen[i] = Ceiling(chosen[i]);
            return chosen;
        }


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

        private const string Label = "air";

        private const string Anchor = "vacuum-shadow";

        private const float Slack = 0.01f;

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

            Dictionary<string, ScenarioOutcome> byName =
                new Dictionary<string, ScenarioOutcome>(StringComparer.Ordinal);

            foreach (Battery.Scenario scenario in scenarios)
            {
                ScenarioOutcome outcome = Battery.Run(ship, scenario);
                byName[scenario.Name] = outcome;
                walked.Outcomes.Add(outcome);
            }

            CorpusRecord.Outcomes(Label, walked.Outcomes);


            ScenarioOutcome anchor = byName[Anchor];

            if (anchor.Blocks != ship.Blocks)
            {
                walked.Violations.Add("accounting: " + ship.Name + " read " + ship.Blocks
                    + " blocks and built " + anchor.Blocks + " nodes");
                return walked;
            }

            foreach (ScenarioOutcome outcome in walked.Outcomes)
            {
                if (outcome.Scenario == Anchor) continue;

                if (outcome.ConvectionWatts != 0f) walked.Convected = true;

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
