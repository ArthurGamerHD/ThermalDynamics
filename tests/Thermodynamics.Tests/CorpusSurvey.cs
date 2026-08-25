using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// One pass over every ship in the corpus: built once, run through each distinct scenario once,
    /// with every invariant evaluated against the shared outcomes and every violation reported by
    /// name (`M8`). Five stepped runs a ship where separate walks cost twelve, most of the difference
    /// being scenarios that are the same simulation under two names.
    ///
    /// <para>
    /// **<c>all-peak</c> is deliberately absent**: it puts 96 % of ships over critical, and a melting
    /// grid never settles, so it starves the balance invariant it would be run for. Every invariant
    /// here asserts a non-zero judged count (`E8`).
    /// See balance-lab.md, and balance.md, The population.
    /// </para>
    /// </summary>
    [Collection("alone")]
    public class CorpusSurvey
    {
        /// <summary>The scenarios a corpus ship is measured under. Distinct by construction.</summary>
        private static List<Battery.Scenario> Scenarios()
        {
            Dictionary<string, Battery.Scenario> byName = new Dictionary<string, Battery.Scenario>();
            foreach (Battery.Scenario scenario in Battery.All()) byName[scenario.Name] = scenario;

            return new List<Battery.Scenario>
            {
                byName["idle"],             // G1, and the shadow reference for the sunlight claim
                byName["vacuum-sunlit"],    // the sunlight claim's other half
                byName["full-electrical"],  // G2, the load a ship can actually be in
                byName["burn-forward"],     // the directional burn, and the balance claim's second look
                byName["recovery"],         // G5: from a full burn throttled to idle, does it come back
            };
        }

        /// <summary>Everything the survey learned about one ship.</summary>
        private class Surveyed
        {
            public string Ship;
            public long SealedBlocks;
            public long Blocks;
            public bool Joints;
            public bool Rooms;
            public bool SmallGrid;
            public bool Stepped;
            public readonly List<ScenarioOutcome> Outcomes = new List<ScenarioOutcome>();
            public readonly List<string> Violations = new List<string>();
        }

        [Fact]
        public void EveryShipInTheCorpusHoldsEveryInvariant()
        {
            if (CorpusFixture.Files().Count == 0) return;

            List<Battery.Scenario> scenarios = Scenarios();

            List<Surveyed> results = CorpusFixture.Sweep("survey",
                delegate (Blueprints.Ship ship) { return Survey(ship, scenarios); });

            Assert.True(results.Count > 0, "the corpus yielded no surveyable ships");

            // ---- aggregate claims --------------------------------------------------------------

            List<string> violations = new List<string>();
            long blocks = 0;
            long sealedBlocks = 0;
            int sealedShips = 0;
            int withJoints = 0;
            int withRooms = 0;
            int smallGrid = 0;
            int stepped = 0;
            int warmedBySun = 0;
            int judgedBalance = 0;

            foreach (Surveyed ship in results)
            {
                blocks += ship.Blocks;
                sealedBlocks += ship.SealedBlocks;
                if (ship.SealedBlocks > 0) sealedShips++;
                if (ship.Joints) withJoints++;
                if (ship.Rooms) withRooms++;
                if (ship.SmallGrid) smallGrid++;
                if (ship.Stepped) stepped++;

                foreach (string violation in ship.Violations)
                {
                    if (violation == WarmedMarker) warmedBySun++;
                    else if (violation == JudgedMarker) judgedBalance++;
                    else violations.Add(violation);
                }
            }

            // Sealed blocks are rare, as a population ratio. The bound and its reasons are
            // unchanged from the standalone walk.
            if (blocks > 0)
            {
                double share = sealedBlocks / (double)blocks;
                if (share >= 0.001d)
                {
                    violations.Add("sealed-blocks: " + sealedBlocks + " of " + blocks
                        + " blocks across " + sealedShips
                        + " ships are thermally sealed — the parser may be dropping mounts again");
                }
            }

            // The population contains what a population must: subgrids, small grids, sealed rooms,
            // ships that actually step. Any of these at zero means a reader subsystem went dark.
            if (withJoints == 0) violations.Add("population: no ship resolved a mechanical joint");
            if (withRooms == 0) violations.Add("population: no ship mapped a sealed room");
            if (smallGrid == 0) violations.Add("population: no small-grid ship was read");
            if (stepped == 0) violations.Add("population: no ship changed temperature when stepped");

            // A sun that delivers no watts anywhere is a solar path contributing nothing; the
            // per-ship claim is only "never cools", which a dead sun satisfies.
            if (warmedBySun == 0) violations.Add("population: the sun delivered watts to not one ship");

            // A balance invariant that never got to judge anything proves nothing.
            if (judgedBalance == 0)
            {
                violations.Add("population: no outcome reached equilibrium, so made-versus-vented "
                    + "was never checked");
            }

            Assert.True(violations.Count == 0,
                violations.Count + " invariant violations across " + results.Count + " ships:\n  "
                + string.Join("\n  ", violations.GetRange(0, Math.Min(violations.Count, 40))));
        }

        /// <summary>Sentinels carried back through the violations list as cheap counters.</summary>
        private const string WarmedMarker = "warmed";
        private const string JudgedMarker = "judged";

        /// <summary>How near made and vented must be at equilibrium, as a share of made.</summary>
        private const float Agreement = 0.05f;

        /// <summary>Bulk drift below this share of made watts counts as equilibrium.</summary>
        private const float BulkFlat = 0.01f;

        /// <summary>
        /// Builds one ship once, checks its structure, runs the five scenarios, and evaluates every
        /// per-ship invariant against the outcomes. All of it on one worker, so nothing is shared
        /// and nothing needs re-reading.
        /// </summary>
        private static Surveyed Survey(Blueprints.Ship ship, List<Battery.Scenario> scenarios)
        {
            Surveyed surveyed = new Surveyed { Ship = ship.Name, SmallGrid = !ship.Large };

            // ---- structure, once ---------------------------------------------------------------

            ShipAssembly assembly = ship.Build();
            surveyed.Blocks = assembly.NodeCount;
            surveyed.Joints = assembly.Bridges.Count > 0;
            surveyed.Rooms = assembly.RoomCount > 0;

            // Every block the reader counted became a node, or the ship is mis-read and every
            // number below would describe a different hull than the file holds.
            if (assembly.NodeCount != ship.Blocks)
            {
                surveyed.Violations.Add("accounting: " + ship.Name + " read " + ship.Blocks
                    + " blocks and built " + assembly.NodeCount + " nodes");
                return surveyed;
            }

            for (int g = 0; g < assembly.Simulations.Count; g++)
            {
                ThermalSolver solver = assembly.Simulations[g].Solver;
                if (solver.Nodes.Count < 2) continue;

                // One definition of *sealed*, shared with `bench sealed`, which is the tool that
                // explains a count this only produces (`P5`). The two disagreed until 2026-08-24:
                // this one counted a block coupled to room air as sealed.
                System.Collections.Generic.HashSet<int> touchingAir =
                    HotSpotLab.NodesTouchingAir(solver);

                for (int i = 0; i < solver.Nodes.Count; i++)
                {
                    if (HotSpotLab.IsSealed(solver, i, touchingAir)) surveyed.SealedBlocks++;
                }
            }

            // A minute in shadow, so "the solver runs on this hull" is established before the
            // long scenarios are paid for.
            AssemblyRunner probe = new AssemblyRunner(assembly);
            probe.Environment = t => Worlds.Shadow();
            float before = assembly.Hottest() == null ? 0f : assembly.Hottest().Temperature;
            probe.Run(60f);
            surveyed.Stepped = probe.Hottest[probe.Hottest.Count - 1] != before;

            // ---- the scenarios, each once ------------------------------------------------------

            Dictionary<string, ScenarioOutcome> byName = new Dictionary<string, ScenarioOutcome>();
            foreach (Battery.Scenario scenario in scenarios)
            {
                ScenarioOutcome outcome = Battery.Run(ship, scenario);
                byName[scenario.Name] = outcome;
                surveyed.Outcomes.Add(outcome);
            }

            CorpusRecord.Outcomes("survey", surveyed.Outcomes);

            if (CorpusRecord.On)
            {
                List<string> row = new List<string>();
                row.Add(CorpusRecord.ShipRow(ship, assembly.NodeCount, assembly.Bridges.Count,
                    assembly.RoomCount, surveyed.SealedBlocks, surveyed.Stepped, true));
                CorpusRecord.Write("ships", CorpusRecord.ShipHeader, row);
            }

            // ---- the per-ship invariants -------------------------------------------------------

            ScenarioOutcome idle = byName["idle"];
            ScenarioOutcome sunlit = byName["vacuum-sunlit"];
            ScenarioOutcome loaded = byName["full-electrical"];
            ScenarioOutcome burn = byName["burn-forward"];
            ScenarioOutcome recovery = byName["recovery"];

            // G1 at ship grain: nothing melts parked. The population share is judged downstream by
            // the verdict script; a single idle-critical ship is already a finding worth naming.
            if (idle.BlocksOverCritical > 0)
            {
                surveyed.Violations.Add("idle-safe: " + ship.Name + " has "
                    + idle.BlocksOverCritical + " blocks over critical while parked");
            }

            // The sunlight-never-cools claim lives in SunlightPanelWalk, which starts hulls at
            // the vacuum floor on equal clocks so the comparison is valid at any stop time. It
            // cannot be judged here: runs that settle fast are too short to measure bulk drift and
            // runs that stop slow are still moving, so an equilibrium gate on these adaptive runs
            // admits nothing — measured, zero ships of eighty-eight qualified.
            // The sun path must be delivering watts — direct evidence, not an inference from two
            // means. SolarWatts is read from the solver's own ledger at the end of the run.
            if (sunlit.SolarWatts > 0f) surveyed.Violations.Add(WarmedMarker);

            // More load is never cooler, measured on the load a ship can actually be in.
            if (loaded.PeakKelvin < idle.PeakKelvin - 1f)
            {
                surveyed.Violations.Add("load-ladder: " + ship.Name + " peaks at "
                    + loaded.PeakKelvin.ToString("n0") + " K loaded, below "
                    + idle.PeakKelvin.ToString("n0") + " K idle");
            }

            // At equilibrium a grid sheds what it makes. A ship with a sealed block can never
            // balance, and one still moving or still melting has not reached the state the claim
            // is about — those are excluded, and the exclusion is counted so a walk that judged
            // nothing is itself a violation.
            if (surveyed.SealedBlocks == 0)
            {
                foreach (ScenarioOutcome outcome in new[] { loaded, burn })
                {
                    if (outcome.SecondsToSettle < 0f) continue;
                    if (outcome.MadeWatts <= 1f) continue;
                    if (outcome.BlocksOverCritical > 0) continue;
                    if (float.IsNaN(outcome.BulkDriftWatts)) continue;
                    if (Math.Abs(outcome.BulkDriftWatts) > BulkFlat * outcome.MadeWatts) continue;

                    surveyed.Violations.Add(JudgedMarker);

                    float difference = Math.Abs(outcome.MadeWatts - outcome.VentedWatts);
                    if (difference / outcome.MadeWatts > Agreement)
                    {
                        surveyed.Violations.Add("balance: " + ship.Name + " / " + outcome.Scenario
                            + " made " + outcome.MadeWatts.ToString("n0") + " W, vented "
                            + outcome.VentedWatts.ToString("n0") + " W");
                    }
                }
            }

            // G5, as the criterion is actually written: "recovery time unbounded, or damage
            // continues after the load stops." The burn phase is the all-out ceiling — every
            // reactor at plate rating — so a heavy hull legitimately ends the fixed cooldown still
            // hot; that is bounded recovery in progress, not a spiral. The spiral is a ship that is
            // over critical *and not cooling* once the load is gone: heat still arriving with
            // nowhere left for it to go.
            if (recovery.BlocksOverCritical > 0
                && !float.IsNaN(recovery.BulkDriftWatts)
                && recovery.BulkDriftWatts >= 0f)
            {
                surveyed.Violations.Add("recovery: " + ship.Name + " has "
                    + recovery.BlocksOverCritical + " blocks over critical and is still gaining "
                    + recovery.BulkDriftWatts.ToString("n0") + " W after the load stopped");
            }

            return surveyed;
        }
    }
}
