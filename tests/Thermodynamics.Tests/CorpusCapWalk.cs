using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Every ship in the corpus, in air, **twice**: with the per-block substep cap off as it ships,
    /// and with `MaxSubstepsPerBlock 6`.
    ///
    /// <para>
    /// **Why it exists.** backlog.md `C3` asks whether that cap should be
    /// a default and is undecided only because what it costs has been measured on *one hull* —
    /// 0.028 K on the worst-placed block of a driven census hull. `G6`'s cost half fails in air
    /// (`F11`), and the cap is the one lever that lowers a step's work rather than moving it into
    /// the frame. They are the same question, and this is the measurement. What the walk is
    /// expected to say was written into
    /// balance-lab.md
    /// with its falsifiers before any of this ran (`E1`, `E11`).
    /// </para>
    ///
    /// <para>
    /// **The one thing the design has to get right: both arms run to the same simulated clock.**
    /// `Battery.Run` stops when the hottest block moves less than 0.25 K in a sixty-second chunk,
    /// so two configurations of one ship stop at different instants — and the difference this walk
    /// is measuring is expected to be a hundredth of a kelvin, two orders of magnitude under that
    /// tolerance. Comparing two settle-stopped runs would have measured the stopping rule (`M1`,
    /// `P6`). So the uncapped arm runs first, and its elapsed clock is handed to the capped one
    /// through <see cref="Battery.RunForSeconds"/>.
    /// </para>
    ///
    /// <para>
    /// **What it judges, and what it only records.** The population figures are scored downstream
    /// by `verdict.py` and `cap.py` against markers already written down; what this walk asserts is
    /// the four things only the run itself can check — that the air scenarios carried convection,
    /// that the cap held something back, that a capped run is never *stiffer* than its own control,
    /// and that the two arms really did run the same clock. The identity the projection rested on —
    /// capped demand being `min(uncapped demand, cap)` — is asserted here too, because it is the
    /// assumption under the predicted benefit and a walk that does not test its own premise has
    /// tested nothing (`P4`).
    /// </para>
    /// </summary>
    [Collection("alone")]
    public class CorpusCapWalk
    {
        /// <summary>The walk's name: its resume record, and the `walk` column of every row.</summary>
        private const string Label = "cap";

        /// <summary>
        /// The cap the arm runs at, and the value `C3` is about.
        ///
        /// **Six rather than the four the measurement supports on its own**, because six is the
        /// value `C3` has carried since it was re-measured at `C24`'s pair and is what a decision
        /// would ship. The reach curve in stiffness.md says a cap of 8
        /// holds back 0.92 % of the corpus's blocks and a cap of 4 holds back 23.22 %, so six is
        /// inside the band where the population's own answer is unmeasured, which is the point.
        /// </summary>
        private const int Cap = 6;

        /// <summary>
        /// Substeps of slack before two readings count as different.
        ///
        /// The estimate is a maximum over elements of a ratio taken at the end of a run, so two
        /// runs that settled a hair apart do not produce identical numbers. A hundredth of a substep
        /// is far below anything any claim here is about and far above what a settled pair produces.
        /// </summary>
        private const float Slack = 0.01f;

        /// <summary>
        /// The four the panel's air pass runs and the walk of `F11` ran, unchanged — so the uncapped
        /// arm is a reproduction of that walk as well as this one's control (`E7`).
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
                    "the cap walk asks for scenario '" + name + "' and the battery has no such case");
                chosen.Add(byName[name]);
            }

            return chosen;
        }

        /// <summary>The scenario with no air in it, which is where the cap is expected to buy least.</summary>
        private const string Anchor = "vacuum-shadow";

        /// <summary>Settings for one arm. The cap is the only thing that differs between them.</summary>
        private static ThermalSettings Arm(int cap)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubstepsPerBlock = cap;
            return settings;
        }

        /// <summary>What the walk learned about one ship.</summary>
        private class Walked
        {
            public string Ship;
            public bool Convected;
            public bool Floored;
            public readonly List<ScenarioOutcome> Outcomes = new List<ScenarioOutcome>();
            public readonly List<string> Violations = new List<string>();
        }

        [Fact]
        public void EveryShipInTheCorpusIsMeasuredWithTheCapAndWithoutIt()
        {
            if (CorpusFixture.Files().Count == 0) return;

            List<Battery.Scenario> scenarios = Scenarios();

            List<Walked> results = CorpusFixture.Sweep(Label,
                delegate (Blueprints.Ship ship) { return Walk(ship, scenarios); });

            Assert.True(results.Count > 0, "the corpus yielded no ships to walk under the cap");

            List<string> violations = new List<string>();
            int convected = 0;
            int floored = 0;

            foreach (Walked ship in results)
            {
                if (ship.Convected) convected++;
                if (ship.Floored) floored++;
                violations.AddRange(ship.Violations);
            }

            // **A walk that judged nothing reports success** (`E8`). Two ways this one could:
            // by running vacuum under three atmospheric names, which is the failure `F11` guards,
            // and by running the capped arm with a cap that reached nothing at all — which would
            // make every delta below a measurement of floating-point noise dressed as a finding.
            Assert.True(convected > 0,
                "no ship of " + results.Count + " recorded any convection, so this walk measured "
                + "vacuum under three atmospheric names");

            Assert.True(floored > 0,
                "the cap held nothing back on any of " + results.Count + " ships, so the capped arm "
                + "is the control run twice and every delta this dataset carries is noise");

            Assert.True(violations.Count == 0,
                violations.Count + " violations across " + results.Count + " ships:\n  "
                + string.Join("\n  ", violations.ToArray()));
        }

        /// <summary>One scenario's two arms, so a parallel map returns them as one item.</summary>
        private class Pair
        {
            public string Name;
            public ScenarioOutcome Uncapped;
            public ScenarioOutcome Capped;
        }

        private static Walked Walk(Blueprints.Ship ship, List<Battery.Scenario> scenarios)
        {
            Walked walked = new Walked { Ship = ship.Name };

            Dictionary<string, ScenarioOutcome> control =
                new Dictionary<string, ScenarioOutcome>(StringComparer.Ordinal);

            // **The scenarios run together in the tail of the walk**, which is where a third of it
            // is spent: the corpus is walked largest-first with one worker per ship, so nothing
            // finishes before the biggest hull does, and on this walk's own 2026-08-28 run the last
            // ships were **30.7 %** of it with the machine idle. The **pair** stays sequential
            // inside each scenario, because the capped arm's clock is the control's elapsed
            // seconds and it cannot be started before the control has finished (`M1`, `P6`).
            List<Pair> pairs = LabRun.Map(scenarios, scenario =>
            {
                // The control: the cap off, stopped at equilibrium, which is exactly what `F11`'s
                // walk did. Its elapsed clock is the second arm's clock.
                ScenarioOutcome uncapped = Battery.Run(ship, scenario, Arm(0));
                uncapped.SubstepsPerBlockCap = 0;

                ScenarioOutcome capped =
                    Battery.RunForSeconds(ship, scenario, uncapped.RunSeconds, Arm(Cap));
                capped.SubstepsPerBlockCap = Cap;

                return new Pair { Name = scenario.Name, Uncapped = uncapped, Capped = capped };
            }, CorpusFixture.WithinShip);

            // **A dropped scenario must not read as a ship that simply has fewer** — see the same
            // guard in `CorpusAirWalk`. `LabRun.Map` swallows what throws, which is right for a
            // corpus of ships and wrong for the fixed set of scenarios inside one.
            if (pairs.Count != scenarios.Count)
            {
                walked.Violations.Add("scenarios: " + ship.Name + " ran " + pairs.Count + " of "
                    + scenarios.Count + " scenarios; one threw and was dropped");
                return walked;
            }

            foreach (Pair pair in pairs)
            {
                control[pair.Name] = pair.Uncapped;
                walked.Outcomes.Add(pair.Uncapped);
                walked.Outcomes.Add(pair.Capped);

                Judge(walked, pair.Name, pair.Uncapped, pair.Capped);
            }

            CorpusRecord.Outcomes(Label, walked.Outcomes);

            ScenarioOutcome anchor = control[Anchor];

            // Every block the reader counted became a node, or every number here describes a
            // different hull than the file holds.
            if (anchor.Blocks != ship.Blocks)
            {
                walked.Violations.Add("accounting: " + ship.Name + " read " + ship.Blocks
                    + " blocks and built " + anchor.Blocks + " nodes");
                return walked;
            }

            foreach (KeyValuePair<string, ScenarioOutcome> entry in control)
            {
                if (entry.Key == Anchor) continue;

                if (entry.Value.ConvectionWatts != 0f) walked.Convected = true;

                // **Air cannot make a hull softer** — the check `F11` carried, kept because this
                // walk's control arm is that walk repeated and a reproduction that drops the
                // original's assertions is not a reproduction.
                if (entry.Value.SubstepsDemanded + Slack < anchor.SubstepsDemanded)
                {
                    walked.Violations.Add("air-softer: " + ship.Name + " / " + entry.Key
                        + " demands " + entry.Value.SubstepsDemanded.ToString("n2")
                        + " substeps against " + anchor.SubstepsDemanded.ToString("n2")
                        + " in vacuum");
                }
            }

            return walked;
        }

        /// <summary>
        /// The three things only the paired run can check, on one scenario of one ship.
        /// </summary>
        private static void Judge(Walked walked, string scenario,
            ScenarioOutcome uncapped, ScenarioOutcome capped)
        {
            string where = walked.Ship + " / " + scenario;

            // **The two arms ran the same clock**, which is the assumption every delta rests on.
            // A chunk is 60 s and the capped arm is advanced in the same chunks, so the two agree
            // to the step rather than approximately.
            if (Math.Abs(capped.RunSeconds - uncapped.RunSeconds) > 1f)
            {
                walked.Violations.Add("clock: " + where + " ran " + uncapped.RunSeconds.ToString("n0")
                    + " s uncapped and " + capped.RunSeconds.ToString("n0") + " s capped");
            }

            // **The cap does what its name says.** A capped demand above the cap is the floor not
            // reaching an element it should have — a loop or a room, both of which the floor also
            // covers, are the ones that have been missed before.
            if (capped.SubstepsDemanded > Cap + Slack)
            {
                walked.Violations.Add("over-cap: " + where + " demands "
                    + capped.SubstepsDemanded.ToString("n2") + " substeps under a cap of " + Cap);
            }

            // **The identity the predicted benefit rests on.** A cap raises capacity until no
            // element asks for more than it grants, so a grid's demand becomes the smaller of its
            // own demand and the cap. The whole projection in balance-lab.md substitutes that into
            // `F11`'s rows, and a walk that does not test its own premise has tested nothing (`P4`).
            float expected = Math.Min(uncapped.SubstepsDemanded, Cap);
            float tolerance = Math.Max(Slack, expected * 0.01f);

            if (Math.Abs(capped.SubstepsDemanded - expected) > tolerance)
            {
                walked.Violations.Add("identity: " + where + " demands "
                    + capped.SubstepsDemanded.ToString("n3") + " capped against min("
                    + uncapped.SubstepsDemanded.ToString("n3") + ", " + Cap + ")");
            }

            // **The reach, read off the solver rather than inferred from the demand.** A cap that
            // binds only the one stiffest element moves the demand and reaches almost nothing;
            // which of those two it is decides the value a cap should take, and only this column
            // says. The control must report nought, or the cap is on in the arm that is meant to
            // be without it — which would make every delta here a comparison of two capped runs.
            if (capped.FlooredNodes > 0) walked.Floored = true;

            if (uncapped.FlooredNodes != 0)
            {
                walked.Violations.Add("control-capped: " + where + " floored "
                    + uncapped.FlooredNodes + " nodes with the cap off");
            }

            if (float.IsNaN(capped.PeakKelvin) || float.IsInfinity(capped.PeakKelvin))
            {
                walked.Violations.Add("not-a-number: " + where + " peaked at " + capped.PeakKelvin
                    + " under the cap");
            }
        }
    }
}
