using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    [Collection("alone")]
    public class CorpusShapeWalk
    {
        private const string Label = "shape";

        private const string Measured = "reentry";
        private const string Control = "vacuum-shadow";

        private const string ShapedSuffix = "-shaped";

        private const float Slack = 0.01f;

/// <summary>Arm operation.</summary>
        private static ThermalSettings Arm(bool shape)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableShapeDrag = shape;
            return settings;
        }

/// <summary>Scenarios operation.</summary>
        private static List<Battery.Scenario> Scenarios()
        {
            Dictionary<string, Battery.Scenario> byName =
                new Dictionary<string, Battery.Scenario>(StringComparer.Ordinal);
            foreach (Battery.Scenario scenario in Battery.All()) byName[scenario.Name] = scenario;

/// <summary>List operation.</summary>
            List<Battery.Scenario> chosen = new List<Battery.Scenario>();
            foreach (string name in new[] { Control, Measured })
            {
                Assert.True(byName.ContainsKey(name),
                    "the shape walk asks for scenario '" + name + "' and the battery has no such case");
                chosen.Add(byName[name]);
            }

            return chosen;
        }

        private class Walked
        {
            public string Ship;
            public bool Moved;
/// <summary>List operation.</summary>
            public readonly List<ScenarioOutcome> Outcomes = new List<ScenarioOutcome>();
/// <summary>List operation.</summary>
            public readonly List<string> Violations = new List<string>();
        }

        [Fact]
/// <summary>EveryShipInTheCorpusIsMeasuredWithTheShapeTermAndWithoutIt operation.</summary>
        public void EveryShipInTheCorpusIsMeasuredWithTheShapeTermAndWithoutIt()
        {
            if (CorpusFixture.Files().Count == 0) return;

/// <summary>Scenarios operation.</summary>
            List<Battery.Scenario> scenarios = Scenarios();

            List<Walked> results = CorpusFixture.Sweep(Label,
                delegate (Blueprints.Ship ship) { return Walk(ship, scenarios); });

            Assert.True(results.Count > 0, "the corpus yielded no ships to walk under the shape term");

/// <summary>List operation.</summary>
            List<string> violations = new List<string>();
            int moved = 0;

            foreach (Walked ship in results)
            {
                if (ship.Moved) moved++;
                violations.AddRange(ship.Violations);
            }

            Assert.True(moved > 0,
                "the shape term moved no temperature on any of " + results.Count + " ships, so the "
                + "shaped arm is the control run twice and this dataset says nothing");

            Assert.True(violations.Count == 0,
                violations.Count + " violations across " + results.Count + " ships:\n  "
                + string.Join("\n  ", violations.ToArray()));
        }

/// <summary>Walk operation.</summary>
        private static Walked Walk(Blueprints.Ship ship, List<Battery.Scenario> scenarios)
        {
            Walked walked = new Walked { Ship = ship.Name };

            foreach (Battery.Scenario scenario in scenarios)
            {
                ScenarioOutcome plain = Battery.Run(ship, scenario, Arm(false));

                ScenarioOutcome shaped =
                    Battery.RunForSeconds(ship, scenario, plain.RunSeconds, Arm(true));
                shaped.Scenario = scenario.Name + ShapedSuffix;

                walked.Outcomes.Add(plain);
                walked.Outcomes.Add(shaped);

                Judge(walked, scenario.Name, plain, shaped);
            }

            CorpusRecord.Outcomes(Label, walked.Outcomes);
            return walked;
        }

/// <summary>Judge operation.</summary>
        private static void Judge(Walked walked, string scenario,
            ScenarioOutcome plain, ScenarioOutcome shaped)
        {
            float delta = shaped.PeakKelvin - plain.PeakKelvin;

            if (scenario == Control)
            {
                if (Math.Abs(delta) > Slack)
                {
                    walked.Violations.Add("control moved: " + walked.Ship + " / " + scenario
                        + " read " + plain.PeakKelvin + " K plain and " + shaped.PeakKelvin
                        + " K shaped, and there is no air in it to carry the difference");
                }

                return;
            }

            if (delta > Slack)
            {
                walked.Violations.Add("shaped hotter: " + walked.Ship + " / " + scenario
                    + " read " + plain.PeakKelvin + " K plain and " + shaped.PeakKelvin
                    + " K shaped, and the shape factor cannot exceed one");
            }

            if (Math.Abs(delta) > Slack) walked.Moved = true;
        }
    }
}
