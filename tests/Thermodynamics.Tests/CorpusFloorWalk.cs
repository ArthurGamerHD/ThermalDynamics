using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    [Collection("alone")]
    public class CorpusFloorWalk
    {
        private const string Label = "floor";

        private const float Slack = 0.01f;

/// <summary>Scenarios operation.</summary>
        private static List<Battery.Scenario> Scenarios()
        {
            return ScenarioIndex.Resolve(PairLab.AirScenarios, "the floor walk");
        }

/// <summary>Arm operation.</summary>
        private static ThermalSettings Arm(bool floorWhenOverBudget)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.FloorBlocksWhenOverBudget = floorWhenOverBudget;
            return settings;
        }

        private class Walked
        {
            public string Ship;
            public bool Floored;
/// <summary>List operation.</summary>
            public readonly List<ScenarioOutcome> Outcomes = new List<ScenarioOutcome>();
/// <summary>List operation.</summary>
            public readonly List<string> Violations = new List<string>();
        }

        [Fact]
/// <summary>EveryShipTheAllowanceBindsOnIsMeasuredWithTheFloorAndWithoutIt operation.</summary>
        public void EveryShipTheAllowanceBindsOnIsMeasuredWithTheFloorAndWithoutIt()
        {
            if (CorpusFixture.Files().Count == 0) return;

/// <summary>Scenarios operation.</summary>
            List<Battery.Scenario> scenarios = Scenarios();

            List<Walked> results = CorpusFixture.Sweep(Label,
                delegate (Blueprints.Ship ship) { return Walk(ship, scenarios); });

            Assert.True(results.Count > 0, "the selection yielded no ships to walk");

/// <summary>List operation.</summary>
            List<string> violations = new List<string>();
            int floored = 0;

            foreach (Walked ship in results)
            {
                if (ship.Floored) floored++;
                violations.AddRange(ship.Violations);
            }

            Assert.True(floored > 0,
                "the floor engaged on none of " + results.Count + " ships, so either the selection "
                + "is not the population the allowance binds on or the switch reaches nothing");

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
                ScenarioOutcome shortened = Battery.Run(ship, scenario, Arm(false));
                shortened.SubstepsPerBlockCap = 0;

                ScenarioOutcome floored =
                    Battery.RunForSeconds(ship, scenario, shortened.RunSeconds, Arm(true));

                floored.SubstepsPerBlockCap = -1;

                walked.Outcomes.Add(shortened);
                walked.Outcomes.Add(floored);

                Judge(walked, scenario.Name, shortened, floored);
            }

            CorpusRecord.Outcomes(Label, walked.Outcomes);
            return walked;
        }

/// <summary>Judge operation.</summary>
        private static void Judge(Walked walked, string scenario,
            ScenarioOutcome shortened, ScenarioOutcome floored)
        {
            string where = walked.Ship + " / " + scenario;

            if (Math.Abs(floored.RunSeconds - shortened.RunSeconds) > 1f)
            {
                walked.Violations.Add("clock: " + where + " ran "
                    + shortened.RunSeconds.ToString("n0") + " s shortened and "
                    + floored.RunSeconds.ToString("n0") + " s floored");
            }

            if (shortened.FlooredNodes != 0)
            {
                walked.Violations.Add("control-floored: " + where + " floored "
                    + shortened.FlooredNodes + " nodes with the switch off");
            }

            if (floored.FlooredNodes > 0) walked.Floored = true;

            if (floored.SubstepsDemanded > shortened.SubstepsDemanded + Slack)
            {
                walked.Violations.Add("stiffer: " + where + " demands "
                    + floored.SubstepsDemanded.ToString("n2") + " floored against "
                    + shortened.SubstepsDemanded.ToString("n2"));
            }

            if (float.IsNaN(floored.PeakKelvin) || float.IsInfinity(floored.PeakKelvin))
            {
                walked.Violations.Add("not-a-number: " + where + " peaked at " + floored.PeakKelvin);
            }
        }
    }
}
